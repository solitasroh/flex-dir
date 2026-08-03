# Step 7: watcher-integration

외부 변경을 목록에 반영하면서 선택을 유지한다. phase 2 의 마지막 step 이다.

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이전 step 산출물
- `src/FlexDir.App/ViewModels/PaneSelection.cs` — 이전 step 산출물. `ReplaceWith` 를 쓴다
- `src/FlexDir.App/ViewModels/BulkObservableCollection.cs` — 이전 step 산출물.
  **갱신 경로에서는 `AddRange`·`ReplaceAll` 을 쓰지 않는다**
- `src/FlexDir.Core/Watching/IFolderWatcher.cs` — phase 1 산출물
- `src/FlexDir.Core/Enumeration/ChangeBatch.cs`,
  `src/FlexDir.Core/Enumeration/ListReconciler.cs` — phase 1 산출물. 병합 로직은 여기 있다
- `src/FlexDir.Core/Enumeration/IFolderSource.cs` — phase 1 산출물. `TryGetItemAsync` 로 다시 읽는다
- `tests/FlexDir.Core.Tests/Fakes/FakeFolderWatcher.cs` — phase 1 산출물. `Push`·`PushOverflow`
- `docs/ADR.md` — ADR-011 (감시로 자동 갱신, **선택과 스크롤 위치를 유지한다**)
- `CLAUDE.md` — §4 (진실원천은 파일시스템. 갱신 중에도 선택은 유지한다)
- `docs/SHELL_NOTES.md` — §폴더 감시 (오버플로 시 전체 새로고침 폴백)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.App.Tests/ViewModels/PaneWatcherTests.cs` 를 쓴다 (red).
2. `PaneViewModel` 수정은 기존 소스 변경이라 훅이 막지 않지만, 위 테스트가 먼저 있어야 한다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

`PaneViewModel` 생성자에 `IFolderWatcher` 를 추가한다 (기존 테스트도 함께 고친다).
`PaneViewModel` 은 `IAsyncDisposable` 을 구현한다.

### 동작 규칙

1. **폴더를 열 때 감시를 시작하고, 폴더를 옮길 때 이전 감시를 취소한다.**
   감시 구독이 누적되면 폴더를 옮길수록 이벤트가 중복으로 들어온다.
   `EnumerationSession` 과 같은 세대(generation) 개념을 쓰거나 `CancellationTokenSource` 를
   교체한다. **낡은 감시의 변경 알림은 버린다.**
2. 감시는 **열거가 끝난 뒤** 시작해도 되지만, 열거 중에 들어온 변경을 잃지 않아야 한다.
   가장 단순한 방식은 열거 시작과 동시에 감시를 시작해 변경을 모아두고,
   열거 완료 후 모아둔 것을 한 번에 적용하는 것이다.
3. 변경 처리 순서:
   1. 모인 `FolderChange` 목록을 `ChangeBatch.From` 으로 번역한다.
   2. `RequiresFullRefresh` 면 **`RefreshAsync` 로 전체를 다시 읽고 끝낸다.**
      이때도 선택 이름은 유지해 재열거 후 `Retain` 으로 복원한다.
   3. 아니면 `NeedsRefresh` 의 이름들을 `IFolderSource.TryGetItemAsync` 로 다시 읽는다.
      `null` 이 오면 그 이름은 `Removals` 로 옮긴다 — 알림과 실제가 어긋나는 것은 정상이고,
      **진실원천은 파일시스템이다**(`CLAUDE.md` §4).
   4. `ListReconciler.Apply` 를 부른다.
   5. 결과를 `Items` 에 반영하고 `Selection.ReplaceWith(result.Selection)` 를 부른다.
4. **`Items` 반영은 개별 `Add`/`Remove`/교체로 한다.** `ReplaceAll`·`AddRange` 를 쓰지 마라 —
   `Reset` 알림이 WPF `ListView` 의 선택을 날린다. 그러면 이 step 의 목적이 무너진다.
   - 사라진 항목: `Remove`
   - 새 항목: 정렬 위치에 `Insert`
   - 바뀐 항목: 같은 인덱스에서 `FileItemViewModel` 을 교체하거나, 표시 문자열만 갱신
5. **선택은 절대 비우지 않는다.** `ListReconciler` 가 낸 `Selection` 을 그대로 반영한다.
   이름이 바뀐 항목의 선택은 새 이름으로 이어진다(phase 1 step 3 이 보장한다).
6. 변경 알림을 **묶어서 처리한다.** 파일 100개를 복사하면 알림이 100개 이상 온다.
   묶음 경계는 **항목 수 기준으로만** 정한다 — 최대 **64개**를 모으면 적용하고,
   스트림이 잠시 비면(더 읽을 것이 없으면) 모인 것을 적용한다.
   **시간 기반 디바운스(`Task.Delay`)를 쓰지 마라** — 테스트가 실행 속도에 의존하게 된다.
7. 갱신 후 `StatusText` 를 다시 계산한다 (항목 수·선택 수가 바뀌었다).
8. 감시 스트림에서 예외가 나면 **목록을 건드리지 말고 감시만 중단한다.**
   `Status` 를 `Error` 로 바꾸지 마라 — 목록은 여전히 유효하고, 감시가 죽은 것은 사용자가
   손쓸 수 있는 일이 아니다. 새로 고침으로 회복한다.
9. `DisposeAsync` 는 감시와 열거를 모두 취소하고 완료를 기다린다.
10. **스크롤 위치는 다루지 않는다.** ViewModel 이 내는 것은 이름 기준 선택뿐이며,
    스크롤 복원은 View 의 일이다(수동 UI phase).

### 테스트가 반드시 덮어야 할 것

`FakeFolderWatcher`(phase 1)와 `FakeFolderSource` 를 쓴다.

- `Push(Added)` → 해당 항목이 **정렬 위치에** 들어간다 (맨 끝이 아니다)
- `Push(Removed)` → 항목이 사라지고 선택에서도 빠진다
- **선택된 항목의 `Renamed` → 선택이 새 이름으로 이어진다** ← 핵심
- 선택되지 않은 항목이 바뀌어도 선택이 그대로다
- **갱신 중에 선택이 한 번도 비지 않는다** (`Selection.Count` 를 관측해 0 이 되지 않음을 확인)
- `PushOverflow` → 전체 재열거가 일어난다 (`FakeFolderSource.EnumerateCalls` 증가)
- 오버플로 후에도 여전히 존재하는 항목의 선택이 유지된다
- `TryGetItemAsync` 가 `null` 을 내면 그 항목이 목록에서 제거된다
- 폴더를 옮기면 이전 폴더의 감시가 취소된다 (`FakeFolderWatcher.CancellationsObserved` 증가)
- **낡은 감시가 밀어넣은 변경이 새 폴더의 목록에 반영되지 않는다**
- 알림 100개를 밀어넣어도 모두 반영된다 (묶음 처리가 알림을 잃지 않는다)
- 감시 스트림 예외 → `Status` 가 `Error` 로 가지 않고 `Items` 가 그대로다
- 갱신 후 `StatusText` 의 항목 수가 맞다
- `Items` 에 `Reset` 알림(`NotifyCollectionChangedAction.Reset`)이 발생하지 않는다
  — 갱신 경로에서 `CollectionChanged` 를 구독해 확인한다
- `DisposeAsync` 후 감시와 열거가 끝난다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 갱신 경로에서 `Reset` 알림이 나지 않는가? (선택이 날아가는 원인이다)
   - 이름 변경 시 선택이 이어지는 테스트가 실제로 있는가? ADR-011 의 핵심이다
   - 시간 기반 디바운스가 없는가?
   - 폴더를 옮길 때 낡은 감시가 새 목록을 오염시키지 않는가?
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

이 step 이 끝나면 phase 2 가 완료다. `summary` 에 남은 수동 작업(실제 `IFolderWatcher` 구현,
View, DI 조립)이 무엇인지 한 줄로 적어라.

## 금지사항

- **갱신 경로에서 `ReplaceAll`·`AddRange`·`Clear` 를 쓰지 마라.** 이유: `Reset` 알림이 WPF
  `ListView` 의 선택을 날린다. ADR-011 — "갱신 때마다 선택이 풀리면 감시가 없느니만 못하다."
- **시간 기반 디바운스(`Task.Delay`·타이머)를 쓰지 마라.** 이유: 테스트가 실행 속도에 의존하면
  간헐적으로 실패하고, 자율 실행에서 그 원인을 찾는 비용이 가장 크다. 묶음은 개수로 정한다.
- **`FileSystemWatcher` 를 직접 쓰지 마라.** 이유: `IFolderWatcher` 를 주입받는다. 실제 구현은
  `FlexDir.Shell` 이 만들고 수동 검증 대상이다(ADR-009).
- **감시 오류를 `Status = Error` 로 올리지 마라.** 이유: 목록은 여전히 유효하다. 오류로 표시하면
  파일이 사라진 것처럼 보인다.
- **오버플로를 무시하지 마라.** 이유: 유실된 이벤트를 무시하면 목록이 파일시스템과 어긋난 채
  남는다(`docs/SHELL_NOTES.md` §폴더 감시).
- **스크롤 위치를 ViewModel 에서 다루지 마라.** 이유: 픽셀·행 인덱스는 View 의 관심사다.
- **낡은 감시의 알림을 적용하지 마라.** 이유: 이전 폴더의 변경이 새 폴더 목록에 섞인다 —
  전작 잔버그의 원천이다.
- **XAML 이나 `*.xaml.cs` 를 만들지 마라.** 이유: View 는 수동 UI phase 의 일이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
