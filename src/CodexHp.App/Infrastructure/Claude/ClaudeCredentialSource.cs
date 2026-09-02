namespace CodexHp.App.Infrastructure.Claude;

internal sealed class ClaudeCredentialSource
{
    private readonly Func<string, string?> readEnvironment;

    public ClaudeCredentialSource()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal ClaudeCredentialSource(Func<string, string?> readEnvironment)
    {
        this.readEnvironment = readEnvironment ?? throw new ArgumentNullException(nameof(readEnvironment));
    }

    public ClaudeCredentials Load()
    {
        foreach (var name in new[] { "CLAUDE_AI_SESSION_KEY", "CLAUDE_WEB_SESSION_KEY" })
        {
            var value = this.readEnvironment(name)?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return new ClaudeCredentials(value.StartsWith("sessionKey=", StringComparison.Ordinal)
                ? value
                : $"sessionKey={value}");
        }

        throw new UsageProviderException(
            "Claude session is not configured. Set CLAUDE_AI_SESSION_KEY or CLAUDE_WEB_SESSION_KEY.");
    }
}
