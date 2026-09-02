using CodexHp.App.Infrastructure.Claude;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeCredentialSourceTests
{
    [Theory]
    [InlineData("CLAUDE_AI_SESSION_KEY", "abc", "sessionKey=abc")]
    [InlineData("CLAUDE_WEB_SESSION_KEY", "sessionKey=abc", "sessionKey=abc")]
    public void Load_normalizes_supported_environment_session_keys(
        string variable,
        string value,
        string expected)
    {
        var values = new Dictionary<string, string?> { [variable] = value };
        var source = new ClaudeCredentialSource(name => values.GetValueOrDefault(name));

        var credentials = source.Load();

        Assert.Equal(expected, credentials.CookieHeader);
    }

    [Fact]
    public void Load_fails_with_actionable_message_when_no_session_key_exists()
    {
        var source = new ClaudeCredentialSource(_ => null);

        var error = Assert.Throws<UsageProviderException>(() => source.Load());

        Assert.Contains("CLAUDE_AI_SESSION_KEY", error.Message);
    }
}
