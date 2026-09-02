using System.IO;
using CodexHp.App.Infrastructure.Claude;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ClaudeCredentialSourceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("claude-creds-").FullName;

    [Fact]
    public void Load_reads_the_access_token_from_Claude_Code_credentials()
    {
        this.WriteCredentials("""
            { "claudeAiOauth": { "accessToken": "file-token", "expiresAt": 4102444800000 } }
            """);
        var source = new ClaudeCredentialSource(Environment(), this.root);

        Assert.Equal("file-token", source.Load().AccessToken);
    }

    [Fact]
    public void Load_prefers_the_environment_override_over_the_credentials_file()
    {
        this.WriteCredentials("""{ "claudeAiOauth": { "accessToken": "file-token" } }""");
        var source = new ClaudeCredentialSource(
            Environment(("CLAUDE_CODE_OAUTH_TOKEN", " env-token ")),
            this.root);

        Assert.Equal("env-token", source.Load().AccessToken);
    }

    [Fact]
    public void Load_honours_CLAUDE_CONFIG_DIR()
    {
        var configDir = Directory.CreateDirectory(Path.Combine(this.root, "custom")).FullName;
        File.WriteAllText(
            Path.Combine(configDir, ".credentials.json"),
            """{ "claudeAiOauth": { "accessToken": "custom-token" } }""");
        var source = new ClaudeCredentialSource(
            Environment(("CLAUDE_CONFIG_DIR", configDir)),
            this.root);

        Assert.Equal("custom-token", source.Load().AccessToken);
    }

    [Fact]
    public void Load_treats_a_missing_expiry_as_usable()
    {
        this.WriteCredentials("""{ "claudeAiOauth": { "accessToken": "no-expiry" } }""");
        var source = new ClaudeCredentialSource(Environment(), this.root);

        Assert.Equal("no-expiry", source.Load().AccessToken);
    }

    [Fact]
    public void Load_reports_an_expired_session_as_actionable()
    {
        this.WriteCredentials("""
            { "claudeAiOauth": { "accessToken": "stale", "expiresAt": 1000000000000 } }
            """);
        var source = new ClaudeCredentialSource(Environment(), this.root);

        var error = Assert.Throws<UsageProviderException>(() => source.Load());

        Assert.Contains("expired", error.Message);
        Assert.DoesNotContain("stale", error.Message);
    }

    [Fact]
    public void Load_reports_a_missing_credentials_file_as_actionable()
    {
        var source = new ClaudeCredentialSource(Environment(), this.root);

        var error = Assert.Throws<UsageProviderException>(() => source.Load());

        Assert.Contains("not signed in", error.Message);
    }

    [Fact]
    public void Load_reports_invalid_json_without_echoing_the_file()
    {
        this.WriteCredentials("{ not json");
        var source = new ClaudeCredentialSource(Environment(), this.root);

        var error = Assert.Throws<UsageProviderException>(() => source.Load());

        Assert.Equal("Claude Code credentials contain invalid JSON.", error.Message);
    }

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    private void WriteCredentials(string json)
    {
        var directory = Directory.CreateDirectory(Path.Combine(this.root, ".claude")).FullName;
        File.WriteAllText(Path.Combine(directory, ".credentials.json"), json);
    }

    private static IReadOnlyDictionary<string, string> Environment(params (string Key, string Value)[] entries)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            result[key] = value;
        }

        return result;
    }
}
