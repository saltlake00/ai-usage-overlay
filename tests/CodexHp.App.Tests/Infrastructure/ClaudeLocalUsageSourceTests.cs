using System.IO;
using CodexHp.App.Infrastructure.Claude;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeLocalUsageSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
    private readonly string root = Directory.CreateTempSubdirectory("claude-local-usage-").FullName;

    [Fact]
    public void Read_counts_input_and_output_tokens_inside_each_window()
    {
        this.WriteTranscript(
            "session-a",
            Entry("2026-09-02T11:30:00Z", input: 100, output: 20),
            Entry("2026-09-02T06:00:00Z", input: 5, output: 5),
            Entry("2026-08-30T09:00:00Z", input: 1000, output: 1));

        var usage = this.Read();

        Assert.Equal(120, usage.ShortTokens);
        Assert.Equal(1131, usage.WeeklyTokens);
        Assert.Equal(1, usage.ShortMessages);
    }

    [Fact]
    public void Read_excludes_cache_tokens_so_cache_reads_cannot_dominate()
    {
        this.WriteTranscript(
            "session-a",
            Entry("2026-09-02T11:30:00Z", input: 2, output: 8, cacheRead: 9_000_000, cacheCreate: 500_000));

        Assert.Equal(10, this.Read().ShortTokens);
    }

    [Fact]
    public void Read_ignores_entries_older_than_the_weekly_window()
    {
        this.WriteTranscript("session-a", Entry("2026-08-20T11:30:00Z", input: 400, output: 400));

        var usage = this.Read();

        Assert.Equal(0, usage.ShortTokens);
        Assert.Equal(0, usage.WeeklyTokens);
    }

    [Fact]
    public void Read_sums_across_nested_project_directories()
    {
        this.WriteTranscript("project-a/session-1", Entry("2026-09-02T11:00:00Z", input: 10, output: 10));
        this.WriteTranscript("project-b/session-2", Entry("2026-09-02T11:00:00Z", input: 30, output: 0));

        Assert.Equal(50, this.Read().ShortTokens);
    }

    [Fact]
    public void Read_skips_malformed_lines_without_losing_the_rest_of_the_file()
    {
        var path = Path.Combine(this.root, "session-a.jsonl");
        File.WriteAllLines(path, [
            "{ not json but mentions \"usage\"",
            "",
            """{"timestamp":"2026-09-02T11:00:00Z","message":{"usage":{"input_tokens":7,"output_tokens":3}}}""",
        ]);

        Assert.Equal(10, this.Read().ShortTokens);
    }

    [Fact]
    public void Read_returns_zero_when_no_transcripts_exist()
    {
        var usage = new ClaudeLocalUsageSource(
            Path.Combine(this.root, "missing"),
            () => Now).Read();

        Assert.Equal(0, usage.ShortTokens);
        Assert.Equal(0, usage.WeeklyTokens);
        Assert.Equal(Now, usage.ObservedAt);
    }

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    private ClaudeLocalUsage Read() => new ClaudeLocalUsageSource(this.root, () => Now).Read();

    private static string Entry(
        string timestamp,
        long input,
        long output,
        long cacheRead = 0,
        long cacheCreate = 0) =>
        "{\"timestamp\":\"" + timestamp + "\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{"
        + "\"input_tokens\":" + input
        + ",\"output_tokens\":" + output
        + ",\"cache_read_input_tokens\":" + cacheRead
        + ",\"cache_creation_input_tokens\":" + cacheCreate
        + "}}}";

    private void WriteTranscript(string name, params string[] entries)
    {
        var path = Path.Combine(this.root, name + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, entries);
    }
}
