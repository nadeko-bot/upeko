using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace upeko.Services;

/// <summary>
/// The last lines that a bot wrote to its output, and the last error among them. The bot's reader threads
/// add lines; the UI thread reads them.
/// </summary>
public sealed class BotOutput
{
    public const int MaxLines = 500;
    private const int MaxErrorLength = 300;

    // Colour codes, which a log library writes when it thinks that the output is a terminal.
    private static readonly Regex Ansi = new(@"\x1B(?:\[[0-9;?]*[ -/]*[@-~]|\][^\x07]*\x07)", RegexOptions.Compiled);

    // NadekoBot's console format: "[12:00:01 ERR] | #Bot | message".
    private static readonly Regex LoggedError = new(@"^\[\d{2}:\d{2}:\d{2} (?:ERR|FTL)\] \|(?: #[^|]*\|)? ?(?<msg>.+)$", RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly Queue<string> _lines = new();
    private string? _lastError;
    private int _version;

    /// <summary>
    /// Changes each time a line is added or the output is cleared.
    /// </summary>
    public int Version
    {
        get { lock (_lock) return _version; }
    }

    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    public string Text
    {
        get { lock (_lock) return string.Join('\n', _lines); }
    }

    public void Add(string line, bool fromStandardError)
    {
        line = Ansi.Replace(line, string.Empty);
        var error = ErrorIn(line, fromStandardError);

        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
                _lines.Dequeue();
            if (error is not null)
                _lastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] + "..." : error;
            _version++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
            _lastError = null;
            _version++;
        }
    }

    private static string? ErrorIn(string line, bool fromStandardError)
    {
        var logged = LoggedError.Match(line);
        if (logged.Success)
            return logged.Groups["msg"].Value.Trim();

        if (!fromStandardError)
            return null;

        // .NET writes a crash to stderr: the exception on one line, then the stack trace and inner exceptions.
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith("at ") || text.StartsWith("---"))
            return null;

        const string crashPrefix = "Unhandled exception.";
        return text.StartsWith(crashPrefix) ? text[crashPrefix.Length..].Trim() : text;
    }
}
