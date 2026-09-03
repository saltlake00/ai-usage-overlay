# AI Usage Overlay

Windows 11 작업 표시줄 위에서 Codex, Claude, Ollama Cloud의 남은 사용량을 동시에 보여주는 3줄 오버레이입니다.

## 사용법

- `C`: Codex 5시간 / 주간 남은 사용량
- `A`: Claude 5시간 / 주간 남은 사용량
- `O`: Ollama Cloud 단기 / 주간 남은 사용량
- 행 클릭: 해당 공급자 상세, 더블클릭: 전체 상세
- 트레이 우클릭: 즉시 새로고침, 위치 잠금, 설정, 종료

## 인증

Codex 인증은 기존 로컬 Codex 설정을 사용합니다. Claude는 Claude Code의 기존 로그인(`~/.claude/.credentials.json`)을 읽기 전용으로 사용하므로 별도 설정이 없습니다. Ollama Cloud만 사용자 환경 변수가 필요합니다.

**권장: API 키.** [ollama.com/settings/keys](https://ollama.com/settings/keys)에서 키를 만든 뒤:

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_API_KEY', 'Ollama API 키 값', 'User')
```

공식 `GET https://ollama.com/api/usage` 엔드포인트(`Authorization: Bearer` 인증)를 쓰므로 세션 쿠키처럼 요청마다 값이 바뀌지 않고, 로그인 세션이 만료돼도 영향받지 않습니다.

**대체 경로: 세션 쿠키.** API 키를 설정할 수 없을 때만 씁니다 — 브라우저 DevTools에서 `__Secure-session` 쿠키 값을 **"Copy Value"**로 통째로 복사합니다(화면에 잘려 보이는 값을 손으로 긁으면 일부만 복사됩니다):

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_SESSION_COOKIE', 'Ollama Cloud 세션 쿠키 값', 'User')
```

이 값은 서버가 요청마다 새로 발급하는(rolling) 방식이라, 복사한 순간에는 유효해도 브라우저가 다음 요청을 보내는 사이 무효화될 수 있습니다. 두 값을 모두 설정하면 API 키가 우선 적용되고 쿠키는 무시됩니다.

세션 쿠키와 API 키는 비밀번호와 같은 비밀 정보입니다. 공유하거나 저장소에 커밋하지 마세요.

## 계정 연동

트레이 아이콘 우클릭 → **계정 연동**에서 각 공급자의 연결 상태를 확인하고, Claude OAuth 토큰·Ollama API 키를 앱 UI에서 등록할 수 있습니다. 등록한 비밀은 `%LOCALAPPDATA%\AIUsageOverlay\credentials`에 **DPAPI(현재 Windows 사용자)로 암호화**되어 저장되며, 같은 PC의 다른 Windows 사용자는 읽을 수 없습니다.

- **Codex**: 기존 `~/.codex/auth.json`을 그대로 읽습니다(이 PC의 Codex 연결). 별도 등록이 필요 없습니다.
- **Claude**: Claude Code가 저장한 로그인을 읽기 전용으로 사용합니다. 앱 UI에서 OAuth 토큰을 직접 등록할 수도 있습니다.
- **Ollama Cloud**: API 키를 앱 UI에서 등록하거나, 위 환경 변수를 사용합니다.

연결 해제는 앱의 조회만 중단하며, Codex·Claude 자체를 로그아웃시키지 않습니다.

## 저비용 설정

보일 때 60초, 숨겨졌을 때 180초 간격으로 조회합니다. 오류 시 최대 900초까지 지수 백오프하며, 상세 창은 네트워크를 다시 조회하지 않습니다.

## 배포

- 지원 OS: Windows 11 x64
- 공급자별 검증 상태: Codex 지원, Claude·Ollama Cloud는 기술 사용자용 베타(수동 등록)
- 로컬 저장 위치: `%LOCALAPPDATA%\AIUsageOverlay\credentials` (DPAPI CurrentUser 암호화)
- 삭제 방법: 설치 프로그램의 '로컬 데이터 삭제' 선택
- 비공개 응답 변경 위험: Claude·Ollama의 비공개 응답 형식이 바뀌면 파서가 깨질 수 있음

## 빌드

```powershell
dotnet test
dotnet publish src/CodexHp.App/CodexHp.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out/win-x64
```

Apache License 2.0. 참고 프로젝트는 `THIRD-PARTY-NOTICES.md`를 확인하세요.
