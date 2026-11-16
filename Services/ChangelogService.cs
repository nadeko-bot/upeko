using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>
/// The kind of a changelog section. A heading that is not known is Notes.
/// </summary>
public enum ChangelogKind
{
    Added,
    Changed,
    Fixed,
    Removed,
    Obsolete,
    Notes,
    Dev,
}

public record ChangelogGroup(string Title, ChangelogKind Kind, IReadOnlyList<ChangelogItem> Items);

public record ChangelogVersion(string Version, string? Date, IReadOnlyList<ChangelogGroup> Groups);

/// <summary>
/// Top-level item counts of some releases, and their first changes. Dev notes are not counted.
/// </summary>
/// <param name="Total">The items that <paramref name="Top"/> takes from, so the number not shown is Total minus Top.Count.</param>
public record ChangelogSummary(int Added, int Changed, int Fixed, int Removed, int Total, IReadOnlyList<ChangelogItem> Top);

/// <summary>
/// Reads NadekoBot's CHANGELOG.md. The GitHub releases only link to it, so it is the one place with the notes.
/// </summary>
public class ChangelogService
{
    private const string RawUrl = "https://raw.githubusercontent.com/nadeko-bot/nadekobot";
    private const string BranchUrl = RawUrl + "/v6/CHANGELOG.md";
    public const string WebsiteUrl = "https://nadeko.bot/changelog";

