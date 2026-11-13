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
using System.Diagnostics;
using System.Text.Json;
using StartupManager.Localization;
using StartupManager.Models;

namespace StartupManager.Services;

public sealed class StartupActionService
{
    public ProcessSnapshot GetProcessSnapshot() => ProcessProbe.Capture();

    public IReadOnlyList<ProcessRecord> FindRunningProcesses(ManagedEntry entry)
    {
        if (entry.Kind == EntryKind.WindowsService) return Array.Empty<ProcessRecord>();
        return ProcessProbe.Capture().Match(entry.ExecutablePath, entry.ScriptPath);
    }

    public string Terminate(ManagedEntry entry)
    {
        switch (entry.Kind)
        {
            case EntryKind.WindowsService:
                RequireAdmin(entry);
                WindowsServiceManager.StopService(entry.ServiceName!);
                Audit(entry, "STOP SERVICE");
                return L.F("已停止服务“{0}”。", entry.Name);

            case EntryKind.ScheduledTask:
                RequireAdminIfNeeded(entry);
                ScheduledTaskManager.StopTask(entry.TaskPath!);
                Audit(entry, "STOP TASK");
                return L.F("已结束计划任务“{0}”。", entry.Name);

            case EntryKind.RegistryRun:
            case EntryKind.StartupFolder:
            {
                var matches = FindRunningProcesses(entry);
                if (matches.Count == 0)
                {
                    throw new InvalidOperationException(L.T("没有发现正在运行的关联进程，可能它已经停止。"));
                }

                var killed = KillProcesses(matches);
                Audit(entry, $"KILL PROCESS x{killed}");
                return L.F("已结束 {0} 个关联进程。", killed);
            }

            default:
                throw new NotSupportedException(L.T("不支持的操作类型。"));
        }
    }

    public string Start(ManagedEntry entry)
    {
        if (!entry.IsEnabled)
        {
            throw new InvalidOperationException(L.T("该项目当前已禁用，请先启用后再启动。"));
        }

        switch (entry.Kind)
        {
            case EntryKind.WindowsService:
                RequireAdmin(entry);
                WindowsServiceManager.StartService(entry.ServiceName!);
                Audit(entry, "START SERVICE");
                return L.F("服务“{0}”已启动。", entry.Name);

            case EntryKind.ScheduledTask:
                RequireAdminIfNeeded(entry);
                ScheduledTaskManager.RunTask(entry.TaskPath!);
                Audit(entry, "RUN TASK");
                return L.F("已触发计划任务“{0}”。", entry.Name);

            case EntryKind.RegistryRun:
            case EntryKind.StartupFolder:
                Launch(entry);
                Audit(entry, "LAUNCH");
                return L.F("已启动“{0}”。", entry.Name);

            default:
                throw new NotSupportedException(L.T("不支持的操作类型。"));
        }
    }

    public string Restart(ManagedEntry entry)
    {
        switch (entry.Kind)
        {
            case EntryKind.WindowsService:
                RequireAdmin(entry);
                WindowsServiceManager.RestartService(entry.ServiceName!);
                Audit(entry, "RESTART SERVICE");
                return L.F("服务“{0}”已重启。", entry.Name);

            case EntryKind.ScheduledTask:
                RequireAdminIfNeeded(entry);
                ScheduledTaskManager.RestartTask(entry.TaskPath!);
                Audit(entry, "RESTART TASK");
                return L.F("计划任务“{0}”已重新触发。", entry.Name);

            case EntryKind.RegistryRun:
            case EntryKind.StartupFolder:
            {
                if (!entry.IsEnabled)
                {
                    throw new InvalidOperationException(L.T("该项目当前已禁用，请先启用后再重启。"));
                }

                var matches = FindRunningProcesses(entry);
                var killed = matches.Count > 0 ? KillProcesses(matches) : 0;
                Thread.Sleep(350);
                Launch(entry);
                Audit(entry, $"RESTART (killed {killed})");
                return killed > 0
                    ? L.F("已结束 {0} 个进程并重新启动“{1}”。", killed, entry.Name)
                    : L.F("“{0}”之前未在运行，已直接启动。", entry.Name);
            }

            default:
                throw new NotSupportedException(L.T("不支持的操作类型。"));
        }
    }

