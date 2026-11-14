using System.IO;
using System.Text.RegularExpressions;
using StartupManager.Models;

namespace StartupManager.Services;

/// <summary>
/// Configurable names that usually indicate a launcher rather than the real
/// executable. Kept separate from the evaluator so the list can be extended
/// without changing matching logic.
/// </summary>
public static class MatchabilityRules
{
    public static IReadOnlySet<string> LauncherNames { get; } = new HashSet<string>(
        new[]
        {
            "start", "launch", "run", "launcher", "updater", "bootstrap",
            "update", "startup", "starter", "loader"
        },
        StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Decides whether a startup item can be matched statically to a live process.
/// It deliberately does not try to infer what a launcher or script ultimately runs.
/// </summary>
public static class MatchabilityEvaluator
{
    private static readonly Regex CmdSwitch = new(@"(^|\s)/[ck](\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerShellSwitch = new(@"-(file|command|encodedcommand)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EnvironmentVariable = new(@"%[^%]+%", RegexOptions.Compiled);

    private static readonly HashSet<string> UnreliableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf"
    };

    public static Matchability Evaluate(string? commandLine, string? executablePath, string? scriptPath)
    {
        var raw = (commandLine ?? string.Empty).Trim();
        if (raw.Length == 0) return Matchability.Unknown;

        var firstToken = ReadFirstToken(raw);
        if (string.IsNullOrWhiteSpace(firstToken)) return Matchability.Unknown;

        firstToken = CommandLineParser.Unquote(firstToken);
        var fileName = Path.GetFileName(firstToken);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var arguments = raw.Length > firstToken.Length
            ? raw[firstToken.Length..]
            : string.Empty;

        // A command shell or script host hides the real payload, even when its
        // own executable path exists.
        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            if (CmdSwitch.IsMatch(arguments)) return Matchability.Unreliable;
        }

        if (fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            if (PowerShellSwitch.IsMatch(arguments)) return Matchability.Unreliable;
        }

        if (fileName.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("wscript", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("cscript.exe", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("cscript", StringComparison.OrdinalIgnoreCase))
        {
            return Matchability.Unreliable;
        }

        if (MatchabilityRules.LauncherNames.Contains(baseName))
        {
            return Matchability.Unreliable;
        }

        // Shell syntax, variable expansion and redirection mean the command
        // line is not the final process command line.
        if (EnvironmentVariable.IsMatch(raw)
            || raw.Contains("$env:", StringComparison.OrdinalIgnoreCase)
            || raw.Contains('>')
            || raw.Contains('|')
            || raw.Contains("&&", StringComparison.Ordinal))
        {
            return Matchability.Unreliable;
        }

        var directPath = string.IsNullOrWhiteSpace(scriptPath) ? firstToken : scriptPath;
        if (UnreliableExtensions.Contains(extension)
            || UnreliableExtensions.Contains(Path.GetExtension(directPath)))
        {
            return Matchability.Unreliable;
        }

        // Only an existing, absolute .exe can be called reliable. Everything
        // else is unknown rather than guessed.
        var resolvedPath = !string.IsNullOrWhiteSpace(executablePath)
            ? CommandLineParser.NormalizePath(executablePath)
            : CommandLineParser.NormalizePath(firstToken);

        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(resolvedPath)
            || !File.Exists(resolvedPath))
        {
            return Matchability.Unknown;
        }

        return Matchability.Reliable;
    }

    private static string ReadFirstToken(string commandLine)
    {
        var text = commandLine.TrimStart();
        if (text.Length == 0) return string.Empty;

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            while (end >= 0 && end + 1 < text.Length && text[end + 1] == '"')
            {
                end = text.IndexOf('"', end + 2);
            }

            return end < 0 ? text : text[..(end + 1)];
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }
}
