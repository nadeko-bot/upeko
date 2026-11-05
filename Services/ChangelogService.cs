using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;

namespace upeko.Services;

public record ChangelogItem(string Text, IReadOnlyList<ChangelogItem> Children)
{
    public bool HasChildren => Children.Count > 0;
}

public record ChangelogGroup(string Title, IReadOnlyList<ChangelogItem> Items);

public record ChangelogVersion(string Version, string? Date, IReadOnlyList<ChangelogGroup> Groups);

/// <summary>
/// Reads NadekoBot's CHANGELOG.md. The GitHub releases only link to it, so it is the one place with the notes.
/// </summary>
public class ChangelogService
{
    public const string ChangelogUrl = "https://raw.githubusercontent.com/nadeko-bot/nadekobot/v6/CHANGELOG.md";
    public const string ChangelogPageUrl = "https://github.com/nadeko-bot/nadekobot/blob/v6/CHANGELOG.md";

    public static ChangelogService Instance { get; } = new(ChangelogUrl);

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Upeko" } },
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly Regex VersionHeader = new(@"^##\s*\[(?<v>[^\]]+)\](?:\s*-\s*(?<d>.+))?\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(?<indent>\s*)[-*]\s+(?<text>.*)$", RegexOptions.Compiled);

    private readonly string _url;
    private Task<IReadOnlyList<ChangelogVersion>>? _load;
    private string? _loadedForRelease;

    public ChangelogService(string url)
    {
        _url = url;
    }

    /// <summary>
    /// The parsed changelog, newest version first. Downloaded once, and again when a newer NadekoBot
    /// release appears or the last attempt failed.
    /// </summary>
    public Task<IReadOnlyList<ChangelogVersion>> GetAsync()
    {
        var release = UpdateChecker.Instance.LatestRelease?.TagName;
        if (_load is null || _load.IsFaulted || _load.IsCanceled || release != _loadedForRelease)
        {
            _loadedForRelease = release;
            _load = LoadAsync();
        }

        return _load;
    }

    private async Task<IReadOnlyList<ChangelogVersion>> LoadAsync()
    {
        try
        {
            var text = await Http.GetStringAsync(_url);
            var versions = Parse(text);
            if (versions.Count == 0)
                throw new FormatException("The changelog has no versions.");
            Log.Information("Loaded NadekoBot changelog, latest {Version}", versions[0].Version);
            return versions;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the NadekoBot changelog");
            throw;
        }
    }

    /// <summary>
    /// Versions newer than <paramref name="installedVersion"/>, newest first. Only the latest one when the
    /// version is unknown, up to date, or not in the changelog.
    /// </summary>
    public static IReadOnlyList<ChangelogVersion> GetSince(IReadOnlyList<ChangelogVersion> all, string? installedVersion)
    {
        if (all.Count == 0)
            return all;

        if (string.IsNullOrWhiteSpace(installedVersion)
            || !System.Version.TryParse(installedVersion.Trim().TrimStart('v'), out _)
            || !all.Any(v => VersionUtil.Compare(v.Version, installedVersion.Trim().TrimStart('v')) <= 0))
            return [all[0]];

        var installed = installedVersion.Trim().TrimStart('v');
        var newer = all.Where(v => VersionUtil.Compare(v.Version, installed) > 0).ToList();
        return newer.Count > 0 ? newer : [all[0]];
    }

    public static IReadOnlyList<ChangelogVersion> Parse(string markdown)
    {
        var versions = new List<ChangelogVersion>();

        string? version = null, date = null;
        List<ChangelogGroup> groups = new();
        string? groupTitle = null;
        List<MutableItem> roots = new();
        // Open bullets by indentation, innermost last.
        var stack = new List<(int Indent, MutableItem Item)>();

        void CloseGroup()
        {
            if (groupTitle is not null || roots.Count > 0)
                groups.Add(new ChangelogGroup(groupTitle ?? string.Empty, roots.Select(r => r.Freeze()).ToList()));
            groupTitle = null;
            roots = new();
            stack.Clear();
        }

        void CloseVersion()
        {
            CloseGroup();
            if (version is not null)
                versions.Add(new ChangelogVersion(version, date, groups));
            groups = new();
        }

        foreach (var raw in markdown.Replace("\r", string.Empty).TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Replace("\t", "    ");

            var header = VersionHeader.Match(line);
            if (header.Success)
            {
                CloseVersion();
                version = header.Groups["v"].Value.Trim();
                date = header.Groups["d"].Success ? header.Groups["d"].Value.Trim() : null;
                continue;
            }

            if (version is null)
                continue;

            if (line.StartsWith("### "))
            {
                CloseGroup();
                groupTitle = line[4..].Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                var indent = bullet.Groups["indent"].Value.Length;
                var item = new MutableItem(Clean(bullet.Groups["text"].Value));
                while (stack.Count > 0 && stack[^1].Indent >= indent)
                    stack.RemoveAt(stack.Count - 1);

                if (stack.Count == 0)
                    roots.Add(item);
                else
                    stack[^1].Item.Children.Add(item);

                stack.Add((indent, item));
                continue;
            }

            // A wrapped line continues the bullet above it.
            if (stack.Count > 0)
                stack[^1].Item.Text += " " + Clean(line.Trim());
            else
                roots.Add(new MutableItem(Clean(line.Trim())));
        }

        CloseVersion();
        return versions;
    }

    private static string Clean(string text) => text.Replace("**", string.Empty).Trim();

    private sealed class MutableItem(string text)
    {
        public string Text { get; set; } = text;
        public List<MutableItem> Children { get; } = new();
        public ChangelogItem Freeze() => new(Text, Children.Select(c => c.Freeze()).ToList());
    }
}
