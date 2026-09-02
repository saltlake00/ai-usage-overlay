using CodexHp.App.Application;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ProviderPollScheduleTests
{
    [Fact]
    public void Successful_visible_poll_uses_the_sixty_second_budget()
    {
        var schedule = new ProviderPollSchedule();

        Assert.Equal(TimeSpan.FromSeconds(60), schedule.NextDelay(PollOutcome.Success, hidden: false));
    }

    [Fact]
    public void Hidden_poll_reduces_network_activity_to_once_every_three_minutes()
    {
        var schedule = new ProviderPollSchedule();

        Assert.Equal(TimeSpan.FromMinutes(3), schedule.NextDelay(PollOutcome.Success, hidden: true));
    }

    [Fact]
    public void Consecutive_failures_back_off_and_success_resets_the_sequence()
    {
        var schedule = new ProviderPollSchedule();

        Assert.Equal(TimeSpan.FromSeconds(60), schedule.NextDelay(PollOutcome.Failure, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(120), schedule.NextDelay(PollOutcome.Failure, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(240), schedule.NextDelay(PollOutcome.Failure, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(480), schedule.NextDelay(PollOutcome.Failure, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(900), schedule.NextDelay(PollOutcome.Failure, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(900), schedule.NextDelay(PollOutcome.Failure, hidden: false));

        Assert.Equal(TimeSpan.FromSeconds(60), schedule.NextDelay(PollOutcome.Success, hidden: false));
        Assert.Equal(TimeSpan.FromSeconds(60), schedule.NextDelay(PollOutcome.Failure, hidden: false));
    }
}
