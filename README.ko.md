# AI Usage Overlay

Windows 11 작업 표시줄 위에서 Codex, Claude, Ollama Cloud의 남은 사용량을 동시에 보여주는 3줄 오버레이입니다.

## 사용법

- `C`: Codex 5시간 / 주간 남은 사용량
- `A`: Claude 5시간 / 주간 남은 사용량
- `O`: Ollama Cloud 단기 / 주간 남은 사용량
- 행 클릭: 해당 공급자 상세, 더블클릭: 전체 상세
- 트레이 우클릭: 즉시 새로고침, 위치 잠금, 설정, 종료

## 인증

Codex 인증은 기존 로컬 Codex 설정을 사용합니다. Claude는 Claude Code의 기존 로그인(`~/.claude/.credentials.json`)을 읽기 전용으로 사용하므로 별도 설정이 없습니다. Ollama Cloud만 사용자 환경 변수로 세션 쿠키를 받습니다.

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_SESSION_COOKIE', 'Ollama Cloud 세션 쿠키 값', 'User')
```

세션 쿠키와 액세스 토큰은 비밀번호와 같은 비밀 정보입니다. 공유하거나 저장소에 커밋하지 마세요.

## 저비용 설정

보일 때 60초, 숨겨졌을 때 180초 간격으로 조회합니다. 오류 시 최대 900초까지 지수 백오프하며, 상세 창은 네트워크를 다시 조회하지 않습니다.

## 빌드

```powershell
dotnet test
dotnet publish src/CodexHp.App/CodexHp.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out/win-x64
```

Apache License 2.0. 참고 프로젝트는 `THIRD-PARTY-NOTICES.md`를 확인하세요.
