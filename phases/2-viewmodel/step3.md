# Step 3: sort-view-commands

정렬과 뷰 모드를 바꾸고, 폴더마다 기억한다.

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이전 step 산출물
- `src/FlexDir.App/ViewModels/PaneSelection.cs` — 이전 step 산출물. 전환 후 선택을 유지해야 한다
- `src/FlexDir.Core/ViewState/IViewStateStore.cs`,
  `src/FlexDir.Core/ViewState/FolderViewState.cs` — phase 0 산출물
- `src/FlexDir.Core/Sorting/FileItemComparer.cs` — phase 0 산출물. `SortKey`·`SortOrder`
- `tests/FlexDir.Core.Tests/Fakes/InMemoryViewStateStore.cs` — phase 0 산출물. 테스트에서 쓴다
- `docs/PRD.md` — §2 뷰 모드 · 정렬 · 폴더별 기억
- `docs/DESIGN.md` — §2 뷰 모드 4종, §6 정렬 방향 표시자
- `docs/UI_GUIDE.md` — §원칙 4 (뷰 전환 후에도 선택 유지)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.App.Tests/ViewModels/PaneViewStateTests.cs` 를 쓴다 (red).
2. `PaneViewModel` 수정은 기존 소스 변경이라 훅이 막지 않지만, 위 테스트가 먼저 있어야 한다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

`PaneViewModel` 에 아래를 추가한다. `IViewStateStore` 를 생성자 인자로 받는다
(생성자 시그니처가 바뀌므로 기존 테스트도 함께 고친다).

```csharp
public ViewMode ViewMode { get; }
public IReadOnlyList<SortOrder> Sort { get; }

/// 컬럼 헤더 클릭. 같은 키를 다시 누르면 방향만 반전한다.
[RelayCommand] private Task ChangeSortAsync(SortKey key);

[RelayCommand] private Task ChangeViewModeAsync(ViewMode mode);
```

### 동작 규칙

1. **정렬 키 전환**: 다른 키를 누르면 그 키의 **오름차순**으로 시작한다.
   같은 키를 다시 누르면 방향만 반전한다. 탐색기와 같은 동작이다.
2. `Sort` 는 **단일 키 리스트**로 유지한다. 다중 키 정렬은 `FileItemComparer` 가 지원하지만
   v1 UI 에는 다중 키를 만드는 조작이 없다. 없는 조작을 위한 상태를 만들지 마라.
3. **전환 후 선택을 유지한다.** 목록을 다시 정렬하고 `PaneSelection` 은 건드리지 않는다.
   선택은 이름 기준이므로 유지된다 — 테스트로 고정한다.
4. **폴더 진입 시 복원**: `NavigateAsync` 는 열거를 시작하기 전에
   `IViewStateStore.TryLoadAsync(folder, ct)` 를 부른다.
   - 값이 있으면 그 `ViewMode`·`Sort` 를 적용한다.
   - `null` 이면 `FolderViewState.Default` 를 적용한다.
   - **이전 폴더의 뷰 상태를 물려주지 않는다.** 이유: 폴더별 기억이 존재 이유다.
5. **변경 시 저장**: `ChangeSortAsync`·`ChangeViewModeAsync` 는 현재 폴더에 대해
   `IViewStateStore.SaveAsync` 를 부른다. `CurrentLocation` 이 `null` 이면 저장하지 않는다.
6. **저장·복원 실패는 조용히 기본값으로 넘어간다.** 예외를 상태표시줄에 올리지 마라.
   이유: 뷰 상태는 캐시다(`CLAUDE.md` §4). 폴더를 못 여는 것과 뷰 설정을 못 읽는 것은
   사용자에게 전혀 다른 사건이고, 후자로 목록을 막으면 안 된다.
7. 열거 중에 정렬을 바꿀 수 있다. 그때는 **이미 받은 항목을 다시 정렬**하고,
   이후 도착하는 배치는 새 정렬로 삽입한다. 열거를 다시 시작하지 마라 —
   `docs/UI_GUIDE.md` §원칙 3 (오래 걸리는 일을 반복하지 않는다).

### 테스트가 반드시 덮어야 할 것

`InMemoryViewStateStore`(phase 0)를 쓴다.

- 다른 키로 `ChangeSortAsync` → 그 키의 오름차순
- 같은 키로 두 번 → 방향이 반전된다
- `ChangeSortAsync` 후 `IViewStateStore` 에 저장된다
- 저장된 뷰 상태가 있는 폴더로 이동 → 그 상태가 복원된다
- 저장된 상태가 없는 폴더로 이동 → `FolderViewState.Default`
- **폴더 A 에서 뷰를 바꾸고 폴더 B 로 가면 B 는 A 의 설정을 쓰지 않는다**
- **정렬을 바꾼 뒤 선택이 유지된다**
- **뷰 모드를 바꾼 뒤 선택이 유지된다**
- `CurrentLocation == null` 일 때 `ChangeSortAsync` 가 예외를 던지지 않는다
- 저장이 실패하는 store 를 주입해도 `Status` 가 `Error` 로 가지 않는다
- 열거 중 정렬 변경 시 열거가 재시작되지 않는다
  (`FakeFolderSource.EnumerateCalls` 개수가 늘지 않는다)

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 뷰 상태 저장·복원 실패가 목록을 막지 않는가?
   - 폴더를 옮길 때 이전 폴더의 뷰 상태가 새 나가지 않는가?
   - 정렬 변경이 열거를 재시작시키지 않는가?
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `PaneViewModel` 생성자 시그니처가 바뀌었음을 반드시 적어라.
다음 step 들이 이 생성자를 호출한다.

## 금지사항

- **다중 키 정렬을 만드는 조작(커맨드·API)을 추가하지 마라.** 이유: v1 UI 에 그 조작이 없다.
  `FileItemComparer` 가 지원하는 것과 UI 가 제공하는 것은 별개다.
- **뷰 상태 오류를 `Status = Error` 로 올리지 마라.** 이유: 뷰 설정은 캐시이고, 폴더를 못 여는
  것과 다른 사건이다. 목록이 멀쩡한데 오류로 보이면 사용자가 파일이 사라진 줄 안다.
- **정렬 변경 시 열거를 다시 시작하지 마라.** 이유: 10만 항목 폴더에서 헤더 클릭 한 번에
  몇 초를 다시 기다리게 된다. 이미 받은 항목을 다시 정렬한다.
- **뷰 모드 전환 시 목록을 다시 읽지 마라.** 이유: 같은 데이터의 다른 표현일 뿐이다
  (`DataTemplate` 교체 — ADR-002). 다시 읽으면 WPF 를 고른 이점을 버린다.
- **폴더별 뷰 상태를 전역 하나로 합치지 마라.** 이유: `docs/PRD.md` §2 폴더별 기억이 v1 범위다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
