# Step 7: settings-terminal-ui

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §2(**코드비하인드 금지** — `*.xaml.cs` 에는
  `InitializeComponent()` 만) · §3(UI 스레드) · §6(TDD 순서)
- `docs/ARCHITECTURE.md` §1 계층 구조 · §2 포트 표 — **참조 방향은 `scripts/check-structure.ps1` 이 기계로 잡지만, 어느 계층에 무엇을 놓는가는 여기 있다**
- `docs/UI_GUIDE.md` — **모달 금지**. 설정 패널은 창 안 오버레이다
- `docs/PRD-v2.md` §12 — 설정 창
- `src/FlexDir.Core/Tools/ExternalToolCommand.cs` — **step 0.**
  `TerminalPreset`(6값) · `TerminalChoice` · `Resolve` · `Label` · `FormatArguments` ·
  `PathToken`("{path}")
- `src/FlexDir.Core/Tools/ExternalToolMessages.cs` — **step 0.** `Describe(kind, toolLabel)`
- `src/FlexDir.Core/Tools/IExternalToolCatalog.cs` · `IExternalToolLauncher.cs` — **step 1**
- `src/FlexDir.Core/Settings/ISettingsStore.cs` — **step 2 가 늘린 부분.**
  `TerminalPreset`·`TerminalExecutable`·`TerminalArguments`·`ResolveTerminal()`
- `src/FlexDir.App/ViewModels/SettingsViewModel.cs` — **이 step 이 고치는 파일.** 특히:
  - 생성자 (`ISettingsStore` · `IUiDispatcher` · 버전 · `stateDirectory` ·
    `ISystemThemeSource` · `UpdateViewModel`)
  - 테마 라디오 셋 (`IsSystemTheme`·`IsLightTheme`·`IsDarkTheme`) — **본뜰 라디오 배관**
  - `StartFolderText`·`StartFolderError` — **본뜰 텍스트 상자 + 오류 문구 배관**
  - `public AppSettings Current { get; }` 와 `LoadAsync`
  - **step 6 이 넣은 `public event EventHandler? TerminalChanged;`**
- `src/FlexDir.App/Views/MainWindow.xaml` §설정 패널 (2398줄 부근부터 파일 끝까지) —
  스타일 `SettingsHeading`·`SettingsNote`·`SettingsChoice`·`SettingsToggle`·
  `SettingsButton`·`SettingsPathBox` 와 §시작 폴더 블록(라디오 둘 + 텍스트 상자 + 버튼 +
  오류 문구)의 생김새
- `tests/FlexDir.App.Tests/ViewModels/SettingsViewModelTests.cs`

## 배경

설정 패널에 **터미널** 절을 하나 늘린다. 프리셋 다섯 + 사용자 지정, 그리고
`[실행해 보기]` 버튼.

**§시작 폴더 블록과 형태가 같다** — 선택지 라디오 + 그 선택일 때만 열리는 입력 칸 +
곁의 버튼 + 오류 문구 한 줄. 그 블록을 본뜨면 이 절의 배치 판단이 거의 없다.

## 작업

### 1. `SettingsViewModel` 에 터미널 상태를 붙인다

생성자에 **선택 주입 둘**을 끝에 늘린다 (기본값 `null` — 기존 테스트가 안 깨지게):

```csharp
IExternalToolCatalog? externalTools = null,
IExternalToolLauncher? toolLauncher = null)
```

공개 표면:

```csharp
/// 목록에 뜨는 것 하나. Label 은 사람이 읽는 이름, Preset 은 값.
public sealed record TerminalOption(TerminalPreset Preset, string Label);

/// 프리셋 여섯 (사용자 지정 포함). 순서는 TerminalPreset 선언 순서다.
public IReadOnlyList<TerminalOption> TerminalOptions { get; }

/// 지금 고른 것. 바뀌면 TerminalChanged 가 난다.
public TerminalOption SelectedTerminal { get; set; }

/// 사용자 지정을 골랐는가. 아래 두 칸의 IsEnabled 가 이것이다.
public bool IsCustomTerminal { get; }

/// 사용자 지정 실행 파일. 비면 null 로 접힌다.
public string? TerminalExecutable { get; set; }

/// 사용자 지정 인자 한 줄. {path} 가 현재 폴더로 바뀐다.
public string? TerminalArguments { get; set; }

/// [실행해 보기] 의 결과 한 줄. 아무 말도 없으면 null.
public string? TerminalTestResult { get; private set; }

/// [실행해 보기].
[RelayCommand]
private Task TestTerminalAsync(CancellationToken ct);
```

