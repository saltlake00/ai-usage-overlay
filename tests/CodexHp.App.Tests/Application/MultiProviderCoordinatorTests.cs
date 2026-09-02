using CodexHp.App.Application;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class MultiProviderCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_keeps_successful_providers_when_one_provider_fails()
    {
        var coordinator = new MultiProviderCoordinator([
            new StubProvider("codex", () => Task.FromResult(Snapshot("codex", 80, 60))),
            new StubProvider("claude", () => throw new HttpRequestException("offline secret-token")),
            new StubProvider("ollama", () => Task.FromResult(Snapshot("ollama", 55, 44))),
        ]);

        var result = await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(["codex", "claude", "ollama"], result.Select(item => item.Id));
        Assert.Equal(ProviderAvailability.Current, result[0].Availability);
        Assert.Equal(ProviderAvailability.Failed, result[1].Availability);
        Assert.Equal(ProviderAvailability.Current, result[2].Availability);
        Assert.Null(result[1].LastSuccessful);
        Assert.DoesNotContain("secret-token", result[1].Error ?? string.Empty);
    }

    [Fact]
    public async Task RefreshAsync_surfaces_an_authored_provider_message_so_the_failure_is_actionable()
    {
        var coordinator = new MultiProviderCoordinator([
            new StubProvider("claude", () => throw new ActionableStubException(
                "Claude Code is not signed in. Run `claude` to sign in.")),
        ]);

        var state = Assert.Single(await coordinator.RefreshAsync(CancellationToken.None));

        Assert.Equal(ProviderAvailability.Failed, state.Availability);
        Assert.Equal("Claude Code is not signed in. Run `claude` to sign in.", state.Error);
    }

    [Fact]
    public async Task RefreshAsync_retains_the_last_successful_value_after_a_later_failure()
    {
        var calls = 0;
        var coordinator = new MultiProviderCoordinator([
            new StubProvider("codex", () => ++calls == 1
                ? Task.FromResult(Snapshot("codex", 80, 60))
                : throw new HttpRequestException("offline")),
        ]);

        await coordinator.RefreshAsync(CancellationToken.None);
        var result = await coordinator.RefreshAsync(CancellationToken.None);

        var state = Assert.Single(result);
        Assert.Equal(ProviderAvailability.Failed, state.Availability);
        Assert.Equal(80, state.LastSuccessful?.ShortWindow.RemainingPercent);
    }

    [Fact]
    public async Task RefreshOneAsync_polls_only_the_selected_provider()
    {
        var claudeCalls = 0;
        var ollamaCalls = 0;
        var coordinator = new MultiProviderCoordinator([
            new StubProvider("claude", () =>
            {
                claudeCalls++;
                return Task.FromResult(Snapshot("claude", 80, 60));
            }),
            new StubProvider("ollama", () =>
            {
                ollamaCalls++;
                return Task.FromResult(Snapshot("ollama", 55, 44));
            }),
        ]);

        var state = await coordinator.RefreshOneAsync("claude", CancellationToken.None);

        Assert.Equal(ProviderAvailability.Current, state.Availability);
        Assert.Equal(1, claudeCalls);
        Assert.Equal(0, ollamaCalls);
    }

    private static ProviderUsageSnapshot Snapshot(string id, double shortRemaining, double weeklyRemaining) => new(
        id,
        id,
        new UsageWindow(shortRemaining, null, TimeSpan.FromHours(5)),
        new UsageWindow(weeklyRemaining, null, TimeSpan.FromDays(7)),
        DateTimeOffset.Parse("2026-09-02T00:00:00Z"));

    private sealed class ActionableStubException(string message)
        : Exception(message), IActionableProviderError;

    private sealed class StubProvider(string id, Func<Task<ProviderUsageSnapshot>> fetch) : IUsageProvider
    {
        public string Id => id;

        public Task<ProviderUsageSnapshot> FetchAsync(CancellationToken cancellationToken = default) => fetch();
    }
}
