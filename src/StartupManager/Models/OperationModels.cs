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
namespace StartupManager.Models;

public sealed class ScanResult
{
    public required IReadOnlyList<ManagedEntry> Entries { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public DateTime ScannedAt { get; init; } = DateTime.Now;
}

public enum AddEntryType
{
    PowerShell,
    CommandPrompt,
    Executable
}

public enum AddStartupMode
{
    CurrentUserLogon,
    AllUsersLogon,
    SystemBootTask,
    WindowsService
}

public sealed class AddEntryRequest
{
    public required string Name { get; init; }
    public required AddEntryType Type { get; init; }
    public required AddStartupMode Mode { get; init; }
    public string ScriptContent { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string? PowerShellHostPath { get; init; }
    public bool RunHidden { get; init; } = true;
    public bool StartServiceImmediately { get; init; }
}

public sealed class AddEntryResult
{
    public required bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? CreatedId { get; init; }
    public string? Warning { get; init; }
}
