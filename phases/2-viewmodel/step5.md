# Step 5: file-op-commands

복사·이동·삭제·이름변경·새 폴더, 그리고 **반대편 페인으로 이동**. 2분할의 존재 이유다.

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/WorkspaceViewModel.cs` — 이전 step 산출물.
  페인 간 커맨드는 여기에 붙는다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs`,
  `src/FlexDir.App/ViewModels/PaneSelection.cs` — 이전 step 산출물
- `src/FlexDir.Core/Locations/LocationId.cs` — phase 0 산출물
- `src/FlexDir.Core/Errors/LocationAccessException.cs`,
  `src/FlexDir.Core/Errors/LocationError.cs` — phase 0 산출물
  (`LocationErrorKind` 열거형과 `LocationErrorMessages.Describe` 가 이 파일에 있다)
- `docs/PRD.md` — §2 파일 조작 · 클립보드, §4 (이름 충돌은 shell 표준 대화상자)
- `docs/UI_GUIDE.md` — §금지 목록 (모달 대화상자로 진행 상황 표시 금지)
- `docs/SHELL_NOTES.md` — **§파일 조작**. 구현체가 지켜야 할 사실이며 포트 모양의 근거다
- `docs/ARCHITECTURE.md` — §2 포트 표

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

> **테스트 파일 이름·위치 규칙.** 소스가 `Foo.cs` 면 테스트 파일명은 정확히 `FooTests.cs` 이고,
> **소스가 속한 프로젝트의 `.Tests`** 에 둔다. 포트 세 개는 `FlexDir.Core` 의 파일이므로
> 테스트도 `FlexDir.Core.Tests` 에 있어야 한다. 인터페이스의 테스트는 fake 의 계약 검증으로 쓴다.

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/Fakes/FakeFileOperations.cs` (호출 기록·예외 주입)
   - `tests/FlexDir.Core.Tests/Fakes/FakeClipboardBridge.cs`
   - `tests/FlexDir.Core.Tests/Fakes/FakeItemActivator.cs`
   - `tests/FlexDir.Core.Tests/Operations/IFileOperationsTests.cs`
   - `tests/FlexDir.Core.Tests/Operations/IClipboardBridgeTests.cs`
     — 복사/잘라내기 왕복, 비었을 때 `TryGetPaste` 가 `false`
   - `tests/FlexDir.Core.Tests/Operations/IItemActivatorTests.cs`
   - `tests/FlexDir.App.Tests/ViewModels/FileOperationCommandsTests.cs` (커맨드 동작 전부)
2. 그 다음 소스를 쓴다:
   - `src/FlexDir.Core/Operations/IFileOperations.cs`
   - `src/FlexDir.Core/Operations/IClipboardBridge.cs`
   - `src/FlexDir.Core/Operations/IItemActivator.cs`
   - `PaneViewModel`·`WorkspaceViewModel` 에 커맨드 추가 (기존 소스 수정이라 훅이 막지 않는다)
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. 포트 — `src/FlexDir.Core/Operations/`

```csharp
namespace FlexDir.Core.Operations;

using FlexDir.Core.Locations;

public interface IFileOperations
{
    Task CopyAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct);
    Task MoveAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct);

    /// 휴지통으로 보낸다. 영구 삭제가 아니다.
    Task DeleteAsync(IReadOnlyList<LocationId> items, CancellationToken ct);

    Task RenameAsync(LocationId item, string newName, CancellationToken ct);

    /// 만들어진 폴더의 위치를 낸다. 이름이 겹치면 구현체가 유일한 이름을 만든다.
    Task<LocationId> CreateFolderAsync(LocationId parentFolder, string name, CancellationToken ct);
}

public interface IClipboardBridge
{
    void SetCopy(IReadOnlyList<LocationId> items);
    void SetCut(IReadOnlyList<LocationId> items);

    /// 붙여넣을 것이 있으면 true. isMove 가 true 면 잘라내기였다.
    bool TryGetPaste(out IReadOnlyList<LocationId> items, out bool isMove);
}

