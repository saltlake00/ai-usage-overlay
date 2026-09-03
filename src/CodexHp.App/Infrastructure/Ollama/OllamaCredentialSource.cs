using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure.Ollama;

internal sealed record OllamaCredentials(string? CookieHeader, string? ApiKey);

internal sealed class OllamaUsageException(string message, Exception? innerException = null)
    : Exception(message, innerException), IActionableProviderError;

internal sealed class OllamaCredentialSource
{
    private readonly Func<string, string?> readEnvironment;

    public OllamaCredentialSource()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal OllamaCredentialSource(Func<string, string?> readEnvironment)
    {
        this.readEnvironment = readEnvironment ?? throw new ArgumentNullException(nameof(readEnvironment));
    }

    public OllamaCredentials Load()
    {
        var cookie = Clean(this.readEnvironment("OLLAMA_SESSION_COOKIE"));
        var apiKey = Clean(this.readEnvironment("OLLAMA_API_KEY"))
            ?? Clean(this.readEnvironment("OLLAMA_KEY"));
        if (cookie is null && apiKey is null)
        {
            throw new OllamaUsageException(
                "Ollama Cloud is not configured. Set OLLAMA_API_KEY (from ollama.com/settings/keys) to show quota windows.");
        }

        if (cookie is not null && !cookie.Contains('='))
        {
            cookie = $"__Secure-session={cookie}";
        }

        return new OllamaCredentials(cookie, apiKey);
    }

    private static string? Clean(string? value)
    {
        value = value?.Trim().Trim('\'', '"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
