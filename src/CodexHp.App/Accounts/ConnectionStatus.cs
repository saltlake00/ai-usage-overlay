namespace CodexHp.App.Accounts;

/// <summary>
/// 계정 연결 상태. 설계의 6개 상태를 표현한다.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>미연결. 조회하지 않는다.</summary>
    Disconnected,

    /// <summary>연결 확인 중.</summary>
    Connecting,

    /// <summary>연결됨. 정상 조회 중.</summary>
    Connected,

    /// <summary>인증 만료 등으로 재연결이 필요하다.</summary>
    ReconnectRequired,

    /// <summary>일시 오류(네트워크, 429 등). 과거 값은 갱신 시각과 함께 표시한다.</summary>
    TransientError,

    /// <summary>이 공급자는 지원하지 않는다.</summary>
    Unsupported,
}
