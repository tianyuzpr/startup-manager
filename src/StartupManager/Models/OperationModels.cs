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
