# Step 1: tool-ports

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(참조 방향) · §3(UI 스레드에서 저장소 호출 금지) ·
  §6(TDD 순서) · §6-5(Core 포트의 fake 는 `tests/FlexDir.Core.Tests/Fakes/` 에 둔다)
- `src/FlexDir.Core/Tools/ExternalToolCommand.cs` — **step 0 이 만든 파일.**
  `TerminalPreset`(6값) · `TerminalChoice` · `ExternalToolCommand.Resolve` 가 여기 있다
- `src/FlexDir.Core/Locations/IKnownFolderList.cs` — **이 step 이 본뜨는 포트다.**
  특히 세 가지를 그대로 따른다: ① 없는 것은 걸러 내지 않고 `null` 로 낸다
  ② **실패는 던지지 않는다, 취소만 예외다** ③ 목록은 한 번에 온다
- `tests/FlexDir.Core.Tests/Fakes/FakeKnownFolderList.cs` — 이 step 이 본뜨는 fake
- `src/FlexDir.Core/Errors/LocationError.cs` — `LocationErrorKind`
- `docs/ARCHITECTURE.md` §2 포트 표

## 배경

외부 도구는 포트 **둘**로 갈린다 — **탐지**(`IExternalToolCatalog`)와
**실행**(`IExternalToolLauncher`). 열거와 조작을 가른 v1 의 수와 같은 판단이다:
탐지는 여러 번 반복되고 실패해도 조용해야 하지만, 실행은 한 번이고 실패하면 사용자에게
말해야 한다. 한 포트에 섞으면 "조용히 실패" 와 "말해야 하는 실패" 의 계약이 충돌한다.

**이 step 은 포트 선언과 fake 뿐이다.** Shell 구현은 step 3·4 다.

## 작업

### 1. `src/FlexDir.Core/Tools/IExternalToolCatalog.cs`

TDD 가드는 **인터페이스도 예외로 두지 않는다** (CLAUDE.md §6-2). 먼저
`tests/FlexDir.Core.Tests/Tools/IExternalToolCatalogTests.cs` 를 쓰고 red 를 확인한다.

```csharp
namespace FlexDir.Core.Tools;

/// 이 기계에 설치된 외부 도구를 찾는 포트. 구현체는 FlexDir.Shell 에 둔다.
public interface IExternalToolCatalog
{
    /// 에디터(VS Code)의 실행 파일 전체 경로. 없으면 null.
    ValueTask<string?> FindEditorAsync(CancellationToken ct);

    /// 프리셋이 쓰는 터미널의 실행 파일 전체 경로. 없으면 null.
    /// Custom 은 사용자가 적은 것이라 탐지 대상이 아니다 — 넘기면 null 을 낸다.
    ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct);
}
```

인터페이스 XML 주석에 **계약 셋을 반드시 적는다**:

1. **실패는 던지지 않는다 — 못 찾으면 `null` 이다.** 취소만 예외로 나온다.
   이유: 외부 도구는 곁다리이고, 못 찾는 것이 폴더를 못 여는 사건이 되면 안 된다
   (`IKnownFolderList` 와 같은 계약).
2. **여러 번 불릴 수 있다.** 상주 앱(ADR-003)이라 창이 다시 보일 때마다 다시 묻는다 —
   그 사이에 사용자가 VS Code 를 설치하거나 지웠을 수 있다. 구현은 결과를 캐시하지 마라.
3. **UI 스레드에서 부를 수 없다** (CLAUDE.md §3). 레지스트리·파일시스템 조회이고,
   `%ProgramFiles%` 가 리디렉션된 기계에서는 얼마나 걸릴지 모른다.

`IExternalToolCatalogTests.cs` 가 채점할 것 — **fake 를 상대로 계약을 고정한다**:

1. 아무것도 안 넣은 fake 가 `FindEditorAsync` 에 `null` 을 낸다 (던지지 않는다)
2. 경로를 넣으면 그것을 낸다
3. `TerminalPreset` 여섯 값 전부에 `FindTerminalAsync` 가 답한다 (던지지 않는다)
4. **`TerminalPreset.Custom` 은 무엇을 넣어도 `null` 이다**
5. 이미 취소된 `CancellationToken` 을 주면 `OperationCanceledException` 이 나온다
6. 같은 인자로 두 번 부르면 **호출이 두 번 센다** (캐시하지 않는 계약)

### 2. `src/FlexDir.Core/Tools/IExternalToolLauncher.cs`

먼저 `tests/FlexDir.Core.Tests/Tools/IExternalToolLauncherTests.cs` 를 쓴다.

