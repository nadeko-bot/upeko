using System;
using System.Collections;
using System.IO;
using System.Text;

namespace upeko.Services;

public enum TokenState
{
    /// <summary>data/creds.yml does not exist. NadekoBot does not start without it.</summary>
    NoFile,
    Empty,
    Set,
    /// <summary>The token line has a YAML form that this class does not read, for example a block scalar.</summary>
    Unknown,
}

/// <summary>
/// Reads and writes the bot token in a bot's data/creds.yml. Only the token line changes, as text, so the
/// comments and the other settings stay as they are. No YAML library: release builds are trimmed.
/// </summary>
public static class CredsFile
{
    private const string TokenKey = "token:";

    public static string PathFor(string botPath) => Path.Combine(botPath, "data", "creds.yml");

    public static string ExamplePathFor(string botPath) => Path.Combine(botPath, "data", "creds_example.yml");

    public static (TokenState State, string Token) ReadToken(string botPath)
    {
        var path = PathFor(botPath);
        if (!File.Exists(path))
            return (TokenState.NoFile, string.Empty);

        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith(TokenKey, StringComparison.Ordinal))
                continue;

            var token = ParseValue(line[TokenKey.Length..].Trim());
            if (token is null)
                return (TokenState.Unknown, string.Empty);

            return token.Length == 0 ? (TokenState.Empty, string.Empty) : (TokenState.Set, token);
        }

        // NadekoBot then uses its default, an empty token.
        return (TokenState.Empty, string.Empty);
    }

    /// <summary>
    /// False only when Upeko is sure that NadekoBot stops at once for lack of a token.
    /// </summary>
    public static bool CanStart(string botPath)
    {
        var (state, _) = ReadToken(botPath);
        return state switch
        {
            TokenState.Set or TokenState.Unknown => true,
            // NadekoBot reads settings from environment variables with the prefix "bot_" too.
            TokenState.Empty => TokenInEnvironment(),
            _ => false,
        };
    }

    /// <summary>
    /// A Discord bot token has three parts, separated by dots. An application ID or a client secret has no dots.
    /// </summary>
    public static bool LooksLikeToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;
            foreach (var c in part)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sets the token. Creates creds.yml from creds_example.yml when it does not exist yet.
    /// </summary>
    /// <exception cref="FileNotFoundException">Neither creds.yml nor creds_example.yml exists.</exception>
    public static void WriteToken(string botPath, string token)
    {
        var path = PathFor(botPath);
        if (!File.Exists(path))
        {
            var example = ExamplePathFor(botPath);
            if (!File.Exists(example))
                throw new FileNotFoundException("creds_example.yml was not found", example);
            File.Copy(example, path);
        }

        var bytes = File.ReadAllBytes(path);
        var preamble = Encoding.UTF8.Preamble;
        var hasBom = bytes.AsSpan().StartsWith(preamble);
        var text = Encoding.UTF8.GetString(hasBom ? bytes[preamble.Length..] : bytes);

        // Split on \n only, so a \r stays at the end of its line and Windows line ends survive.
        var lines = text.Split('\n');
        var newLine = text.Contains("\r\n") ? "\r" : string.Empty;
        var tokenLine = $"{TokenKey} '{token.Replace("'", "''")}'";

        var index = Array.FindIndex(lines, l => l.StartsWith(TokenKey, StringComparison.Ordinal));
        if (index >= 0)
        {
            lines[index] = tokenLine + (lines[index].EndsWith('\r') ? "\r" : string.Empty);
        }
        else
        {
            var version = Array.FindIndex(lines, l => l.StartsWith("version:", StringComparison.Ordinal));
            var list = new System.Collections.Generic.List<string>(lines);
            list.Insert(version + 1, tokenLine + newLine);
            lines = list.ToArray();
        }

        var output = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            if (hasBom)
                stream.Write(preamble);
            stream.Write(output);
        }

        // The replacement is a new file; keep the permissions that the user gave creds.yml.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tempPath, File.GetUnixFileMode(path));

        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// The value of a YAML scalar on one line. Null when the form is not a plain, single-quoted or double-quoted scalar.
    /// </summary>
    private static string? ParseValue(string raw)
    {
        if (raw.Length == 0)
            return string.Empty;

        switch (raw[0])
        {
            case '\'':
            {
                var sb = new StringBuilder();
                for (var i = 1; i < raw.Length; i++)
                {
                    if (raw[i] != '\'')
                    {
                        sb.Append(raw[i]);
                        continue;
                    }
                    if (i + 1 < raw.Length && raw[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        continue;
                    }
                    return sb.ToString().Trim();
                }
                return null;
            }
            case '"':
            {
                var sb = new StringBuilder();
                for (var i = 1; i < raw.Length; i++)
                {
                    if (raw[i] == '\\' && i + 1 < raw.Length)
                    {
                        sb.Append(raw[++i]);
                        continue;
                    }
                    if (raw[i] == '"')
                        return sb.ToString().Trim();
                    sb.Append(raw[i]);
                }
                return null;
            }
            case '|' or '>' or '&' or '*' or '!' or '[' or '{':
                return null;
            default:
            {
                var comment = raw.IndexOf(" #", StringComparison.Ordinal);
                var value = (comment >= 0 ? raw[..comment] : raw).Trim();
                return value is "~" or "null" or "Null" or "NULL" ? string.Empty : value;
            }
        }
    }

    private static bool TokenInEnvironment()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key
                && key.Equals("bot_token", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Value as string))
                return true;
        }

        return false;
    }
}
