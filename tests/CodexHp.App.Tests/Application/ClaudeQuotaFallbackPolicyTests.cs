using CodexHp.App.Application;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ClaudeQuotaFallbackPolicyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-02T15:00:00Z");

    [Fact]
    public void Counts_local_transcripts_when_the_quota_has_never_been_read()
    {
        Assert.True(new ClaudeQuotaFallbackPolicy().ShouldCountLocalTranscripts(Start));
    }

    [Fact]
    public void Keeps_the_last_quota_through_a_transient_failure()
    {
        var policy = new ClaudeQuotaFallbackPolicy();
        policy.RecordQuotaSuccess(Start);

        Assert.False(policy.ShouldCountLocalTranscripts(Start + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Falls_back_once_the_last_quota_is_too_old_to_trust()
    {
        var policy = new ClaudeQuotaFallbackPolicy();
        policy.RecordQuotaSuccess(Start);

        Assert.True(policy.ShouldCountLocalTranscripts(Start + TimeSpan.FromMinutes(31)));
    }
}
