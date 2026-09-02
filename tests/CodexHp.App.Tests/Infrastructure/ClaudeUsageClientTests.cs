using System.Net;
using System.Text;
using CodexHp.App.Infrastructure.Claude;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeUsageClientTests
{
    [Fact]
    public async Task FetchAsync_converts_Claude_used_percent_to_remaining_percent()
    {
        var handler = new FixtureHandler(
            Json(HttpStatusCode.OK, """
                {
                  "email_address": "user@example.com",
                  "rate_limit_tier": "pro",
                  "memberships": [
                    { "uuid": "membership-1", "organization": { "uuid": "org-123" } }
                  ]
                }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "five_hour": {
                    "utilization": 27.5,
                    "resets_at": "2026-09-01T05:00:00Z"
                  },
                  "seven_day": {
                    "utilization": 61.0,
                    "resets_at": "2026-09-08T00:00:00Z"
                  },
                  "seven_day_opus": null,
                  "seven_day_sonnet": null,
                  "extra_usage": { "is_enabled": false }
                }
                """));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var result = await client.FetchAsync(
            new ClaudeCredentials("sessionKey=secret-value"),
            CancellationToken.None);

        Assert.Equal("claude", result.Id);
        Assert.Equal(72.5, result.ShortWindow.RemainingPercent, 3);
        Assert.Equal(39, result.WeeklyWindow.RemainingPercent, 3);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T05:00:00Z"), result.ShortWindow.ResetsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T00:00:00Z"), result.WeeklyWindow.ResetsAt);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("https://claude.ai/api/account", request.Uri),
            request => Assert.Equal("https://claude.ai/api/organizations/org-123/usage", request.Uri));
        Assert.All(handler.Requests, request => Assert.Equal("sessionKey=secret-value", request.Cookie));
    }

    [Fact]
    public async Task FetchAsync_reports_authentication_failure_without_echoing_the_cookie()
    {
        var handler = new FixtureHandler(Json(HttpStatusCode.Unauthorized, """{"error":"expired"}"""));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<UsageProviderException>(() => client.FetchAsync(
            new ClaudeCredentials("sessionKey=top-secret"),
            CancellationToken.None));

        Assert.Contains("Claude authentication failed", error.Message);
        Assert.DoesNotContain("top-secret", error.ToString());
    }

    [Fact]
    public async Task FetchAsync_reports_schema_failure_without_returning_raw_response()
    {
        var handler = new FixtureHandler(Json(HttpStatusCode.OK, """{"memberships":[]}"""));
        var client = new ClaudeUsageClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<UsageProviderException>(() => client.FetchAsync(
            new ClaudeCredentials("sessionKey=secret"),
            CancellationToken.None));

        Assert.Equal("Claude account did not expose an organization.", error.Message);
        Assert.DoesNotContain("memberships", error.Message);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
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
