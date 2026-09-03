using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexHp.App.Infrastructure.Ollama;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OllamaUsageClientTests
{
    // The official GET https://ollama.com/api/usage endpoint (Authorization:
    // Bearer <key>) - no session cookie, no HTML to parse, no rotation to race.
    // Confirmed against the response shape used by community Ollama Cloud usage
    // tools (e.g. mpartipilo/ollama-cloud-usage).
    [Fact]
    public async Task FetchAsync_parses_session_and_weekly_usage_from_the_official_api()
    {
        var startingAt = DateTimeOffset.UtcNow.AddDays(-10);
        var json = $$"""
            {
              "limits": {
                "session": { "usage": 0.42 },
                "weekly": { "usage": 0.84 }
              },
              "activity": {
                "period": { "starting_at": "{{startingAt:O}}" }
              }
            }
            """;
        var handler = new FixtureHandler(Json(HttpStatusCode.OK, json));
        var client = new OllamaUsageClient(new HttpClient(handler));

        var result = await client.FetchAsync(
            new OllamaCredentials(null, "ollama-api-key"),
            CancellationToken.None);

        Assert.Equal("ollama", result.Id);
        Assert.Equal(58, result.ShortWindow.RemainingPercent);
        Assert.Equal(16, result.WeeklyWindow.RemainingPercent);
        Assert.Null(result.ShortWindow.ResetsAt);
        // starting_at was 10 days ago (1.43 weekly blocks); the next 7-day
        // boundary after now is starting_at + 14d, ~4 days from now.
        Assert.NotNull(result.WeeklyWindow.ResetsAt);
        Assert.InRange(
            (result.WeeklyWindow.ResetsAt!.Value - (startingAt + TimeSpan.FromDays(14))).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("ollama-api-key", request.Authorization?.Parameter);
        Assert.Null(request.Cookie);
    }

    // Both credentials can be configured at once (a leftover cookie env var next
    // to a freshly added key). The API key is strictly better - no cookie
    // rotation, no HTML - so it wins and the cookie is never sent.
    [Fact]
    public async Task FetchAsync_prefers_the_api_key_over_a_cookie_when_both_are_configured()
    {
        const string json = """
            { "limits": { "session": { "usage": 0.1 }, "weekly": { "usage": 0.2 } } }
            """;
        var handler = new FixtureHandler(Json(HttpStatusCode.OK, json));
        var client = new OllamaUsageClient(new HttpClient(handler));

        await client.FetchAsync(
            new OllamaCredentials("__Secure-session=secret", "ollama-api-key"),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("ollama-api-key", request.Authorization?.Parameter);
        Assert.Null(request.Cookie);
    }

    [Fact]
    public async Task FetchAsync_invalidates_session_when_the_api_key_is_rejected()
    {
        var invalidated = false;
        var client = new OllamaUsageClient(
            new HttpClient(new FixtureHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized))),
            () => invalidated = true);

        var error = await Assert.ThrowsAsync<OllamaUsageException>(() => client.FetchAsync(
            new OllamaCredentials(null, "bad-key"),
            CancellationToken.None));

        Assert.True(invalidated);
        Assert.DoesNotContain("bad-key", error.ToString());
    }

    [Fact]
    public async Task FetchAsync_parses_session_and_weekly_remaining_usage_from_settings_html()
    {
        const string html = """
            <html><body>
              <section>Session usage <div style="width: 42%"></div><span>resets in 2h</span></section>
              <section>Weekly usage <span>84% used</span><time>2026-09-08T00:00:00Z</time></section>
            </body></html>
            """;
        var handler = new FixtureHandler(Html(HttpStatusCode.OK, html));
        var client = new OllamaUsageClient(new HttpClient(handler));

        var result = await client.FetchAsync(
            new OllamaCredentials("__Secure-session=secret", null),
            CancellationToken.None);

        Assert.Equal("ollama", result.Id);
        Assert.Equal(58, result.ShortWindow.RemainingPercent);
        Assert.Equal(16, result.WeeklyWindow.RemainingPercent);
        Assert.Equal(TimeSpan.FromHours(5), result.ShortWindow.Duration);
        Assert.Equal(TimeSpan.FromDays(7), result.WeeklyWindow.Duration);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T00:00:00Z"), result.WeeklyWindow.ResetsAt);
        Assert.Equal("__Secure-session=secret", Assert.Single(handler.Requests).Cookie);
    }

    [Fact]
    public async Task FetchAsync_does_not_invent_quota_when_neither_credential_is_available()
    {
        var client = new OllamaUsageClient(new HttpClient(new FixtureHandler()));

        var error = await Assert.ThrowsAsync<OllamaUsageException>(() => client.FetchAsync(
            new OllamaCredentials(null, null),
            CancellationToken.None));

        Assert.Contains("OLLAMA_API_KEY", error.Message);
    }

    [Fact]
    public async Task FetchAsync_invalidates_session_when_Ollama_redirects_to_sign_in()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://signin.ollama.com/");
        var invalidated = false;
        var client = new OllamaUsageClient(
            new HttpClient(new FixtureHandler(redirect)),
            () => invalidated = true);

        await Assert.ThrowsAsync<OllamaUsageException>(() => client.FetchAsync(
            new OllamaCredentials("__Secure-session=expired", null),
            CancellationToken.None));

        Assert.True(invalidated);
    }

    // RFC 7231 does not require a Location header to carry an authority, and
    // ollama.com's own sign-in redirect is a bare relative path. Before this
    // was fixed, IsSignInUri called uri.Host on a relative Uri and the
    // InvalidOperationException surfaced as an undiagnosable "Usage
    // unavailable" instead of the actionable message this test checks for.
    [Fact]
    public async Task FetchAsync_invalidates_session_when_the_sign_in_redirect_is_a_relative_location()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("/signin", UriKind.Relative);
        var invalidated = false;
        var client = new OllamaUsageClient(
            new HttpClient(new FixtureHandler(redirect)),
            () => invalidated = true);

        await Assert.ThrowsAsync<OllamaUsageException>(() => client.FetchAsync(
            new OllamaCredentials("__Secure-session=expired", null),
            CancellationToken.None));

        Assert.True(invalidated);
    }

    private static HttpResponseMessage Html(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/html"),
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record CapturedRequest(string Uri, string? Cookie, AuthenticationHeaderValue? Authorization);

    private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(new CapturedRequest(
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null,
                request.Headers.Authorization));
            return Task.FromResult(this.responses.Dequeue());
        }
    }
}
