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

Codex는 기존 `%CODEX_HOME%\auth.json` 또는 `%USERPROFILE%\.codex\auth.json`을 읽습니다. Claude와 Ollama Cloud는 브라우저 비밀 값을 앱 설정이나 로그에 저장하지 않도록 사용자 환경 변수로 받습니다.

```powershell
[Environment]::SetEnvironmentVariable('CLAUDE_AI_SESSION_KEY', 'Claude 세션 쿠키 값', 'User')
[Environment]::SetEnvironmentVariable('OLLAMA_SESSION_COOKIE', 'Ollama Cloud 세션 쿠키 값', 'User')
```

설정 후 앱을 완전히 종료하고 다시 실행합니다. Ollama API 키(`OLLAMA_API_KEY`)만으로는 현재 웹 요금제의 남은 비율을 확인할 수 없으므로 세션 쿠키가 필요합니다.

> 세션 쿠키는 비밀번호와 같은 비밀 정보입니다. 공유하거나 저장소에 커밋하지 마세요. 공급자의 비공개 웹 응답 형식이 바뀌면 해당 행이 마지막 정상 값으로 전환될 수 있습니다.

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
