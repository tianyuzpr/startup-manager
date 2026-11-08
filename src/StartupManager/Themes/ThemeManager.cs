using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using StartupManager.Services;

namespace StartupManager.Themes;

public enum ThemeMode
{
    Light,
    Dark,
    System
}

public sealed class ThemeSettings
{
    public string Theme { get; set; } = "Light";
}

public static class ThemeManager
{
    private const string SettingsFileName = "settings.json";
    private static bool _systemWatcherRegistered;

    public static ThemeMode Current { get; private set; } = ThemeMode.Light;
    public static ThemeMode ResolvedMode => Current == ThemeMode.System ? DetectSystemMode() : Current;
    public static bool IsDark => ResolvedMode == ThemeMode.Dark;

    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        RegisterSystemWatcher();
        Apply(LoadSavedTheme(), persist: false);
    }

    public static void Toggle()
    {
        // The quick header button switches the concrete palette. The full
        // Light / Dark / Follow system choice lives in Settings.
        Apply(IsDark ? ThemeMode.Light : ThemeMode.Dark);
    }

    public static void Apply(ThemeMode mode, bool persist = true)
    {
        var application = Application.Current;
        if (application is null) return;

        var resolved = mode == ThemeMode.System ? DetectSystemMode() : mode;
        var fileName = resolved == ThemeMode.Dark ? "DarkTheme.xaml" : "LightTheme.xaml";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/StartupManager;component/Themes/{fileName}",
                UriKind.Absolute)
        };

        var merged = application.Resources.MergedDictionaries;
        var existing = merged
            .Where(d => d.Source is not null
                        && d.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase)
                        && d.Source.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in existing)
        {
            merged.Remove(item);
        }

        merged.Insert(0, dictionary);
        Current = mode;

        if (persist)
        {
            SaveTheme(mode);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void RegisterSystemWatcher()
    {
        if (_systemWatcherRegistered) return;
        _systemWatcherRegistered = true;

        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (Current != ThemeMode.System) return;
            if (args.Category is not (UserPreferenceCategory.General
                or UserPreferenceCategory.VisualStyle
                or UserPreferenceCategory.Color))
            {
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (Current == ThemeMode.System)
                {
                    Apply(ThemeMode.System, persist: false);
                }
            });
        };
    }

    private static ThemeMode DetectSystemMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? ThemeMode.Dark
                : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
    }

    public static ThemeMode LoadSavedTheme()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, SettingsFileName);
            if (!File.Exists(path)) return ThemeMode.Light;

            var settings = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(path));
            if (settings is null) return ThemeMode.Light;

            return Enum.TryParse<ThemeMode>(settings.Theme, ignoreCase: true, out var mode)
                ? mode
                : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
    }

    private static void SaveTheme(ThemeMode mode)
    {
        try
        {
            var folder = AppPaths.EnsureDirectory(AppPaths.Root);
            var path = Path.Combine(folder, SettingsFileName);
            var json = JsonSerializer.Serialize(
                new ThemeSettings { Theme = mode.ToString() },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            File.WriteAllText(path, json);
        }
        catch
        {
            // A theme preference is a convenience; never block UI for it.
        }
    }
}
