using System.Collections;
using System.IO;
using System.Text.Json;

namespace CodexHp.App.Infrastructure.Claude;

internal sealed record ClaudeCredentials(string AccessToken);

internal sealed class ClaudeCredentialSource
{
    private const string TokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";
    private readonly IReadOnlyDictionary<string, string> environment;
    private readonly string userProfile;

    public ClaudeCredentialSource()
        : this(ReadEnvironment(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal ClaudeCredentialSource(IReadOnlyDictionary<string, string> environment, string userProfile)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.userProfile = string.IsNullOrWhiteSpace(userProfile)
            ? throw new ArgumentException("User profile path is required.", nameof(userProfile))
            : userProfile;
    }

    public ClaudeCredentials Load()
    {
        if (this.environment.TryGetValue(TokenVariable, out var overrideToken)
            && !string.IsNullOrWhiteSpace(overrideToken))
        {
            return new ClaudeCredentials(overrideToken.Trim());
        }

        var path = this.GetCredentialsPath();
        if (!File.Exists(path))
        {
            throw new UsageProviderException(
                $"Claude Code is not signed in. Run `claude` to sign in, or set {TokenVariable}.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UsageProviderException("Claude Code credentials could not be read.", exception);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                throw new UsageProviderException("Claude Code credentials have an unsupported format.");
            }

            var accessToken = ReadString(oauth, "accessToken", "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new UsageProviderException("Claude Code credentials do not contain an access token.");
            }

            if (IsExpired(oauth))
            {
                throw new UsageProviderException(
                    "Claude Code session has expired. Run `claude` to sign in again.");
            }

            return new ClaudeCredentials(accessToken.Trim());
        }
        catch (UsageProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new UsageProviderException("Claude Code credentials contain invalid JSON.", exception);
        }
    }

    // `expiresAt` is epoch milliseconds. A missing or unreadable value is treated as
    // "not expired" so that a format change degrades into a request failure with a
    // real status code rather than a misleading "sign in again".
    private static bool IsExpired(JsonElement oauth)
    {
        foreach (var key in new[] { "expiresAt", "expires_at" })
        {
            if (oauth.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var epochMilliseconds))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds) <= DateTimeOffset.UtcNow;
            }
        }

        return false;
    }

    private string GetCredentialsPath()
    {
        if (this.environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configDir)
            && !string.IsNullOrWhiteSpace(configDir))
        {
            return Path.Combine(configDir, ".credentials.json");
        }

        return Path.Combine(this.userProfile, ".claude", ".credentials.json");
    }

    private static string? ReadString(JsonElement element, string camelCaseKey, string snakeCaseKey)
    {
        foreach (var key in new[] { camelCaseKey, snakeCaseKey })
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            if (item.Key is string key && item.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
