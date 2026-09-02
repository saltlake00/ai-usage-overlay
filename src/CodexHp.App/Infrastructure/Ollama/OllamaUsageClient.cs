using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure.Ollama;

internal sealed partial class OllamaUsageClient
{
    private static readonly Uri SettingsUri = new("https://ollama.com/settings");
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
        if (string.IsNullOrWhiteSpace(credentials.CookieHeader))
        {
            throw new OllamaUsageException(
                "An Ollama API key validates Cloud access but does not expose Cloud quota windows. Configure OLLAMA_SESSION_COOKIE.");
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
            throw new OllamaUsageException("Ollama Cloud authentication failed. Sign in again or update the session cookie.");
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
            throw new OllamaUsageException("Ollama Cloud authentication failed. Sign in again or update the session cookie.");
        }

        return ParseUsageHtml(html);
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

    private static bool IsSignInUri(Uri uri) =>
        uri.Host.Equals("signin.ollama.com", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.Contains("signin", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase);

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
