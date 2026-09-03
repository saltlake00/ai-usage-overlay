using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexHp.App.Accounts;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure.Ollama;

internal sealed partial class OllamaUsageClient
{
    private static readonly Uri SettingsUri = new("https://ollama.com/settings");
    private static readonly Uri UsageApiUri = new("https://ollama.com/api/usage");
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private readonly HttpClient httpClient;
    private readonly Action invalidateSession;

    public OllamaUsageClient(HttpClient httpClient, Action? invalidateSession = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.invalidateSession = invalidateSession ?? (() => { });
    }

    public async Task<ProviderUsageSnapshot> FetchAsync(
        OllamaCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        // An API key hits the official JSON endpoint - no cookie rotation, no
        // HTML to parse, no relative-redirect edge case. Prefer it whenever one
        // is configured; the cookie scrape below only runs without one.
        if (!string.IsNullOrWhiteSpace(credentials.ApiKey))
        {
            return await this.FetchViaApiAsync(credentials.ApiKey, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(credentials.CookieHeader))
        {
            throw new OllamaUsageException(
                "Ollama Cloud is not configured. Set OLLAMA_API_KEY (from ollama.com/settings/keys) to show quota windows.")
            {
                Kind = ProviderErrorKind.UnsupportedFormat,
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, SettingsUri);
        request.Headers.TryAddWithoutValidation("Cookie", credentials.CookieHeader);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        using var response = await this.httpClient.SendAsync(request, cancellationToken);

        if (IsAuthenticationFailure(response))
        {
            this.invalidateSession();
            throw new OllamaUsageException("Ollama Cloud authentication failed. Sign in again or update the session cookie.")
            {
                Kind = ProviderErrorKind.Authentication,
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OllamaUsageException($"Ollama settings request failed with status {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (html.Contains("Sign in", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("Session usage", StringComparison.OrdinalIgnoreCase))
        {
            this.invalidateSession();
            throw new OllamaUsageException("Ollama Cloud authentication failed. Sign in again or update the session cookie.")
            {
                Kind = ProviderErrorKind.Authentication,
            };
        }

        return ParseUsageHtml(html);
    }

    // Official endpoint - GET https://ollama.com/api/usage, Authorization: Bearer
    // {key}. Confirmed against the response shape used by community Ollama Cloud
    // usage tools (e.g. mpartipilo/ollama-cloud-usage); no session cookie or HTML
    // scraping involved.
    private async Task<ProviderUsageSnapshot> FetchViaApiAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageApiUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await this.httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            this.invalidateSession();
            throw new OllamaUsageException("Ollama API key was rejected. Create a new key at ollama.com/settings/keys.")
            {
                Kind = ProviderErrorKind.Authentication,
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OllamaUsageException($"Ollama usage API request failed with status {(int)response.StatusCode}.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new OllamaUsageException("Ollama usage API returned invalid JSON.", exception);
        }

        using (document)
        {
            return ParseUsageJson(document.RootElement);
        }
    }

    // The API returns usage as a used-fraction (0..1), not a remaining percent,
    // and exposes no reset timestamp for either window. The weekly window's
    // billing-period start is derivable (a weekday 00:00 UTC boundary that
    // repeats every 7 days), so the next reset is stepped forward from it. The
    // session window is a rolling 5h block with no anchor in the response, so
    // its reset is left null rather than guessed.
    internal static ProviderUsageSnapshot ParseUsageJson(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits))
        {
            throw new OllamaUsageException("Ollama usage API response did not include quota limits.");
        }

        var sessionUsed = ReadUsageFraction(limits, "session");
        var weeklyUsed = ReadUsageFraction(limits, "weekly");
        if (sessionUsed is null || weeklyUsed is null)
        {
            throw new OllamaUsageException("Ollama usage API response did not expose both Cloud quota windows.");
        }

        return new ProviderUsageSnapshot(
            "ollama",
            "Ollama",
            UsageWindow.FromUsedPercent(sessionUsed.Value * 100, null, TimeSpan.FromHours(5)),
            UsageWindow.FromUsedPercent(weeklyUsed.Value * 100, NextWeeklyReset(root), Week),
            DateTimeOffset.UtcNow);
    }

    private static double? ReadUsageFraction(JsonElement limits, string windowKey)
    {
        if (!limits.TryGetProperty(windowKey, out var window)
            || !window.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return usage.GetDouble();
    }

    private static DateTimeOffset? NextWeeklyReset(JsonElement root)
    {
        if (!root.TryGetProperty("activity", out var activity)
            || !activity.TryGetProperty("period", out var period)
            || !period.TryGetProperty("starting_at", out var startingAtProperty)
            || startingAtProperty.GetString() is not { } startingAtText
            || !DateTimeOffset.TryParse(
                startingAtText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var startingAt))
        {
            return null;
        }

        var elapsedWeeks = Math.Floor((DateTimeOffset.UtcNow - startingAt) / Week);
        return startingAt + ((elapsedWeeks + 1) * Week);
    }

    internal static ProviderUsageSnapshot ParseUsageHtml(string html)
    {
        var shortWindow = ParseWindow(html, ["Session usage", "Hourly usage"], TimeSpan.FromHours(5));
        var weeklyWindow = ParseWindow(html, ["Weekly usage"], TimeSpan.FromDays(7));
        if (shortWindow is null || weeklyWindow is null)
        {
            throw new OllamaUsageException("Ollama settings did not expose both Cloud quota windows.");
        }

        return new ProviderUsageSnapshot(
            "ollama",
            "Ollama",
            shortWindow,
            weeklyWindow,
            DateTimeOffset.UtcNow);
    }

    private static UsageWindow? ParseWindow(string html, string[] labels, TimeSpan duration)
    {
        foreach (var label in labels)
        {
            var start = html.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            var end = FindNextWindowStart(html, start + label.Length);
            var length = Math.Min((end < 0 ? html.Length : end) - start, 4000);
            var block = html.Substring(start, Math.Max(0, length));
            var percent = UsedPercentRegex().Match(block);
            if (!percent.Success)
            {
                percent = WidthPercentRegex().Match(block);
            }

            if (!percent.Success || !double.TryParse(
                    percent.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var usedPercent))
            {
                continue;
            }

            var timestamp = TimestampRegex().Match(block);
            var resetsAt = timestamp.Success && DateTimeOffset.TryParse(timestamp.Value, out var parsed)
                ? parsed
                : null as DateTimeOffset?;
            return UsageWindow.FromUsedPercent(usedPercent, resetsAt, duration);
        }

        return null;
    }

    private static int FindNextWindowStart(string html, int searchStart)
    {
        var positions = new[] { "Session usage", "Hourly usage", "Weekly usage" }
            .Select(label => html.IndexOf(label, searchStart, StringComparison.OrdinalIgnoreCase))
            .Where(position => position >= 0)
            .ToArray();
        return positions.Length == 0 ? -1 : positions.Min();
    }

    private static bool IsAuthenticationFailure(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        if (!response.StatusCode.IsRedirect())
        {
            return response.RequestMessage?.RequestUri is { } finalUri && IsSignInUri(finalUri);
        }

        return response.Headers.Location is { } location && IsSignInUri(location);
    }

    // A redirect's Location header is allowed to be relative (RFC 7231 does not
    // require an authority), and ollama.com's own sign-in redirect is one - a bare
    // "/signin?...". uri.Host throws InvalidOperationException on a relative Uri,
    // which turned every session-expired redirect into an unhandled crash instead
    // of the "sign in again" message this check exists to produce.
    private static bool IsSignInUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return uri.OriginalString.Contains("signin", StringComparison.OrdinalIgnoreCase)
                || uri.OriginalString.Contains("login", StringComparison.OrdinalIgnoreCase);
        }

        return uri.Host.Equals("signin.ollama.com", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("signin", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%\s*used", RegexOptions.IgnoreCase)]
    private static partial Regex UsedPercentRegex();

    [GeneratedRegex(@"width:\s*(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex WidthPercentRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})")]
    private static partial Regex TimestampRegex();
}

internal static class HttpStatusCodeExtensions
{
    public static bool IsRedirect(this HttpStatusCode status) => (int)status is >= 300 and < 400;
}
