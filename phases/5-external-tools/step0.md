# Step 0: tool-command

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(`FlexDir.Core` 는 WPF·COM·Windows API 를
  참조하지 않는다) · §6(TDD 순서: 디렉터리 먼저 → `FooTests.cs` 먼저 → red 상태에서만 새 소스)
- `src/FlexDir.Core/Locations/LocationId.cs` — 특히 아래 셋. **새로 만들 것이 없다**:
  - `public string DisplayPath { get; }` — 표시형 경로. 로컬은 `C:\Users\…`,
    UNC 는 `\\server\share\…` (내부 표현의 `\\?\`·`\\?\UNC\` 접두사를 벗긴 것)
  - `public bool IsNetwork { get; }` — UNC 인가
  - `public bool IsNetworkServer { get; }` — **서버 루트인가** (`\\server`, 공유 없음).
    이 step 의 비활성 판정이 이것이다
- `src/FlexDir.Core/Errors/LocationError.cs` — `LocationErrorKind` 열거형과
  `LocationErrorMessages`. 이 step 의 문구 함수가 같은 자리에 놓이는 형제다
- `docs/ARCHITECTURE.md` §1·§2 — Core 의 자리와 포트 표

## 배경

2구간(v0.9.0)은 페인 툴바에 외부 도구 버튼 둘을 넣는다 — `[VS]`(VS Code 로 현재 폴더
열기)와 `[>_]`(터미널로 현재 폴더 열기). 둘 다 **항상 페인의 현재 폴더**를 열고 선택
항목은 보지 않는다.

**이 step 은 Core 의 순수 판정만 만든다.** 레지스트리도 프로세스도 여기 없다 —
그것은 step 3·4 의 `FlexDir.Shell` 이 한다.

## 작업

### 1. 디렉터리를 먼저 만든다

```
mkdir -p src/FlexDir.Core/Tools
mkdir -p tests/FlexDir.Core.Tests/Tools
```

**TDD 가드가 디렉터리 없이는 테스트 프로젝트를 못 찾아 무조건 거부한다** (CLAUDE.md §6-1).

### 2. `tests/FlexDir.Core.Tests/Tools/ExternalToolCommandTests.cs` 를 먼저 쓴다

아래 §4 의 채점 항목을 테스트로 쓰고, **실패(red)를 확인한 뒤** 소스를 쓴다.

### 3. `src/FlexDir.Core/Tools/ExternalToolCommand.cs`

한 파일에 아래 넷을 담는다. **열거형과 record 를 별도 파일로 쪼개지 마라** —
동작이 없는 선언은 동작이 있는 파일에 함께 둔다 (CLAUDE.md §6 마지막 문단).

```csharp
namespace FlexDir.Core.Tools;

/// 터미널 프리셋. Custom 은 사용자가 실행 파일과 인자를 직접 적은 것이다.
public enum TerminalPreset
{
    WindowsTerminal,
    PowerShell7,
    WindowsPowerShell,
    CommandPrompt,
    GitBash,
    Custom,
}

/// 어느 터미널을 어떻게 열 것인가. Executable·Arguments 는 Custom 일 때만 쓰인다.
public sealed record TerminalChoice(
    TerminalPreset Preset,
    string? Executable = null,
    string? Arguments = null);

public static class ExternalToolCommand
{
    public const string PathToken = "{path}";

    /// 이 폴더에서 외부 도구를 실행할 수 있는가.
    public static bool CanRunAt(LocationId? folder);

    /// 인자 틀의 {path} 를 폴더의 표시형 경로로 바꾼다.
    public static string FormatArguments(string? template, LocationId folder);

    /// 프리셋이 쓰는 실행 파일 이름과 기본 인자 틀. Custom 은 choice 의 값을 그대로 낸다.
    public static (string Executable, string Arguments) Resolve(TerminalChoice choice);

    /// 프리셋의 사람이 읽는 이름 (설정 창의 목록과 상태 문구가 쓴다).
    public static string Label(TerminalPreset preset);
}
```

#### `CanRunAt`

- `folder` 가 `null` → `false`
- `folder.IsNetworkServer` 가 `true` → `false`
- 그 외 전부 → `true`

**`IsNetwork` 로 판정하지 마라.** `\\10.10.10.23\home` 은 실행 가능한 작업 디렉터리이고
`\\10.10.10.23` 만 아니다. UNC 전체를 막으면 NAS 에서 두 버튼이 영영 죽는다.

#### `FormatArguments`

- `template` 이 `null`·빈 문자열·공백뿐이면 `string.Empty` 를 낸다
- `{path}` 를 **모두** `folder.DisplayPath` 로 바꾼다. **따옴표를 붙이지 마라** —
  따옴표가 필요하면 틀 쪽에 이미 적혀 있다 (`-d "{path}"`). 여기서 또 붙이면 `-d ""C:\""`
  가 된다
- `{path}` 가 없는 틀은 그대로 낸다 (작업 디렉터리만으로 여는 프리셋이 실제로 있다)
- 치환은 **대소문자를 구분한다** (`{PATH}` 는 치환하지 않는다). 이유: 설정 화면이
  `{path}` 하나만 안내하고, 그 하나만 계약이다

#### `Resolve` — 프리셋 표

**이 표는 이 기계에서 실측했다 (2026-08-24).** 그대로 박아라.

| 프리셋 | Executable | Arguments |
|---|---|---|
| `WindowsTerminal` | `wt.exe` | `-d "{path}"` |
| `PowerShell7` | `pwsh.exe` | `-NoLogo -WorkingDirectory "{path}"` |
| `WindowsPowerShell` | `powershell.exe` | *(빈 문자열)* |
| `CommandPrompt` | `cmd.exe` | *(빈 문자열)* |
| `GitBash` | `git-bash.exe` | *(빈 문자열)* |
| `Custom` | `choice.Executable ?? ""` | `choice.Arguments ?? ""` |

**인자가 빈 셋은 일부러 그렇다.** 실행기(step 4)가 `ProcessStartInfo.WorkingDirectory` 를
언제나 그 폴더로 세우므로, 자기 작업 디렉터리에서 뜨는 프로그램은 인자가 필요 없다.
`wt.exe` 와 `pwsh.exe` 만 예외로 명시 인자를 받는다 — 둘은 작업 디렉터리를 물려받지 않고
자기 기본 프로필의 시작 폴더로 간다.

**`Executable` 은 경로가 아니라 파일 이름이다.** 실제 위치를 찾는 것은 step 3 의
`IExternalToolCatalog` 다. Core 는 파일시스템에 닿지 않는다 (CLAUDE.md §1).

#### `Label`

`Windows Terminal` · `PowerShell 7` · `Windows PowerShell` · `명령 프롬프트` ·
`Git Bash` · `사용자 지정`.

### 4. `src/FlexDir.Core/Tools/ExternalToolMessages.cs`

실패를 상태표시줄 한 줄로 접는다. `tests/FlexDir.Core.Tests/Tools/ExternalToolMessagesTests.cs`
를 **먼저** 쓴다.

```csharp
namespace FlexDir.Core.Tools;

public static class ExternalToolMessages
{
    /// 실행 실패를 사람이 읽는 한 줄로. toolLabel 은 "VS Code" · "Windows Terminal" 처럼
    /// 무엇을 열려다 실패했는지다.
    public static string Describe(LocationErrorKind kind, string toolLabel);
}
```

- `None` 은 넘어오지 않는 값이다 — 넘어오면 `ArgumentOutOfRangeException` 을 던져라.
  이유: 성공을 실패 문구로 만드는 호출은 버그이고 조용히 넘기면 화면에 헛말이 뜬다
- `NotFound` → `"{toolLabel} 을(를) 찾을 수 없습니다"`
- `AccessDenied` → `"{toolLabel} 을(를) 실행할 권한이 없습니다"`
- 나머지(`DeviceNotReady`·`Sharing`·`CredentialConflict`·`Unknown`) →
  `"{toolLabel} 을(를) 열지 못했습니다"`
- 문구에 **마침표를 찍지 마라** — `docs/UI_GUIDE.md` 의 상태 표현과 기존
  `LocationErrorMessages` 가 그렇다

**`LocationErrorMessages` 를 고치거나 재사용하지 마라.** 그쪽 문구는 *폴더*를 못 여는
사건을 말한다 ("폴더를 찾을 수 없습니다"). 여기서 그것을 내면 사용자는 자기 폴더가
사라진 줄 안다.

### 5. 테스트가 채점할 것

`ExternalToolCommandTests.cs`:

1. `CanRunAt(null)` 이 `false`
2. `CanRunAt(C:\)` · `CanRunAt(C:\Users\X)` 가 `true`
3. **`CanRunAt(\\10.10.10.23)` 가 `false`** (서버 루트)
4. **`CanRunAt(\\10.10.10.23\home)` 가 `true`** · `CanRunAt(\\10.10.10.23\home\sub)` 가 `true`
5. `FormatArguments("-d \"{path}\"", C:\Temp)` 가 `-d "C:\Temp"` — **따옴표가 하나씩만**
6. `FormatArguments(null, …)` · `FormatArguments("  ", …)` 가 빈 문자열
7. `FormatArguments("--a {path} --b {path}", …)` 가 **둘 다** 치환
8. `FormatArguments("{PATH}", …)` 는 치환하지 않는다
9. UNC 폴더의 치환 결과가 `\\10.10.10.23\home` 이다 (**`\\?\UNC\` 가 새지 않는다**)
10. `Resolve` 가 프리셋 다섯에 위 표 그대로를 낸다
11. `Resolve(new TerminalChoice(TerminalPreset.Custom, "wezterm-gui.exe", "start --cwd \"{path}\""))`
    가 그 둘을 그대로 낸다
12. `Resolve(new TerminalChoice(TerminalPreset.Custom))` 가 빈 문자열 둘을 낸다 (던지지 않는다)
13. `Label` 이 프리셋 여섯 전부에 답한다 (`Enum.GetValues` 로 돈다 — 프리셋을 늘리고
    라벨을 빠뜨리면 이 테스트가 잡는다)

`ExternalToolMessagesTests.cs`:

14. `Describe(NotFound, "VS Code")` 가 `VS Code` 를 포함하고 "찾을 수 없" 을 포함한다
15. `Describe(AccessDenied, …)` 가 14번과 **다른** 문자열이다
16. `Describe(None, …)` 가 `ArgumentOutOfRangeException` 을 던진다
17. `LocationErrorKind` 전 값에 대해 (None 제외) 빈 문자열이 아니고 마침표로 끝나지 않는다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 **2093 보다 커야** 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `FlexDir.Core` 가 Windows API·COM·WPF 를 참조하지 않는가? (`using System.Diagnostics`
     도 없다 — 프로세스는 step 4 의 일이다)
   - 서버 루트 판정에 `IsNetworkServer` 를 썼는가? (`IsNetwork` 가 아니라)
   - `FormatArguments` 가 따옴표를 스스로 붙이지 않는가?
   - 기존 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 0 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `TerminalPreset` 값 여섯 · `TerminalChoice` 시그니처 ·
`ExternalToolCommand` 메서드 넷의 정확한 시그니처를 적어라 — step 1~5 가 전부 이것을 부른다.

## 금지사항

- **`System.Diagnostics.Process` 를 쓰지 마라.** 이유: Core 는 프로세스를 띄우지 않는다.
  실행은 step 4 의 `FlexDir.Shell` 이다 (CLAUDE.md §1).
- **레지스트리를 읽지 마라.** 이유: 같다. 탐지는 step 3 이다.
- **`FlexDir.Shell`·`FlexDir.App` 을 건드리지 마라.** 이유: 이 step 은 Core 뿐이다.
- **`LocationErrorMessages` 를 고치지 마라.** 이유: 위 §4. 폴더 오류와 도구 오류는
  사용자가 손쓸 것이 다르다.
- **UNC 전체를 비활성으로 만들지 마라.** 이유: `\\server\share` 아래는 정상적인 작업
  디렉터리다. 막으면 NAS 에서 기능이 통째로 없어진다.
- **`{path}` 말고 다른 토큰을 만들지 마라.** 이유: 설정 화면이 안내하는 계약이 그 하나다.
  토큰이 둘이 되는 순간 어느 것이 되는지 사용자가 시험해 봐야 한다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
