using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace StartupManager.Services;

public sealed record StartupFolderEntry(
    string FilePath,
    string Name,
    string CommandLine,
    string? TargetPath,
    string? Arguments,
    string? ScriptPath,
    bool IsEnabled,
    bool IsCommon);

public static class StartupFolderManager
{
    public static IReadOnlyList<StartupFolderEntry> Enumerate()
    {
        var list = new List<StartupFolderEntry>();
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), isCommon: false);
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), isCommon: true);
        return list;
    }

    public static string UserStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static void DeleteFile(string filePath, bool isCommon)
    {
        if (!File.Exists(filePath)) return;
        File.Delete(filePath);
        RemoveApprovedState(Path.GetFileName(filePath), isCommon);
    }

    public static void SetFileEnabled(string filePath, bool isCommon, bool enabled)
    {
        var fileName = Path.GetFileName(filePath);
        var baseKey = isCommon ? Registry.LocalMachine : Registry.CurrentUser;
        using var approved = baseKey.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder")
            ?? throw new UnauthorizedAccessException(
                "无法写入 StartupApproved 注册表键，请以管理员身份运行。");

        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }

        approved.SetValue(fileName, data, RegistryValueKind.Binary);
    }

    private static void AddFolder(List<StartupFolderEntry> list, string folder, bool isCommon)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder);
        }
        catch
        {
            return;
        }

        var shell = TryCreateShell();
        try
        {
            foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                var command = file;
                string? target = null;
                string? arguments = null;
                string? scriptPath = null;

                try
                {
                    if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        && shell is not null)
                    {
                        var shortcut = shell.CreateShortcut(file);
                        try
                        {
                            target = CommandLineParser.ExpandQuietly((string)shortcut.TargetPath);
                            arguments = (string)shortcut.Arguments;
                            if (!string.IsNullOrWhiteSpace(target))
                            {
                                command = CommandLineParser.BuildCommandLine(target, arguments ?? string.Empty);
                                scriptPath = CommandLineParser.TryExtractScriptPath(arguments, target);
                                var title = Path.GetFileNameWithoutExtension(
                                    string.IsNullOrWhiteSpace((string)shortcut.Description)
                                        ? fileName
                                        : (string)shortcut.Description);
                                if (!string.IsNullOrWhiteSpace(title)) name = title;
                            }
                        }
                        finally
                        {
                            ReleaseCom(shortcut);
                        }
                    }
                    else if (Path.GetExtension(file).Equals(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        var url = File.ReadLines(file)
                            .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            command = url[4..].Trim();
                        }
                    }
                    else
                    {
                        command = CommandLineParser.BuildCommandLine(file, string.Empty, quoteExecutable: true);
                        if (CommandLineParser.LooksLikeScript(file))
                        {
                            scriptPath = file;
                        }
                        else if (Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            target = file;
                        }
                    }
                }
                catch
                {
                    // Keep the file in the list even if its shortcut cannot be resolved.
                }

                list.Add(new StartupFolderEntry(
                    file,
                    name,
                    command,
                    NullIfEmpty(target),
                    NullIfEmpty(arguments),
                    NullIfEmpty(scriptPath),
                    IsEnabled(fileName, isCommon),
                    isCommon));
            }
        }
        finally
        {
            ReleaseCom(shell);
        }
    }

    private static bool IsEnabled(string fileName, bool isCommon)
    {
        var hiveOrder = isCommon
            ? new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }
            : new[] { RegistryHive.CurrentUser };

        foreach (var hive in hiveOrder)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
                if (key?.GetValue(fileName) is byte[] bytes && bytes.Length > 0)
                {
                    return bytes[0] != 0x03 && bytes[0] != 0x07;
                }
            }
            catch
            {
                // Ignore approval lookup failures.
            }
        }

        return true;
    }

    private static void RemoveApprovedState(string fileName, bool isCommon)
    {
        var hives = isCommon
            ? new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }
            : new[] { RegistryHive.CurrentUser };

        foreach (var hive in hives)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
                    writable: true);
                key?.DeleteValue(fileName, throwOnMissingValue: false);
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private static dynamic? TryCreateShell()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            return type is null ? null : Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // Ignore release failures.
            }
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
