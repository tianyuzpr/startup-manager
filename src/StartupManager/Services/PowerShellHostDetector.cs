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
using System.Text.RegularExpressions;
using Microsoft.Win32;
using StartupManager.Localization;

namespace StartupManager.Services;

public sealed record PowerShellHostOption(
    string Path,
    string DisplayName,
    string Version,
    bool IsUserScoped,
    bool IsWindowsPowerShell)
{
    public string SourceLabel => IsUserScoped ? L.T("当前用户安装") : L.T("系统安装");
}

public static class PowerShellHostDetector
{
    private static readonly object Gate = new();
    private static List<PowerShellHostOption>? _cache;

    public static IReadOnlyList<PowerShellHostOption> Detect(bool includeUserScoped)
    {
        lock (Gate)
        {
            _cache ??= DetectAll();
            return _cache
                .Where(host => includeUserScoped || !host.IsUserScoped)
                .ToList();
        }
    }

    public static string GetDefaultPath(bool includeUserScoped)
    {
        var hosts = Detect(includeUserScoped);
        return hosts.FirstOrDefault()?.Path
               ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    public static bool IsAllowedHost(string path, bool includeUserScoped)
        => Detect(includeUserScoped)
            .Any(host => string.Equals(host.Path, path, StringComparison.OrdinalIgnoreCase));

    public static string GetDisplayName(string path, bool includeUserScoped)
        => Detect(includeUserScoped)
               .FirstOrDefault(host => string.Equals(host.Path, path, StringComparison.OrdinalIgnoreCase))
               ?.DisplayName
           ?? path;

    private static List<PowerShellHostOption> DetectAll()
    {
        var candidates = new List<(string Path, bool UserScoped, bool WindowsPowerShell)>();

        // Machine-wide PowerShell Core installations.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var folder in new[] { "7", "7-preview", "6" })
        {
            candidates.Add((
                Path.Combine(programFiles, "PowerShell", folder, "pwsh.exe"),
                false,
                false));
        }

        // PATH entries. A user-scoped alias is only valid for current-user entries.
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim('"'), "pwsh.exe");
                }
                catch
                {
                    continue;
                }

                candidates.Add((candidate, IsUserScopedPath(candidate), false));
            }
        }

        candidates.Add((
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "pwsh.exe"),
            true,
            false));

        // Windows PowerShell is always machine-wide.
        candidates.Add((
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            false,
            true));

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PowerShellHostOption>();

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path)) continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate.Path);
            }
            catch
            {
                continue;
            }

            if (!seenPaths.Add(fullPath)) continue;

            var version = candidate.WindowsPowerShell
                ? GetWindowsPowerShellVersion()
                : GetPowerShellCoreVersion(fullPath);
            var displayVersion = FormatVersion(version, candidate.WindowsPowerShell);
            var displayName = candidate.WindowsPowerShell
                ? $"Windows PowerShell {displayVersion}".Trim()
                : $"PowerShell {displayVersion}".Trim();

            var versionKey = candidate.WindowsPowerShell
                ? $"wps:{displayVersion}"
                : $"core:{displayVersion}";
            if (!seenVersions.Add(versionKey)) continue;

            results.Add(new PowerShellHostOption(
                fullPath,
                displayName,
                displayVersion,
                candidate.UserScoped,
                candidate.WindowsPowerShell));
        }

        return results;
    }

    private static string GetWindowsPowerShellVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine");
            if (key?.GetValue("PowerShellVersion") is string version && !string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch
        {
            // Fall through to a generic label.
        }

        return "5.1";
    }

    private static string GetPowerShellCoreVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = info.ProductVersion ?? info.FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch
        {
            // Fall through to asking the host itself.
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null) return string.Empty;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            var match = Regex.Match(output, @"PowerShell\s+([0-9]+(?:\.[0-9]+)+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatVersion(string version, bool windowsPowerShell)
    {
        if (!Version.TryParse(version.Split(' ')[0], out var parsed))
        {
            return version.Trim();
        }

        if (windowsPowerShell && parsed.Major == 5)
        {
            return $"{parsed.Major}.{parsed.Minor}";
        }

        return parsed.Build >= 0
            ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
            : $"{parsed.Major}.{parsed.Minor}";
    }

    private static bool IsUserScopedPath(string path)
        => path.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase)
           || path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase);
}
