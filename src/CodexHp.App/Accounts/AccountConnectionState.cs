namespace CodexHp.App.Accounts;

/// <summary>
/// 공급자별 계정 연결 상태와 세대 번호.
/// 세대 번호는 재연결/계정 변경/연결 해제마다 증가하며,
/// 이전 세대에서 늦게 도착한 조회 결과는 화면과 디스크 캐시에 반영하지 않는다.
/// </summary>
public sealed record AccountConnectionState(
    string ProviderId,
    ConnectionStatus Status,
    long Generation);