`Current` 에 새 항목 셋을 실어야 한다 — **`Current` 를 고치지 않으면 저장이 안 된다.**

셋 중 무엇이 바뀌어도 **step 6 의 `TerminalChanged` 를 낸다.** 같은 값이면 안 낸다.

`LoadAsync` 가 저장된 값을 화면 상태로 편다 (`SelectedTerminal`·두 문자열).

### 2. `[실행해 보기]` — `TestTerminalAsync`

```
1. 지금 화면 상태로 TerminalChoice 를 만든다 (Current.ResolveTerminal())
2. 실행 파일을 정한다
     Custom 이 아니면 → externalTools.FindTerminalAsync(preset, ct)
     Custom 이면      → TerminalExecutable 그대로
3. 못 찾았으면 → TerminalTestResult = ExternalToolMessages.Describe(NotFound, 라벨)  후 끝
4. launcher.LaunchAsync(exe, 치환된 인자, 시험 폴더, ct)
5. None 이면  → TerminalTestResult = "{라벨} 을(를) 열었습니다"
   아니면     → TerminalTestResult = ExternalToolMessages.Describe(결과, 라벨)
```

**시험 폴더는 상태 폴더다** (`%APPDATA%\flex-dir` — 생성자가 이미 `stateDirectory` 로
받고 있고 `StateFolderText` 가 그것이다). 이유: 설정 패널은 어느 페인의 폴더인지 모르고,
그것을 알려면 배관이 하나 더 는다. 상태 폴더는 **언제나 있고** 눌러 본 사람이 무엇이
열렸는지 바로 안다. **버튼 곁 안내 문구에 "상태 폴더에서 열어 봅니다" 라고 적어라** —
어디가 열릴지 모르는 버튼은 누르기 무섭다.

`LocationId.TryParse` 가 실패하면 (있을 수 없지만) 조용히 아무것도 하지 않는다.

- **포트가 `null` 인 조립에서는 아무것도 하지 않는다** (기존 `Update` 가 `null` 일 수
  있는 것과 같은 자리)
- **예외를 밖으로 내보내지 마라** (`.harness/HANDOFF.md` §규칙 10)
- **`TerminalTestResult` 는 다음 시도에서 지운다** — 옛 결과가 남아 있으면 방금 누른
  것의 답으로 읽힌다

### 3. `MainWindow.xaml` 설정 패널에 절을 하나 늘린다

**자리**: §숨김·시스템 파일 **바로 앞** (테마 → 시작 폴더 → **터미널** → 숨김 항목).
앞뒤로 기존과 같은 `<Separator Margin="0,16" .../>` 를 둔다.

```
TextBlock  SettingsHeading   "터미널"
ComboBox   (TerminalOptions / SelectedTerminal / DisplayMemberPath=Label)
TextBlock  SettingsNote      "[>_] 버튼이 현재 폴더에서 여는 프로그램입니다."

Grid (20,4,0,0)  ─ 사용자 지정 실행 파일
  TextBox  SettingsPathBox   IsEnabled={IsCustomTerminal}  Text={TerminalExecutable}
TextBox    SettingsPathBox   IsEnabled={IsCustomTerminal}  Text={TerminalArguments}
TextBlock  SettingsNote      "인자의 {path} 가 현재 폴더 경로로 바뀝니다. 예: start --cwd \"{path}\""

Button     SettingsButton    "실행해 보기"   Command={Settings.TestTerminalCommand}
TextBlock  SettingsNote      "상태 폴더에서 열어 봅니다."
TextBlock  (TerminalTestResult · null 이면 Collapsed — StartFolderError 블록을 본뜬다)
```

