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
using System.Reflection;
using System.Xml.Linq;

namespace StartupManager.Services;

/// <summary>
/// Reads build/app.props, which is embedded into the assembly at build time.
/// It is the single place to change the display name, version, publisher and
/// runtime facts; everything below is derived from it.
/// </summary>
public static class AppInfo
{
    private const string ResourceName = "StartupManager.app.props";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Values =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string DisplayName => Get("AppDisplayName", "启航 · 开机启动管理器");

    public static string ProductName => Get("AppProductName", "启航");

    public static string TaglineZh => Get("AppTaglineZh", "本地运行，数据不上传");

    public static string TaglineEn => Get("AppTaglineEn", "Local only, data stays on this PC");

    public static string Version => AssemblyInformationalVersion;

    public static string InformationalVersion => AssemblyInformationalVersion;

    public static string FileVersion => Get("AppFileVersion", AssemblyFileVersion);

    public static string Copyright => Get("AppCopyright", string.Empty);

    public static string RepositoryUrl => Get("AppRepositoryUrl", string.Empty);

    public static string Company => Get("AppCompany", string.Empty);

    public static string TargetFramework => Get("AppTargetFramework", string.Empty);

    public static string RuntimeIdentifier => Get("AppRuntimeIdentifier", string.Empty);

    public static string MinimumWindows => Get("AppMinimumWindows", string.Empty);

    public static string VersionTag
    {
        get
        {
            var version = InformationalVersion;
            return System.Text.RegularExpressions.Regex.IsMatch(
                    version,
                    @"^\d{2}w\d{2}d[1-7][a-z]+(?:\.NIGHTLY)?$")
                ? version
                : $"v{version}";
        }
    }

    public static string FooterTextZh => $"{VersionTag}  ·  {TaglineZh}";

    public static string FooterTextEn => $"{VersionTag}  ·  {TaglineEn}";

    public static string AboutTextZh
        => $"{DisplayName} {VersionTag}\n最低系统要求：{MinimumWindows}\n{Copyright}";

    public static string AboutTextEn
        => $"{DisplayName} {VersionTag}\nMinimum system: {MinimumWindows}\n{Copyright}";

    private static string AssemblyInformationalVersion
    {
        get
        {
            var value = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(value)) return AssemblyVersion;

            // MSBuild appends "+<commit>" when SourceLink information is present.
            var plus = value.IndexOf('+');
            return plus > 0 ? value[..plus] : value;
        }
    }

    private static string AssemblyVersion
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string AssemblyFileVersion
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0.0";

    private static string Get(string key, string fallback)
        => Values.Value.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static IReadOnlyDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return result;

            var document = XDocument.Load(stream);
            foreach (var element in document.Descendants().Where(e => !e.HasElements))
            {
                var value = element.Value.Trim();
                if (value.Length > 0)
                {
                    result[element.Name.LocalName] = value;
                }
            }
        }
        catch
        {
            // A missing or malformed props file must not prevent startup; the
            // fallbacks above keep the app usable.
        }

        return result;
    }
}
