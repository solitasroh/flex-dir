# Step 4: two-pane

두 페인을 나란히 두고 활성 페인을 관리한다. 이 앱의 존재 이유가 여기서 형태를 갖춘다.

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이전 step 산출물.
  **생성자 시그니처를 정확히 확인하라** (step 3 에서 `IViewStateStore` 가 추가됐다)
- `src/FlexDir.App/ViewModels/PaneSelection.cs` — 이전 step 산출물
- `src/FlexDir.Core/ViewState/FolderViewState.cs` — phase 0 산출물. `GlobalViewState`·`WindowPlacement`
- `src/FlexDir.Core/ViewState/IViewStateStore.cs` — phase 0 산출물. 전역 상태 저장·복원
- `docs/PRD.md` — §2 레이아웃 (좌/우 2분할 · 활성 페인 표시 · 스플리터), §4 (같은 폴더를 양쪽에)
- `docs/DESIGN.md` — §1 페인 최소 너비 320 · 스플리터 두께 6, §6 활성 페인 표현
- `docs/ADR.md` — ADR-004 (v1 축은 "여러 폴더 왕복" 하나)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.App.Tests/ViewModels/WorkspaceViewModelTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.App/ViewModels/WorkspaceViewModel.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

> 클래스 이름을 `ShellViewModel` 로 짓지 마라. 이 저장소에서 "Shell" 은 `FlexDir.Shell`
> (Windows Shell interop)을 뜻한다. 창 전체를 대표하는 ViewModel 은 `WorkspaceViewModel` 이다.

## 작업

`src/FlexDir.App/ViewModels/WorkspaceViewModel.cs`

```csharp
namespace FlexDir.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlexDir.Core.Locations;
using FlexDir.Core.ViewState;

public enum PaneSide { Left, Right }

public sealed partial class WorkspaceViewModel : ObservableObject
{
    public WorkspaceViewModel(
        PaneViewModel left,
        PaneViewModel right,
        IViewStateStore viewStateStore);

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    public PaneSide ActiveSide { get; }

    /// 활성 페인. ActiveSide 에 따라 Left 또는 Right.
    public PaneViewModel ActivePane { get; }

    /// 활성이 아닌 페인. 페인 간 복사·이동의 대상이다.
    public PaneViewModel InactivePane { get; }

    /// 좌 페인이 차지하는 비율 (0.15 ~ 0.85 로 제한).
    public double SplitterRatio { get; set; }

    [RelayCommand] private void Activate(PaneSide side);

    /// 활성 페인을 반대쪽으로 넘긴다 (키보드 페인 전환).
    [RelayCommand] private void SwitchPane();

    /// 반대편 페인의 현재 폴더를 활성 페인에서 연다.
    [RelayCommand] private Task OpenOtherPaneLocationAsync();

    /// 저장된 전역 상태(스플리터 비율·창 배치)를 복원한다.
    public Task RestoreAsync(CancellationToken ct = default);

    /// 현재 전역 상태를 저장한다.
    public Task PersistAsync(CancellationToken ct = default);

    /// 복원·저장 대상 창 배치. View 가 읽고 쓴다.
    public WindowPlacement? WindowPlacement { get; set; }
}
```

### 동작 규칙

1. **활성 페인은 항상 정확히 하나다.** 기본값은 `PaneSide.Left`.
   `ActivePane`·`InactivePane` 은 파생 속성이며 `ActiveSide` 가 바뀌면 둘 다
   `PropertyChanged` 를 낸다. View 가 활성 표시를 바인딩한다(`docs/DESIGN.md` §6).
2. **두 페인은 완전히 독립이다.** 같은 폴더를 양쪽에 열 수 있고, 각자의 경로·히스토리·
   뷰 모드·정렬·선택을 가진다 (`docs/PRD.md` §4). 한쪽 조작이 다른 쪽을 바꾸는 경로를 만들지 마라.
