using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

internal interface IUsageProvider
{
    string Id { get; }

    Task<ProviderUsageSnapshot> FetchAsync(CancellationToken cancellationToken = default);
}

internal sealed class DelegateUsageProvider(
    string id,
    Func<CancellationToken, Task<ProviderUsageSnapshot>> fetch) : IUsageProvider
{
    public string Id { get; } = id;

    public Task<ProviderUsageSnapshot> FetchAsync(CancellationToken cancellationToken = default) =>
        fetch(cancellationToken);
}

internal sealed record ProviderUsageState(
    string Id,
    ProviderAvailability Availability,
    ProviderUsageSnapshot? LastSuccessful,
    string? Error);
