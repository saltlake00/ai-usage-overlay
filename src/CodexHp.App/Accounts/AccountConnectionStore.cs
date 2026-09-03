using System.IO;
using System.Text.Json;

namespace CodexHp.App.Accounts;

/// <summary>
/// 계정 연결 상태(비밀 없는 비활성 상태 포함)를 디스크에 영속화하는 저장소.
/// </summary>
public sealed class AccountConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public AccountConnectionStore(string path)
    {
        this.path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public IReadOnlyDictionary<string, AccountConnectionState> Load()
    {
        if (!File.Exists(this.path))
        {
            return new Dictionary<string, AccountConnectionState>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(this.path);
            var states = JsonSerializer.Deserialize<Dictionary<string, AccountConnectionState>>(
                json,
                JsonOptions);
            return states ?? new Dictionary<string, AccountConnectionState>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, AccountConnectionState>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IReadOnlyDictionary<string, AccountConnectionState> states)
    {
        var directory = Path.GetDirectoryName(this.path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(states, JsonOptions);
        var temp = this.path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, this.path, overwrite: true);
    }
}
