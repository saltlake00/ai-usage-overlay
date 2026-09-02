namespace CodexHp.App.Application;

// Decides what a failed Claude quota read should show.
//
// Counting local transcripts is the right answer when the quota is simply not
// available (an expired token that Claude Code has not refreshed). It is the wrong
// answer for a transient failure such as a 429: returning a different metric reads
// as a success, so the poll schedule stops backing off and the row visibly flips
// between a percentage and a token count. While a recent quota exists, a failure
// stays a failure and the last good percentage is kept.
public sealed class ClaudeQuotaFallbackPolicy(TimeSpan? quotaTrustWindow = null)
{
    private readonly TimeSpan quotaTrustWindow = quotaTrustWindow ?? TimeSpan.FromMinutes(30);
    private DateTimeOffset? lastQuotaAt;

    public void RecordQuotaSuccess(DateTimeOffset observedAt) => this.lastQuotaAt = observedAt;

    public bool ShouldCountLocalTranscripts(DateTimeOffset now) =>
        this.lastQuotaAt is not { } last || now - last > this.quotaTrustWindow;
}
