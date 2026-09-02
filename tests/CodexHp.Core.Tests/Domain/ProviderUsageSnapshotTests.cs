using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class ProviderUsageSnapshotTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(27.5, 72.5)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(140, 0)]
    public void Usage_window_converts_used_percent_to_clamped_remaining_percent(
        double usedPercent,
        double expectedRemaining)
    {
        var window = UsageWindow.FromUsedPercent(usedPercent, null, null);

        Assert.Equal(expectedRemaining, window.RemainingPercent, 3);
    }

    [Fact]
    public void Snapshot_requires_stable_provider_identity()
    {
        var shortWindow = UsageWindow.FromUsedPercent(20, null, TimeSpan.FromHours(5));
        var weeklyWindow = UsageWindow.FromUsedPercent(40, null, TimeSpan.FromDays(7));

        var snapshot = new ProviderUsageSnapshot(
            "claude",
            "Claude",
            shortWindow,
            weeklyWindow,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"));

        Assert.Equal("claude", snapshot.Id);
        Assert.Equal(80, snapshot.ShortWindow.RemainingPercent);
        Assert.Equal(60, snapshot.WeeklyWindow.RemainingPercent);
    }
}
