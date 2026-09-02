using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ProviderUsageCacheTests
{
    [Fact]
    public async Task SaveAsync_persists_only_display_data_and_round_trips_snapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ai-usage-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "usage.json");
        var cache = new ProviderUsageCache(path);
        var snapshot = new ProviderUsageSnapshot(
            "claude",
            "Claude",
            new UsageWindow(72.5, DateTimeOffset.Parse("2026-09-02T05:00:00Z"), TimeSpan.FromHours(5)),
            new UsageWindow(39, DateTimeOffset.Parse("2026-09-09T00:00:00Z"), TimeSpan.FromDays(7)),
            DateTimeOffset.Parse("2026-09-02T00:00:00Z"));

        try
        {
            await cache.SaveAsync([snapshot], CancellationToken.None);
            var raw = await File.ReadAllTextAsync(path);
            var restored = await cache.LoadAsync(CancellationToken.None);

            Assert.DoesNotContain("cookie", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(snapshot, Assert.Single(restored));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
