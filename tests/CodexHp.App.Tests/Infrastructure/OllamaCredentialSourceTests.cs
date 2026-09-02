using CodexHp.App.Infrastructure.Ollama;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OllamaCredentialSourceTests
{
    [Fact]
    public void Load_prefers_and_normalizes_the_cloud_session_cookie()
    {
        var values = new Dictionary<string, string?>
        {
            ["OLLAMA_SESSION_COOKIE"] = "abc123",
            ["OLLAMA_API_KEY"] = "api-key",
        };
        var source = new OllamaCredentialSource(name => values.GetValueOrDefault(name));

        var credentials = source.Load();

        Assert.Equal("__Secure-session=abc123", credentials.CookieHeader);
        Assert.Equal("api-key", credentials.ApiKey);
    }

    [Fact]
    public void Load_rejects_empty_configuration()
    {
        var source = new OllamaCredentialSource(_ => null);

        var error = Assert.Throws<OllamaUsageException>(() => source.Load());

        Assert.Contains("OLLAMA_SESSION_COOKIE", error.Message);
    }
}
