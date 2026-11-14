using System.Text.Json;
using StartupManager.Models;

namespace StartupManager.Services;

public sealed class SnapshotService
{
    private const int HistoryLimit = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string LatestPath => Path.Combine(AppPaths.Snapshots, "latest.json");

    public string HistoryPath => AppPaths.SnapshotHistory;

    public SnapshotScanResult Process(IReadOnlyList<ManagedEntry> entries)
    {
        var current = Capture(entries);
        SnapshotFile? previous = null;
        var baseline = false;

        try
        {
            previous = LoadLatest();
            baseline = previous is null;
        }
        catch (Exception ex)
        {
            baseline = true;
            AuditLog.Write("SNAPSHOT READ FAILED", ex.Message);
        }

        var diff = previous is null
            ? new SnapshotDiff
            {
                BaselineEstablished = true,
                UnchangedCount = current.Entries.Count
            }
            : ComputeDiff(previous, current);

        WriteLatest(current);
        WriteHistory(current);

        return new SnapshotScanResult(current, diff);
    }

    public void EstablishBaseline(SnapshotFile snapshot)
    {
        WriteLatest(snapshot);
    }

    public SnapshotFile? LoadLatest()
    {
        if (!File.Exists(LatestPath)) return null;
        var json = File.ReadAllText(LatestPath);
        return JsonSerializer.Deserialize<SnapshotFile>(json, JsonOptions);
    }

    public static SnapshotFile Capture(IReadOnlyList<ManagedEntry> entries)
    {
        return new SnapshotFile
        {
            SchemaVersion = 1,
            CapturedAt = DateTime.UtcNow,
            Entries = entries.Select(CreateEntry).ToList()
        };
    }

    public static SnapshotDiff ComputeDiff(SnapshotFile previous, SnapshotFile current)
    {
        var oldEntries = previous.Entries
            .GroupBy(e => e.StableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var newEntries = current.Entries
            .GroupBy(e => e.StableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var added = newEntries
            .Where(pair => !oldEntries.ContainsKey(pair.Key))
            .Select(pair => pair.Value)
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var removed = oldEntries
            .Where(pair => !newEntries.ContainsKey(pair.Key))
            .Select(pair => pair.Value)
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var modified = new List<ModifiedSnapshotEntry>();
        var unchanged = 0;

        foreach (var pair in newEntries)
        {
            if (!oldEntries.TryGetValue(pair.Key, out var oldEntry)) continue;

            var changedFields = new List<string>();
            if (!string.Equals(oldEntry.CommandLine, pair.Value.CommandLine, StringComparison.Ordinal))
            {
                changedFields.Add("commandLine");
            }

            if (!string.Equals(oldEntry.FilePath, pair.Value.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                changedFields.Add("filePath");
            }

            if (!string.Equals(oldEntry.StartupType, pair.Value.StartupType, StringComparison.Ordinal))
            {
                changedFields.Add("startupType");
            }

            if (oldEntry.Enabled != pair.Value.Enabled)
            {
                changedFields.Add("enabled");
            }

            if (!string.Equals(oldEntry.Scope, pair.Value.Scope, StringComparison.Ordinal))
            {
                changedFields.Add("scope");
            }

            if (changedFields.Count == 0)
            {
                unchanged++;
                continue;
            }

            modified.Add(new ModifiedSnapshotEntry
            {
                Old = oldEntry,
                New = pair.Value,
                ChangedFields = changedFields
            });
        }

        return new SnapshotDiff
        {
            BaselineEstablished = false,
            Added = added,
            Removed = removed,
            Modified = modified
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            UnchangedCount = unchanged
        };
    }

    private static SnapshotEntry CreateEntry(ManagedEntry entry)
    {
        return new SnapshotEntry
        {
            StableId = BuildStableId(entry),
            Category = BuildCategory(entry.Kind),
            Name = entry.Name,
            CommandLine = entry.CommandLine,
            FilePath = entry.FilePath ?? entry.ExecutablePath,
            StartupType = entry.StartupTypeCode,
            Enabled = entry.IsEnabled,
            Scope = entry.ScopeCode,
            RequiresAdmin = entry.RequiresAdmin
        };
    }

    private static string BuildStableId(ManagedEntry entry) => entry.Kind switch
    {
        EntryKind.WindowsService =>
            $"service:{entry.ServiceName ?? entry.Name}",
        EntryKind.RegistryRun =>
            $"registry:{entry.RegistryHive}\\{entry.RegistryKeyPath}\\{entry.RegistryValueName}",
        EntryKind.StartupFolder =>
            $"startupfolder:{entry.ScopeCode}:{Path.GetFileName(entry.FilePath ?? string.Empty)}",
        EntryKind.ScheduledTask =>
            $"task:{entry.TaskPath}",
        _ => entry.Id
    };

    private static string BuildCategory(EntryKind kind) => kind switch
    {
        EntryKind.WindowsService => "windows-service",
        EntryKind.RegistryRun => "registry-run",
        EntryKind.StartupFolder => "startup-folder",
        EntryKind.ScheduledTask => "scheduled-task",
        _ => "unknown"
    };

    private void WriteLatest(SnapshotFile snapshot)
    {
        try
        {
            AppPaths.EnsureDirectory(AppPaths.Snapshots);
            WriteAtomic(LatestPath, snapshot);
        }
        catch (Exception ex)
        {
            AuditLog.Write("SNAPSHOT WRITE FAILED", ex.Message);
        }
    }

    private void WriteHistory(SnapshotFile snapshot)
    {
        try
        {
            AppPaths.EnsureDirectory(HistoryPath);
            var file = Path.Combine(
                HistoryPath,
                $"snapshot-{snapshot.CapturedAt:yyyyMMdd-HHmmss-fff}.json");
            WriteAtomic(file, snapshot);

            var oldFiles = new DirectoryInfo(HistoryPath)
                .GetFiles("snapshot-*.json")
                .OrderByDescending(fileInfo => fileInfo.LastWriteTimeUtc)
                .Skip(HistoryLimit)
                .ToList();

            foreach (var oldFile in oldFiles)
            {
                try { oldFile.Delete(); } catch { }
            }
        }
        catch (Exception ex)
        {
            AuditLog.Write("SNAPSHOT HISTORY FAILED", ex.Message);
        }
    }

    private static void WriteAtomic(string path, SnapshotFile snapshot)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
