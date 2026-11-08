using System.IO;
using System.Text;

namespace StartupManager.Services;

public sealed record ParsedCommand(
    string ExecutablePath,
    string Arguments,
    string? ScriptPath,
    string DisplayTarget);

public static class CommandLineParser
{
    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe"
    };

    private static readonly HashSet<string> GenericProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "powershell", "pwsh", "cmd", "wscript", "cscript", "rundll32",
        "explorer", "dllhost", "conhost", "mshta", "regsvr32"
    };

    public static ParsedCommand Parse(string? commandLine)
    {
        var raw = (commandLine ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new ParsedCommand(string.Empty, string.Empty, null, string.Empty);
        }

        var expanded = ExpandQuietly(raw.Trim());
        if (expanded.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            expanded = expanded[4..];
        }

        var (firstToken, rest) = SplitFirstToken(expanded);
        firstToken = Unquote(firstToken.Trim());
        rest = rest.Trim();

        if (firstToken.Length == 0)
        {
            return new ParsedCommand(string.Empty, rest, TryExtractScriptPath(rest, null), expanded);
        }

        var executable = firstToken;
        var arguments = rest;

        // Some Run entries omit quotes even when the executable path contains spaces.
        // Find the longest existing file prefix before treating the tail as arguments.
        if (!File.Exists(executable) && !executable.Contains('"'))
        {
            var parts = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var take = parts.Length - 1; take >= 1; take--)
            {
                var candidate = string.Join(' ', parts.Take(take));
                if (File.Exists(candidate))
                {
                    executable = candidate;
                    arguments = string.Join(' ', parts.Skip(take)).Trim();
                    break;
                }
            }
        }

        executable = NormalizePath(executable);
        var scriptPath = TryExtractScriptPath(arguments, executable);
        var display = string.IsNullOrWhiteSpace(arguments) ? executable : $"{executable} {arguments}";
        return new ParsedCommand(executable, arguments, scriptPath, display);
    }

    public static string BuildCommandLine(string executable, string arguments, bool quoteExecutable = true)
    {
        var exe = executable.Trim();
        var args = arguments.Trim();
        var rendered = quoteExecutable && exe.Contains(' ') && !exe.StartsWith('"')
            ? $"\"{exe}\""
            : exe;
        return args.Length == 0 ? rendered : $"{rendered} {args}";
    }

    public static string? TryExtractScriptPath(string? arguments, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;

        var args = arguments.Trim();
        var hostName = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : Path.GetFileName(Unquote(executablePath));

        if (ScriptHosts.Contains(hostName))
        {
            if (hostName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
                || hostName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
            {
                var fromFile = ExtractOptionValue(args, "-File", "-f");
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    return NormalizePath(fromFile);
                }
            }

            if (hostName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                var command = args.Trim();
                var switchIndex = command.IndexOf(" /c ", StringComparison.OrdinalIgnoreCase);
                if (switchIndex < 0)
                {
                    switchIndex = command.IndexOf(" /k ", StringComparison.OrdinalIgnoreCase);
                }

                if (switchIndex < 0 && command.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
                {
                    switchIndex = 0;
                }

                if (switchIndex < 0 && command.StartsWith("/k ", StringComparison.OrdinalIgnoreCase))
                {
                    switchIndex = 0;
                }

                if (switchIndex >= 0)
                {
                    var start = switchIndex == 0 ? 3 : switchIndex + 4;
                    command = command[start..].Trim();
                }

                // cmd /c ""C:\path with spaces\script.cmd"" contains doubled quotes.
                var unquoted = Unquote(Unquote(command.Trim()));
                if (LooksLikeScript(unquoted)) return NormalizePath(unquoted);

                var first = SplitFirstToken(unquoted).Token;
                if (LooksLikeScript(first)) return NormalizePath(first);
            }

            if (hostName.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase)
                || hostName.Equals("cscript.exe", StringComparison.OrdinalIgnoreCase)
                || hostName.Equals("mshta.exe", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = Tokenize(args);
                foreach (var token in tokens)
                {
                    var candidate = Unquote(token);
                    if (LooksLikeScript(candidate)) return NormalizePath(candidate);
                }
            }
        }

        // A direct script file is itself the launch target.
        if (LooksLikeScript(Unquote(args)))
        {
            return NormalizePath(Unquote(args));
        }

        return null;
    }

    public static string Unquote(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return text;
    }

    public static string NormalizePath(string value)
    {
        var text = Unquote(value.Trim());
        if (text.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        text = ExpandQuietly(text);
        try
        {
            if (Path.IsPathFullyQualified(text))
            {
                return Path.GetFullPath(text);
            }
        }
        catch
        {
            // Keep the original string; the path may be a shell URI or contain invalid chars.
        }

        return text;
    }

    public static string ExpandQuietly(string value)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(value);
        }
        catch
        {
            return value;
        }
    }

    public static bool LooksLikeScript(string value)
    {
        var extension = Path.GetExtension(Unquote(value));
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".wsf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGenericHost(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var imageName = Path.GetFileNameWithoutExtension(Unquote(executablePath));
        return GenericProcessNames.Contains(imageName);
    }

    private static (string Token, string Remainder) SplitFirstToken(string value)
    {
        var text = value.TrimStart();
        if (text.Length == 0) return (string.Empty, string.Empty);

        if (text[0] == '"')
        {
            var builder = new StringBuilder();
            var index = 1;
            while (index < text.Length)
            {
                if (text[index] == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        builder.Append('"');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                builder.Append(text[index]);
                index++;
            }

            return (builder.ToString(), text[index..]);
        }

        var space = text.IndexOf(' ');
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..]);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0) yield return current.ToString();
    }

    private static string? ExtractOptionValue(string args, params string[] optionNames)
    {
        var tokens = Tokenize(args).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (optionNames.Any(o => tokens[i].Equals(o, StringComparison.OrdinalIgnoreCase))
                && i + 1 < tokens.Count)
            {
                return Unquote(tokens[i + 1]);
            }
        }

        return null;
    }
}
