# AI Usage Overlay

Windows 11 작업 표시줄 위에서 Codex, Claude, Ollama Cloud의 남은 사용량을 동시에 보여주는 3줄 오버레이입니다. CodexHp를 기반으로 만들었으며 네트워크 조회 자체에 LLM 호출을 사용하지 않습니다.

## 표시 방식

- `C`: Codex 5시간 / 주간 남은 사용량
- `A`: Claude 5시간 / 주간 남은 사용량
- `O`: Ollama Cloud 단기 / 주간 남은 사용량
- 초록: 30% 초과, 노랑: 30% 이하, 빨강: 15% 이하
- 흐리게 표시된 행은 캐시 또는 마지막 정상 값을 뜻합니다.

한 번 클릭하면 해당 공급자 상세를, 두 번 클릭하면 전체 상세를 엽니다. 트레이 아이콘 우클릭 메뉴에서 즉시 새로고침, 위치 잠금 전환, 설정, 종료를 사용할 수 있습니다.

## 인증 설정

Codex는 기존 `%CODEX_HOME%\auth.json` 또는 `%USERPROFILE%\.codex\auth.json`을 읽습니다. Claude는 **Claude Code가 이미 저장해 둔 로그인**을 그대로 씁니다 — `%USERPROFILE%\.claude\.credentials.json`(또는 `%CLAUDE_CONFIG_DIR%\.credentials.json`)의 `claudeAiOauth.accessToken`을 읽어 `api.anthropic.com/api/oauth/usage`를 호출합니다. `claude`로 로그인돼 있으면 별도 설정이 필요 없고, 앱은 이 파일을 **읽기만 하고 쓰지 않습니다** — 토큰이 만료되면 다시 로그인하라고 표시할 뿐 스스로 갱신하지 않습니다.

Ollama Cloud는 사용자 환경 변수가 필요합니다. **권장은 API 키** — [ollama.com/settings/keys](https://ollama.com/settings/keys)에서 만든 뒤:

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_API_KEY', 'Ollama API 키 값', 'User')
```

공식 `GET https://ollama.com/api/usage`(`Authorization: Bearer` 인증)를 쓰므로 세션처럼 요청마다 값이 바뀌지 않습니다. API 키를 쓸 수 없을 때만 세션 쿠키로 대체합니다 — DevTools에서 `__Secure-session` 값을 **"Copy Value"**로 전체 복사해야 합니다(화면에 잘려 보이는 값을 손으로 긁으면 일부만 복사됩니다):

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_SESSION_COOKIE', 'Ollama Cloud 세션 쿠키 값', 'User')
```

세션 쿠키는 서버가 요청마다 새로 발급하는(rolling) 방식이라 복사한 순간 이후 무효화될 수 있습니다. 두 값을 모두 설정하면 API 키가 우선 적용됩니다.

설정 후 앱을 완전히 종료하고 다시 실행합니다. 다른 계정으로 확인할 때는 `CLAUDE_CODE_OAUTH_TOKEN`으로 Claude 토큰을 덮어쓸 수 있습니다.

> 세션 쿠키와 액세스 토큰은 비밀번호와 같은 비밀 정보입니다. 공유하거나 저장소에 커밋하지 마세요. 공급자의 비공개 웹 응답 형식이 바뀌면 해당 행이 마지막 정상 값으로 전환될 수 있습니다.

## 계정 연동

트레이 아이콘 우클릭 → **계정 연동**에서 각 공급자의 연결 상태를 확인하고, Claude OAuth 토큰·Ollama API 키를 앱 UI에서 등록할 수 있습니다. 등록한 비밀은 `%LOCALAPPDATA%\AIUsageOverlay\credentials`에 **DPAPI(현재 Windows 사용자)로 암호화**되어 저장되며, 같은 PC의 다른 Windows 사용자는 읽을 수 없습니다.

- **Codex**: 기존 `~/.codex/auth.json`을 그대로 읽습니다(이 PC의 Codex 연결). 별도 등록이 필요 없습니다.
- **Claude**: Claude Code가 저장한 로그인을 읽기 전용으로 사용합니다. 앱 UI에서 OAuth 토큰을 직접 등록할 수도 있습니다.
- **Ollama Cloud**: API 키를 앱 UI에서 등록하거나, 위 환경 변수를 사용합니다.

연결 해제는 앱의 조회만 중단하며, Codex·Claude 자체를 로그아웃시키지 않습니다.

## 저비용 동작

- 화면이 보일 때 60초마다 공급자 사용량 조회
- 화면이 숨겨졌을 때 180초 간격
- 오류가 계속되면 60/120/240/480/900초로 지수 백오프
- 상세 창과 로컬 Codex 활동 그래프는 추가 API 호출 없음
- `%LOCALAPPDATA%\AIUsageOverlay\usage-cache.json`에는 비밀 값 없이 표시용 백분율만 저장

## 빌드

.NET 10 SDK가 필요합니다.

```powershell
dotnet test
dotnet publish src/CodexHp.App/CodexHp.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out/win-x64
```

Google Drive 같은 동기화 드라이브에서 Windows apphost 생성이 실패하면 저장소를 로컬 NTFS 경로로 복사해 빌드하세요.

## 라이선스

Apache License 2.0. 참고한 프로젝트와 라이선스는 `THIRD-PARTY-NOTICES.md`에 정리되어 있습니다.
