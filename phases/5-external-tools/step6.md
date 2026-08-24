# Step 6: workspace-tools

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §2(코드비하인드 금지) · §3(UI 스레드) · §6(TDD 순서)
- `docs/ADR.md` ADR-003 — **상주 프로세스.** 닫기는 숨기기이고 프로세스는 살아 있다.
  창은 여러 번 다시 보인다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — **step 5 가 늘린 부분.**
  `public TerminalChoice Terminal { get; set; }` · `public void RefreshExternalTools()`
- `src/FlexDir.App/ViewModels/WorkspaceViewModel.cs` — **이 step 이 고치는 파일.** 특히:
  - `settings.HiddenItemsChanged += (_, _) => hiddenItemsWork = ApplyHiddenItemsAsync();`
    — **본뜰 배관.** 설정이 바뀌면 페인들에 다시 민다
  - `pane.ShowHiddenItems = settings.ShowHiddenItems;` 가 나오는 자리 셋 (새 탭 생성 ·
    시작 적재 · 탭 복제) — **터미널 선택도 같은 자리마다 밀어야 한다.** 하나라도
    빠뜨리면 그 탭만 기본 프리셋으로 남는다
  - `private async ValueTask<AppSettings> LoadSettingsAsync(CancellationToken ct)`
- `src/FlexDir.App/ViewModels/SettingsViewModel.cs` — `HiddenItemsChanged` 이벤트와
  `public AppSettings Current { get; }`
- `src/FlexDir.Core/Settings/ISettingsStore.cs` — **step 2 가 늘린 부분.**
  `TerminalPreset`·`TerminalExecutable`·`TerminalArguments` 와 `ResolveTerminal()`
- `src/FlexDir.App/Views/ResidentWindow.cs` — **이 step 이 고치는 파일.**
  `Attach(Window, WorkspaceViewModel)` 이 창 이벤트를 거는 유일한 자리다.
  클래스 주석: *"판정만 채점하고, 이벤트 훅은 사람이 확인한다"*
- `tests/FlexDir.App.Tests/ViewModels/WorkspaceSettingsTests.cs` — 본뜰 테스트
- `tests/FlexDir.App.Tests/Views/ResidentWindowTests.cs`

## 배경

step 5 가 페인에 `Terminal`(무엇을 열까)과 `RefreshExternalTools()`(다시 찾아라) 를
만들었다. **이 step 이 그 둘에 실제 신호를 연결한다.**

- **설정 → 페인**: 사용자가 설정에서 터미널을 바꾸면 열려 있는 모든 탭에 반영된다
- **창이 다시 보임 → 재탐지**: 상주 앱(ADR-003)이라 창을 숨겼다 다시 보이는 사이에
  사용자가 VS Code 를 설치하거나 지웠을 수 있다. **사람 확인 8번이 정확히 이 경로다**

## 작업

### 1. `SettingsViewModel` 에 이벤트 하나를 늘린다

```csharp
/// 터미널 선택이 바뀌었다. WorkspaceViewModel 이 열려 있는 페인들에 다시 민다.
public event EventHandler? TerminalChanged;
```

`HiddenItemsChanged` 를 **그대로 본뜬다** — 같은 곳에서 같은 방식으로 낸다.
**`TerminalPreset`·`TerminalExecutable`·`TerminalArguments` 셋 중 무엇이 바뀌어도 낸다.**
아직 화면(라디오·텍스트 상자)은 step 7 이 붙인다 — 이 step 은 **이벤트와 그것을 내는
내부 경로만** 만든다. `LoadAsync` 로 값이 처음 들어올 때도 낸다.

### 2. `WorkspaceViewModel` 이 설정을 페인에 민다

`pane.ShowHiddenItems = ...` 가 나오는 **모든 자리**에 `pane.Terminal = ...` 를 나란히
붙인다. 값은 `settings.Current.ResolveTerminal()` 이다.

그리고 `HiddenItemsChanged` 구독 옆에 하나 더:

```csharp
settings.TerminalChanged += (_, _) => ApplyTerminal();
```

`ApplyTerminal()` 은 열려 있는 **모든 탭의 모든 페인**에 지금 선택을 민다.
`ApplyHiddenItemsAsync` 가 도는 범위와 **정확히 같은 범위**여야 한다 — 그쪽이
"접힌 페인의 배경 탭까지" 를 이미 풀어 두었다.

`Settings` 가 `null` 인 조립(테스트용)에서는 아무것도 하지 않는다 — 기존 코드가
`if (Settings is not { } settings)` 로 그렇게 하고 있다.

### 3. 창이 다시 보이면 재탐지한다

`WorkspaceViewModel` 에:

```csharp
/// 창이 (다시) 보였다. 상주 앱(ADR-003)이라 이 사이에 사용자가 외부 도구를 설치하거나
/// 지웠을 수 있다 — 열려 있는 페인들이 다시 찾게 한다.
public void OnWindowShown();
```

- 열려 있는 모든 탭의 모든 페인에 `RefreshExternalTools()` 를 부른다
- **기다리지 않는다.** 창이 뜨는 길에 레지스트리 조회를 얹으면 `WindowShown` 예산
  100ms 를 먹는다 (`.harness/HANDOFF.md` §Host 가 지금 하는 일 — 상주 중 실측 3~9ms)
- **던지지 않는다.** 창이 뜨는 길에서 던지면 창이 안 뜬다

