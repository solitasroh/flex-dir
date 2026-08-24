# Step 5: pane-tools

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(**`FlexDir.App` 은 `FlexDir.Shell` 을 참조하지
  않는다** — Core 포트만 안다) · §3(UI 스레드) · §6(TDD 순서)
- `docs/ARCHITECTURE.md` §1 계층 구조 · §2 포트 표 — **참조 방향은 `scripts/check-structure.ps1` 이 기계로 잡지만, 어느 계층에 무엇을 놓는가는 여기 있다**
- `src/FlexDir.Core/Tools/ExternalToolCommand.cs` — **step 0.**
  `TerminalPreset` · `TerminalChoice` · `CanRunAt` · `FormatArguments` · `Resolve` · `Label`
- `src/FlexDir.Core/Tools/ExternalToolMessages.cs` — **step 0.** `Describe(kind, toolLabel)`
- `src/FlexDir.Core/Tools/IExternalToolCatalog.cs` · `IExternalToolLauncher.cs` — **step 1**
- `tests/FlexDir.Core.Tests/Fakes/FakeExternalToolCatalog.cs` ·
  `FakeExternalToolLauncher.cs` — **step 1**
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — **이 step 이 고치는 파일.** 특히:
  - 생성자 끝의 **선택 주입** 둘 (`IDriveSpace? driveSpace = null` ·
    `IKnownFolderList? knownFolders = null`) 과 그 주석 —
    *"주지 않으면 … 빈 채로 나머지가 돈다. 이 포트 하나 때문에 페인 테스트 수백 개를
    고치지 않는다"*. **이 step 도 같은 자리에 같은 방식으로 붙인다**
  - `private void LoadKnownFolders(CancellationToken ct)` — **본뜰 배경 적재 경로.**
    한 번만 묻는 가드 · 취소 처리 · `dispatcher.InvokeAsync` 로 공지 · 예외 삼킴
  - `public string StatusText { get; set; }` 와 `RefreshStatusText()`
  - `public LocationId? CurrentLocation { get; }`
  - `[RelayCommand]` 사용 예 (CommunityToolkit.Mvvm 소스 생성기)
  - `public bool ShowHiddenItems { get; set; }` — **바깥에서 밀어 넣는 설정 값의 기존 수**
- `tests/FlexDir.App.Tests/ViewModels/PaneKnownFoldersTests.cs` — 본뜰 테스트

## 배경

툴바 버튼 둘의 ViewModel 쪽이다. **두 버튼은 언제나 페인의 현재 폴더를 연다** —
선택 항목은 보지 않는다.

- `[VS]` — VS Code 로 현재 폴더를 연다. 탐지 실패면 비활성
- `[>_]` — 설정이 가리키는 터미널로 현재 폴더를 연다. 탐지 실패면 비활성
- **`\\server` 루트에서만** 둘 다 비활성. UNC 아래는 그대로 연다
- 실패는 `StatusText` 한 줄. 대화상자는 없다

## 작업

### 1. 생성자에 선택 주입 둘을 늘린다

기존 선택 주입(`driveSpace`·`knownFolders`) **바로 뒤**에 붙인다:

```csharp
// 같은 이유로 선택이다. 주지 않으면 외부 도구 버튼이 비활성인 채로 나머지가 돈다.
IExternalToolCatalog? externalTools = null,
IExternalToolLauncher? toolLauncher = null)
```

**기본값 `null` 이어야 한다.** 이유: 기존 페인 테스트 수백 개가 이 생성자를 부르고 있고
그것들을 고치지 않는다 (기존 두 포트가 그렇게 들어왔다).

### 2. 설정에서 밀어 넣는 값 하나

```csharp
/// 터미널 버튼이 열 것. WorkspaceViewModel 이 설정에서 밀어 넣는다
/// (ShowHiddenItems 와 같은 자리). 기본은 Windows Terminal 프리셋이다.
public TerminalChoice Terminal { get; set; } = new(TerminalPreset.WindowsTerminal);
```

