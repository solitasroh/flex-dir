# Step 2: selection

선택을 잃지 않는 것이 목표다. 정렬·뷰 전환·갱신을 지나도 같은 항목이 선택돼 있어야 한다.

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이전 step 산출물. 여기에 선택을 붙인다
- `src/FlexDir.App/ViewModels/FileItemViewModel.cs` — 이전 step 산출물
- `src/FlexDir.Core/Formatting/StatusSummary.cs` — phase 0 산출물. `ForSelection` 을 쓴다
- `src/FlexDir.Core/Enumeration/ListReconciler.cs` — phase 1 산출물.
  **선택을 이름으로 다루는 이유가 여기 있다** — reconcile 결과가 이름 목록이다
- `docs/UI_GUIDE.md` — §원칙 4 (선택은 함부로 잃지 않는다)
- `docs/DESIGN.md` — §6 상태 표현 (선택 항목 · 비활성 페인의 선택색 강등)
- `CLAUDE.md` — §4 (갱신 중에도 선택은 유지한다)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.App.Tests/ViewModels/PaneSelectionTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.App/ViewModels/PaneSelection.cs` 를 쓴다.
3. `PaneViewModel` 에 선택을 붙이는 변경은 기존 소스 수정이므로 훅이 막지 않는다.
   단 `tests/FlexDir.App.Tests/ViewModels/PaneViewModelTests.cs` 에 검증을 추가해야 한다.
4. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.App/ViewModels/PaneSelection.cs`

**선택은 인덱스가 아니라 이름으로 보관한다.** 정렬이 바뀌면 인덱스는 의미를 잃는다.

```csharp
namespace FlexDir.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class PaneSelection : ObservableObject
{
    /// 선택된 항목 이름. 순서는 보장하지 않는다. 비교는 OrdinalIgnoreCase.
    public IReadOnlyCollection<string> SelectedNames { get; }

    public int Count { get; }

    /// 범위 선택(Shift)의 기준점. 선택이 비면 null.
    public string? Anchor { get; }

    /// 하나만 선택한다. 앵커도 이 항목이 된다.
    public void SelectSingle(string name);

    /// 토글한다(Ctrl). 앵커는 토글한 항목이 된다.
    public void Toggle(string name);

    /// 앵커부터 지정 항목까지 선택한다(Shift). 앵커가 없으면 SelectSingle 과 같다.
    /// order 는 현재 화면 순서다 — 정렬이 바뀌면 다른 범위가 된다.
    public void SelectRange(string name, IReadOnlyList<string> order);

    public void Clear();

    /// 목록이 바뀐 뒤 존재하지 않는 이름을 떨군다. 남은 선택은 유지한다.
    public void Retain(IReadOnlyCollection<string> existingNames);

    /// reconcile 결과를 그대로 반영한다 (이름 변경으로 이어진 선택 포함).
    public void ReplaceWith(IReadOnlyCollection<string> names);

    public bool IsSelected(string name);
}
```

규칙:

1. 이름 비교는 전부 `OrdinalIgnoreCase` 다. 내부 저장은
   `HashSet<string>(StringComparer.OrdinalIgnoreCase)` 를 쓴다.
2. `Retain` 은 **없어진 이름만 떨군다.** 남은 것을 건드리지 않는다.
   앵커가 사라졌으면 앵커를 `null` 로 만든다.
3. `SelectRange` 는 `order` 안에서 앵커와 대상의 위치를 찾아 그 사이를 **모두** 선택한다.
   기존 선택은 대체된다(탐색기와 동일). 앵커는 그대로 유지한다 —
   Shift 를 누른 채 방향을 바꿀 수 있어야 한다.
4. `order` 에 앵커나 대상이 없으면 `SelectSingle(name)` 로 폴백한다. 예외를 던지지 않는다.
   이유: 갱신과 조작이 겹치는 정상 상황이다.
5. `Count`·`SelectedNames` 변경 시 `PropertyChanged` 를 낸다. 상태표시줄이 이것을 본다.

### 2. `PaneViewModel` 에 붙이기

- `public PaneSelection Selection { get; }` 노출.
- **폴더를 옮기면 선택을 비운다** (`Clear`). 다른 폴더의 이름이 남아 있으면 안 된다.
- **정렬·뷰 모드 전환 후에는 선택을 유지한다.** 이름 기준이므로 자동으로 유지되지만,
  테스트로 고정한다.
- `StatusText` 를 선택 상태에 따라 바꾼다:
  - 선택 0개 → `StatusSummary.ForItems(항목수)`
  - 선택 1개 이상 → `StatusSummary.ForSelection(항목수, 선택수, 선택크기합)`
  - 선택 크기 합은 **디렉터리를 0 으로** 센다 (`SizeFormatter.ForItem` 과 같은 규칙).
- `Status == Enumerating` 이면 선택 요약보다 열거 진행 표시가 우선이다.
  이유: 열거 중에는 총 개수가 아직 확정되지 않았다.

### 테스트가 반드시 덮어야 할 것

- `SelectSingle` → `Count == 1`, 앵커가 그 항목
- `Toggle` 두 번 → 선택이 비고 `Count == 0`
- `SelectRange`: 앵커 A, 대상 D, 순서 `[A,B,C,D,E]` → A~D 4개 선택
- `SelectRange` 로 방향을 바꿔도 앵커가 유지된다
- 앵커 없이 `SelectRange` → 단일 선택으로 폴백
- `order` 에 대상이 없으면 예외 없이 단일 선택으로 폴백
- `Retain` 이 **없어진 이름만** 떨구고 나머지를 유지한다
- 앵커가 사라지면 앵커가 `null` 이 된다
- 대소문자만 다른 이름이 같은 항목으로 취급된다
- **정렬을 바꾼 뒤에도 선택이 유지된다** (PaneViewModel 수준 테스트)
- **폴더를 옮기면 선택이 비워진다**
- 선택 1개 이상이면 `StatusText` 가 `ForSelection` 형태다
- 선택 크기 합에서 디렉터리가 0 으로 센다
- 열거 중에는 선택 요약이 아니라 진행 표시가 나온다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 선택이 **인덱스가 아니라 이름**으로 보관되는가?
   - 정렬 변경 후 선택 유지 테스트가 실제로 있는가?
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1`)
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **선택을 인덱스나 `FileItemViewModel` 참조로 보관하지 마라.** 이유: 정렬이 바뀌면 인덱스가
  다른 항목을 가리키고, 갱신으로 인스턴스가 교체되면 참조가 끊긴다. reconcile 결과도 이름 목록이다.
- **`SelectionMode`·`ListView.SelectedItems` 같은 WPF 타입을 쓰지 마라.** 이유: `PaneSelection` 은
  테스트 가능한 평범한 객체여야 한다. View 와의 동기화는 수동 UI phase 의 일이다.
- **정렬·뷰 전환 시 선택을 비우지 마라.** 이유: `docs/UI_GUIDE.md` §원칙 4.
- **`order` 에 항목이 없을 때 예외를 던지지 마라.** 이유: 갱신과 클릭이 겹치는 것은 정상이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
