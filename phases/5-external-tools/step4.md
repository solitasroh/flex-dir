# Step 4: shell-launcher

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(참조 방향) · §3(UI 스레드) · §6(TDD 순서)
- `src/FlexDir.Core/Tools/IExternalToolLauncher.cs` — **step 1 이 만든 포트.**
  계약 넷이 XML 주석에 있다: 실패는 `LocationErrorKind` · 기다리지 않는다 ·
  `workingFolder` 는 작업 디렉터리로만 · UI 스레드 금지
- `src/FlexDir.Core/Errors/Win32ErrorMapping.cs` — **재사용한다.**
  `Classify(int win32Error)` 가 Win32 코드를 `LocationErrorKind` 로 옮긴다
- `src/FlexDir.Core/Locations/LocationId.cs` — `DisplayPath`
- `src/FlexDir.Shell/Activation/ShellItemActivator.cs` — **본뜰 구현.** 이 저장소에서
  이미 프로그램을 띄우는 유일한 자리다. 오류를 어떻게 접는지, 테스트에서 실제 실행을
  어떻게 피하는지 그대로 따른다
- `src/FlexDir.Shell/Tools/AppPathsToolCatalog.cs` — **step 3 이 만든 파일**
- `docs/SHELL_NOTES.md` §경로 정규화 · §네트워크

## 배경

탐지된 실행 파일을 **현재 폴더를 작업 디렉터리로** 띄운다. 실패하면 사유를 낸다 —
던지지 않는다.

> ⚠ **자동 테스트가 프로세스를 실제로 띄우면 안 된다** (`.harness/HANDOFF.md` §규칙 5).
> 게이트가 돌 때마다 터미널 창이 뜬다. **실행 지점을 `internal` 생성자로 갈아 끼운다** —
> `ShellItemActivator` 가 이미 그 수를 쓴다.

## 작업

### 1. `src/FlexDir.Shell/Tools/ProcessToolLauncher.cs`

먼저 `tests/FlexDir.Shell.Tests/Tools/ProcessToolLauncherTests.cs` 를 쓰고 red 를 확인한다.

```csharp
namespace FlexDir.Shell.Tools;

public sealed class ProcessToolLauncher : IExternalToolLauncher
{
    public ProcessToolLauncher();

    /// 테스트용. 실제 프로세스 생성을 갈아 끼운다.
    internal ProcessToolLauncher(Action<ProcessStartInfo> start);

    public ValueTask<LocationErrorKind> LaunchAsync(
        string executable,
        string arguments,
        LocationId workingFolder,
        CancellationToken ct);
}
```

#### `ProcessStartInfo` 를 어떻게 채우는가

| 항목 | 값 | 왜 |
|---|---|---|
| `FileName` | `executable` (step 3 이 찾은 **전체 경로**) | |
| `Arguments` | `arguments` 문자열 그대로 | 이미 치환이 끝났다 (step 0 `FormatArguments`). **`ArgumentList` 를 쓰지 마라** — 그러면 사용자가 적은 한 줄을 우리가 쪼개게 되고, 따옴표 규칙이 사용자 기대와 갈린다 |
| `WorkingDirectory` | `workingFolder.DisplayPath` | **`Value` 가 아니다** — `\\?\C:\Temp` 를 작업 디렉터리로 주면 대부분의 프로그램이 거부한다 |
| `UseShellExecute` | `false` | `true` 면 `WorkingDirectory` 가 무시되는 경우가 있고, 오류가 `Win32Exception` 이 아니라 shell 대화상자로 나온다 (모달 금지) |

#### 오류를 어떻게 접는가

- 성공 → `LocationErrorKind.None`
- `Win32Exception ex` → `Win32ErrorMapping.Classify(ex.NativeErrorCode)`
- `InvalidOperationException`·`PlatformNotSupportedException`·`FileNotFoundException`·
  `DirectoryNotFoundException` → `LocationErrorKind.NotFound`
- 그 밖의 예외 → `LocationErrorKind.Unknown`
- `OperationCanceledException` 은 **삼키지 말고 그대로 나가게 둔다** (포트 계약)

`executable` 이 `null`·빈 문자열·공백뿐이면 **프로세스를 만들려 하지 말고 곧장
`LocationErrorKind.NotFound`** 를 낸다. 이유: `Custom` 프리셋을 고르고 실행 파일을 아직
안 적은 중간 상태가 정상이다 (step 2 §1). 그때 예외 문자열이 상태표시줄에 뜨면 안 된다.

#### 스레드