3. **활성 페인이 바뀔 때 선택을 건드리지 마라.** 비활성 페인의 선택은 그대로 남고,
   표시만 강등된다(`--sel-inactive` — 그것은 View 의 일이다).
4. `SplitterRatio` 는 **0.15 ~ 0.85** 로 클램프한다. 범위를 벗어난 값을 대입하면 예외 없이
   가장 가까운 경계로 잘린다. 이유: 저장 파일이 손상돼도 페인이 사라지면 안 되고,
   `docs/DESIGN.md` §1 의 페인 최소 너비 320px 를 창 최소 너비 900px 에서 지키려면
   비율이 양 끝으로 가서는 안 된다.
5. `RestoreAsync` 는 `IViewStateStore.LoadGlobalAsync` 를 부른다. 실패하면
   `GlobalViewState.Default` 를 쓰고 **조용히 넘어간다.** 이유: 전역 뷰 상태는 캐시다.
6. `OpenOtherPaneLocationAsync` 는 반대편이 아직 아무 곳도 열지 않았으면(`CurrentLocation == null`)
   아무 일도 하지 않는다.
7. `SwitchPane` 은 `Activate` 를 반대쪽으로 부르는 것과 같다. 별도 상태를 만들지 마라.

### 테스트가 반드시 덮어야 할 것

- 기본 활성은 `Left`, `ActivePane == Left`, `InactivePane == Right`
- `Activate(Right)` → `ActivePane == Right`, `InactivePane == Left`
- `SwitchPane` 두 번 → 원래 페인으로 돌아온다
- `ActiveSide` 변경 시 `ActivePane`·`InactivePane` 의 `PropertyChanged` 가 둘 다 발생한다
- **활성 페인을 바꿔도 양쪽 선택이 그대로다**
- 양쪽에 같은 폴더를 열고 한쪽 정렬을 바꿔도 **다른 쪽 정렬이 바뀌지 않는다**
- 양쪽에 같은 폴더를 열고 한쪽 선택을 바꿔도 **다른 쪽 선택이 바뀌지 않는다**
- `SplitterRatio = 0.01` → `0.15`, `= 0.99` → `0.85` (예외 없이 클램프)
- 저장된 전역 상태를 `RestoreAsync` 로 복원한다
- 실패하는 store 를 주입해도 `RestoreAsync` 가 예외를 던지지 않고 기본값을 쓴다
- 반대편이 비어 있을 때 `OpenOtherPaneLocationAsync` 가 아무 일도 하지 않는다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 두 페인이 상태를 공유하는 경로가 없는가? (같은 폴더 테스트로 확인)
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1`)
   - 클래스 이름이 `WorkspaceViewModel` 인가? (`ShellViewModel` 이 아니다)
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **탭·4분할·세 번째 페인을 위한 구조를 만들지 마라** (`IList<PaneViewModel>` 등).
  이유: `docs/PRD.md` §3 이 v2+ 로 미뤘다. 지금 일반화하면 쓰이지 않는 유연성이 남고,
  2분할이 daily-drivable 한지 확인하는 것이 v1 의 목적이다.
- **두 페인이 상태를 공유하게 만들지 마라** (공용 정렬·공용 선택 등). 이유: `docs/PRD.md` §4 —
  같은 폴더를 양쪽에 열어도 독립이어야 한다.
- **활성 전환 시 선택을 비우지 마라.** 이유: 페인 간 복사의 출발점이 선택이다. 전환하면
  선택이 사라지는 2분할은 쓸 수 없다.
- **창 배치를 여기서 실제로 적용하지 마라** (`Window.Left` 대입 등). 이유: `FlexDir.App` 의
  ViewModel 은 WPF 창을 만지지 않는다. `WindowPlacement` 를 노출하는 것까지가 범위다.
- **XAML 이나 `*.xaml.cs` 를 만들지 마라.** 이유: View 는 수동 UI phase 의 일이다(ADR-009).
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
