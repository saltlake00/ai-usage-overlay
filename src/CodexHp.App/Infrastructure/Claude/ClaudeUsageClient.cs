using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure.Claude;

internal sealed class UsageProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// Reads the same quota the `claude` CLI reports, via the OAuth usage endpoint that
// Claude Code itself authenticates against. The earlier implementation scraped
// claude.ai with a browser session cookie, which reported *web* usage instead.
internal sealed class ClaudeUsageClient(HttpClient httpClient)
{
    private static readonly Uri UsageUri = new("https://api.anthropic.com/api/oauth/usage");
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<ProviderUsageSnapshot> FetchAsync(
        ClaudeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.AccessToken);

        var usage = await this.GetUsageAsync(credentials.AccessToken, cancellationToken);
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

    private async Task<UsageResponse> GetUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);

        using var response = await this.httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UsageProviderException(
                "Claude Code authentication was rejected. Run `claude` to sign in again.");
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            throw new UsageProviderException("Claude usage endpoint is rate limited. It will retry on the next poll.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new UsageProviderException($"Claude usage request failed with status {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<UsageResponse>(stream, cancellationToken: cancellationToken)
                ?? throw new UsageProviderException("Claude usage response was empty.");
        }
        catch (JsonException exception)
        {
            throw new UsageProviderException("Claude usage response format changed.", exception);
        }
    }

    private static UsageWindow ToWindow(UsageWindowResponse window, TimeSpan duration) =>
        UsageWindow.FromUsedPercent(
            window.Utilization ?? 0,
            ParseTimestamp(window.ResetsAt),
            duration);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    // The endpoint returns camelCase; snake_case is accepted as well because the
    // upstream payload has carried both spellings across versions.
    private sealed class UsageResponse
    {
        [JsonPropertyName("fiveHour")]
        public UsageWindowResponse? FiveHourCamel { get; init; }

        [JsonPropertyName("five_hour")]
        public UsageWindowResponse? FiveHourSnake { get; init; }

        [JsonPropertyName("sevenDay")]
        public UsageWindowResponse? SevenDayCamel { get; init; }

        [JsonPropertyName("seven_day")]
        public UsageWindowResponse? SevenDaySnake { get; init; }

        [JsonIgnore]
        public UsageWindowResponse? FiveHour => this.FiveHourCamel ?? this.FiveHourSnake;

        [JsonIgnore]
        public UsageWindowResponse? SevenDay => this.SevenDayCamel ?? this.SevenDaySnake;
    }

    private sealed class UsageWindowResponse
    {
        [JsonPropertyName("utilization")]
        public double? Utilization { get; init; }

        [JsonPropertyName("resetsAt")]
        public string? ResetsAtCamel { get; init; }

        [JsonPropertyName("resets_at")]
        public string? ResetsAtSnake { get; init; }

        [JsonIgnore]
        public string? ResetsAt => this.ResetsAtCamel ?? this.ResetsAtSnake;
    }
}
