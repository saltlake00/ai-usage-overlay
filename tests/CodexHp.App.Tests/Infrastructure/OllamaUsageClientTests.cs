using System.Net;
using System.Text;
using CodexHp.App.Infrastructure.Ollama;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OllamaUsageClientTests
{
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
    public async Task FetchAsync_does_not_invent_quota_when_only_an_api_key_is_available()
    {
        var client = new OllamaUsageClient(new HttpClient(new FixtureHandler()));

        var error = await Assert.ThrowsAsync<OllamaUsageException>(() => client.FetchAsync(
            new OllamaCredentials(null, "ollama-api-key"),
            CancellationToken.None));

        Assert.Contains("does not expose Cloud quota windows", error.Message);
        Assert.DoesNotContain("ollama-api-key", error.ToString());
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

    private static HttpResponseMessage Html(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/html"),
    };

    private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<(string Uri, string? Cookie)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add((
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null));
            return Task.FromResult(this.responses.Dequeue());
        }
    }
}
