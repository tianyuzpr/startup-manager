using System.Text.Json;

namespace StartupManager.Services;

/// <summary>
/// Windows does not keep the previous start type when a service is disabled.
/// This store remembers it so "enable" can restore the original mode.
/// </summary>
public static class ServiceStartTypeStore
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "service-start-types.json");

    public static uint? Get(string serviceName)
    {
        lock (Gate)
        {
            return Load().TryGetValue(serviceName, out var startType) ? startType : null;
        }
    }

    public static void Remember(string serviceName, uint startType)
    {
        if (startType == 4) return;

        lock (Gate)
        {
            var map = Load();
            map[serviceName] = startType;
            Save(map);
        }
    }

    public static void Forget(string serviceName)
    {
        lock (Gate)
        {
            var map = Load();
            if (map.Remove(serviceName))
            {
                Save(map);
            }
        }
    }

    private static Dictionary<string, uint> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, uint>>(json)
                   ?? new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(Dictionary<string, uint> map)
    {
        try
        {
            AppPaths.EnsureDirectory(AppPaths.Root);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Remembrance is best-effort; disabling the service still works.
        }
    }
}
