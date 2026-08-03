# Step 4: view-state

폴더마다 뷰 모드와 정렬을 기억한다. 첫 포트 인터페이스와 첫 계약 테스트가 여기서 나온다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — 이전 step 산출물. 폴더 키
- `src/FlexDir.Core/Sorting/FileItemComparer.cs` — 이전 step 산출물. `SortOrder`·`SortKey` 를 그대로 쓴다
- `docs/ARCHITECTURE.md` — §2 포트 표(`IViewStateStore`), §4 상태 소유자와 수명, 저장 위치
- `docs/PRD.md` — §2 폴더별 기억
- `docs/DESIGN.md` — §2 뷰 모드 4종의 정확한 이름

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다. 순수 인터페이스도 예외가 아니다 —
그래서 이 step 은 포트와 함께 **테스트용 fake 와 계약 테스트**를 만든다.

> **테스트 파일 이름 규칙 — 훅이 파일명으로 찾는다.** 소스가 `Foo.cs` 면 테스트는 반드시
> `FooTests.cs` 여야 한다. **인터페이스도 예외가 아니다** — `IViewStateStore.cs` 의 테스트는
> `IViewStateStoreTests.cs` 다. 파일 안의 클래스 이름은 달라도 되지만 **파일명은 정확히 맞춰야
> 한다.** 어기면 `Write` 가 거부된다.

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/ViewState/FolderViewStateTests.cs`
   - `tests/FlexDir.Core.Tests/ViewState/ViewStateStoreContract.cs` (추상 클래스, 테스트 아님)
   - `tests/FlexDir.Core.Tests/Fakes/InMemoryViewStateStore.cs` (fake, 테스트 아님)
   - `tests/FlexDir.Core.Tests/ViewState/IViewStateStoreTests.cs`
     — 파일명은 인터페이스에 맞추고, 안에 `InMemoryViewStateStoreTests : ViewStateStoreContract` 를 둔다
2. 그 다음 `src/FlexDir.Core/ViewState/FolderViewState.cs` 와
   `src/FlexDir.Core/ViewState/IViewStateStore.cs` 를 쓴다. **파일은 이 둘뿐이다.**
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/ViewState/FolderViewState.cs`

`ViewMode` 열거형을 **이 파일 안에** 선언한다. 별도 파일로 두면 훅이
`ViewModeTests.cs` 를 요구하는데, 열거형 선언만으로는 의미 있는 테스트를 쓸 수 없다.

```csharp
namespace FlexDir.Core.ViewState;

using FlexDir.Core.Sorting;

/// 이름은 docs/DESIGN.md §2 의 네 뷰와 1:1 로 대응한다.
public enum ViewMode { Details, List, Tiles, LargeIcons }

public sealed record FolderViewState(ViewMode Mode, IReadOnlyList<SortOrder> Sort)
{
    /// Details + 이름 오름차순. 기억된 상태가 없는 폴더에 쓴다.
    public static FolderViewState Default { get; }
}

/// 폴더와 무관한 전역 상태. 창 배치와 스플리터 비율.
public sealed record GlobalViewState(double SplitterRatio, WindowPlacement? Window)
{
    public static GlobalViewState Default { get; }   // SplitterRatio 0.5, Window null
}

public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized);
```

- `SplitterRatio` 는 좌 페인이 차지하는 비율(0~1)이다. 생성 시 범위를 검사하고
  벗어나면 `ArgumentOutOfRangeException`. 이유: 저장 파일이 손상돼 `-3` 이 들어오면
  페인이 사라지는데, 그때 원인을 찾기 어렵다.
- `Sort` 가 비어 있으면 `ArgumentException`. 이유: `FileItemComparer` 가 빈 키를 거부한다.

### 2. `src/FlexDir.Core/ViewState/IViewStateStore.cs`

```csharp
namespace FlexDir.Core.ViewState;

using FlexDir.Core.Locations;

public interface IViewStateStore
{
    /// 기억된 상태가 없으면 null. 없는 것은 오류가 아니다.
    ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct);

    ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct);

    ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct);

    ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct);
}
```

- 전역 상태는 **null 을 내지 않는다.** 없으면 `GlobalViewState.Default` 다.
  이유: 호출자가 매번 기본값을 조립하면 기본값이 두 군데에 생긴다.

### 3. `tests/FlexDir.Core.Tests/ViewState/ViewStateStoreContract.cs`

구현체가 반드시 만족해야 하는 성질을 담는 추상 클래스다. **나중에 `FlexDir.Shell` 의 실제
구현체 테스트가 이 클래스를 상속해 같은 검증을 받는다** — 그것이 이 파일의 존재 이유다.

```csharp
public abstract class ViewStateStoreContract
{
    protected abstract IViewStateStore CreateStore();

    // 아래를 [Fact] 로 구현한다:
    // - 저장한 상태를 그대로 다시 읽는다 (왕복)
    // - 기억이 없는 폴더는 null 을 낸다
    // - 대소문자만 다른 경로는 같은 폴더로 취급한다 (LocationId 동등성)
    // - 같은 폴더에 두 번 저장하면 나중 값이 남는다
    // - 전역 상태를 저장하지 않은 상태에서 LoadGlobalAsync 는 Default 를 낸다
    // - 취소된 CancellationToken 을 주면 OperationCanceledException 을 던진다
}
```

`InMemoryViewStateStore` 는 이 계약을 만족하는 사전 기반 fake 다. 폴더 키는 `LocationId` 를
그대로 쓴다(동등성이 이미 `OrdinalIgnoreCase` 다).

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 계약 테스트가 `IViewStateStore` 만 보고 있는가? (fake 의 내부 구조를 들여다보면 재사용이 안 된다)
   - `FlexDir.Core` 에 파일 I/O·`%LOCALAPPDATA%` 접근이 없는가? 저장 위치는 `FlexDir.Shell` 의 책임이다
   - 뷰 모드 이름이 `docs/DESIGN.md` §2 의 네 뷰와 대응하는가?
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `InMemoryViewStateStore` 와 `ViewStateStoreContract` 의 경로를 반드시 적어라.
이후 step 들이 이 fake 를 재사용한다.

## 금지사항

- **`FlexDir.Core` 에서 파일을 읽거나 쓰지 마라.** 이유: `%LOCALAPPDATA%\flex-dir\` 접근은
  `FlexDir.Shell` 의 구현체 몫이다(`docs/ARCHITECTURE.md` §4). Core 가 I/O 를 하면 순수성이 깨지고
  테스트가 디스크에 묶인다.
- **JSON·직렬화 포맷을 정하지 마라.** 이유: 저장 형식은 구현체의 자유이며, 포트가 포맷을 규정하면
  나중에 바꿀 수 없다.
- **`InMemoryViewStateStore` 를 `src/` 아래에 두지 마라.** 이유: 프로덕션 어셈블리에 테스트용
  구현체가 섞이면 실수로 주입될 수 있다. 위치는 `tests/FlexDir.Core.Tests/Fakes/` 다.
- **기억이 없는 폴더에 예외를 던지지 마라.** 이유: 처음 방문하는 폴더가 정상 상황이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
