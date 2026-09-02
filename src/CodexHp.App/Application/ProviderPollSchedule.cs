namespace CodexHp.App.Application;

public enum PollOutcome
{
    Success,
    Failure,
}

public sealed class ProviderPollSchedule
{
    private const int MaximumFailureSeconds = 900;
    private int consecutiveFailures;

    public TimeSpan NextDelay(PollOutcome outcome, bool hidden)
    {
        if (outcome == PollOutcome.Success)
        {
            this.consecutiveFailures = 0;
            return hidden ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(60);
        }

        var exponent = Math.Min(this.consecutiveFailures, 4);
        this.consecutiveFailures++;
        var seconds = Math.Min(60 * (1 << exponent), MaximumFailureSeconds);
        if (hidden)
        {
            seconds = Math.Max(seconds, 180);
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