- 프로세스 생성은 **UI 스레드 밖**이어야 한다 (CLAUDE.md §3). 실행 파일이 네트워크
  경로에 있으면 초 단위로 블로킹된다
- **STA 는 필요 없다** — COM 이 아니다. `Task.Run` 이면 된다.
  **아파트먼트와 블로킹은 다른 문제다** (`.harness/HANDOFF.md` §규칙 8)
- **띄우고 기다리지 마라.** `WaitForExit` 를 부르지 마라 — 그 터미널이 닫힐 때까지
  묶인다. 반환한 `Process` 는 곧바로 `Dispose` 한다 (핸들 누수)

### 2. 테스트가 채점할 것

주입 생성자로 `Action<ProcessStartInfo>` 를 준다:

1. 성공하면 `LocationErrorKind.None`
2. **`FileName`·`Arguments` 가 받은 그대로 실린다** (우리가 따옴표를 더하지 않는다)
3. **`WorkingDirectory` 가 `DisplayPath` 다** — `\\?\` 가 새지 않는다
   (로컬 `C:\Temp` 와 UNC `\\10.10.10.23\home` 둘 다 확인)
4. `UseShellExecute` 가 `false` 다
5. `Win32Exception(2)`(파일 없음)을 던지면 `NotFound`
6. `Win32Exception(5)`(접근 거부)를 던지면 `AccessDenied`
7. `Win32Exception(1223)`(사용자 취소 · UAC 거부)처럼 매핑에 없는 코드는 `Unknown`
8. `FileNotFoundException` 을 던지면 `NotFound`
9. 아무 `Exception` 을 던지면 `Unknown`
10. `OperationCanceledException` 은 **그대로 나온다** (삼키지 않는다)
11. `executable` 이 `null`·`""`·`"   "` 면 **`start` 델리게이트가 아예 안 불리고**
    `NotFound` 가 나온다
12. `arguments` 가 빈 문자열이어도 정상이다 (인자 없는 프리셋 셋이 그렇다)
13. 이미 취소된 토큰이면 `OperationCanceledException`
14. **`WaitForExit` 를 안 부른다** — 주입 델리게이트가 반환한 뒤 `LaunchAsync` 가
    즉시 완료되는 것으로 채점한다 (델리게이트 안에서 시한을 재라)

**`start` 델리게이트를 안 주는 공개 생성자로 실제 프로세스를 띄우는 테스트를 쓰지 마라.**
이유: 게이트가 돌 때마다 창이 뜬다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 3 직후보다 커야 한다.
**`dotnet test` 도중 터미널·에디터 창이 하나도 뜨지 않아야 한다.**

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - 테스트 중에 **창이 하나도 안 떴는가?**
   - `WorkingDirectory` 가 `DisplayPath` 인가? (`Value` 가 아니라)
   - `UseShellExecute` 가 `false` 인가?
   - `WaitForExit` 를 안 부르는가?
   - `Win32ErrorMapping` 을 **재사용**했는가? (새 매핑 표를 만들지 않았는가)
   - 기존 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 4 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 클래스 이름·생성자 둘의 시그니처·오류 접기 표를 적어라 — step 5 가 이것을 문다.

## 금지사항

- **자동 테스트에서 실제 프로세스를 띄우지 마라.** 이유: 게이트가 돌 때마다 창이 뜬다
  (`.harness/HANDOFF.md` §규칙 5).
- **`WaitForExit` 를 부르지 마라.** 이유: 터미널이 닫힐 때까지 앱이 묶인다.
- **`ArgumentList` 를 쓰지 마라.** 이유: 사용자가 적은 한 줄을 우리가 쪼개면 따옴표
  규칙이 사용자 기대와 갈린다. 인자는 한 줄 문자열이라는 것이 스펙이다.
- **`WorkingDirectory` 에 `LocationId.Value` 를 넣지 마라.** 이유: `\\?\` 확장 접두사가
  붙은 경로를 작업 디렉터리로 받는 프로그램은 거의 없다.
- **`UseShellExecute = true` 로 두지 마라.** 이유: shell 이 대화상자를 띄우는 경로가
  열리고 그것은 모달 금지 위반이자 `--blame-hang` 에 걸린다.
- **새 오류 매핑 표를 만들지 마라.** 이유: `Win32ErrorMapping` 이 이미 있다. 표가 둘이
  되면 같은 코드가 두 뜻이 된다.
- **STA 워커를 쓰지 마라.** 이유: COM 이 아니다.
- **`FlexDir.Core`·`FlexDir.App` 을 건드리지 마라.** 이유: 포트는 step 1 이 확정했고
  배선은 step 5 다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
