using System.Diagnostics;
using System.Security.Principal;

namespace StartupManager.Services;

public static class AdminService
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RelaunchElevated()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定当前程序路径。");
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        var processName = Path.GetFileNameWithoutExtension(processPath);
        if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, "StartupManager.dll");
            if (!File.Exists(assembly))
            {
                throw new InvalidOperationException("无法确定应用程序集路径。");
            }

            startInfo.FileName = processPath;
            startInfo.Arguments = $"\"{assembly}\"";
        }
        else
        {
            startInfo.FileName = processPath;
        }

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // The user cancelled the UAC prompt.
        }
    }
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartupManager");

    public static string Scripts { get; } = Path.Combine(Root, "Scripts");
    public static string Logs { get; } = Path.Combine(Root, "Logs");
    public static string Backups { get; } = Path.Combine(Root, "Backups");

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

public static class AuditLog
{
    private static readonly object Gate = new();

    public static void Write(string action, string detail)
    {
        try
        {
            var folder = AppPaths.EnsureDirectory(AppPaths.Logs);
            var file = Path.Combine(folder, $"actions-{DateTime.Now:yyyyMM}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{action}\t{detail}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(file, line);
            }
        }
        catch
        {
            // Logging must never break an operation.
        }
    }
}
