using System.Text.Json;
using StartupManager.Localization;

namespace StartupManager.Services;

/// <summary>
/// Configurable deletion protection for system services. The default list is
/// deliberately unchanged from the original built-in hard-coded list; users can
/// edit it from Settings → Danger mode.
/// </summary>
public static class SystemServiceProtection
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "protection.json");

    private static readonly HashSet<string> DefaultServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "LSM", "SamSs", "Winmgmt", "EventLog", "Schedule",
        "PlugPlay", "Power", "ProfSvc", "gpsvc", "CryptSvc", "TrustedInstaller",
        "BFE", "mpssvc", "WinDefend", "SecurityHealthService", "wscsvc", "MDCoreSvc", "Sense", "W32Time",
        "LanmanWorkstation", "LanmanServer", "Dhcp", "Dnscache", "NlaSvc",
        "netprofm", "nsi", "Audiosrv", "AudioEndpointBuilder", "BrokerInfrastructure",
        "SystemEventsBroker", "StateRepository", "UserManager", "CoreMessagingRegistrar",
        "camsvc", "WpnService", "WpnUserService", "TextInputManagementService",
        "ShellHWDetection", "Themes", "SysMain", "FontCache", "Spooler",
        "WinHttpAutoProxySvc", "iphlpsvc", "DiagTrack", "ClipSVC", "LicenseManager",
        "mpssvc", "SgrmBroker", "SecurityHealthService", "edgeupdate", "edgeupdatem"
    };

    private static readonly object Gate = new();
    private static HashSet<string> _services = new(DefaultServices, StringComparer.OrdinalIgnoreCase);

    public static bool RefuseDeletingSystemServices { get; private set; } = true;

    public static IReadOnlyList<string> ProtectedServices
    {
        get
        {
            lock (Gate)
            {
                return _services
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            Load();
        }
    }

    public static bool IsProtected(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        lock (Gate)
        {
            return _services.Contains(serviceName);
        }
    }

    public static string ProtectedReason(string serviceName)
        => L.T("系统服务保护名单中的服务");

    public static void Save(IEnumerable<string> serviceNames, bool refuseDeleting)
    {
        var normalized = serviceNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (Gate)
        {
            _services = normalized;
            RefuseDeletingSystemServices = refuseDeleting;
            Persist();
        }
    }

    public static void RestoreDefaults()
    {
        lock (Gate)
        {
            _services = new HashSet<string>(DefaultServices, StringComparer.OrdinalIgnoreCase);
            RefuseDeletingSystemServices = true;
            Persist();
        }
    }

    public static IReadOnlyList<string> DefaultServiceList => DefaultServices
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = JsonSerializer.Deserialize<ProtectionSettings>(File.ReadAllText(FilePath));
            if (data is null) return;

            RefuseDeletingSystemServices = data.RefuseDeletingSystemServices;
            if (data.ProtectedServices is not null)
            {
                _services = data.ProtectedServices
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _services = new HashSet<string>(DefaultServices, StringComparer.OrdinalIgnoreCase);
            RefuseDeletingSystemServices = true;
        }
    }

    private static void Persist()
    {
        try
        {
            AppPaths.EnsureDirectory(AppPaths.Root);
            var data = new ProtectionSettings
            {
                RefuseDeletingSystemServices = RefuseDeletingSystemServices,
                ProtectedServices = _services
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }));
        }
        catch
        {
            // Protection changes still apply in memory for the current session.
        }
    }

    private sealed class ProtectionSettings
    {
        public bool RefuseDeletingSystemServices { get; set; } = true;
        public List<string> ProtectedServices { get; set; } = new();
    }
}