**setter 에서 재탐지를 걸어라** — 프리셋이 바뀌면 탐지 대상이 바뀐다 (아래 §3).
값이 실제로 달라질 때만 걸어라 (같은 값을 다시 밀어 넣는 것이 재탐지 폭풍이 되면 안 된다).

### 3. 탐지를 배경에서 적재한다

```csharp
/// 외부 도구를 다시 찾는다. 시작 시 한 번, 그리고 창이 다시 보일 때마다
/// WorkspaceViewModel 이 부른다 — 상주 앱(ADR-003)이라 그 사이에 사용자가 VS Code 를
/// 설치하거나 지웠을 수 있다.
public void RefreshExternalTools();
```

`LoadKnownFolders` 를 **본뜨되 한 가지가 다르다**: 알려진 폴더는 *한 번만* 묻고,
이것은 **부를 때마다 다시 묻는다.** 그것이 사람 확인 8번이 밟는 자리다.

지켜야 할 것:

- **UI 스레드 밖에서 조회한다** (CLAUDE.md §3). 이미 배경인 자리에서 `await` 한다
- **결과 공지는 `dispatcher.InvokeAsync`** 로 UI 스레드에 올린다
- **취소는 조용히 물러난다.** 다음 호출이 다시 묻는다
- **예외는 삼킨다.** 포트 계약이 "실패는 `null`" 이지만 새면 버튼만 비활성으로 남는다 —
  외부 도구를 못 찾는 것이 폴더를 못 여는 사건이 되면 안 된다
- **겹쳐 부르는 것을 견뎌라.** 앞선 조회가 아직 안 끝났는데 또 불리면 **나중 것이
  이긴다** (`CancellationTokenSource` 를 교체한다). 이유: 창을 빠르게 여닫으면 실제로
  겹치고, 먼저 시작한 느린 조회가 나중 답을 덮으면 화면이 과거로 되돌아간다
- **터미널은 지금 고른 프리셋 하나만 묻는다.** 다섯을 다 묻지 마라 — 화면에 쓰이지 않는
  넷의 레지스트리 조회가 창을 열 때마다 나간다

내부 상태 둘을 둔다 (private, `OnPropertyChanged` 로 아래 §4 를 깨운다):

```csharp
private string? editorExecutable;    // null = 이 기계에 VS Code 가 없다
private string? terminalExecutable;  // null = 지금 고른 터미널이 없다
```

### 4. 활성 판정 둘

```csharp
/// [VS] 가 눌리는가. 탐지됐고, 현재 폴더가 서버 루트가 아니어야 한다.
public bool CanOpenInEditor => editorExecutable is not null
    && ExternalToolCommand.CanRunAt(CurrentLocation);

/// [>_] 가 눌리는가. 같은 판정이다.
public bool CanOpenInTerminal => terminalExecutable is not null
    && ExternalToolCommand.CanRunAt(CurrentLocation);
```

**`CurrentLocation` 이 바뀔 때 둘 다 `OnPropertyChanged` 를 내야 한다.** 안 내면
`\\10.10.10.23` 에서 `\\10.10.10.23\home` 으로 옮겨도 버튼이 회색인 채로 남는다.
`CurrentLocation` 을 세우는 기존 자리에 붙여라.

**`Custom` 프리셋은 탐지 대상이 아니다** (포트 계약 4번 — 카탈로그가 언제나 `null`).
그래서 `Custom` 일 때 `terminalExecutable` 은 **설정의 `TerminalExecutable` 을 그대로**
쓴다. 사용자가 적은 경로는 우리가 찾아 주는 것이 아니라 받는 것이다 — 틀렸으면
실행이 실패하고 그 사유가 `StatusText` 에 뜬다.

### 5. 커맨드 둘

