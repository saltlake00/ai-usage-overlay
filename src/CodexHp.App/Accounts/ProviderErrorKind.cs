namespace CodexHp.App.Accounts;

/// <summary>
/// 공급자 조회 실패의 명시적 종류. 문자열 검색 대신 이 종류로 상태를 분류한다.
/// </summary>
internal enum ProviderErrorKind
{
    /// <summary>인증 실패(401 등). 재연결이 필요하다.</summary>
    Authentication,

    /// <summary>접근 제한(403 등). 공급자 응답에 따라 인증 또는 접근 제한이다.</summary>
    AccessDenied,

    /// <summary>일시 제한(429 등). 백오프 후 재시도한다.</summary>
    RateLimited,

    /// <summary>네트워크 오류. 일시 오류로 처리한다.</summary>
    Network,

    /// <summary>응답 형식 변경 등 지원 불가. 파서가 깨졌다.</summary>
    UnsupportedFormat,

    /// <summary>기타 실패.</summary>
    Other,
}