public interface IItemActivator
{
    /// 더블클릭·Enter. 폴더가 아닌 항목의 연결 프로그램을 실행한다.
    Task ActivateAsync(LocationId item, CancellationToken ct);
}
```

**포트 계약에 못박을 것** (구현체가 어기면 사용자 데이터가 사라진다):

- `DeleteAsync` 는 **반드시 휴지통으로** 보낸다. 구현체는 `FOF_ALLOWUNDO` 만으로 부족하고
  `FOFX_RECYCLEONDELETE` 를 함께 줘야 한다 — 휴지통 할당량을 넘으면 Windows 가 **말없이
  영구 삭제로 바꾼다** (`docs/SHELL_NOTES.md` §파일 조작). 이 문장을 인터페이스 XML 주석에 남겨라.
- **이름 충돌 대화상자를 억누르지 마라.** shell 표준 대화상자를 그대로 쓴다
  (`docs/PRD.md` §4). 전작은 모든 대화상자를 껐고 조용히 실패했다.
- 진행 상황을 반환값으로 요구하지 마라. 모달 진행 표시는 금지다(`docs/UI_GUIDE.md`).

### 2. `PaneViewModel` 의 커맨드

```csharp
[RelayCommand] private void CopySelection();          // IClipboardBridge.SetCopy
[RelayCommand] private void CutSelection();           // IClipboardBridge.SetCut
[RelayCommand] private Task PasteAsync();
[RelayCommand] private Task DeleteSelectionAsync();
[RelayCommand] private Task CreateFolderAsync();
[RelayCommand] private Task ActivateAsync(FileItemViewModel item);

