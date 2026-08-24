# Step 2: terminal-settings

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙
- `src/FlexDir.Core/Settings/ISettingsStore.cs` — **이 step 이 고치는 파일.**
  `AppSettings` record 와 `ISettingsStore` 포트가 여기 있다. 기존 항목:
  `StartMode` · `StartFolder` · `ShowHiddenItems` · `Theme`
- `src/FlexDir.Core/Tools/ExternalToolCommand.cs` — **step 0 이 만든 파일.**
  `TerminalPreset`(6값) · `TerminalChoice`
- `src/FlexDir.Shell/Settings/JsonSettingsStore.cs` — **이 step 이 고치는 파일.**
  특히 `private sealed record Document` · `SaveAsync` · `Parse` **셋**
- `tests/FlexDir.Core.Tests/Settings/ISettingsStoreTests.cs` — 계약 테스트
- `tests/FlexDir.Core.Tests/Fakes/FakeSettingsStore.cs`
- `tests/FlexDir.Shell.Tests/Settings/` — `JsonSettingsStore` 의 고유 테스트

## 배경

`[>_]` 는 프리셋 다섯(Windows Terminal · PowerShell 7 · Windows PowerShell ·
명령 프롬프트 · Git Bash) 중 하나 **또는** 사용자 지정(실행 파일 + 인자)으로 연다.
그 선택은 사용자의 의도이므로 `AppSettings` 에 산다 — 뷰 상태가 아니다.

> ⚠ **이 step 이 가장 조용히 틀릴 수 있는 자리다.** v0.7.0 에서 `Theme` 을 늘렸을 때
> `JsonSettingsStore` 의 자체 `Document` record 를 안 고쳐서 **저장도 복원도 안 됐는데
> 계약 테스트는 전부 통과했다** — `FakeSettingsStore` 가 객체째로 들고 있어서다
> (`docs/PRD-v2.md` §19 · `.harness/HANDOFF.md` §0-3). 그 파일의 163줄 주석이
> *"`AppSettings` 에 항목을 늘리면 이 record 와 `SaveAsync`·`Parse` 를 함께 고친다"*
> 라고 적어 둔 이유다. **왕복을 실제 파일로 채점하는 테스트가 이 step 의 핵심이다.**

## 작업

### 1. `AppSettings` 에 항목 셋을 늘린다

`src/FlexDir.Core/Settings/ISettingsStore.cs` 의 `AppSettings` record 에:

```csharp
/// 터미널 버튼이 여는 것. 기본은 Windows Terminal 이다.
public TerminalPreset TerminalPreset { get; init; } = TerminalPreset.WindowsTerminal;

/// TerminalPreset.Custom 일 때 띄울 실행 파일. 프리셋과 따로 사는 이유는
/// StartFolder 와 같다 — Custom 을 고르고 아직 경로를 안 적은 중간 상태가 실제로 있다.
public string? TerminalExecutable { get; init; }

/// TerminalPreset.Custom 일 때 넘길 인자 한 줄. {path} 가 현재 폴더로 바뀐다.
public string? TerminalArguments { get; init; }
```

그리고 `TerminalChoice` 로 접는 메서드 하나:

```csharp
/// 지금 설정이 가리키는 터미널. 실행·탐지 양쪽이 같은 답을 써야 해서 여기 있다
/// (ResolveStartFolder·ResolveIsDarkMode 와 같은 자리).
public TerminalChoice ResolveTerminal();
```

- `TerminalPreset` 이 `Custom` 이 아니면 `new TerminalChoice(TerminalPreset)`
- `Custom` 이면 `new TerminalChoice(Custom, TerminalExecutable, TerminalArguments)`

**기본값이 `WindowsTerminal` 인 이유를 주석에 적어라**: Windows 11 의 기본 터미널이고
이 기계에 앱 실행 별칭으로 이미 있다. 없는 기계에서는 탐지가 `null` 을 내고 버튼이
비활성이 되며, 그때 설정에서 고르면 된다.

### 2. `JsonSettingsStore` 를 **세 곳** 고친다

`src/FlexDir.Shell/Settings/JsonSettingsStore.cs`:

1. `private sealed record Document` 에 `public string? TerminalPreset { get; init; }` ·
   `public string? TerminalExecutable { get; init; }` ·
   `public string? TerminalArguments { get; init; }`
2. `SaveAsync` 가 셋을 채운다. **프리셋은 열거형 이름 문자열로 쓴다**
   (`Theme` 이 이미 그렇게 한다) — 숫자로 쓰면 열거형에 값을 끼워 넣는 날 저장 파일이
   조용히 다른 뜻이 된다
