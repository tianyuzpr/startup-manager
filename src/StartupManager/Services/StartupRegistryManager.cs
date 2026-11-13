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
using Microsoft.Win32;

namespace StartupManager.Services;

public sealed record RegistryRunEntry(
    string Hive,
    string KeyPath,
    string ValueName,
    string Command,
    bool IsRunOnce,
    bool IsWow64,
    bool IsEnabled,
    bool IsCurrentUser,
    string ApprovedKeyName);

public static class StartupRegistryManager
{
    private static readonly (string Hive, string Path, bool RunOnce, bool Wow64)[] RunKeys =
    {
        ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false, false),
        ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true, false),
        ("HKCU", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", false, true),
        ("HKCU", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", true, true),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false, false),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true, false),
        ("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", false, true),
        ("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", true, true),
        ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", false, false),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", false, false)
    };

    public static IReadOnlyList<RegistryRunEntry> Enumerate()
    {
        var list = new List<RegistryRunEntry>();
        foreach (var definition in RunKeys)
        {
            try
            {
                using var key = OpenKey(definition.Hive, definition.Path, writable: false);
                if (key is null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (string.IsNullOrWhiteSpace(command)) continue;

                    var approvedName = GetApprovedKeyName(definition.Wow64);
                    var enabled = IsEnabled(definition.Hive, approvedName, valueName);
                    list.Add(new RegistryRunEntry(
                        definition.Hive,
                        definition.Path,
                        valueName,
                        command,
                        definition.RunOnce,
                        definition.Wow64,
                        enabled,
                        definition.Hive == "HKCU",
                        approvedName));
                }
            }
            catch
            {
                // Some policy keys are not readable for standard users; skip them.
            }
        }

        return list;
    }

    public static (string Hive, string KeyPath, string ValueName, string Command) AddRunEntry(
        string preferredName,
        string command,
        bool allUsers)
    {
        var hive = allUsers ? "HKLM" : "HKCU";
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        var normalized = preferredName.Trim();
        if (normalized.Length == 0) normalized = "StartupManager Item";

        using var key = OpenKey(hive, keyPath, writable: true)
                        ?? throw new UnauthorizedAccessException("无法打开启动项注册表键，请以管理员身份运行。");

        var name = normalized;
        var suffix = 2;
        while (key.GetValue(name) is not null)
        {
            name = $"{normalized} ({suffix++})";
        }

        key.SetValue(name, command, RegistryValueKind.String);
        return (hive, keyPath, name, command);
    }

    public static void DeleteRunEntry(string hive, string keyPath, string valueName)
    {
        using (var key = OpenKey(hive, keyPath, writable: true)
                        ?? throw new UnauthorizedAccessException("无法打开启动项注册表键，请以管理员身份运行。"))
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }

        var approvedKey = keyPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
            ? "Run32"
            : "Run";
        try
        {
            var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var approved = baseKey.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedKey}",
                writable: true);
            approved?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch
        {
            // Stale approval state is harmless; ignore access failures.
        }
    }

    public static void SetRunEnabled(string hive, string keyPath, string valueName, bool enabled)
    {
        var approvedKey = GetApprovedKeyName(
            keyPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase));
        var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;

        using var approved = baseKey.CreateSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedKey}")
            ?? throw new UnauthorizedAccessException("无法写入 StartupApproved 注册表键，请以管理员身份运行。");

        approved.SetValue(valueName, BuildApprovalData(enabled), RegistryValueKind.Binary);
    }

    public static string FriendlyHive(string hive) => hive switch
    {
        "HKCU" => "HKEY_CURRENT_USER",
        "HKLM" => "HKEY_LOCAL_MACHINE",
        _ => hive
    };

    private static RegistryKey? OpenKey(string hive, string path, bool writable)
    {
        var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        return baseKey.OpenSubKey(path, writable);
    }

    private static string GetApprovedKeyName(bool wow64)
        => wow64 ? "Run32" : "Run";

    private static byte[] BuildApprovalData(bool enabled)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }

        return data;
    }

    private static bool IsEnabled(string hive, string approvedKeyName, string valueName)
    {
        try
        {
            var baseKey = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var approved = baseKey.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedKeyName}");
            if (approved?.GetValue(valueName) is not byte[] bytes || bytes.Length == 0) return true;

            // Windows stores 02/06 for enabled entries and 03/07 for entries
            // disabled through Task Manager or Settings.
            return bytes[0] != 0x03 && bytes[0] != 0x07;
        }
        catch
        {
            return true;
        }
    }
}