    public string SetEnabled(ManagedEntry entry, bool enabled)
    {
        if (!entry.CanToggleEnabled)
        {
            throw new InvalidOperationException(entry.ToggleEnabledToolTip);
        }

        if (!enabled && entry.IsProtected)
        {
            throw new InvalidOperationException(
                entry.ProtectedReason ?? L.T("该启动项受到保护，不能禁用。"));
        }

        if (entry.IsEnabled == enabled)
        {
            return enabled ? L.T("该项目已经处于启用状态。") : L.T("该项目已经处于禁用状态。");
        }

        switch (entry.Kind)
        {
            case EntryKind.WindowsService:
            {
                RequireAdmin(entry);
                var (startType, usedFallback) = WindowsServiceManager.SetEnabled(entry.ServiceName!, enabled);
                Audit(entry, enabled ? "ENABLE SERVICE" : "DISABLE SERVICE");

                if (!enabled)
                {
                    return L.F("已禁用服务“{0}”的自动启动。当前运行中的进程不会被结束。", entry.Name);
                }

                var mode = WindowsServiceManager.StartModeLabel(startType, delayed: false);
                var fallbackNote = usedFallback
                    ? L.T("（未找到原启动类型，已恢复为手动启动）")
                    : string.Empty;
                return L.F("已启用服务“{0}”，启动类型：{1}{2}。", entry.Name, mode, fallbackNote);
            }

            case EntryKind.ScheduledTask:
                RequireAdminIfNeeded(entry);
                ScheduledTaskManager.SetTaskEnabled(entry.TaskPath!, enabled);
                Audit(entry, enabled ? "ENABLE TASK" : "DISABLE TASK");
                return enabled
                    ? L.F("已启用计划任务“{0}”。", entry.Name)
                    : L.F("已禁用计划任务“{0}”的触发器。", entry.Name);

            case EntryKind.RegistryRun:
                RequireAdminIfNeeded(entry);
                StartupRegistryManager.SetRunEnabled(
                    entry.RegistryHive!, entry.RegistryKeyPath!, entry.RegistryValueName!, enabled);
                Audit(entry, enabled ? "ENABLE RUN" : "DISABLE RUN");
                return enabled
                    ? L.F("已启用注册表启动项“{0}”。", entry.Name)
                    : L.F("已禁用注册表启动项“{0}”，下次登录不再自动运行。", entry.Name);

            case EntryKind.StartupFolder:
                RequireAdminIfNeeded(entry);
                if (!File.Exists(entry.FilePath))
                {
                    throw new FileNotFoundException(L.T("启动文件不存在，可能已被移动或删除。"), entry.FilePath);
                }

                StartupFolderManager.SetFileEnabled(entry.FilePath!, entry.RequiresAdmin, enabled);
                Audit(entry, enabled ? "ENABLE STARTUP FILE" : "DISABLE STARTUP FILE");
                return enabled
                    ? L.F("已启用启动文件夹项目“{0}”。", entry.Name)
                    : L.F("已禁用启动文件夹项目“{0}”，下次登录不再自动运行。", entry.Name);

            default:
                throw new NotSupportedException(L.T("不支持的操作类型。"));
        }
    }