지켜야 할 것:

- **`ComboBox` 를 쓴다** (라디오 여섯이 아니다). 이유: 테마는 셋이라 라디오가 맞았지만
  여섯은 패널을 세로로 길게 늘여 그 아래 §숨김 항목이 스크롤 밖으로 밀린다
- **텍스트 상자 둘은 `IsCustomTerminal` 일 때만 열린다** — §시작 폴더의 판단 그대로다:
  *"고칠 수 있는데 아무 효과가 없으면 고쳐 놓고 왜 안 되는지 찾게 된다"*
- **`[실행해 보기]` 는 잠그지 마라.** 프리셋을 고른 상태에서도 눌러 봐야 그 프리셋이
  이 기계에 있는지 알 수 있다 — 그것이 이 버튼의 존재 이유다
- **`UpdateSourceTrigger` 는 기본(`LostFocus`)** 을 그대로 쓴다. 이유는 §시작 폴더
  주석에 있다 — 글자마다 바꾸면 치는 도중의 반쪽 값이 매번 저장된다
- **`{path}` 를 XAML 에 그대로 쓰면 WPF 가 마크업 확장으로 읽고 파스 에러가 난다.**
  `{}{path}` 로 이스케이프하거나 안내 문구를 리소스 문자열로 뺀다.
  **이것을 빼먹으면 창이 아예 안 뜬다** (`XamlParseException` — §10 의 `StaticResource`
  크래시와 같은 종류로, 컴파일은 통과하고 실행에서 터진다)
- **`ComboBox` 는 앱의 암시적 스타일이 없을 수 있다** — 다크에서 흰 상자로 뜨는지
  아래 §4 의 실물 캡처로 확인하고, 그렇다면 `Foreground`/`Background` 를
  `DynamicResource` 로 명시한다. **v0.7.0 에서 `PaneList` 에 `Foreground` 가 없어
  라이트 시절에 우연히 맞던 것이 다크에서 드러났다** (`.harness/HANDOFF.md` §0-3)
- **코드비하인드를 만들지 마라** (CLAUDE.md §2)

### 4. 실물 확인 — 이 step 안에서 한다

> 사용자 결정 2026-08-24: **UI step 은 화면을 실제로 본 것까지가 완료 조건이다.**
> 게이트 4종이 1구간에서 화면 결함 둘(`MenuItem.Icon` 미표시 · 두부 글리프)을 그대로
> 통과시켰다.

```powershell
# ① 저장소 Debug 빌드로 띄운다
Start-Process .\src\FlexDir.Host\bin\Debug\net9.0-windows\FlexDir.Host.exe
# ② 설정 패널을 열고 터미널 절이 보이는 것을 확인한다 (UI Automation)
#    - 창이 실제로 떴는가            ← XamlParseException 이면 안 뜬다
#    - "터미널" 제목이 트리에 있는가
#    - ComboBox 항목이 여섯인가
#    - "실행해 보기" 버튼이 있는가
# ③ 반드시 죽인다
Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue
```

⚠ **③을 빼먹으면 다음 게이트가 파일 잠금으로 깨진다** (`.harness/HANDOFF.md` §배포 절차).
저장소 Debug 빌드는 자기 산출물을 잠근다.

⚠ 설정 패널을 여는 것은 툴바 오른쪽의 설정 버튼(`E713`)이다. **UI Automation 의
`Invoke` 로 누를 수 있다** — 포그라운드가 필요 없다. 합성 마우스 입력은 쓰지 마라
(포그라운드를 못 잡아 조용히 실패한다).

**창이 안 뜨거나 절이 안 보이면 step 실패다.** 고쳐서 통과시켜라.

### 5. 테스트

`tests/FlexDir.App.Tests/ViewModels/SettingsViewModelTests.cs` 에 추가:

1. `TerminalOptions` 가 여섯이고 순서가 `TerminalPreset` 선언 순서다
2. 라벨이 `ExternalToolCommand.Label` 과 같다
3. `SelectedTerminal` 을 바꾸면 `Current.TerminalPreset` 이 따라간다
4. `Custom` 을 고르면 `IsCustomTerminal` 이 `true`, 아니면 `false`
5. `IsCustomTerminal` 이 `SelectedTerminal` 변경 시 `PropertyChanged` 를 낸다
6. `TerminalExecutable = ""` 이면 `Current.TerminalExecutable` 이 `null`
7. `LoadAsync` 가 저장된 `Custom` + 실행 파일 + 인자를 화면 상태로 편다
8. `TestTerminalAsync` 가 프리셋을 골랐을 때 **카탈로그로 찾고** launcher 에 넘긴다
9. **인자의 `{path}` 가 상태 폴더 경로로 치환돼 넘어간다**
10. `Custom` 이면 카탈로그를 **안 묻고** 적힌 실행 파일을 그대로 쓴다
11. 카탈로그가 `null` 을 내면 launcher 를 **안 부르고** `TerminalTestResult` 가 찬다
12. launcher 가 `AccessDenied` 를 내면 `TerminalTestResult` 가 그 사유다
13. 성공하면 `TerminalTestResult` 가 성공 문구다 (빈 문자열이 아니다)
14. 두 번째 시도 전에 `TerminalTestResult` 가 지워진다
15. 포트가 `null` 인 조립에서 `TestTerminalCommand` 가 **던지지 않는다**
16. launcher 가 던져도 던지지 않는다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 6 직후보다 커야 한다.
**그리고 위 §4 의 실물 확인이 통과해야 한다** — 창이 뜨고 터미널 절이 트리에 있다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. §4 의 실물 확인을 하고 **`Stop-Process` 로 반드시 죽인다.**
3. 체크리스트:
   - `*.xaml.cs` 에 `InitializeComponent()` 외의 것이 없는가?
   - `Current` 에 새 항목 셋이 실렸는가? (안 실으면 저장이 안 된다)
   - `{path}` 를 XAML 에 이스케이프했는가? (안 하면 창이 안 뜬다)
   - 실물에서 창이 떴는가? 터미널 절이 보이는가?
   - **`FlexDir.Host` 프로세스를 죽였는가?** (`Get-Process FlexDir.Host` 가 비어야 한다)
   - 기존 설정 테스트가 하나도 안 깨졌는가?
4. `phases/5-external-tools/index.json` 의 step 7 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 새 공개 이름 일곱과 **실물에서 무엇을 보았는지**를 적어라.

## 금지사항

- **코드비하인드를 만들지 마라.** 이유: CLAUDE.md §2 — WPF 에서 god object 가 자라는 자리다.
- **대화상자를 띄우지 마라.** 이유: `docs/UI_GUIDE.md` 모달 금지. 결과는 패널 안 한 줄이다.
- **`{path}` 를 XAML 에 이스케이프 없이 쓰지 마라.** 이유: WPF 가 마크업 확장으로 읽어
  `XamlParseException` 이 나고 **창이 아예 안 뜬다.**
- **`[실행해 보기]` 를 잠그지 마라.** 이유: 그 프리셋이 이 기계에 있는지 알아보는 것이
  이 버튼의 목적이다.
- **툴바를 건드리지 마라.** 이유: `[VS]`·`[>_]` 버튼은 step 8 이다.
- **전역 `ComboBox` 스타일을 새로 만들지 마라.** 이유: 앱의 모든 콤보에 번진다
  (CLAUDE.md §2.3). 색이 필요하면 이 자리에서만 `DynamicResource` 로 준다.
- **실물 확인 뒤 `FlexDir.Host` 를 살려 두지 마라.** 이유: 저장소 Debug 빌드는 산출물을
  잠그고 다음 게이트가 오류 수십 개로 깨진다.
- **`WorkspaceViewModel`·`PaneViewModel` 을 건드리지 마라.** 이유: step 5·6 이 끝냈다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