```csharp
namespace FlexDir.Core.Tools;

/// 외부 도구를 띄우는 포트. 구현체는 FlexDir.Shell 에 둔다.
public interface IExternalToolLauncher
{
    /// 프로그램을 그 폴더를 작업 디렉터리로 띄운다.
    /// 성공은 LocationErrorKind.None, 실패는 그 사유다.
    ValueTask<LocationErrorKind> LaunchAsync(
        string executable,
        string arguments,
        LocationId workingFolder,
        CancellationToken ct);
}
```

인터페이스 XML 주석에 **계약 넷을 반드시 적는다**:

1. **실패를 던지지 않고 `LocationErrorKind` 로 낸다.** 이유: 실패는 정상 상황이고
   (프로그램을 지웠을 수 있다) 호출자는 그것을 상태표시줄 한 줄로 만들어야 한다.
   대화상자를 띄우는 길은 없다 (`docs/UI_GUIDE.md` 모달 금지).
2. **띄우기만 하고 기다리지 않는다.** 종료를 기다리면 UI 가 그 프로그램이 닫힐 때까지
   묶인다. `arguments` 는 이미 치환이 끝난 문자열이다 (step 0 `FormatArguments`).
3. **`workingFolder` 는 작업 디렉터리로만 쓴다** — 인자에 자동으로 덧붙이지 않는다.
   무엇을 인자로 줄지는 호출자가 이미 정했다.
4. **UI 스레드에서 부를 수 없다** (CLAUDE.md §3). 프로세스 생성은 실행 파일이 네트워크
   경로에 있으면 초 단위로 블로킹된다.

`IExternalToolLauncherTests.cs` 가 채점할 것:

1. fake 가 기본으로 `LocationErrorKind.None` 을 낸다
2. 실패를 주입하면 그 값이 나온다
3. **호출 인자 셋(executable · arguments · workingFolder)이 그대로 기록된다**
4. 이미 취소된 토큰이면 `OperationCanceledException`
5. 여러 번 부르면 기록이 순서대로 쌓인다

### 3. fake 둘

`tests/FlexDir.Core.Tests/Fakes/FakeExternalToolCatalog.cs`:

- `public string? Editor { get; set; }`
- `public Dictionary<TerminalPreset, string?> Terminals { get; } = [];`
- `public int EditorRequests { get; private set; }` ·
  `public IReadOnlyList<TerminalPreset> TerminalRequests { get; }`
- `Custom` 은 사전에 무엇이 있든 `null` (계약 4번)
- 취소 토큰을 **반드시 확인한다** (`ct.ThrowIfCancellationRequested()`)

`tests/FlexDir.Core.Tests/Fakes/FakeExternalToolLauncher.cs`:

- `public LocationErrorKind Result { get; set; } = LocationErrorKind.None;`
- `public IReadOnlyList<(string Executable, string Arguments, LocationId Folder)> Launches { get; }`
- 취소 토큰 확인

**둘 다 `tests/FlexDir.Core.Tests/Fakes/` 에 둔다** (CLAUDE.md §6-5). `FlexDir.App.Tests`
가 그 프로젝트를 참조해 step 5·6·7 에서 재사용한다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 0 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - 포트 둘 다 `FlexDir.Core` 안에 있고 Windows API 를 참조하지 않는가?
   - 계약(실패는 null / 실패는 `LocationErrorKind` / 취소만 예외)이 **XML 주석에 적혀
     있는가?** 다음 step 이 그 주석만 읽고 구현한다
   - fake 둘이 `tests/FlexDir.Core.Tests/Fakes/` 에 있는가?
   - 기존 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 1 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 포트 둘의 정확한 시그니처와 fake 둘의 공개 속성 이름을 적어라 —
step 3·4 가 구현하고 step 5·6·7 이 fake 를 쓴다.

## 금지사항

- **구현체를 만들지 마라.** 이유: `FlexDir.Shell` 은 step 3·4 다. 이 step 은 선언뿐이다.
- **결과를 캐시하는 계약을 적지 마라.** 이유: 사람 확인 8번이 *"VS Code 를 지우고 창을
  숨겼다 다시 보이면 비활성이 된다"* 를 밟는다. 캐시하면 그 확인이 영원히 실패한다.
- **포트 둘을 하나로 합치지 마라.** 이유: 위 §배경. 조용한 실패와 말해야 하는 실패는
  계약이 다르다.
- **`FindTerminalAsync` 가 `Custom` 에 무언가를 찾게 하지 마라.** 이유: 사용자가 적은
  경로는 탐지의 대상이 아니라 입력이다. 여기서 찾으려 들면 오타를 조용히 고쳐 버린다.
- **포트에 창 핸들·`nint` 를 넣지 마라.** 이유: Core 포트에는 창 핸들이 없다
  (`.harness/HANDOFF.md` §포트 현황).
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