### 4. `ResidentWindow` 가 그 신호를 만든다

`ResidentWindow.Attach` 안에서 `window.IsVisibleChanged` 를 건다 —
**보이게 될 때만** `workspace.OnWindowShown()` 을 부른다.

```csharp
window.IsVisibleChanged += (_, e) =>
{
    if (e.NewValue is true)
    {
        workspace.OnWindowShown();
    }
};
```

**첫 표시도 이것으로 덮인다** — 시작 시 재탐지를 따로 걸 필요가 없다. 스펙의
*"시작 시 + 창이 다시 보일 때"* 가 한 경로로 성립한다.

**`Activated` 를 쓰지 마라.** 그것은 다른 앱에서 돌아올 때마다 뜨고, 알트탭 한 번에
레지스트리 조회가 페인 수만큼 나간다. 우리가 알고 싶은 것은 *숨겼다 다시 보였는가* 다.

`ResidentWindow` 클래스 주석이 *"이벤트 훅은 사람이 확인한다"* 라고 적어 둔 그대로,
이 훅 자체는 채점하지 않는다 — 채점하는 것은 `WorkspaceViewModel.OnWindowShown()` 이다.

### 5. 테스트

**`tests/FlexDir.App.Tests/ViewModels/WorkspaceExternalToolsTests.cs`** 를 새로 만든다.

1. 시작 적재 뒤 **모든 페인**의 `Terminal` 이 설정값을 반영한다
2. 설정이 `Custom` + 실행 파일 + 인자면 그 셋이 페인의 `Terminal` 에 실린다
3. **새 탭을 열면** 그 페인도 같은 `Terminal` 을 갖는다
4. **분할을 늘려 페인이 생기면** 그 페인도 같은 `Terminal` 을 갖는다
5. `TerminalChanged` 가 오면 **열려 있는 모든 탭의 모든 페인**이 갱신된다
   (배경 탭·접힌 페인 포함 — `ApplyHiddenItemsAsync` 와 같은 범위)
6. `Settings` 가 `null` 인 조립에서 `TerminalChanged` 배관이 던지지 않는다
7. `OnWindowShown()` 이 **모든 페인**의 카탈로그 조회를 한 번씩 더 내보낸다
   (step 1 의 `FakeExternalToolCatalog.EditorRequests` 로 센다)
8. `OnWindowShown()` 을 두 번 부르면 조회가 두 번씩 나간다
9. **`OnWindowShown()` 이 동기로 즉시 반환한다** — 조회 완료를 기다리지 않는다
   (fake 를 느리게 만들어 시한으로 잰다)
10. 페인 하나가 던져도 `OnWindowShown()` 이 **던지지 않는다**

**`tests/FlexDir.App.Tests/ViewModels/SettingsViewModelTests.cs` 에 추가**:

11. `TerminalPreset` 을 바꾸면 `TerminalChanged` 가 한 번 나온다
12. `TerminalExecutable`·`TerminalArguments` 를 바꿔도 각각 나온다
13. 같은 값을 다시 넣으면 **안 나온다**
14. `Current.ResolveTerminal()` 이 화면 상태와 일치한다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 5 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `pane.ShowHiddenItems = ...` 가 나오는 **모든 자리**에 `pane.Terminal = ...` 를
     붙였는가? (`grep -n "ShowHiddenItems =" src/FlexDir.App/ViewModels/WorkspaceViewModel.cs`
     로 세어 확인한다. 하나라도 빠지면 그 탭만 기본 프리셋으로 남는다)
   - `OnWindowShown()` 이 **기다리지 않고** 던지지 않는가?
   - `IsVisibleChanged` 를 썼는가? (`Activated` 가 아니라)
   - `ResidentWindow.cs` 에 판단 로직을 넣지 않았는가? (훅은 한 줄, 판단은 ViewModel)
   - 기존 워크스페이스·설정 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 6 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `SettingsViewModel.TerminalChanged` · `WorkspaceViewModel.OnWindowShown()` ·
`pane.Terminal` 을 미는 자리의 개수를 적어라 — step 7 이 그 이벤트를 내는 화면을 붙인다.

## 금지사항

- **`Activated` 이벤트를 쓰지 마라.** 이유: 알트탭 한 번마다 페인 수만큼 레지스트리
  조회가 나간다. 우리가 알고 싶은 것은 숨겼다 다시 보였는가다.
- **`OnWindowShown()` 에서 기다리지 마라.** 이유: 창이 뜨는 길이고 `WindowShown` 예산은
  100ms 다 (상주 중 실측 3~9ms).
- **`ResidentWindow.cs` 에 판단을 넣지 마라.** 이유: 그 파일의 클래스 주석이 "판정만
  채점하고 이벤트 훅은 사람이 확인한다" 라고 선을 그어 두었다. 판단이 거기 들어가면
  채점되지 않는 자리에 로직이 자란다.
- **`MainWindow.xaml.cs` 를 건드리지 마라.** 이유: CLAUDE.md §2 — 코드비하인드에는
  `InitializeComponent()` 만 둔다.
- **페인이 설정을 직접 읽게 하지 마라.** 이유: step 5 가 이미 그 선을 그었다.
  `ShowHiddenItems` 와 같은 방향으로만 흐른다.
- **XAML 을 건드리지 마라.** 이유: 설정 화면은 step 7, 툴바는 step 8 이다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
