# Step 3: shell-catalog

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(참조 방향) · §3(UI 스레드) · §6(TDD 순서)
- `src/FlexDir.Core/Tools/IExternalToolCatalog.cs` — **step 1 이 만든 포트.**
  계약 셋이 XML 주석에 있다: 실패는 `null` · 취소만 예외 · **캐시하지 않는다**
- `src/FlexDir.Core/Tools/ExternalToolCommand.cs` — **step 0 이 만든 파일.**
  `TerminalPreset`(6값)과 `Resolve` 의 실행 파일 이름 표
- `src/FlexDir.Shell/Storage/SystemDriveList.cs` — **본뜰 구현.** COM 이 아니라
  STA 도 `IDisposable` 도 필요 없는 Shell 구현이 어떻게 생겼는지 보여 준다
- `src/FlexDir.Shell/Settings/RegistrySystemThemeSource.cs` — **레지스트리를 읽는 기존
  Shell 구현.** 같은 방식(`Microsoft.Win32.Registry`)을 쓴다
- `src/FlexDir.Shell/Locations/KnownFolderList.cs` — 조용한 실패의 기존 사례
- `docs/SHELL_NOTES.md` — 경로 정규화 (§경로 정규화)

## 배경

`[VS]`·`[>_]` 버튼은 그 프로그램이 이 기계에 **있을 때만** 활성이다. 이 step 이 그
탐지를 만든다.

**탐지 규칙이 도구마다 다르다. 그 차이가 이 step 의 전부다.**

## 작업

### 1. 디렉터리를 먼저 만든다

```
mkdir -p src/FlexDir.Shell/Tools
mkdir -p tests/FlexDir.Shell.Tests/Tools
```

### 2. `src/FlexDir.Shell/Tools/AppPathsToolCatalog.cs`

먼저 `tests/FlexDir.Shell.Tests/Tools/AppPathsToolCatalogTests.cs` 를 쓰고 red 를 확인한다.

```csharp
namespace FlexDir.Shell.Tools;

public sealed class AppPathsToolCatalog : IExternalToolCatalog
{
    public AppPathsToolCatalog();

    /// 테스트용. 레지스트리 조회와 파일 존재 확인을 갈아 끼운다.
    internal AppPathsToolCatalog(
        Func<string, string?> appPath,
        Func<string, bool> fileExists,
        Func<string, string?> environmentVariable);

    public ValueTask<string?> FindEditorAsync(CancellationToken ct);
    public ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct);
}
```

**`internal` 생성자로 갈아 끼우는 방식은 이 저장소의 기존 수다** —
`ShellThumbnailSource` 가 델리게이트 주입 생성자를 이미 그렇게 쓴다
(`.harness/HANDOFF.md` §규칙 5: "실행 지점을 `internal` 생성자로 바꿔 끼운다").
레지스트리를 직접 때리는 테스트는 기계마다 다른 답이 나와 게이트가 갈린다.

#### 에디터 탐지 — **App Paths 만 본다**

```
HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\code.exe  (기본값)
  없으면 →
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\code.exe (기본값)
  없으면 → null
```

**이 기계의 실측값 (2026-08-24)**: HKCU 에만 있고
`C:\Users\SOOJANG\AppData\Local\Programs\Microsoft VS Code\Code.exe` 다. HKLM 에는 없다.
**그래서 HKCU 를 먼저 본다** — 순서를 뒤집으면 사용자 설치본을 못 찾는다.

> ⛔ **`PATH` 로 폴백하지 마라.** 이유: 사람 확인 8번이 *"App Paths 를 임시로 옮기고 창을
> 숨겼다 다시 보이면 비활성이 된다"* 를 밟는다. `PATH` 폴백이 있으면 `code` 셸 래퍼가
> 여전히 잡혀 그 확인이 **영원히 실패한다.** 탐지원을 하나로 두는 것이 계약이다.

레지스트리 값이 있어도 **그 파일이 실제로 있는지 확인한다** — 지운 프로그램의 App Paths
항목은 남는다. 없으면 `null`.

값에 따옴표가 감싸여 있을 수 있다 (`"C:\...\Code.exe"`). **벗겨라.**

#### 터미널 탐지 — 프리셋마다 다르다

