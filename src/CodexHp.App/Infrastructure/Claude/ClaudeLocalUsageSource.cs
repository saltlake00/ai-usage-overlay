using System.IO;
using System.Text.Json;

namespace CodexHp.App.Infrastructure.Claude;

internal sealed record ClaudeLocalUsage(
    long ShortTokens,
    long WeeklyTokens,
    int ShortMessages,
    DateTimeOffset ObservedAt);

// Counts Claude Code's own transcript records under ~/.claude/projects.
//
// This needs no credentials and no network: Claude Code writes a `usage` block on
// every assistant message, so the totals are already on disk. Cache tokens are
// deliberately excluded — cache reads dominate the raw sum (129M of 134M in a
// measured 5-hour window) and would drown out the part that tracks real work.
internal sealed class ClaudeLocalUsageSource
{
    private static readonly TimeSpan ShortWindow = TimeSpan.FromHours(5);
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);
    private readonly string projectsRoot;
    private readonly Func<DateTimeOffset> clock;

    public ClaudeLocalUsageSource()
        : this(DefaultProjectsRoot(), null)
    {
    }

    internal ClaudeLocalUsageSource(string projectsRoot, Func<DateTimeOffset>? clock)
    {
        this.projectsRoot = string.IsNullOrWhiteSpace(projectsRoot)
            ? throw new ArgumentException("Projects root is required.", nameof(projectsRoot))
            : projectsRoot;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ClaudeLocalUsage Read(CancellationToken cancellationToken = default)
    {
        var now = this.clock();
        var shortStart = now - ShortWindow;
        var weeklyStart = now - WeeklyWindow;
        long shortTokens = 0;
        long weeklyTokens = 0;
        var shortMessages = 0;

        foreach (var path in this.TranscriptPaths(weeklyStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (timestamp, tokens) in ReadEntries(path))
            {
                if (timestamp < weeklyStart)
                {
                    continue;
                }

                weeklyTokens += tokens;
                if (timestamp >= shortStart)
                {
                    shortTokens += tokens;
                    shortMessages++;
                }
            }
        }

        return new ClaudeLocalUsage(shortTokens, weeklyTokens, shortMessages, now);
    }

    // A transcript last written before the window opened cannot hold an entry
    // inside it, so skipping those keeps a poll proportional to recent work
    // rather than to the whole history.
    private IEnumerable<string> TranscriptPaths(DateTimeOffset weeklyStart)
    {
        if (!Directory.Exists(this.projectsRoot))
        {
            yield break;
        }

        string[] paths;
        try
        {
            paths = Directory.GetFiles(this.projectsRoot, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in paths)
        {
            DateTimeOffset lastWrite;
            try
            {
                lastWrite = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (lastWrite >= weeklyStart)
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<(DateTimeOffset Timestamp, long Tokens)> ReadEntries(string path)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        using var enumerator = lines.GetEnumerator();
        while (true)
        {
            string line;
            try
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                line = enumerator.Current;
            }
            catch (IOException)
            {
                // The file is appended to while Claude Code runs; a torn read ends
                // this transcript instead of failing the whole poll.
                break;
            }

            if (string.IsNullOrEmpty(line) || !line.Contains("\"usage\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryReadEntry(line) is { } entry)
            {
                yield return entry;
            }
        }
    }

    private static (DateTimeOffset Timestamp, long Tokens)? TryReadEntry(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out var timestampElement)
                || timestampElement.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(timestampElement.GetString(), out var timestamp)
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var tokens = ReadLong(usage, "input_tokens") + ReadLong(usage, "output_tokens");
            return tokens <= 0 ? null : (timestamp.ToUniversalTime(), tokens);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var parsed)
        && parsed > 0
            ? parsed
            : 0;

    private static string DefaultProjectsRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");
}
