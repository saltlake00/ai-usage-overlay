using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexHp.App.Accounts;

/// <summary>
/// Windows DPAPI(CurrentUser)로 공급자 인증 비밀을 암호화해 저장하는 저장소.
/// 같은 폴더의 암호문 임시 파일에 쓴 뒤 원자적으로 교체한다.
/// </summary>
internal sealed class DpapiAccountSecretStore : IAccountSecretStore
{
    private static readonly string[] AllowedProviderIds = ["codex", "claude", "ollama"];

    private readonly string directory;

    public DpapiAccountSecretStore(string directory)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public string? Read(string providerId)
    {
        this.ValidateProviderId(providerId);
        var path = this.GetPath(providerId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var ciphertext = File.ReadAllBytes(path);
            var plaintext = ProtectedData.Unprotect(
                ciphertext,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // 손상된 암호문이나 읽기 권한 오류는 비밀 없는 결과로 반환한다.
            // UI는 이를 재연결로 처리한다. 평문 대체 저장은 두지 않는다.
            return null;
        }
    }

    public void Write(string providerId, string secret)
    {
        this.ValidateProviderId(providerId);
        ArgumentNullException.ThrowIfNull(secret);

        Directory.CreateDirectory(this.directory);
        var ciphertext = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var target = this.GetPath(providerId);
        var temp = Path.Combine(this.directory, $"{providerId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, ciphertext);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void Delete(string providerId)
    {
        this.ValidateProviderId(providerId);
        var path = this.GetPath(providerId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetPath(string providerId) => Path.Combine(this.directory, $"{providerId}.bin");

    private void ValidateProviderId(string providerId)
    {
        if (!AllowedProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerId),
                $"Provider id '{providerId}' is not allowed.");
        }
    }
}
