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
using StartupManager.Services;

namespace StartupManager.Models;

public enum EntryKind
{
    WindowsService,
    RegistryRun,
    StartupFolder,
    ScheduledTask
}

public enum EntryRuntimeState
{
    Running,
    Stopped,
    Ready,
    Disabled,
    Unknown
}

/// <summary>
/// A single item that can start with Windows. The same shape is used for
/// Windows services, Run-key entries, Startup-folder files and scheduled tasks.
/// </summary>
public sealed class ManagedEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EntryKind Kind { get; init; }

    public string Description { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string? ScriptPath { get; init; }
    public string Arguments { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public string ScopeLabel { get; init; } = string.Empty;
    public string TriggerLabel { get; init; } = string.Empty;
    public string StartupModeLabel { get; init; } = string.Empty;

    public bool RequiresAdmin { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsRunning { get; init; }
    public EntryRuntimeState State { get; init; } = EntryRuntimeState.Unknown;
    public string StateDetail { get; init; } = string.Empty;
    public IReadOnlyList<int> ProcessIds { get; init; } = Array.Empty<int>();

    public string? ServiceName { get; init; }
    public string? RegistryHive { get; init; }
    public string? RegistryKeyPath { get; init; }
    public string? RegistryValueName { get; init; }
    public string? FilePath { get; init; }
    public string? TaskPath { get; init; }

    public bool IsProtected { get; init; }
    public string? ProtectedReason { get; init; }
    public bool SupportsEnableToggle { get; init; } = true;
    public string? EnableToggleBlockedReason { get; init; }
    public bool RequiresTypedConfirmation { get; init; }
    public string? ConfirmationPhrase { get; init; }

    public string KindLabel => Kind switch
    {
        EntryKind.WindowsService => L.IsEnglish ? "Windows service" : "Windows 服务",
        EntryKind.RegistryRun => L.T("注册表启动项"),
        EntryKind.StartupFolder => L.T("启动文件夹"),
        EntryKind.ScheduledTask => L.IsEnglish ? "Scheduled task" : "计划任务",
        _ => L.T("启动项")
    };

    public string KindShortLabel => Kind switch
    {
        EntryKind.WindowsService => L.T("服务"),
        EntryKind.RegistryRun => L.T("注册表"),
        EntryKind.StartupFolder => L.T("启动文件夹"),
        EntryKind.ScheduledTask => L.IsEnglish ? "Task" : "计划任务",
        _ => L.T("启动项")
    };

    public string KindMonogram => Kind switch
    {
        EntryKind.WindowsService => L.IsEnglish ? "W" : "服",
        EntryKind.RegistryRun => L.IsEnglish ? "R" : "注",
        EntryKind.StartupFolder => L.IsEnglish ? "F" : "启",
        EntryKind.ScheduledTask => L.IsEnglish ? "T" : "任",
        _ => L.IsEnglish ? "S" : "启"
    };

    public string StatusLabel => State switch
    {
        EntryRuntimeState.Running => L.T("运行中"),
        EntryRuntimeState.Disabled => L.T("已禁用"),
        EntryRuntimeState.Ready => L.T("就绪"),
        EntryRuntimeState.Stopped when Kind == EntryKind.WindowsService => L.T("已停止"),
        EntryRuntimeState.Stopped => L.T("未运行"),
        _ => L.T("未知")
    };

    public string AdminLabel => RequiresAdmin ? L.T("需要管理员") : L.T("当前用户");

    public string ProcessLabel => ProcessIds.Count switch
    {
        0 => string.Empty,
        1 => $"PID {ProcessIds[0]}",
        _ => $"PID {string.Join(", ", ProcessIds.Take(3))}{(ProcessIds.Count > 3 ? "…" : string.Empty)}"
    };

    public string ProcessDisplay => string.IsNullOrWhiteSpace(ProcessLabel) ? "—" : ProcessLabel;

    public string CommandDisplay => string.IsNullOrWhiteSpace(CommandLine) ? "—" : CommandLine;

    public string LocationDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FilePath)) return FilePath!;
            if (!string.IsNullOrWhiteSpace(TaskPath)) return TaskPath!;
            if (!string.IsNullOrWhiteSpace(RegistryKeyPath)) return SourcePath;
            return SourcePath;
        }
    }

    public bool CanOpenLocation => !string.IsNullOrWhiteSpace(FilePath)
                                   || (!string.IsNullOrWhiteSpace(ExecutablePath) && System.IO.File.Exists(ExecutablePath));

    public bool CanStop => IsRunning;
    public bool CanStart => !IsRunning && IsEnabled;
    public bool CanToggle => IsRunning ? CanStop : CanStart;
    public bool CanRestart => IsEnabled;
    public bool CanDelete => !IsProtected || !SystemServiceProtection.RefuseDeletingSystemServices;
    public bool CanToggleEnabled => SupportsEnableToggle && (!IsEnabled || !IsProtected);
    public string ToggleEnabledLabel => IsEnabled ? L.T("禁用") : L.T("启用");
    public string ToggleEnabledGlyph => IsEnabled ? "\uE7E8" : "\uE73E";
    public string ToggleEnabledActionName => IsEnabled ? L.T("禁用此启动项") : L.T("启用此启动项");
    public string ToggleEnabledToolTip
    {
        get
        {
            if (!SupportsEnableToggle)
            {
                return EnableToggleBlockedReason ?? L.T("该项目不支持禁用自动启动。");
            }

            if (IsEnabled && IsProtected)
            {
                return ProtectedReason ?? L.T("系统关键服务受保护，不能禁用。");
            }

            return IsEnabled ? L.T("禁用自动启动") : L.T("启用自动启动");
        }
    }

    public string PrimaryActionLabel => IsRunning ? L.T("停止") : L.T("启动");
    public string PrimaryActionGlyph => IsRunning ? "\uE71A" : "\uE768";
}