```csharp
/// [VS] — 현재 폴더를 VS Code 로 연다.
[RelayCommand(CanExecute = nameof(CanOpenInEditor))]
private Task OpenInEditorAsync(CancellationToken ct);

/// [>_] — 현재 폴더를 설정이 가리키는 터미널로 연다.
[RelayCommand(CanExecute = nameof(CanOpenInTerminal))]
private Task OpenInTerminalAsync(CancellationToken ct);
```

두 커맨드가 하는 일:

```
1. 실행 파일과 인자를 정한다
     에디터  : editorExecutable · 인자는 "\"{path}\"" 를 FormatArguments 로 치환
     터미널  : terminalExecutable · Resolve(Terminal).Arguments 를 FormatArguments 로 치환
2. launcher.LaunchAsync(exe, args, CurrentLocation, ct)
3. 결과가 None 이 아니면
     dispatcher 로 StatusText = ExternalToolMessages.Describe(결과, 라벨)
   라벨은 에디터가 "VS Code", 터미널은 ExternalToolCommand.Label(Terminal.Preset)
```

- **`CanExecute` 가 거짓인데도 불릴 수 있다고 가정하라** — 방어적으로 `CurrentLocation`
  이 `null` 이거나 실행 파일이 `null` 이면 아무것도 하지 않고 돌아간다.
  이유: `Command` 가 살아 있는 `MenuItem` 은 정상으로 뜨고 눌린다 (`docs/PRD-v2.md` §17)
- **`CanExecute` 가 바뀌면 `NotifyCanExecuteChanged` 를 내라.** `[RelayCommand]` 는
  스스로 감지하지 않는다. `CurrentLocation` · 탐지 결과 · `Terminal` 셋이 바뀌는 자리
  전부에서 낸다. 안 내면 **버튼이 영원히 회색이다**
- **성공했을 때 `StatusText` 를 건드리지 마라.** 상태표시줄의 항목 수 요약이 이유 없이
  지워진다. 말할 것이 있을 때만 말한다
- **예외를 밖으로 내보내지 마라.** 커맨드 밖으로 나간 예외는 잡을 사람이 없고
  프로세스를 죽인다 (`.harness/HANDOFF.md` §규칙 10 — 실물에서 밟았다).
  포트가 안 던지는 계약이어도 여기서 한 번 더 막는다

### 6. 테스트

**`tests/FlexDir.App.Tests/ViewModels/PaneExternalToolsTests.cs`** 를 새로 만든다.
step 1 의 fake 둘을 쓴다.

채점할 것:

1. 포트를 안 주면 (`null`) `CanOpenInEditor`·`CanOpenInTerminal` 이 둘 다 `false` 이고
   **폴더 열기는 정상이다**
2. 에디터가 탐지되고 `C:\Temp` 에 있으면 `CanOpenInEditor` 가 `true`
3. **에디터 탐지가 `null` 이면 `CanOpenInEditor` 가 `false`** (사람 확인 8번의 기계 판정)
4. **`\\10.10.10.23` 에서 둘 다 `false`**
5. **`\\10.10.10.23\home` 에서 둘 다 `true`** (탐지가 됐다면)
6. `\\10.10.10.23` → `\\10.10.10.23\home` 으로 옮기면 **`PropertyChanged` 가 나오고**
   판정이 뒤집힌다
7. `OpenInEditorCommand` 를 실행하면 launcher 에 **(에디터 경로, `"C:\Temp"`, 그 폴더)**
   가 기록된다 — **인자에 따옴표가 하나씩만** 있다
8. `OpenInTerminalCommand` 가 프리셋의 인자 틀을 치환해 넘긴다
   (`WindowsTerminal` → `-d "C:\Temp"`)
9. **`Custom` 프리셋**: `Terminal = new(Custom, "wezterm-gui.exe", "start --cwd \"{path}\"")`
   이면 카탈로그가 `null` 을 내도 **`CanOpenInTerminal` 이 `true`** 이고 launcher 에
   `wezterm-gui.exe` / `start --cwd "C:\Temp"` 가 간다 (사람 확인 6번의 기계 판정)
