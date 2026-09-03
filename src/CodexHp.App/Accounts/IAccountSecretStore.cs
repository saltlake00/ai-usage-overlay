namespace CodexHp.App.Accounts;

/// <summary>
/// 사용자별 공급자 인증 비밀을 저장하는 저장소 계약.
/// 경로 키는 허용한 공급자 ID만 받는다.
/// </summary>
public interface IAccountSecretStore
{
    string? Read(string providerId);
    void Write(string providerId, string secret);
    void Delete(string providerId);
}