// 이름변경 — 인라인 편집의 시각 표현은 수동 UI phase 의 일이다.
public string? RenamingName { get; }                  // 편집 중인 항목 이름. 아니면 null
[RelayCommand] private void BeginRename();            // 선택이 정확히 1개일 때만
[RelayCommand] private void CancelRename();
[RelayCommand] private Task CommitRenameAsync(string newName);
```

### 3. `WorkspaceViewModel` 의 페인 간 커맨드

```csharp
[RelayCommand] private Task CopyToOtherPaneAsync();
[RelayCommand] private Task MoveToOtherPaneAsync();
```

### 동작 규칙

1. **선택이 비면 조작 커맨드는 아무 일도 하지 않는다.** 예외를 던지지 마라.
   `CanExecute` 로도 막되, 실행됐을 때도 안전해야 한다 (경합이 있다).
2. `ActivateAsync`: 폴더면 그 폴더로 `NavigateAsync`, 파일이면 `IItemActivator`.
   폴더 진입을 `IItemActivator` 에 맡기지 마라 — 그러면 shell 이 새 탐색기 창을 띄운다.
3. **`IsContentAccessRisky` 항목도 정상적으로 활성화한다.** 다운로드를 트리거하는 것은
   사용자가 의도한 행위다. 접근을 피해야 하는 것은 **썸네일**뿐이다(step 6).
4. `CommitRenameAsync`: 새 이름이 비었거나 현재 이름과 같으면 편집만 취소한다.
   `RenameAsync` 실패는 `LocationErrorMessages` 로 `StatusText` 에 올리고 목록은 건드리지 않는다.
   **목록을 직접 고치지 마라** — 감시가 갱신한다(step 7). 이유: 진실원천은 파일시스템이다.
5. **조작 후 목록을 직접 수정하지 마라.** 복사·이동·삭제·새 폴더 전부 감시 갱신에 맡긴다.
   이유: `CLAUDE.md` §4. 낙관적 갱신은 실패 시 유령 항목을 남긴다.
6. `CreateFolderAsync` 는 기본 이름(`새 폴더`)으로 만든 뒤 **그 항목의 이름변경 편집을 시작한다**
   (`RenamingName` 을 새 폴더 이름으로). 탐색기와 같은 흐름이다.
7. `PasteAsync` 는 `TryGetPaste` 가 `false` 면 아무 일도 하지 않는다.
   `isMove` 면 `MoveAsync`, 아니면 `CopyAsync`. 대상은 **현재 폴더**다.
8. `CopyToOtherPaneAsync`·`MoveToOtherPaneAsync` 는 활성 페인의 선택을 비활성 페인의
   현재 폴더로 보낸다. 비활성 페인이 비어 있으면(`CurrentLocation == null`) 아무 일도 하지 않는다.
   **양쪽이 같은 폴더면 이동은 아무 일도 하지 않는다** (복사는 shell 이 사본을 만든다).
9. 모든 비동기 조작은 **취소 가능**해야 하고, 진행 중에도 반대편 페인이 동작해야 한다.
   조작을 `await` 하는 동안 UI 를 잠그는 상태 플래그를 만들지 마라.
10. `LocationAccessException` 은 `StatusText` 로 보여준다. 예외를 밖으로 던지지 마라.

### 테스트가 반드시 덮어야 할 것

- 선택이 빈 상태에서 모든 커맨드가 예외 없이 아무 일도 하지 않는다
- `CopySelection` → `FakeClipboardBridge` 에 복사 항목이 기록된다
- `CutSelection` 후 `PasteAsync` → `MoveAsync` 가 호출된다 (`CopyAsync` 가 아니다)
- 붙여넣을 것이 없으면 `PasteAsync` 가 아무 일도 하지 않는다
- `DeleteSelectionAsync` → `FakeFileOperations.DeleteAsync` 가 선택 항목으로 호출된다
- 폴더를 `ActivateAsync` → `NavigateAsync` 로 진입하고 `IItemActivator` 는 호출되지 않는다
- 파일을 `ActivateAsync` → `IItemActivator` 가 호출된다
- `IsContentAccessRisky` 파일도 `IItemActivator` 가 호출된다
- `BeginRename` 은 선택이 2개면 시작되지 않는다
- `CommitRenameAsync("")` 는 편집만 취소하고 `RenameAsync` 를 부르지 않는다
- 이름변경 실패 → `StatusText` 에 사유, **`Items` 는 변하지 않는다**
- 조작 성공 후에도 `Items` 가 직접 수정되지 않는다 (감시가 할 일이다)
- `CreateFolderAsync` 후 `RenamingName` 이 새 폴더 이름이다
- `CopyToOtherPaneAsync` → 대상 폴더가 **비활성 페인의 현재 폴더**다
- 비활성 페인이 비어 있으면 페인 간 커맨드가 아무 일도 하지 않는다
- 양쪽이 같은 폴더면 `MoveToOtherPaneAsync` 가 `MoveAsync` 를 부르지 않는다
- `LocationAccessException` 이 밖으로 던져지지 않고 `StatusText` 로 간다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 조작 후 목록을 직접 고치는 코드가 없는가?
   - `DeleteAsync` 의 XML 주석에 휴지통 강제 규칙이 적혀 있는가?
   - 폴더 진입이 `IItemActivator` 를 타지 않는가?
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **`IContextMenuProvider` 를 정의하지 마라.** 이유: shell 컨텍스트 메뉴는 창 핸들과 네이티브
  메뉴 메시지 펌핑을 요구한다(`docs/SHELL_NOTES.md` §컨텍스트 메뉴 — `IContextMenu2/3` 의
  `HandleMenuMsg`). 자동 채점이 불가능하므로 수동 UI phase 의 일이다(ADR-009).
- **영구 삭제 경로를 만들지 마라** (Shift+Delete 등). 이유: v1 에 없는 조작이고, 되돌릴 수 없는
  동작을 자동 채점만 거친 코드에 두면 안 된다.
- **조작 후 목록을 낙관적으로 갱신하지 마라.** 이유: `CLAUDE.md` §4 — 진실원천은 파일시스템이다.
  실패 시 유령 항목이 남고 그게 전작의 잔버그였다.
- **진행 상황을 모달로 표시하는 API 를 만들지 마라.** 이유: `docs/UI_GUIDE.md` §금지 목록 —
  작업 중에 반대편 페인을 쓸 수 있어야 한다.
- **이름 충돌을 직접 해결하지 마라** (자동 이름 변경·덮어쓰기 선택). 이유: `docs/PRD.md` §4 가
  shell 표준 대화상자를 쓰라고 정했다. 구현체가 shell 에 맡긴다.
- **UI 를 잠그는 전역 "작업 중" 플래그를 만들지 마라.** 이유: 2분할의 이점이 사라진다.
- **XAML 이나 `*.xaml.cs` 를 만들지 마라.** 이유: View 는 수동 UI phase 의 일이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