| 프리셋 | 탐지 순서 |
|---|---|
| `WindowsTerminal` (`wt.exe`) | `%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe` → App Paths(HKCU→HKLM) |
| `PowerShell7` (`pwsh.exe`) | App Paths(HKCU→HKLM) → `%ProgramFiles%\PowerShell\7\pwsh.exe` |
| `WindowsPowerShell` (`powershell.exe`) | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` |
| `CommandPrompt` (`cmd.exe`) | `%SystemRoot%\System32\cmd.exe` |
| `GitBash` (`git-bash.exe`) | `%ProgramFiles%\Git\git-bash.exe` → `%ProgramW6432%\Git\git-bash.exe` → `%ProgramFiles(x86)%\Git\git-bash.exe` |
| `Custom` | **언제나 `null`** (계약 4번 — 사용자가 적은 것은 탐지 대상이 아니다) |

**이 기계의 실측값 (2026-08-24)**:
`wt.exe` = `C:\Users\SOOJANG\AppData\Local\Microsoft\WindowsApps\wt.exe` ·
`pwsh.exe` = HKLM App Paths → `C:\Program Files\PowerShell\7\pwsh.exe` ·
`powershell.exe` = `C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe` ·
`cmd.exe` = `C:\WINDOWS\system32\cmd.exe` ·
`git-bash.exe` = `C:\Program Files\Git\git-bash.exe` (**App Paths 에 없다** — 그래서
고정 후보 경로다).

**모든 후보는 파일 존재를 확인한 뒤에만 낸다.** 환경 변수가 없으면 그 후보를 건너뛴다
(`%ProgramFiles(x86)%` 는 32비트 OS 에 없다).

#### 공통 계약

- **실패를 던지지 마라.** 레지스트리 접근 거부(`SecurityException`)·경로 오류를 전부
  삼키고 `null` 을 낸다. 이유: 포트 계약이고, 외부 도구를 못 찾는 것이 앱을 못 쓰게
  하면 안 된다
- **취소는 예외로 낸다.** 각 조회 앞에 `ct.ThrowIfCancellationRequested()`
- **캐시하지 마라.** 필드에 결과를 담아 두지 마라. 이유: 상주 앱(ADR-003)이라 창이
  다시 보일 때마다 다시 묻고, 그 사이에 사용자가 설치·삭제했을 수 있다
- **`IDisposable` 을 달지 마라.** COM 이 아니다 — `RegistrySystemThemeSource` ·
  `SystemDriveList` 와 같은 자리이고 `AppComposition` 정리 목록에 들어가지 않는다
- **STA 워커를 쓰지 마라.** 같은 이유. `Interop/StaWorkQueue.cs` 는 COM 을 위한 것이다

### 3. 테스트가 채점할 것

주입 생성자로 레지스트리·파일 존재·환경 변수를 전부 가짜로 준다:

1. HKCU 에만 `code.exe` 가 있으면 그 경로가 나온다
2. HKLM 에만 있으면 그 경로가 나온다
3. **둘 다 있으면 HKCU 가 이긴다**
4. **레지스트리에 있는데 파일이 없으면 `null`** (지운 프로그램의 잔여 항목)
5. **레지스트리에 없으면 `null`** — `PATH` 를 흉내 낸 무엇을 넣어도 안 잡힌다
6. 값이 `"..."` 로 감싸여 있으면 벗겨진다
7. 프리셋 다섯이 각각 위 표의 **첫 후보**를 찾으면 그것을 낸다
8. 첫 후보가 없으면 **다음 후보**로 넘어간다 (`pwsh` · `GitBash` 로 확인)
9. 후보가 전부 없으면 `null`
10. **`Custom` 은 언제나 `null`**
11. 환경 변수가 `null` 인 후보는 **던지지 않고** 건너뛴다
12. 레지스트리 조회가 던지면 **삼키고 `null`** 을 낸다
13. 이미 취소된 토큰이면 `OperationCanceledException`
14. **두 번 부르면 조회가 두 번 나간다** (캐시하지 않는다는 증거)
15. **실물 확인 하나**: 인자 없는 공개 생성자로 만든 인스턴스가
    `FindTerminalAsync(CommandPrompt)` 에 `cmd.exe` 로 끝나는 경로를 낸다.
    `cmd.exe` 는 모든 Windows 에 있으므로 기계마다 갈리지 않는다.
    **`code.exe`·`wt.exe`·`git-bash.exe` 로는 실물 테스트를 쓰지 마라** — 그것들은
    기계마다 있고 없어서 CI 나 다른 개발 기계에서 게이트가 갈린다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 2 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - **에디터가 App Paths 말고 다른 곳을 보지 않는가?** (사람 확인 8번이 이것에 걸린다)
   - HKCU 를 HKLM 보다 먼저 보는가?
   - 결과를 캐시하지 않는가? (14번 테스트)
   - `IDisposable`·STA 워커를 안 달았는가?
   - 실물 테스트가 `cmd.exe` 하나뿐인가? (기계마다 갈리는 것으로 채점하지 않는다)
   - 기존 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 3 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 클래스 이름·공개/내부 생성자 시그니처·프리셋별 탐지 순서 표를 적어라 —
step 9 가 `ARCHITECTURE.md` 포트 표에 그대로 옮긴다.

## 금지사항

- **`PATH` 를 뒤지지 마라 (에디터).** 이유: 위 §에디터 탐지. 사람 확인 8번이 못 밟게 된다.
- **결과를 캐시하지 마라.** 이유: 같다.
- **레지스트리 예외를 밖으로 내보내지 마라.** 이유: 포트 계약이 "실패는 `null`" 이다.
- **`IDisposable`·`StaWorkQueue` 를 쓰지 마라.** 이유: COM 이 아니다. 정리 목록에 들어가면
  `AppComposition` 의 종료 순서 계약(탭을 먼저 접고 shell 을 닫는다)에 이유 없이 얹힌다.
- **프로세스를 띄우지 마라.** 이유: 실행은 step 4 다. 탐지가 프로그램을 띄우면 게이트가
  돌 때마다 창이 뜬다 (`.harness/HANDOFF.md` §규칙 5).
- **`FlexDir.Core`·`FlexDir.App` 을 건드리지 마라.** 이유: 포트는 step 1 이 확정했다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
