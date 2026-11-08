using System.Diagnostics;
using System.Management;
using StartupManager.Interop;
using StartupManager.Models;

namespace StartupManager.Services;

public sealed record ProcessRecord(int Id, string Name, string? Path, string? CommandLine);

public sealed class ProcessSnapshot
{
    public static ProcessSnapshot Empty { get; } = new(Array.Empty<ProcessRecord>());

    public ProcessSnapshot(IReadOnlyList<ProcessRecord> processes)
    {
        Processes = processes;
    }

    public IReadOnlyList<ProcessRecord> Processes { get; }

    public IReadOnlyList<ProcessRecord> Match(ManagedEntry entry)
        => Match(entry.ExecutablePath, entry.ScriptPath);

    public IReadOnlyList<ProcessRecord> Match(string? executablePath, string? scriptPath)
    {
        var matches = new Dictionary<int, ProcessRecord>();

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            var script = CommandLineParser.NormalizePath(scriptPath);
            if (script.Length > 0)
            {
                foreach (var process in Processes)
                {
                    if (process.CommandLine is { Length: > 0 } commandLine
                        && commandLine.Contains(script, StringComparison.OrdinalIgnoreCase))
                    {
                        matches[process.Id] = process;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var executable = CommandLineParser.NormalizePath(executablePath);
            if (executable.Length > 0)
            {
                foreach (var process in Processes)
                {
                    if (PathsEqual(process.Path, executable))
                    {
                        matches[process.Id] = process;
                    }
                }

                // Store/aliased apps (for example Windows 11 Notepad) often execute
                // from a different package path than the one configured in Run.
                // Fall back to the image name for non-system hosts, but never for
                // generic hosts such as svchost.exe.
                if (matches.Count == 0 && !CommandLineParser.IsGenericHost(executable))
                {
                    var imageName = Path.GetFileNameWithoutExtension(executable);
                    if (imageName.Length >= 3)
                    {
                    foreach (var process in Processes)
                    {
                        if (string.Equals(
                                Path.GetFileNameWithoutExtension(process.Name),
                                imageName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            matches[process.Id] = process;
                        }
                    }
                    }
                }
            }
        }

        return matches.Values.OrderBy(p => p.Id).ToList();
    }

    public ProcessRecord? FindByProcessId(int processId)
        => Processes.FirstOrDefault(p => p.Id == processId);

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        var a = CommandLineParser.NormalizePath(left);
        var b = CommandLineParser.NormalizePath(right);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

public static class ProcessProbe
{
    public static ProcessSnapshot Capture()
    {
        var processes = CaptureWithWmi();
        if (processes.Count == 0)
        {
            processes = CaptureWithProcessApi();
        }

        return new ProcessSnapshot(processes);
    }

    private static List<ProcessRecord> CaptureWithWmi()
    {
        var list = new List<ProcessRecord>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                try
                {
                    var pid = Convert.ToInt32(item["ProcessId"]);
                    if (pid <= 0) continue;

                    var name = item["Name"]?.ToString() ?? string.Empty;
                    var path = item["ExecutablePath"]?.ToString();
                    var commandLine = item["CommandLine"]?.ToString();
                    list.Add(new ProcessRecord(pid, name, NullIfEmpty(path), NullIfEmpty(commandLine)));
                }
                catch
                {
                    // Protected processes can deny individual property reads.
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            return new List<ProcessRecord>();
        }

        return list;
    }

    private static List<ProcessRecord> CaptureWithProcessApi()
    {
        var list = new List<ProcessRecord>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var path = NativeMethods.TryGetProcessPath(process.Id);
                    list.Add(new ProcessRecord(process.Id, process.ProcessName, path, null));
                }
                catch
                {
                    // Ignore processes that disappear or deny access.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Return whatever was collected.
        }

        return list;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