    public string Delete(ManagedEntry entry)
    {
        if (entry.IsProtected && SystemServiceProtection.RefuseDeletingSystemServices)
        {
            throw new InvalidOperationException(entry.ProtectedReason ?? L.T("该项目受到保护，不能删除。"));
        }

        RequireAdminIfNeeded(entry);
        WriteDeletionBackup(entry);

        switch (entry.Kind)
        {
            case EntryKind.WindowsService:
                WindowsServiceManager.DeleteService(entry.ServiceName!);
                ServiceStartTypeStore.Forget(entry.ServiceName!);
                Audit(entry, "DELETE SERVICE");
                return L.F("服务“{0}”已删除。该服务将从下次启动时消失。", entry.Name);

            case EntryKind.RegistryRun:
                StartupRegistryManager.DeleteRunEntry(
                    entry.RegistryHive!, entry.RegistryKeyPath!, entry.RegistryValueName!);
                CleanupGeneratedScript(entry);
                Audit(entry, "DELETE REGISTRY RUN");
                return L.F("已删除注册表启动项“{0}”。", entry.Name);

            case EntryKind.StartupFolder:
                StartupFolderManager.DeleteFile(entry.FilePath!, entry.RequiresAdmin);
                Audit(entry, "DELETE STARTUP FILE");
                return L.F("已删除启动文件“{0}”。", entry.Name);

            case EntryKind.ScheduledTask:
                ScheduledTaskManager.DeleteTask(entry.TaskPath!);
                CleanupGeneratedScript(entry);
                Audit(entry, "DELETE TASK");
                return L.F("已删除计划任务“{0}”。", entry.Name);

            default:
                throw new NotSupportedException(L.T("不支持的操作类型。"));
        }
    }

    private static void Launch(ManagedEntry entry)
    {
        if (entry.Kind == EntryKind.StartupFolder
            && !string.IsNullOrWhiteSpace(entry.FilePath)
            && File.Exists(entry.FilePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.FilePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(entry.FilePath) ?? AppContext.BaseDirectory
            });
            return;
        }

        var parsed = CommandLineParser.Parse(entry.CommandLine);
        var executable = parsed.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(L.T("无法从该启动项中解析出可执行文件。"));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = parsed.Arguments,
            UseShellExecute = true
        };

        var directory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            startInfo.WorkingDirectory = directory;
        }

        Process.Start(startInfo);
    }

    private static int KillProcesses(IReadOnlyList<ProcessRecord> matches)
    {
        var killed = 0;
        var errors = new List<string>();

        foreach (var match in matches.DistinctBy(m => m.Id))
        {
            if (match.Id == Environment.ProcessId) continue;

            try
            {
                using var process = Process.GetProcessById(match.Id);
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    errors.Add(L.F("PID {0} 未在 5 秒内退出", match.Id));
                }
                else
                {
                    killed++;
                }
            }
            catch (ArgumentException)
            {
                // The process already exited.
            }
            catch (Exception ex)
            {
                errors.Add(L.F("PID {0}：{1}", match.Id, ex.Message));
            }
        }

        if (killed == 0 && errors.Count > 0)
        {
            throw new InvalidOperationException(
                L.F("无法结束关联进程。{0}", string.Join(L.T("；"), errors.Take(3))));
        }

        return killed;
    }

    private static void RequireAdminIfNeeded(ManagedEntry entry)
    {
        if (entry.RequiresAdmin)
        {
            RequireAdmin(entry);
        }
    }

    private static void RequireAdmin(ManagedEntry entry)
    {
        if (!AdminService.IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                L.F("“{0}”需要管理员权限。请点击左下角的“以管理员身份重启”，然后再试一次。", entry.Name));
        }
    }

    private static void Audit(ManagedEntry entry, string action)
    {
        AuditLog.Write(action, $"{entry.Name} | {entry.KindLabel} | {entry.SourcePath}");
    }

    private static void WriteDeletionBackup(ManagedEntry entry)
    {
        try
        {
            var folder = AppPaths.EnsureDirectory(AppPaths.Backups);
            var file = Path.Combine(folder, $"deleted-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            var payload = new
            {
                DeletedAt = DateTime.Now,
                entry.Name,
                Kind = entry.KindLabel,
                entry.SourcePath,
                entry.CommandLine,
                entry.ScopeLabel,
                entry.ServiceName,
                entry.RegistryHive,
                entry.RegistryKeyPath,
                entry.RegistryValueName,
                entry.FilePath,
                entry.TaskPath
            };
            File.WriteAllText(file, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        catch
        {
            // A backup is helpful but never blocks deletion.
        }
    }

    private static void CleanupGeneratedScript(ManagedEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ScriptPath)) return;

        try
        {
            var scriptsRoot = Path.GetFullPath(AppPaths.Scripts);
            var scriptPath = Path.GetFullPath(entry.ScriptPath);
            if (scriptPath.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
        catch
        {
            // Leave non-generated or locked files alone.
        }
    }
}