3. `Parse` 가 셋을 읽는다. **모르는 문자열·`null` 은 기본값으로 접는다** —
   던지지 마라. 이유: 손으로 편집한 `settings.json` 하나가 앱을 못 뜨게 하면 안 된다
   (`Theme` 의 기존 처리와 같은 수)

빈 문자열과 `null` 을 **같게** 다뤄라 — 설정 창의 텍스트 상자를 비우면 빈 문자열이 온다.
`TerminalExecutable = ""` 은 `null` 로 접어 저장한다.

### 3. 테스트

**`tests/FlexDir.Core.Tests/Settings/ISettingsStoreTests.cs` 에 추가** (계약):

1. `AppSettings.Default.TerminalPreset` 이 `WindowsTerminal`
2. `ResolveTerminal()` 이 프리셋 다섯에 `new TerminalChoice(그 프리셋)` 을 낸다
   (`Executable`·`Arguments` 는 `null`)
3. `Custom` + 실행 파일·인자를 넣으면 셋이 그대로 실린 `TerminalChoice` 가 나온다
4. `Custom` 인데 실행 파일이 `null` 이어도 던지지 않는다 (중간 상태가 정상이다)
5. 계약 테스트의 왕복(저장→불러오기)에 새 항목 셋이 들어간다

**`tests/FlexDir.Shell.Tests/Settings/` 에 추가** — ⚠ **여기가 §19 를 갚는 자리다**:

6. **실제 파일 왕복**: `Custom` + `wezterm-gui.exe` + `start --cwd "{path}"` 를
   `SaveAsync` 한 뒤 **새 `JsonSettingsStore` 인스턴스**로 `LoadAsync` 하면 셋이 그대로다.
   **같은 인스턴스로 다시 읽지 마라** — 그러면 메모리를 읽어 §19 를 다시 놓친다
7. 프리셋 다섯 각각에 대해 6번과 같은 왕복이 성립한다 (`Enum.GetValues` 로 돈다 —
   프리셋을 늘리고 저장을 빠뜨리면 이 테스트가 잡는다)
8. **저장된 JSON 안에 프리셋이 문자열로 있다** (텍스트를 읽어 `"WindowsTerminal"` 을
   확인한다. 숫자면 실패)
9. `TerminalPreset` 자리에 모르는 문자열(`"Nonsense"`)이 든 파일을 읽으면
   `WindowsTerminal` 로 접히고 **나머지 설정(테마·숨김 항목)은 살아 있다**
10. 항목 셋이 아예 없는 **v0.8.x 시절 파일**을 읽으면 기본값으로 접히고 나머지가 살아 있다
    (실제 그 시절 JSON 문자열을 테스트에 박아라 — 마이그레이션의 증거다)
11. `TerminalExecutable = ""` 를 저장하고 읽으면 `null` 이다

**파일을 쓰는 테스트는 `Path.GetTempPath()` 아래에서만 쓴다** — 실제
`%APPDATA%\flex-dir\` 를 건드리면 게이트가 돌 때마다 사용자의 설정이 덮인다
(`.harness/HANDOFF.md` §규칙 5).

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 1 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `Document` · `SaveAsync` · `Parse` **셋을 다 고쳤는가?** (하나만 고치면 계약
     테스트는 통과하고 실물은 저장되지 않는다 — §19 가 정확히 그랬다)
   - 실제 파일 왕복 테스트가 **새 인스턴스**로 읽는가?
   - 프리셋이 JSON 에 **문자열**로 들어가는가?
   - 파일 쓰는 테스트가 `Path.GetTempPath()` 아래인가?
   - 기존 설정 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 2 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 새 `AppSettings` 속성 셋의 이름·타입·기본값과 `ResolveTerminal()` 시그니처를
적어라 — step 6·7 이 그것을 읽고 쓴다.

## 금지사항

- **`Document` 만 고치고 `SaveAsync`·`Parse` 를 빠뜨리지 마라.** 이유: 위 §배경.
  v0.7.0 이 정확히 그렇게 나갔고 게이트가 전부 초록이었다.
- **모르는 값에 던지지 마라.** 이유: 손으로 편집한 설정 파일 하나가 앱을 못 뜨게 하면
  안 된다.
- **프리셋을 숫자로 저장하지 마라.** 이유: 열거형 중간에 값이 끼면 저장 파일의 뜻이
  조용히 바뀐다.
- **`IViewStateStore` 를 건드리지 마라.** 이유: 설정과 뷰 상태는 일부러 다른 파일이다
  (`ISettingsStore` 주석).
- **`FlexDir.App` 을 건드리지 마라.** 이유: 설정 화면은 step 7 이다.
- **테스트에서 `%APPDATA%\flex-dir\` 에 쓰지 마라.** 이유: 게이트가 돌 때마다 사용자의
  실제 설정이 덮인다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
