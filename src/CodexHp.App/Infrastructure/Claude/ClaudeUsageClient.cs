using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure.Claude;

internal sealed record ClaudeCredentials(string CookieHeader);

internal sealed class UsageProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class ClaudeUsageClient(HttpClient httpClient)
{
    private static readonly Uri AccountUri = new("https://claude.ai/api/account");
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<ProviderUsageSnapshot> FetchAsync(
        ClaudeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.CookieHeader);

        var account = await this.GetJsonAsync<AccountResponse>(
            AccountUri,
            credentials.CookieHeader,
            "account",
            cancellationToken);
        var organizationId = account.Memberships
            .Select(membership => membership.Organization?.Uuid ?? membership.Uuid)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new UsageProviderException("Claude account did not expose an organization.");
        }

        var usageUri = new Uri($"https://claude.ai/api/organizations/{Uri.EscapeDataString(organizationId)}/usage");
        var usage = await this.GetJsonAsync<UsageResponse>(
            usageUri,
            credentials.CookieHeader,
            "usage",
            cancellationToken);
        if (usage.FiveHour is null || usage.SevenDay is null)
        {
            throw new UsageProviderException("Claude usage response did not include both quota windows.");
        }

        return new ProviderUsageSnapshot(
            "claude",
            "Claude",
            ToWindow(usage.FiveHour, TimeSpan.FromHours(5)),
            ToWindow(usage.SevenDay, TimeSpan.FromDays(7)),
            DateTimeOffset.UtcNow);
    }

    private async Task<T> GetJsonAsync<T>(
        Uri uri,
        string cookieHeader,
        string responseName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Referrer = new Uri("https://claude.ai/settings/usage");
        request.Headers.Add("Origin", "https://claude.ai");

        using var response = await this.httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UsageProviderException("Claude authentication failed. Sign in again or update the session cookie.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new UsageProviderException($"Claude {responseName} request failed with status {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
                ?? throw new UsageProviderException($"Claude {responseName} response was empty.");
        }
        catch (JsonException exception)
        {
            throw new UsageProviderException($"Claude {responseName} response format changed.", exception);
        }
    }

    private static UsageWindow ToWindow(UsageWindowResponse window, TimeSpan duration) =>
        UsageWindow.FromUsedPercent(
            window.Utilization ?? 0,
            ParseTimestamp(window.ResetsAt),
            duration);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class AccountResponse
    {
        [JsonPropertyName("memberships")]
        public AccountMembership[] Memberships { get; init; } = [];
    }

    private sealed record AccountMembership(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("organization")] AccountOrganization? Organization);

    private sealed record AccountOrganization(
        [property: JsonPropertyName("uuid")] string? Uuid);

    private sealed record UsageResponse(
        [property: JsonPropertyName("five_hour")] UsageWindowResponse? FiveHour,
        [property: JsonPropertyName("seven_day")] UsageWindowResponse? SevenDay);

    private sealed record UsageWindowResponse(
        [property: JsonPropertyName("utilization")] double? Utilization,
        [property: JsonPropertyName("resets_at")] string? ResetsAt);
}