    public static ChangelogService Instance { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "upeko", "cache"));

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Upeko" } },
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly Regex VersionHeader = new(@"^##\s*\[(?<v>[^\]]+)\](?:\s*-\s*(?<d>.+))?\s*$", RegexOptions.Compiled);
    // Old releases have a few "## Added" headings instead of "### Added".
    private static readonly Regex GroupHeader = new(@"^#{2,3}\s+(?<t>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(?<indent>\s*)[-*]\s+(?<text>.*)$", RegexOptions.Compiled);

    private static readonly ChangelogKind[] TopOrder =
        [ChangelogKind.Added, ChangelogKind.Changed, ChangelogKind.Fixed, ChangelogKind.Removed, ChangelogKind.Notes, ChangelogKind.Obsolete];

    private readonly string _cacheDir;
    private readonly Func<string, Task<string>> _download;
    private Task<IReadOnlyList<ChangelogVersion>>? _load;
    private string? _loadedForRelease;

    /// <param name="cacheDir">Keeps the file of the latest release, so the notes show at startup and offline.</param>
    /// <param name="download">Reads a URL; the default uses HTTP.</param>
    public ChangelogService(string cacheDir, Func<string, Task<string>>? download = null)
    {
        _cacheDir = cacheDir;
        _download = download ?? (url => Http.GetStringAsync(url));
    }

    /// <summary>
    /// The page of a version on nadeko.bot, or the changelog page when the version is not known.
    /// </summary>
    public static string WebsiteUrlFor(string? version)
    {
        var v = version?.Trim().TrimStart('v');
        if (string.IsNullOrEmpty(v) || v.Split('.') is not { Length: >= 2 } parts)
            return WebsiteUrl;
        return $"{WebsiteUrl}/{parts[0]}.{parts[1]}#{v}";
    }

    /// <summary>
    /// The parsed changelog of the latest release, newest version first. Read once, and again when a newer
    /// NadekoBot release appears or the last attempt failed.
    /// </summary>
    public async Task<IReadOnlyList<ChangelogVersion>> GetAsync()
    {
        // Without the release tag, the v6 branch is the only source, and it can hold versions that are not released.
        if (UpdateChecker.Instance.LatestRelease is null)
            await UpdateChecker.Instance.CheckForUpdatesAsync();

        var release = UpdateChecker.Instance.LatestRelease?.TagName;
        if (_load is null || _load.IsFaulted || _load.IsCanceled || release != _loadedForRelease)
        {
            _loadedForRelease = release;
            _load = LoadAsync(release);
        }

        return await _load;
    }

    /// <summary>
    /// The changelog of <paramref name="tag"/> from the disk cache, or null. Reads no network.
    /// </summary>
    public IReadOnlyList<ChangelogVersion>? TryGetCached(string tag)
    {
        try
        {
            var path = CachePath(tag);
            return File.Exists(path) ? UpTo(Parse(File.ReadAllText(path)), tag) : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the cached changelog of {Tag}", tag);
            return null;
        }
    }

    private async Task<IReadOnlyList<ChangelogVersion>> LoadAsync(string? tag)
    {
        // When GitHub's release API cannot be reached, the cached release is the newest one known.
        tag ??= CachedTag();
        if (tag is not null)
        {
            if (TryGetCached(tag) is { } cached)
                return cached;

            try
            {
                var text = await _download($"{RawUrl}/refs/tags/{Uri.EscapeDataString(tag)}/CHANGELOG.md");
                var versions = UpTo(Parse(text), tag);
                WriteCache(tag, text);
                Log.Information("Loaded the NadekoBot changelog of release {Tag}", tag);
                return versions;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load the changelog of release {Tag}, reading the v6 branch", tag);
            }
        }

        try
        {
            var versions = UpTo(Parse(await _download(BranchUrl)), tag);
            Log.Information("Loaded the NadekoBot changelog of the v6 branch, latest {Version}", versions[0].Version);
            return versions;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the NadekoBot changelog");
            throw;
        }
    }

    /// <summary>
    /// Drops the versions newer than <paramref name="tag"/>: the v6 branch can hold notes of a version that is not released yet.
    /// </summary>
    private static IReadOnlyList<ChangelogVersion> UpTo(IReadOnlyList<ChangelogVersion> versions, string? tag)
    {
        var latest = tag?.Trim().TrimStart('v');
        var kept = latest is null
            ? versions
            : versions.Where(v => VersionUtil.Compare(v.Version, latest) <= 0).ToList();
        if (kept.Count == 0)
            throw new FormatException("The changelog has no versions.");
        return kept;
    }

    private string? CachedTag()
    {
        try
        {
            if (!Directory.Exists(_cacheDir))
                return null;
            return Directory.GetFiles(_cacheDir, "CHANGELOG-*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f)["CHANGELOG-".Length..])
                .OrderByDescending(t => t.TrimStart('v'), Comparer<string>.Create(VersionUtil.Compare))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not list the cached changelogs");
            return null;
        }
    }

    private string CachePath(string tag)
    {
        var safe = string.Join("_", tag.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDir, $"CHANGELOG-{safe}.md");
    }

    /// <summary>
    /// Keeps only the file of <paramref name="tag"/>; a tag never changes, so the file never goes stale.
    /// </summary>
    private void WriteCache(string tag, string text)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = CachePath(tag);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);

            foreach (var old in Directory.GetFiles(_cacheDir, "CHANGELOG-*.md"))
            {
                if (!string.Equals(old, path, StringComparison.Ordinal))
                    File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not cache the changelog of {Tag}", tag);
        }
    }

    /// <summary>
    /// Versions newer than <paramref name="since"/> and not newer than <paramref name="upTo"/>, newest first.
    /// </summary>
    public static IReadOnlyList<ChangelogVersion> Between(IReadOnlyList<ChangelogVersion> all, string since, string upTo)
    {
        var from = since.Trim().TrimStart('v');
        var to = upTo.Trim().TrimStart('v');
        return all.Where(v => VersionUtil.Compare(v.Version, from) > 0 && VersionUtil.Compare(v.Version, to) <= 0).ToList();
    }

    /// <summary>
    /// Counts the top-level items of <paramref name="versions"/> and takes the first <paramref name="top"/> changes:
    /// additions first, then changes, then fixes, each newest release first.
    /// </summary>
    public static ChangelogSummary Summarize(IReadOnlyList<ChangelogVersion> versions, int top = 3)
    {
        IEnumerable<ChangelogItem> ItemsOf(ChangelogKind kind)
            => versions.SelectMany(v => v.Groups.Where(g => g.Kind == kind).SelectMany(g => g.Items));

        return new ChangelogSummary(
            ItemsOf(ChangelogKind.Added).Count(),
            ItemsOf(ChangelogKind.Changed).Count(),
            ItemsOf(ChangelogKind.Fixed).Count(),
            ItemsOf(ChangelogKind.Removed).Count(),
            TopOrder.SelectMany(ItemsOf).Count(),
            TopOrder.SelectMany(ItemsOf).Take(top).ToList());
    }

    private static ChangelogKind KindOf(string? title) => title?.Trim().ToLowerInvariant() switch
    {
        "added" or "add" => ChangelogKind.Added,
        "changed" or "change" => ChangelogKind.Changed,
        "fixed" or "fix" => ChangelogKind.Fixed,
        "removed" or "remove" => ChangelogKind.Removed,
        "obsolete" => ChangelogKind.Obsolete,
        "dev" => ChangelogKind.Dev,
        _ => ChangelogKind.Notes,
    };

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
            // A heading without items (an empty "### Removed") shows nothing.
            if (roots.Count > 0)
                groups.Add(new ChangelogGroup(groupTitle ?? string.Empty, KindOf(groupTitle), roots.Select(r => r.Freeze()).ToList()));
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

            var group = GroupHeader.Match(line);
            if (group.Success)
            {
                CloseGroup();
                groupTitle = group.Groups["t"].Value;
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
