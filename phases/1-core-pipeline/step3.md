# Step 3: list-reconcile

외부 변경을 목록에 병합하면서 **선택을 잃지 않는다.** phase 1 의 마지막 step 이다.

## 읽어야 할 파일

- `src/FlexDir.Core/Watching/IFolderWatcher.cs` — 이전 step 산출물. `FolderChange`·`FolderChangeKind`
- `src/FlexDir.Core/Model/FileItem.cs` — phase 0 산출물
- `src/FlexDir.Core/Sorting/FileItemComparer.cs` — phase 0 산출물. 삽입 위치를 이 비교기로 정한다
- `docs/ADR.md` — ADR-011 (감지해 갱신하되 **선택과 스크롤 위치를 유지한다**)
- `CLAUDE.md` — §4 (진실원천은 파일시스템. 갱신 중에도 선택은 유지한다)
- `docs/UI_GUIDE.md` — §원칙 4 (선택은 함부로 잃지 않는다)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.Core.Tests/Enumeration/ChangeBatchTests.cs` 와
   `tests/FlexDir.Core.Tests/Enumeration/ListReconcilerTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.Core/Enumeration/ChangeBatch.cs` 와
   `src/FlexDir.Core/Enumeration/ListReconciler.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Enumeration/ChangeBatch.cs`

변경 알림 묶음을 "무엇을 다시 읽어야 하는가"로 번역한다. 순수 함수다 — I/O 는 하지 않는다.

```csharp
namespace FlexDir.Core.Enumeration;

using FlexDir.Core.Watching;

public sealed record ChangeBatch(
    IReadOnlyList<string> NeedsRefresh,                        // Added·Changed 된 이름
    IReadOnlyList<string> Removals,
    IReadOnlyList<(string OldName, string NewName)> Renames,
    bool RequiresFullRefresh)                                  // Overflow 가 하나라도 있었다
{
    public static ChangeBatch From(IReadOnlyList<FolderChange> changes);
}
```

규칙:

1. `Overflow` 가 하나라도 있으면 `RequiresFullRefresh == true` 이고 **나머지 목록은 비운다.**
   이유: 전체를 다시 읽을 것이므로 개별 처리는 낭비이며, 유실된 이벤트 때문에 어차피 불완전하다.
2. 같은 이름이 여러 번 나오면 한 번으로 합친다.
3. `Added` 뒤에 `Removed` 가 오면 최종 상태는 제거다. 순서를 보존해 마지막 것이 이긴다.
4. `Renamed` 의 `OldName` 은 `Removals` 에 넣지 않는다. 이름 변경은 제거가 아니다 —
   그렇게 처리하면 선택이 풀린다.
5. `Renamed` 의 `NewName` 은 `NeedsRefresh` 에 넣는다. 이름이 바뀌면 유형·아이콘이 바뀔 수 있다.

### 2. `src/FlexDir.Core/Enumeration/ListReconciler.cs`

```csharp
namespace FlexDir.Core.Enumeration;

using FlexDir.Core.Model;

public sealed record ReconcileResult(
    IReadOnlyList<FileItem> Items,
    IReadOnlyList<string> Selection);

public static class ListReconciler
{
    /// current 에 변경을 적용한 새 목록과, 유지되어야 할 선택 이름을 낸다.
    /// upserts 는 호출자가 이미 다시 읽어온 항목들이다 (이 함수는 I/O 를 하지 않는다).
    public static ReconcileResult Apply(
        IReadOnlyList<FileItem> current,
        IReadOnlyCollection<string> selection,
        IReadOnlyList<FileItem> upserts,
        IReadOnlyCollection<string> removals,
        IReadOnlyList<(string OldName, string NewName)> renames,
        IComparer<FileItem> comparer);
}
```

규칙:

1. **선택 유지가 이 함수의 존재 이유다.**
   - 제거된 이름은 선택에서 빠진다.
   - 이름이 바뀐 항목이 선택돼 있었으면 **새 이름으로 선택이 이어진다.**
   - 그 밖의 선택은 그대로 남는다. 목록이 바뀌었다고 선택을 비우지 않는다.
2. 이름 비교는 **`OrdinalIgnoreCase`** 다. Windows 파일시스템이 대소문자를 구분하지 않는다.
3. 결과 목록은 `comparer` 기준으로 정렬된 상태를 유지한다. 삽입 위치를 찾아 넣되,
   전체를 다시 정렬해도 된다 — 정확성이 우선이고 성능은 다음 문제다.
4. `upserts` 의 항목이 이미 목록에 있으면 **교체**한다. 중복으로 넣지 않는다.
5. 적용 순서는 **rename → remove → upsert** 다. 이름 변경을 먼저 처리하지 않으면
   옛 이름이 남아 중복이 생긴다.
6. 반환하는 `Selection` 에는 **결과 목록에 실제로 존재하는 이름만** 담는다.
   존재하지 않는 이름이 선택에 남으면 상태표시줄 개수가 실제와 어긋난다.

### 테스트가 반드시 덮어야 할 것

- 추가된 항목이 정렬 위치에 들어간다 (맨 끝이 아니다)
- 제거된 항목이 목록과 선택에서 함께 빠진다
- **선택된 항목의 이름이 바뀌면 선택이 새 이름으로 이어진다** ← 핵심
- 선택되지 않은 항목의 변경이 선택을 건드리지 않는다
- `upserts` 로 같은 이름이 오면 교체되고 중복이 생기지 않는다
- 대소문자만 다른 이름이 같은 항목으로 취급된다
- `ChangeBatch.From` 에 `Overflow` 가 섞이면 `RequiresFullRefresh == true` 이고 나머지가 비어 있다
- `Added` 후 `Removed` 순서면 최종적으로 제거된다
- `Renamed` 의 `OldName` 이 `Removals` 에 들어가지 않는다
- 결과 `Selection` 에 목록에 없는 이름이 남지 않는다
- 빈 변경 묶음을 적용하면 목록과 선택이 그대로다 (멱등)

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 이름 변경 시 선택이 이어지는 테스트가 실제로 있는가? 이것이 ADR-011 의 핵심이다
   - 두 함수 모두 I/O 를 하지 않는가? (파일시스템 접근 0)
   - `FlexDir.Core` 가 여전히 순수 .NET 인가? (`check-structure.ps1`)
3. 결과에 따라 `phases/1-core-pipeline/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **갱신할 때 선택을 비우지 마라.** 이유: ADR-011 — "갱신 때마다 선택이 풀리면 감시가
  없느니만 못하다." 이 한 줄이 이 step 의 존재 이유다.
- **이름 변경을 제거 + 추가로 처리하지 마라.** 이유: 선택이 풀린다. `Renames` 를 따로 받는 이유다.
- **이 함수 안에서 파일시스템을 읽지 마라.** 이유: 순수 함수여야 테스트가 결정적이다.
  다시 읽어온 항목은 호출자가 `upserts` 로 넘긴다.
- **`ObservableCollection` 을 반환하지 마라.** 이유: `FlexDir.Core` 는 WPF·UI 를 모른다.
  목록 반영과 알림은 ViewModel 의 일이다.
- **스크롤 위치를 여기서 다루지 마라.** 이유: 픽셀·행 인덱스는 View 의 관심사다. 여기서 내는
  것은 이름 기준 선택뿐이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
