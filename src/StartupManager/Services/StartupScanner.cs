/*
 * Copyright 2026 Xiayue
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Services;

public sealed class StartupScanner
{
    public ScanResult Scan(IProgress<string>? progress = null)
    {
        var warnings = new List<string>();
        var entries = new List<ManagedEntry>();

        progress?.Report(L.T("正在并行读取系统服务与启动项…"));

        var processTask = Task.Run(() => RunSafe(ProcessProbe.Capture));
        var serviceTask = Task.Run(() => RunSafe(WindowsServiceManager.Enumerate));
        var registryTask = Task.Run(() => RunSafe(StartupRegistryManager.Enumerate));
        var folderTask = Task.Run(() => RunSafe(StartupFolderManager.Enumerate));
        var taskTask = Task.Run(() => RunSafe(ScheduledTaskManager.Enumerate));

        Task.WhenAll(processTask, serviceTask, registryTask, folderTask, taskTask)
            .GetAwaiter()
            .GetResult();

        var processResult = processTask.Result;
        var processes = processResult.Result ?? ProcessSnapshot.Empty;
        if (processResult.Error is not null)
        {
            warnings.Add(L.F("进程信息读取失败：{0}", processResult.Error.Message));
        }

        var serviceResult = serviceTask.Result;
        if (serviceResult.Error is not null)
        {
            warnings.Add(L.F("Windows 服务读取失败：{0}", serviceResult.Error.Message));
        }
        else if (serviceResult.Result is not null)
        {
            foreach (var service in serviceResult.Result)
            {
                entries.Add(MapService(service));
            }        }

        var registryResult = registryTask.Result;
        if (registryResult.Error is not null)
        {
            warnings.Add(L.F("注册表启动项读取失败：{0}", registryResult.Error.Message));
        }
        else if (registryResult.Result is not null)
        {
            foreach (var run in registryResult.Result)
            {
                entries.Add(MapRegistryEntry(run, processes, processResult.Error is null));
            }
        }

        var folderResult = folderTask.Result;
        if (folderResult.Error is not null)
        {
            warnings.Add(L.F("启动文件夹读取失败：{0}", folderResult.Error.Message));
        }
        else if (folderResult.Result is not null)
        {
            foreach (var folderEntry in folderResult.Result)
            {
                entries.Add(MapStartupFolderEntry(folderEntry, processes, processResult.Error is null));
            }
        }

        var taskResult = taskTask.Result;
        if (taskResult.Error is not null)
        {
            warnings.Add(L.F("计划任务读取失败：{0}", taskResult.Error.Message));
        }
        else if (taskResult.Result is not null)
        {
            foreach (var task in taskResult.Result)
            {
                entries.Add(MapTask(task, processes));
            }
        }

        var ordered = entries
            .OrderBy(e => KindRank(e.Kind))
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        progress?.Report(L.F("扫描完成，共 {0} 项", ordered.Count));
        return new ScanResult
        {
            Entries = ordered,
            Warnings = warnings,
            ScannedAt = DateTime.Now
        };
    }

    private static (T? Result, Exception? Error) RunSafe<T>(Func<T> action)
    {
        try
        {
            return (action(), null);
        }
        catch (Exception ex)
        {
            return (default, ex);
        }
    }

    private static ManagedEntry MapService(WindowsServiceInfo service)
    {
        var parsed = CommandLineParser.Parse(service.ImagePath);
        var isRunning = WindowsServiceManager.IsRunning(service.State);
        var runState = isRunning ? EntryRunState.Running : EntryRunState.NotRunning;
        var state = !isRunning && service.StartType == 4
            ? EntryRuntimeState.Disabled
            : isRunning
                ? EntryRuntimeState.Running
                : EntryRuntimeState.Stopped;

        var startMode = WindowsServiceManager.StartModeLabel(service.StartType, service.DelayedAutoStart);
        var startupTypeCode = service.StartType switch
        {
            0 => "boot",
            1 => "system",
            2 => service.DelayedAutoStart ? "auto-delayed" : "auto",
            3 => "manual",
            4 => "disabled",
            _ => "unknown"
        };

        var stateDetail = service.ProcessId > 0
            ? $"PID {service.ProcessId}{(string.IsNullOrWhiteSpace(service.ProcessName) ? string.Empty : $" · {service.ProcessName}")}"
            : WindowsServiceManager.StateLabel(service.State);
        var trigger = service.StartType switch
        {
            0 => L.T("引导启动"),
            1 => L.T("系统启动"),
            2 => service.DelayedAutoStart ? L.T("开机时（延迟）") : L.T("开机时"),
            3 => L.T("手动启动"),
            4 => L.T("已禁用"),
            _ => L.T("未知")
        };

        var serviceKind = L.IsEnglish ? "Windows service" : "Windows 服务";
        var description = string.IsNullOrWhiteSpace(service.Description)
            ? $"{startMode} · {serviceKind}"
            : service.Description;

        var processIds = service.ProcessId > 0
            ? new[] { service.ProcessId }
            : Array.Empty<int>();

        return new ManagedEntry
        {
            Id = $"service:{service.Name}",
            Name = service.DisplayName,
            Kind = EntryKind.WindowsService,
            Description = description,
            CommandLine = service.ImagePath,
            ExecutablePath = parsed.ExecutablePath,
            Arguments = parsed.Arguments,
            SourcePath = $@"HKLM\SYSTEM\CurrentControlSet\Services\{service.Name}",
            SourceLabel = L.IsEnglish ? "Windows service" : "Windows 服务",
            ScopeLabel = L.T("系统级"),
            TriggerLabel = trigger,
            StartupModeLabel = startMode,
            StartupTypeCode = startupTypeCode,
            ScopeCode = "system",
            RequiresAdmin = true,
            IsEnabled = service.StartType != 4,
            IsRunning = isRunning,
            RunState = runState,
            Matchability = Matchability.Reliable,
            MatchEvidence = isRunning
                ? L.F("PID {0} · {1}", service.ProcessId, service.ImagePath)
                : L.F("未在可观测进程中发现匹配项（服务状态：{0}）", WindowsServiceManager.StateLabel(service.State)),
            State = state,
            StateDetail = stateDetail,
            ProcessIds = processIds,
            ServiceName = service.Name,
            IsProtected = WindowsServiceManager.IsProtected(service.Name),
            ProtectedReason = WindowsServiceManager.IsProtected(service.Name)
                ? WindowsServiceManager.ProtectedReason(service.Name)
                : null,
            RequiresTypedConfirmation = true,
            ConfirmationPhrase = service.Name
        };
    }

    private static ManagedEntry MapRegistryEntry(RegistryRunEntry run, ProcessSnapshot processes, bool processSnapshotAvailable)
    {
        var parsed = CommandLineParser.Parse(run.Command);
        var matchability = MatchabilityEvaluator.Evaluate(run.Command, parsed.ExecutablePath, parsed.ScriptPath);
        IReadOnlyList<ProcessRecord> matches;
        try
        {
            matches = processes.Match(parsed.ExecutablePath, parsed.ScriptPath);
        }
        catch
        {
            matches = Array.Empty<ProcessRecord>();
            processSnapshotAvailable = false;
        }

        var classification = ClassifyProcessEntry(
            run.IsEnabled,
            matchability,
            processSnapshotAvailable,
            matches,
            run.Command);

        var isPolicy = run.KeyPath.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase);
        var sourceLabel = isPolicy
            ? L.T("策略启动项")
            : run.IsRunOnce
                ? L.T("注册表 RunOnce")
                : L.T("注册表 Run");

        var supportsToggle = !isPolicy && !run.IsRunOnce;
        return new ManagedEntry
        {
            Id = $"run:{run.Hive}:{run.KeyPath}:{run.ValueName}",
            Name = string.IsNullOrWhiteSpace(run.ValueName) ? L.T("(默认值)") : run.ValueName,
            Kind = EntryKind.RegistryRun,
            Description = run.IsRunOnce ? L.T("下次登录时运行一次") : L.T("登录时自动运行"),
            CommandLine = run.Command,
            ExecutablePath = parsed.ExecutablePath,
            Arguments = parsed.Arguments,
            ScriptPath = parsed.ScriptPath,
            SourcePath = $@"{StartupRegistryManager.FriendlyHive(run.Hive)}\{run.KeyPath}",
            SourceLabel = sourceLabel,
            ScopeLabel = run.IsCurrentUser ? L.T("当前用户") : L.T("所有用户"),
            TriggerLabel = run.IsRunOnce ? L.T("下次登录一次") : L.T("用户登录时"),
            StartupModeLabel = run.IsRunOnce ? L.T("注册表 RunOnce") : L.T("注册表 Run"),
            StartupTypeCode = isPolicy ? "policy" : run.IsRunOnce ? "runonce" : "run",
            ScopeCode = run.IsCurrentUser ? "current-user" : "all-users",
            RequiresAdmin = !run.IsCurrentUser,
            IsEnabled = run.IsEnabled,
            IsRunning = classification.RunState == EntryRunState.Running,
            RunState = classification.RunState,
            Matchability = matchability,
            MatchEvidence = classification.Evidence,
            State = classification.State,
            StateDetail = classification.Evidence,
            ProcessIds = classification.RunState == EntryRunState.Running
                ? matches.Select(m => m.Id).ToList()
                : Array.Empty<int>(),
            RegistryHive = run.Hive,
            RegistryKeyPath = run.KeyPath,
            RegistryValueName = run.ValueName,
            SupportsEnableToggle = supportsToggle,
            EnableToggleBlockedReason = isPolicy
                ? L.T("策略启动项由组策略控制，不能在此禁用。")
                : run.IsRunOnce
                    ? L.T("RunOnce 项目会在下次登录后自动移除，不支持禁用。")
                    : null
        };
    }

    private static ManagedEntry MapStartupFolderEntry(StartupFolderEntry entry, ProcessSnapshot processes, bool processSnapshotAvailable)
    {
        var parsed = CommandLineParser.Parse(entry.CommandLine);
        var matchability = MatchabilityEvaluator.Evaluate(
            entry.CommandLine,
            entry.TargetPath ?? parsed.ExecutablePath,
            entry.ScriptPath ?? parsed.ScriptPath);

        IReadOnlyList<ProcessRecord> matches;
        try
        {
            matches = processes.Match(entry.TargetPath, entry.ScriptPath);
        }
        catch
        {
            matches = Array.Empty<ProcessRecord>();
            processSnapshotAvailable = false;
        }

        var classification = ClassifyProcessEntry(
            entry.IsEnabled,
            matchability,
            processSnapshotAvailable,
            matches,
            entry.CommandLine);

        return new ManagedEntry
        {
            Id = $"startup:{entry.FilePath}",
            Name = entry.Name,
            Kind = EntryKind.StartupFolder,
            Description = entry.IsCommon ? L.T("所有用户登录时自动运行") : L.T("当前用户登录时自动运行"),
            CommandLine = entry.CommandLine,
            ExecutablePath = entry.TargetPath ?? parsed.ExecutablePath,
            Arguments = entry.Arguments ?? parsed.Arguments,
            ScriptPath = entry.ScriptPath ?? parsed.ScriptPath,
            SourcePath = entry.FilePath,
            SourceLabel = L.T("启动文件夹"),
            ScopeLabel = entry.IsCommon ? L.T("所有用户") : L.T("当前用户"),
            TriggerLabel = entry.IsCommon ? L.T("所有用户登录时") : L.T("用户登录时"),
            StartupModeLabel = L.T("启动文件夹"),
            StartupTypeCode = "startup-folder",
            ScopeCode = entry.IsCommon ? "all-users" : "current-user",
            RequiresAdmin = entry.IsCommon,
            IsEnabled = entry.IsEnabled,
            IsRunning = classification.RunState == EntryRunState.Running,
            RunState = classification.RunState,
            Matchability = matchability,
            MatchEvidence = classification.Evidence,
            State = classification.State,
            StateDetail = classification.Evidence,
            ProcessIds = classification.RunState == EntryRunState.Running
                ? matches.Select(m => m.Id).ToList()
                : Array.Empty<int>(),
            FilePath = entry.FilePath
        };
    }

    private static ManagedEntry MapTask(ScheduledTaskInfo task, ProcessSnapshot processes)
    {
        var parsed = CommandLineParser.Parse(task.CommandLine);
        var matches = processes.Match(parsed.ExecutablePath, parsed.ScriptPath);
        var isRunning = task.State == 4 || matches.Count > 0;

        var state = task.State == 1 || !task.IsEnabled
            ? EntryRuntimeState.Disabled
            : isRunning
                ? EntryRuntimeState.Running
                : EntryRuntimeState.Stopped;

        var stateDetail = L.F("任务状态：{0}", ScheduledTaskManager.StateLabel(task.State));
        var evidence = isRunning && matches.Count > 0
            ? L.F("PID {0} · {1}", string.Join(", ", matches.Select(m => m.Id)), task.CommandLine)
            : L.F("未在可观测进程中发现匹配项（任务状态：{0}）", ScheduledTaskManager.StateLabel(task.State));

        var systemTask = task.FullPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
        var description = string.IsNullOrWhiteSpace(task.Description)
            ? L.F("{0}触发的计划任务", task.TriggerLabel)
            : task.Description;

        return new ManagedEntry
        {
            Id = $"task:{task.FullPath}",
            Name = task.Name,
            Kind = EntryKind.ScheduledTask,
            Description = description,
            CommandLine = string.IsNullOrWhiteSpace(task.CommandLine)
                ? L.T("COM 处理程序（无命令行）")
                : task.CommandLine,
            ExecutablePath = parsed.ExecutablePath,
            Arguments = parsed.Arguments,
            ScriptPath = parsed.ScriptPath,
            SourcePath = task.FullPath,
            SourceLabel = L.IsEnglish ? "Scheduled task" : "计划任务",
            ScopeLabel = task.RequiresAdmin ? L.T("系统级") : L.T("当前用户"),
            TriggerLabel = task.TriggerLabel,
            StartupModeLabel = L.T("任务计划程序"),
            StartupTypeCode = "scheduled-task",
            ScopeCode = task.RequiresAdmin ? "system" : "current-user",
            RequiresAdmin = task.RequiresAdmin,
            IsEnabled = task.IsEnabled,
            IsRunning = isRunning,
            RunState = isRunning ? EntryRunState.Running : EntryRunState.NotRunning,
            Matchability = Matchability.Reliable,
            MatchEvidence = evidence,
            State = state,
            StateDetail = stateDetail,
            ProcessIds = matches.Select(m => m.Id).ToList(),
            TaskPath = task.FullPath,
            RequiresTypedConfirmation = systemTask,
            ConfirmationPhrase = systemTask ? task.Name : null
        };
    }

    private static (EntryRunState RunState, EntryRuntimeState State, string Evidence) ClassifyProcessEntry(
        bool enabled,
        Matchability matchability,
        bool processSnapshotAvailable,
        IReadOnlyList<ProcessRecord> matches,
        string commandLine)
    {
        var reliable = matchability == Matchability.Reliable && processSnapshotAvailable;
        var runState = reliable
            ? matches.Count > 0 ? EntryRunState.Running : EntryRunState.NotRunning
            : EntryRunState.Indeterminate;

        var state = !enabled
            ? EntryRuntimeState.Disabled
            : runState switch
            {
                EntryRunState.Running => EntryRuntimeState.Running,
                EntryRunState.NotRunning => EntryRuntimeState.Stopped,
                _ => EntryRuntimeState.Unknown
            };

        var evidence = runState switch
        {
            EntryRunState.Running => L.F(
                "PID {0} · {1}",
                string.Join(", ", matches.Select(m => m.Id)),
                commandLine),
            EntryRunState.NotRunning => L.T("未在可观测进程中发现匹配项"),
            _ => L.T("由启动器或脚本间接拉起，无法可靠匹配；可能正在运行")
        };

        return (runState, state, evidence);
    }

    private static int KindRank(EntryKind kind) => kind switch
    {
        EntryKind.WindowsService => 0,
        EntryKind.RegistryRun => 1,
        EntryKind.StartupFolder => 2,
        EntryKind.ScheduledTask => 3,
        _ => 4
    };
}
