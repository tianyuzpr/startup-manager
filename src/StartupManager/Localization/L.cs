namespace StartupManager.Localization;

/// <summary>Short alias used by code-behind and services.</summary>
public static class L
{
    public static bool IsEnglish => LocalizationManager.IsEnglish;
    public static string T(string key) => LocalizationManager.T(key);
    public static string F(string key, params object?[] args) => LocalizationManager.F(key, args);
}
