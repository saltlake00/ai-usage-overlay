using System.Net;
using System.Text;
using CodexHp.App.Infrastructure.Claude;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeUsageClientTests
{
    [Fact]
    public async Task FetchAsync_converts_Claude_Code_used_percent_to_remaining_percent()
    {
        var handler = new FixtureHandler(
            Json(HttpStatusCode.OK, """
                {
                  "fiveHour": {
                    "utilization": 27.5,
                    "resetsAt": "2026-09-01T05:00:00Z"
                  },
                  "sevenDay": {
                    "utilization": 61.0,
                    "resetsAt": "2026-09-08T00:00:00Z"
                  },
                  "sevenDayOpus": null,
                  "extraUsage": { "isEnabled": false }
                }
                """));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var result = await client.FetchAsync(
            new ClaudeCredentials("secret-value"),
            CancellationToken.None);

        Assert.Equal("claude", result.Id);
        Assert.Equal(72.5, result.ShortWindow.RemainingPercent, 3);
        Assert.Equal(39, result.WeeklyWindow.RemainingPercent, 3);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T05:00:00Z"), result.ShortWindow.ResetsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T00:00:00Z"), result.WeeklyWindow.ResetsAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.Uri);
        Assert.Equal("Bearer secret-value", request.Authorization);
        Assert.Equal("oauth-2025-04-20", request.AnthropicBeta);
    }

    [Fact]
    public async Task FetchAsync_accepts_snake_case_quota_windows()
    {
        var handler = new FixtureHandler(
            Json(HttpStatusCode.OK, """
                {
                  "five_hour": { "utilization": 10, "resets_at": "2026-09-01T05:00:00Z" },
                  "seven_day": { "utilization": 20, "resets_at": "2026-09-08T00:00:00Z" }
                }
                """));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var result = await client.FetchAsync(new ClaudeCredentials("token"), CancellationToken.None);

        Assert.Equal(90, result.ShortWindow.RemainingPercent, 3);
        Assert.Equal(80, result.WeeklyWindow.RemainingPercent, 3);
    }

    [Fact]
    public async Task FetchAsync_reports_authentication_failure_without_echoing_the_token()
    {
        var handler = new FixtureHandler(Json(HttpStatusCode.Unauthorized, """{"error":"expired"}"""));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<UsageProviderException>(() => client.FetchAsync(
            new ClaudeCredentials("top-secret"),
            CancellationToken.None));

        Assert.Contains("sign in again", error.Message);
        Assert.DoesNotContain("top-secret", error.ToString());
    }

    [Fact]
    public async Task FetchAsync_reports_schema_failure_without_returning_raw_response()
    {
        var handler = new FixtureHandler(Json(HttpStatusCode.OK, """{"fiveHour":{"utilization":5}}"""));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<UsageProviderException>(() => client.FetchAsync(
            new ClaudeCredentials("token"),
            CancellationToken.None));

        Assert.Equal("Claude usage response did not include both quota windows.", error.Message);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<(string Uri, string? Authorization, string? AnthropicBeta)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add((
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("anthropic-beta", out var values) ? values.Single() : null));
            return Task.FromResult(this.responses.Dequeue());
        }
    }
}
