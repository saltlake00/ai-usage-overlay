using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

internal sealed class MultiProviderCoordinator
{
    private readonly IReadOnlyList<IUsageProvider> providers;
    private readonly Dictionary<string, ProviderUsageState> states;
    private readonly object sync = new();

    public MultiProviderCoordinator(IReadOnlyList<IUsageProvider> providers)
    {
        this.providers = providers ?? throw new ArgumentNullException(nameof(providers));
        this.states = providers.ToDictionary(
            provider => provider.Id,
            provider => new ProviderUsageState(provider.Id, ProviderAvailability.Waiting, null, null),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ProviderUsageState>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(this.providers.Select(provider => this.FetchOneAsync(provider, cancellationToken)));
        foreach (var state in results)
        {
            lock (this.sync)
            {
                this.states[state.Id] = state;
            }
        }

        return this.CurrentStates;
    }

    public IReadOnlyList<ProviderUsageState> CurrentStates
    {
        get
        {
            lock (this.sync)
            {
                return this.providers.Select(provider => this.states[provider.Id]).ToArray();
            }
        }
    }

    public async Task<ProviderUsageState> RefreshOneAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = this.providers.FirstOrDefault(provider =>
            provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(providerId));
        var state = await this.FetchOneAsync(provider, cancellationToken);
        lock (this.sync)
        {
            this.states[state.Id] = state;
        }

        return state;
    }

    private async Task<ProviderUsageState> FetchOneAsync(
        IUsageProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await provider.FetchAsync(cancellationToken);
            return new ProviderUsageState(provider.Id, ProviderAvailability.Current, snapshot, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ProviderUsageState previous;
            lock (this.sync)
            {
                previous = this.states[provider.Id];
            }

            // Keep the provider's own message: the credential sources and clients
            // phrase these as the next action to take ("run `claude` to sign in"),
            // and they are written never to echo a token or cookie. Collapsing them
            // into one generic string left every failure undiagnosable.
            return new ProviderUsageState(
                provider.Id,
                ProviderAvailability.Failed,
                previous.LastSuccessful,
                exception is IActionableProviderError && !string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.Message
                    : "Usage unavailable");
        }
    }
}
