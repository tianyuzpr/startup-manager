using System.Text.Json.Serialization;
using StartupManager.Localization;

namespace StartupManager.Models;

public sealed class SnapshotFile
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CapturedAt { get; set; }
    public List<SnapshotEntry> Entries { get; set; } = new();
}

public sealed class SnapshotEntry
{
    public string StableId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string StartupType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Scope { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }

    [JsonIgnore]
    public string CategoryLabel => SnapshotLabels.Category(Category);
}

public sealed class SnapshotFieldChange
{
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;

    [JsonIgnore]
    public string FieldLabel => SnapshotLabels.Field(Field);
}

public sealed class ModifiedSnapshotEntry
{
    public SnapshotEntry Old { get; set; } = new();
    public SnapshotEntry New { get; set; } = new();
    public List<string> ChangedFields { get; set; } = new();

    [JsonIgnore]
    public string Name => New.Name;

    [JsonIgnore]
    public string CategoryLabel => New.CategoryLabel;

    [JsonIgnore]
    public IReadOnlyList<SnapshotFieldChange> Changes => ChangedFields
        .Select(fieldName => new SnapshotFieldChange
        {
            Field = fieldName,
            OldValue = SnapshotLabels.Value(fieldName, Old),
            NewValue = SnapshotLabels.Value(fieldName, New)
        })
        .ToList();
}

public sealed class SnapshotDiff
{
    public bool BaselineEstablished { get; init; }
    public List<SnapshotEntry> Added { get; init; } = new();
    public List<SnapshotEntry> Removed { get; init; } = new();
    public List<ModifiedSnapshotEntry> Modified { get; init; } = new();
    public int UnchangedCount { get; init; }

    [JsonIgnore]
    public int ChangeCount => Added.Count + Removed.Count + Modified.Count;

    [JsonIgnore]
    public bool HasChanges => ChangeCount > 0;
}

public sealed record SnapshotScanResult(SnapshotFile Snapshot, SnapshotDiff Diff);

public static class SnapshotLabels
{
    public static string Category(string category) => category switch
    {
        "windows-service" => L.T("Windows 服务"),
        "registry-run" => L.T("注册表启动项"),
        "startup-folder" => L.T("启动文件夹"),
        "scheduled-task" => L.T("计划任务"),
        _ => category
    };

    public static string Field(string field) => field switch
    {
        "commandLine" => L.T("启动命令"),
        "filePath" => L.T("文件路径"),
        "startupType" => L.T("启动方式"),
        "enabled" => L.T("启用状态"),
        "scope" => L.T("作用范围"),
        _ => field
    };

    public static string Value(string field, SnapshotEntry entry) => field switch
    {
        "commandLine" => Display(entry.CommandLine),
        "filePath" => Display(entry.FilePath),
        "startupType" => entry.StartupType,
        "enabled" => entry.Enabled ? L.T("已启用") : L.T("已禁用"),
        "scope" => Scope(entry.Scope),
        _ => string.Empty
    };

    public static string Scope(string scope) => scope switch
    {
        "current-user" => L.T("当前用户"),
        "all-users" => L.T("所有用户"),
        "system" => L.T("系统级"),
        _ => scope
    };

    private static string Display(string value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
