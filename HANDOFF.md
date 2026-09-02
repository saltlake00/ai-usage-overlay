# HANDOFF — AI Usage Overlay

새 세션 진입점. **이 파일만 읽으면 이어서 작업할 수 있다.**

## 이 프로젝트가 무엇인가

Windows 작업표시줄/데스크톱에 **Codex·Claude Code·Ollama Cloud 사용량**을 띄우는 오버레이.

[netics01/CodexHp](https://github.com/netics01/CodexHp)(C#/WPF, Apache-2.0)를 베이스로 Claude·Ollama
프로바이더를 직접 붙인 개조판이다. 귀속은 `THIRD-PARTY-NOTICES.md`에 있다(CodexHp Apache-2.0 +
Win-CodexBar/CodexBar MIT). 업스트림에 없는 추가분:

- `src/CodexHp.App/Infrastructure/Claude/`, `Infrastructure/Ollama/`
- `src/CodexHp.App/Application/MultiProviderCoordinator.cs`, `IUsageProvider.cs`,
  `ClaudeQuotaFallbackPolicy.cs`

원격: `saltlake00/ai-usage-overlay` (**private**).

## 명령

```powershell
.\scripts\Verify-Core.ps1     # 전체 게이트: restore -> build -> test -> publish. 커밋 전 이것만 통과하면 된다
dotnet test --nologo -v q     # 테스트만
dotnet publish src\CodexHp.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out\win-x64
```

`out\win-x64\CodexHp.exe`가 산출물. **렌더러/UI를 고쳤으면 반드시 재게시하고 앱을 재시작하라** —
게시를 빼먹어 낡은 바이너리를 보고 "안 고쳐졌다"고 판단한 사고가 실제로 있었다.

## 런타임 경로

| 무엇 | 어디 |
|---|---|
| 설정 | `%LOCALAPPDATA%\CodexHp\settings.json` (현재 200x60, `target: desktop`) |
| 진단 로그 | `%LOCALAPPDATA%\CodexHp\Logs\CodexHp.log` |
| 표시 캐시 | `%LOCALAPPDATA%\AIUsageOverlay\usage-cache.json` |

디버깅은 **로그가 1순위**다. 프로바이더 실패는 `[Providers]` 항목으로 남는다.

## 프로바이더별 데이터 출처 (실측으로 확정된 것)

**Codex** — `~/.codex/auth.json` + `~/.codex` 세션 로그. chatgpt.com 폴링. 동작 확인됨.

**Claude Code** — 2단 구조다.

1. 1순위: `GET https://api.anthropic.com/api/oauth/usage`,
   `Authorization: Bearer <accessToken>` + `anthropic-beta: oauth-2025-04-20`.
   토큰은 `~/.claude/.credentials.json`의 `claudeAiOauth.accessToken`(또는 `CLAUDE_CODE_OAUTH_TOKEN`,
   `CLAUDE_CONFIG_DIR`). **파일은 읽기만 하고 쓰지 않는다.**
2. 2순위: `~/.claude/projects/**/*.jsonl`의 `message.usage` 집계 (`ClaudeLocalUsageSource`).
   **`input_tokens + output_tokens`만 센다** — 캐시 토큰을 넣으면 묻힌다(실측: 5시간 1억 3467만 중
   1억 2920만이 캐시 읽기).

`ClaudeQuotaFallbackPolicy`가 둘 사이를 중재한다: **최근 30분 안에 할당량을 읽었으면 실패를 그대로
전파**한다(마지막 정상값 유지 + 폴링 백오프). 오래 못 읽었을 때만 로컬 집계로 내려간다.

**Ollama** — **Ollama Cloud**다(로컬 `ollama serve`가 아니다). `ollama.com/settings` HTML을 세션
쿠키로 긁는다. `OLLAMA_SESSION_COOKIE` 환경변수 필요. **현재 미설정이라 `--`로 표시된다.**

## 하지 말 것 (이미 막힌 길)

- **Windows 자격 증명 관리자 읽기** — 항목이 없다. `cmdkey /list | findstr /i "Claude"` 결과 공백 확인.
  CodexBar가 쓰는 `Claude Code-credentials` 항목은 이 머신에 존재하지 않는다.
- **`refreshToken`으로 토큰 갱신** — 회전형이면 Claude Code 본체 로그인을 깨뜨릴 수 있다.
  CodexBar도 Agent Cat도 하지 않는다(Agent Cat은 `token_expired`로 포기하는 코드가 그대로 있다).
- **`AppearanceSettings.Default` 크기 변경** — 144x34를 200x60으로 바꿨더니 승인된 기본값·그래프
  뷰포트·드래그 인수 테스트가 무더기로 깨졌다. 저장된 설정이 이미 200x60이므로 바꿀 이유도 없다.
- **할당량 엔드포인트를 60초마다 폴링** — 429가 온다. 실패는 실패로 전파해 지수 백오프
  (`ProviderPollSchedule`: 60→120→240→480→900초)가 작동하게 두어라.

## 레이아웃 규칙

`UsageOverlayRenderer.AddProviderColumns`가 **가로 3열**을 그린다. 열마다 `[이름 / 단기값 / 주간값]`.

- 세로 공간을 **예산으로 배분**한다. 높이가 모자라면 주간줄 → 막대 순으로 접어 비트맵 밖으로
  넘치지 않게 한다(`showName` 22px, `showWeekly` 40px, 막대 52px 임계).
- 표시값은 **남은 비율**이다(`100%` = 안 씀). 퍼센트가 없고 토큰이 있으면 `FormatTokens`로 축약해
  숫자를 그리고 **막대 채움은 그리지 않는다** — 없는 한도를 암시하지 않기 위함.
- 창 이름은 `ProviderUsageRowState.ShortWindowLabel`이 들고 온다. 렌더러에서 프로바이더 id를
  분기하지 마라(그 냄새를 걷어낸 상태다).

## 테스트에서 걸리는 것들

- `ReleaseConfigurationTests` — **README 2종의 문구를 검증한다.** README를 고치면 같이 본다.
- `ProviderUsageCacheTests` — 캐시 파일에 자격증명 문자열이 없는지 본다. `accessToken`·`refreshToken`·
  `sessionKey`·`cookie`·`Bearer` 금지. 토큰 *개수*는 정당한 표시 데이터다.
- `SettingsWindowTests` — About 화면이 **40자 커밋 SHA**를 표시하는지 본다. `.git`이 없으면
  SDK 내장 SourceLink가 SHA를 못 채워 실패한다. **저장소 밖으로 소스를 복사해 테스트하지 마라.**
- `MultiProviderCoordinatorTests` — 임의 예외 메시지가 오버레이로 새지 않는지 본다.
  사용자에게 보여줄 메시지는 `IActionableProviderError`를 구현한 예외에만 담아라.

## 다음 작업

**토큰 활동 스파크라인.** 3열 각 열 아래에 얇은 활동 그래프를 넣는다.

- 업스트림의 토큰 히스토그램 코드(`AddGraph`, `OverlayElementRole.TokenBar`)가 남아 있으나
  프로바이더 행이 있으면 `CreateLayout` 119행에서 조기 반환해 **도달하지 않는다.** 재사용하거나
  열 단위로 다시 그려야 한다.
- Claude 쪽 데이터는 `ClaudeLocalUsageSource`를 시간 버킷으로 확장하면 나온다(지금은 창 합계만 반환).

## 미해결

- Ollama는 `OLLAMA_SESSION_COOKIE`를 넣어야 값이 나온다.
- `settings.json`의 `showOnlyWhenChatGptRunning: true` — Codex 프로세스가 없거나 해당 모니터에
  전체화면 앱이 있으면 오버레이가 숨는다. "갑자기 사라진다"는 제보가 오면 여기를 먼저 보라.
- 실제 화면은 사용자 스크린샷으로만 확인했다. GUI라 자동 캡처 경로가 없다.
