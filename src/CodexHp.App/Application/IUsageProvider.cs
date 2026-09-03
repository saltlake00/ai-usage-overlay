using CodexHp.App.Accounts;
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
    string? Error,
    ProviderErrorKind ErrorKind = ProviderErrorKind.Other);

// Marks an exception whose message was authored for the user and is known not to
// carry a token or cookie. Only these messages survive into ProviderUsageState.Error;
// anything else collapses to a generic string so an arbitrary exception (a request
// URL, a parser dump) can never leak a secret onto the overlay.
internal interface IActionableProviderError
{
}