10. `Custom` 인데 실행 파일이 `null`·빈 문자열이면 `CanOpenInTerminal` 이 `false`
11. launcher 가 `NotFound` 를 내면 `StatusText` 가 바뀌고 **`"VS Code"` 를 포함**한다
12. launcher 가 `None` 을 내면 **`StatusText` 가 안 바뀐다**
13. launcher 가 던져도 **예외가 밖으로 안 나오고** 페인이 살아 있다
14. `RefreshExternalTools()` 를 두 번 부르면 **카탈로그 조회가 두 번 나간다**
    (창 재표시마다 다시 묻는다는 증거)
15. 탐지 결과가 `null` → 경로 로 바뀌면 `CanOpenInEditor` 의 `PropertyChanged` 와
    커맨드의 `CanExecuteChanged` 가 **둘 다** 나온다
16. `Terminal` 을 다른 프리셋으로 바꾸면 재탐지가 나가고, **같은 값을 다시 넣으면
    안 나간다**
17. 겹쳐 부르면 **나중 호출의 결과가 이긴다**

**기존 페인 테스트가 하나도 안 깨져야 한다** — 그것이 선택 주입을 고른 이유다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 4 직후보다 커야 한다.
**`dotnet test` 도중 터미널·에디터 창이 하나도 뜨지 않아야 한다** (fake 를 쓴다).

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1` 이 본다)
   - 탐지가 **UI 스레드 밖**인가?
   - `RefreshExternalTools` 가 **부를 때마다** 다시 묻는가? (캐시하지 않는가)
   - `CanExecuteChanged` 를 내는가? (안 내면 버튼이 영원히 회색이다)
   - 성공했을 때 `StatusText` 를 안 건드리는가?
   - 기존 페인 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 5 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 **정확한 공개 이름 다섯**을 적어라 — `Terminal` · `CanOpenInEditor` ·
`CanOpenInTerminal` · `OpenInEditorCommand` · `OpenInTerminalCommand` · `RefreshExternalTools`.
step 6 이 밀어 넣고 step 8 이 XAML 에서 바인딩한다. **이름이 하나라도 어긋나면 WPF 는
컴파일 에러 없이 조용히 죽는다** (`docs/PRD-v2.md` §17 §값을 치르고 배운 것).

## 금지사항

- **`FlexDir.Shell` 을 참조하지 마라.** 이유: CLAUDE.md §1. 구현체는 Host 가 주입한다.
- **생성자의 새 매개변수를 필수로 만들지 마라.** 이유: 기존 페인 테스트 수백 개가 깨진다.
  기존 선택 주입 둘이 정확히 그 이유로 그렇게 들어왔다.
- **탐지 결과를 캐시하지 마라.** 이유: 사람 확인 8번(*VS Code 를 지우고 창을 다시 보이면
  비활성*)이 못 밟힌다.
- **터미널 프리셋 다섯을 한꺼번에 묻지 마라.** 이유: 화면에 안 쓰이는 넷의 레지스트리
  조회가 창을 열 때마다 나간다.
- **XAML 을 건드리지 마라.** 이유: 배선은 step 8 이다.
- **설정(`SettingsViewModel`)을 직접 읽지 마라.** 이유: 페인은 설정을 모른다.
  `ShowHiddenItems` 와 같이 `WorkspaceViewModel` 이 밀어 넣는다 (step 6).
- **대화상자를 띄우지 마라.** 이유: `docs/UI_GUIDE.md` 의 모달 금지. 실패는 `StatusText`
  한 줄이다.
- **커맨드 안에서 예외가 밖으로 나가게 두지 마라.** 이유: 프로세스가 죽는다
  (`.harness/HANDOFF.md` §규칙 10 — 실물에서 밟았다).
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
