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
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using StartupManager.Localization;

namespace StartupManager.Services;

public sealed record ScheduledTaskInfo(
    string Name,
    string FullPath,
    string FolderPath,
    string Description,
    string TriggerLabel,
    string? ActionPath,
    string? Arguments,
    string? WorkingDirectory,
    bool IsEnabled,
    int State,
    string? UserId,
    string? Author,
    bool RequiresAdmin,
    string CommandLine);

public static class ScheduledTaskManager
{
    private const int TaskTriggerBoot = 8;
    private const int TaskTriggerLogon = 9;
    private const int TaskStateRunning = 4;

    public static IReadOnlyList<ScheduledTaskInfo> Enumerate()
    {
        var results = new List<ScheduledTaskInfo>();
        dynamic? service = null;
        try
        {
            service = Connect();
            var root = service.GetFolder("\\");
            try
            {
                WalkFolder(root, results);
            }
            finally
            {
                ReleaseCom(root);
            }
        }
        finally
        {
            ReleaseCom(service);
        }

        return results
            .OrderBy(t => t.FullPath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void RunTask(string fullPath)
    {
        WithTask(fullPath, task =>
        {
            task.Run(null);
        });
    }

    public static void StopTask(string fullPath)
    {
        WithTask(fullPath, task =>
        {
            if ((int)task.State != TaskStateRunning) return;
            task.Stop(null);
            WaitForTaskState(task, false, TimeSpan.FromSeconds(12));
        });
    }

    public static void RestartTask(string fullPath)
    {
        WithTask(fullPath, task =>
        {
            if ((int)task.State == TaskStateRunning)
            {
                try
                {
                    task.Stop(null);
                    WaitForTaskState(task, false, TimeSpan.FromSeconds(12));
                }
                catch
                {
                    // Some tasks cannot be stopped; still attempt a fresh run.
                }
            }

            task.Run(null);
        });
    }

    public static void DeleteTask(string fullPath)
    {
        dynamic? service = null;
        try
        {
            service = Connect();
            var (folderPath, name) = SplitTaskPath(fullPath);
            var folder = service.GetFolder(folderPath);
            try
            {
                folder.DeleteTask(name, 0);
            }
            finally
            {
                ReleaseCom(folder);
            }
        }
        finally
        {
            ReleaseCom(service);
        }
    }

    public static void SetTaskEnabled(string fullPath, bool enabled)
    {
        WithTask(fullPath, task =>
        {
            task.Enabled = enabled;
        });
    }

    public static bool TaskExists(string fullPath)
    {
        dynamic? service = null;
        try
        {
            service = Connect();
            var (folderPath, name) = SplitTaskPath(fullPath);
            var folder = service.GetFolder(folderPath);
            try
            {
                var task = folder.GetTask(name);
                ReleaseCom(task);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseCom(folder);
            }
        }
        finally
        {
            ReleaseCom(service);
        }
    }

    public static string CreateBootTask(
        string displayName,
        string actionPath,
        string arguments,
        string workingDirectory,
        string description)
    {
        var safeName = SanitizeTaskName(displayName);
        EnsureTaskFolder();
        var fullPath = $@"\StartupManager\{safeName}";
        var suffix = 2;
        while (TaskExists(fullPath))
        {
            fullPath = $@"\StartupManager\{safeName} ({suffix++})";
        }

        var xml = BuildBootTaskXml(actionPath, arguments, workingDirectory, description);
        var temp = Path.Combine(Path.GetTempPath(), $"startupmanager-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(temp, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/Create");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(fullPath);
            startInfo.ArgumentList.Add("/XML");
            startInfo.ArgumentList.Add(temp);
            startInfo.ArgumentList.Add("/F");

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法启动 schtasks.exe。");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("创建计划任务超时。");
            }

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException(
                    $"创建系统启动任务失败（退出码 {process.ExitCode}）。{detail.Trim()}".Trim());
            }
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }

        return fullPath;
    }

    public static string TriggerLabel(int boot, int logon)
    {
        var parts = new List<string>();
        if (boot > 0) parts.Add(L.T("开机时"));
        if (logon > 0) parts.Add(L.T("登录时"));
        return parts.Count == 0 ? L.T("计划任务") : string.Join(" / ", parts);
    }

    public static string StateLabel(int state) => state switch
    {
        0 => L.T("未知"),
        1 => L.T("已禁用"),
        2 => L.T("排队中"),
        3 => L.T("就绪"),
        4 => L.T("运行中"),
        _ => L.T("未知")
    };

    private static void WalkFolder(dynamic folder, List<ScheduledTaskInfo> results)
    {
        dynamic? tasks = null;
        try
        {
            tasks = folder.GetTasks(1);
            var count = (int)tasks.Count;
            for (var i = 1; i <= count; i++)
            {
                dynamic? task = null;
                try
                {
                    task = tasks.Item(i);
                    var info = ReadTask(task);
                    if (info is not null) results.Add(info);
                }
                catch
                {
                    // Ignore tasks that cannot be read.
                }
                finally
                {
                    ReleaseCom(task);
                }
            }
        }
        finally
        {
            ReleaseCom(tasks);
        }

        dynamic? folders = null;
        try
        {
            folders = folder.GetFolders(0);
            var count = (int)folders.Count;
            for (var i = 1; i <= count; i++)
            {
                dynamic? child = null;
                try
                {
                    child = folders.Item(i);
                    WalkFolder(child, results);
                }
                catch
                {
                    // Ignore inaccessible subfolders.
                }
                finally
                {
                    ReleaseCom(child);
                }
            }
        }
        finally
        {
            ReleaseCom(folders);
        }
    }

    private static ScheduledTaskInfo? ReadTask(dynamic task)
    {
        var name = (string)task.Name;
        var fullPath = (string)task.Path;
        var enabled = (bool)task.Enabled;
        var state = (int)task.State;

        dynamic definition = task.Definition;
        dynamic triggers = definition.Triggers;
        var boot = 0;
        var logon = 0;
        try
        {
            var triggerCount = (int)triggers.Count;
            for (var i = 1; i <= triggerCount; i++)
            {
                dynamic? trigger = null;
                try
                {
                    trigger = triggers.Item(i);
                    var type = (int)trigger.Type;
                    if (type == TaskTriggerBoot) boot++;
                    if (type == TaskTriggerLogon) logon++;
                }
                finally
                {
                    ReleaseCom(trigger);
                }
            }
        }
        finally
        {
            ReleaseCom(triggers);
        }

        if (boot == 0 && logon == 0)
        {
            ReleaseCom(definition);
            return null;
        }

        string? actionPath = null;
        string? arguments = null;
        string? workingDirectory = null;
        dynamic actions = definition.Actions;
        try
        {
            var actionCount = (int)actions.Count;
            for (var i = 1; i <= actionCount; i++)
            {
                dynamic? action = null;
                try
                {
                    action = actions.Item(i);
                    if ((int)action.Type != 0) continue;
                    actionPath = (string?)action.Path;
                    arguments = (string?)action.Arguments;
                    workingDirectory = (string?)action.WorkingDirectory;
                    break;
                }
                finally
                {
                    ReleaseCom(action);
                }
            }
        }
        finally
        {
            ReleaseCom(actions);
        }

        string? userId = null;
        string? author = null;
        string description = string.Empty;
        try
        {
            userId = (string?)definition.Principal.UserId;
            author = (string?)definition.RegistrationInfo.Author;
            description = (string?)definition.RegistrationInfo.Description ?? string.Empty;
        }
        catch
        {
            // Registration metadata is optional.
        }

        ReleaseCom(definition);

        var command = string.IsNullOrWhiteSpace(actionPath)
            ? string.Empty
            : CommandLineParser.BuildCommandLine(
                CommandLineParser.ExpandQuietly(actionPath!),
                arguments ?? string.Empty);

        var folderPath = fullPath.Contains('\\')
            ? fullPath[..fullPath.LastIndexOf('\\')]
            : "\\";
        if (string.IsNullOrWhiteSpace(folderPath)) folderPath = "\\";

        var systemPrincipal = userId is not null
                              && (userId.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase)
                                  || userId.Contains("S-1-5-18", StringComparison.OrdinalIgnoreCase)
                                  || userId.Contains("S-1-5-19", StringComparison.OrdinalIgnoreCase)
                                  || userId.Contains("S-1-5-20", StringComparison.OrdinalIgnoreCase));
        var systemFolder = fullPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

        return new ScheduledTaskInfo(
            name,
            fullPath,
            folderPath,
            description.Trim(),
            TriggerLabel(boot, logon),
            NullIfEmpty(actionPath),
            NullIfEmpty(arguments),
            NullIfEmpty(workingDirectory),
            enabled && state != 1,
            state,
            NullIfEmpty(userId),
            NullIfEmpty(author),
            systemPrincipal || systemFolder,
            command);
    }

    private static void WithTask(string fullPath, Action<dynamic> action)
    {
        dynamic? service = null;
        try
        {
            service = Connect();
            var (folderPath, name) = SplitTaskPath(fullPath);
            var folder = service.GetFolder(folderPath);
            dynamic? task = null;
            try
            {
                task = folder.GetTask(name);
                action(task);
            }
            finally
            {
                ReleaseCom(task);
                ReleaseCom(folder);
            }
        }
        finally
        {
            ReleaseCom(service);
        }
    }

    private static void WaitForTaskState(dynamic task, bool running, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = (int)task.State;
            if (running ? state == TaskStateRunning : state != TaskStateRunning) return;
            Thread.Sleep(200);
        }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
                   ?? throw new InvalidOperationException("当前系统未提供任务计划程序 COM 接口。");
        dynamic service = Activator.CreateInstance(type)
                          ?? throw new InvalidOperationException("无法创建任务计划程序 COM 对象。");
        service.Connect();
        return service;
    }

    private static (string FolderPath, string Name) SplitTaskPath(string fullPath)
    {
        var trimmed = fullPath.Trim();
        if (!trimmed.StartsWith('\\')) trimmed = "\\" + trimmed;
        var last = trimmed.LastIndexOf('\\');
        if (last <= 0) return ("\\", trimmed.TrimStart('\\'));
        return (trimmed[..last], trimmed[(last + 1)..]);
    }

    private static void EnsureTaskFolder()
    {
        dynamic? service = null;
        dynamic? folder = null;
        try
        {
            service = Connect();
            var root = service.GetFolder("\\");
            try
            {
                try
                {
                    folder = root.GetFolder(@"\StartupManager");
                }
                catch
                {
                    folder = root.CreateFolder("StartupManager", null);
                }
            }
            finally
            {
                ReleaseCom(root);
            }
        }
        finally
        {
            ReleaseCom(folder);
            ReleaseCom(service);
        }
    }

    private static string BuildBootTaskXml(
        string actionPath,
        string arguments,
        string workingDirectory,
        string description)
    {
        var escapedDescription = SecurityElement.Escape(description) ?? string.Empty;
        var escapedPath = SecurityElement.Escape(actionPath) ?? actionPath;
        var escapedArguments = SecurityElement.Escape(arguments) ?? string.Empty;
        var escapedWorkingDirectory = SecurityElement.Escape(workingDirectory) ?? string.Empty;
        var author = SecurityElement.Escape(Environment.UserName) ?? "StartupManager";

        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>{author}</Author>
    <Description>{escapedDescription}</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <LogonType>ServiceAccount</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{escapedPath}</Command>
      <Arguments>{escapedArguments}</Arguments>
      <WorkingDirectory>{escapedWorkingDirectory}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
""";
    }

    private static string SanitizeTaskName(string value)
    {
        var invalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var builder = new StringBuilder(value.Trim());
        foreach (var ch in invalid)
        {
            builder.Replace(ch, '_');
        }

        var result = builder.ToString().Trim();
        if (result.Length == 0) result = "Startup Item";
        if (result.Length > 80) result = result[..80];
        return result;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Ignore COM release failures.
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
