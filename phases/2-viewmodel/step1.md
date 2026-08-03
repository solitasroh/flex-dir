# Step 1: pane-viewmodel

페인 하나가 폴더를 열고 목록을 점진적으로 채운다. **이 phase 에서 가장 큰 step 이다** —
목록 표시에 필요한 배관까지 함께 만든다.

## 읽어야 할 파일

- `src/FlexDir.App/Navigation/PaneHistory.cs` — 이전 step 산출물
- `src/FlexDir.Core/Enumeration/EnumerationSession.cs` — phase 1 산출물. 열거는 이것으로만 한다
- `src/FlexDir.Core/Enumeration/IFolderSource.cs` — phase 1 산출물
- `src/FlexDir.Core/Errors/LocationAccessException.cs`,
  `src/FlexDir.Core/Errors/LocationError.cs` — phase 0 산출물
  (`LocationErrorKind` 열거형과 `LocationErrorMessages.Describe` 가 이 파일에 있다)
- `src/FlexDir.Core/Formatting/SizeFormatter.cs`,
  `src/FlexDir.Core/Formatting/TimestampFormatter.cs`,
  `src/FlexDir.Core/Formatting/StatusSummary.cs` — phase 0 산출물. 표시 문자열은 전부 여기서 온다
- `src/FlexDir.Core/Sorting/FileItemComparer.cs` — phase 0 산출물
- `tests/FlexDir.Core.Tests/Fakes/FakeFolderSource.cs` — phase 1 산출물. 테스트에서 이것을 쓴다
- `docs/ARCHITECTURE.md` — §3 데이터 흐름, §4 상태 표, §5 리스트 가상화 금지 목록
- `docs/UI_GUIDE.md` — §상태 표현 전체
- `docs/PRD.md` — §4 엣지케이스 (빈 폴더 · 권한 없음 · 경로가 사라짐)
- `docs/DESIGN.md` — §3 Details 컬럼 (이름·크기·유형·수정한 날짜)

MVVM 은 **CommunityToolkit.Mvvm 8.4.2** 를 쓴다 (`src/FlexDir.App/FlexDir.App.csproj` 에 이미 있다).
`ObservableObject` 를 상속하고 `SetProperty` 또는 `[ObservableProperty]` 를 쓴다.
소스 생성기 어트리뷰트를 쓰면 클래스를 `partial` 로 선언해야 한다.

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다. 파일마다 테스트를 먼저 쓴다.

> **테스트 파일 이름 규칙 — 훅이 파일명으로 찾는다.** 소스가 `Foo.cs` 면 테스트 파일은 반드시
> `FooTests.cs` 여야 한다. **인터페이스도 예외가 아니다** — `IUiDispatcher.cs` 에는
> `IUiDispatcherTests.cs` 가 필요하다. 파일 안의 클래스 이름은 달라도 되지만 파일명은 정확히 맞춘다.
> 인터페이스의 테스트는 그 인터페이스를 만족하는 **fake 의 계약을 검증**하는 것으로 쓴다 —
> 나중에 실제 구현체가 같은 검증을 받는다.

1. 먼저 아래를 쓴다 (red). **테스트는 소스가 속한 프로젝트의 `.Tests` 에 둔다** —
   `ITypeNameProvider` 는 `FlexDir.Core` 의 파일이므로 그 테스트는 `FlexDir.Core.Tests` 다.
   - `tests/FlexDir.Core.Tests/Fakes/FakeTypeNameProvider.cs` (fake)
   - `tests/FlexDir.Core.Tests/Presentation/ITypeNameProviderTests.cs`
     — `FakeTypeNameProvider` 가 확장자당 한 번만 조회되는지 검증
   - `tests/FlexDir.App.Tests/Fakes/InlineUiDispatcher.cs` (fake)
   - `tests/FlexDir.App.Tests/Threading/IUiDispatcherTests.cs`
     — `InlineUiDispatcher` 가 동작을 실행하고 `IsOnUiThread` 를 참으로 내는지 검증
   - `tests/FlexDir.App.Tests/ViewModels/BulkObservableCollectionTests.cs`
   - `tests/FlexDir.App.Tests/ViewModels/FileItemViewModelTests.cs`
   - `tests/FlexDir.App.Tests/ViewModels/PaneViewModelTests.cs`
2. 그 다음 순서대로 소스를 쓴다:
   - `src/FlexDir.Core/Presentation/ITypeNameProvider.cs`
   - `src/FlexDir.App/Threading/IUiDispatcher.cs`
   - `src/FlexDir.App/ViewModels/BulkObservableCollection.cs`
   - `src/FlexDir.App/ViewModels/FileItemViewModel.cs`
   - `src/FlexDir.App/ViewModels/PaneViewModel.cs`
3. AC 가 통과할 때까지 구현을 고친다.

`FlexDir.App.Tests` 는 `FlexDir.Core.Tests` 를 참조한다(`FlexDir.App.Tests.csproj` 에 이미 있다).
그래서 `FakeTypeNameProvider`·`FakeFolderSource` 같은 Core 쪽 fake 를 App 테스트에서 그대로 쓸 수 있다.

## 작업

### 1. `src/FlexDir.Core/Presentation/ITypeNameProvider.cs`

"유형" 컬럼 문자열은 Windows Shell 에서 온다. Core 는 포트만 정의한다.

```csharp
namespace FlexDir.Core.Presentation;

public interface ITypeNameProvider
{
    /// 확장자에 대응하는 표시용 유형 이름 (예: "텍스트 문서").
    /// extension 은 점을 포함하지 않는 소문자다. 빈 문자열은 확장자 없음.
    ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct);
}
```

> 구현체는 확장자마다 **한 번만** 조회해 캐시한다 (`docs/SHELL_NOTES.md` §아이콘 —
> "파일마다 아이콘을 조회하지 마라. 확장자마다 한 번만"). 그 구현은 수동 Shell phase 의 일이다.

fake: `tests/FlexDir.Core.Tests/Fakes/FakeTypeNameProvider.cs` — 확장자를 그대로 대문자로 돌려주고
호출 횟수를 센다. **같은 확장자로 두 번 호출되지 않는지 검증하는 데 쓴다.**

### 2. `src/FlexDir.App/Threading/IUiDispatcher.cs`

열거는 UI 스레드 밖에서 돈다(`CLAUDE.md` §3). 결과를 목록에 반영할 때 UI 스레드로 옮겨야 한다.

```csharp
namespace FlexDir.App.Threading;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
    bool IsOnUiThread { get; }
}
```

fake: `tests/FlexDir.App.Tests/Fakes/InlineUiDispatcher.cs` — 즉시 실행하고 `IsOnUiThread == true`.
실제 `Dispatcher` 기반 구현은 수동 UI phase 에서 만든다.

### 3. `src/FlexDir.App/ViewModels/BulkObservableCollection.cs`

```csharp
namespace FlexDir.App.ViewModels;

using System.Collections.ObjectModel;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// 여러 항목을 넣고 Reset 알림 한 번만 낸다. 열거 중 배치 추가에 쓴다.
    public void AddRange(IEnumerable<T> items);

    /// 항목을 통째로 교체하고 Reset 알림 한 번만 낸다.
    public void ReplaceAll(IEnumerable<T> items);
}
```

- `AddRange` 는 **열거 중에만** 쓴다. 그때는 선택이 없으므로 `Reset` 알림이 안전하다.
- 갱신(감시 반영)에는 쓰지 마라 — `Reset` 은 WPF `ListView` 의 선택을 날린다.
  개별 `Add`/`Remove` 를 써야 한다. 그 경로는 step 7 이 만든다.
- 빈 컬렉션을 `AddRange` 하면 알림을 내지 않는다.

### 4. `src/FlexDir.App/ViewModels/FileItemViewModel.cs`

목록 한 줄이다. `ObservableObject` 를 상속한다 (step 6 에서 썸네일 속성이 추가된다).

```csharp
namespace FlexDir.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using FlexDir.Core.Model;

public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileItem item, string sizeText, string modifiedText, string typeText);

    public FileItem Item { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    public string SizeText { get; }        // SizeFormatter.ForItem 결과
    public string ModifiedText { get; }    // TimestampFormatter.Format 결과
    public string TypeText { get; }        // ITypeNameProvider 결과
}
```

- 표시 문자열을 **여기서 만들지 않는다.** 생성자로 받는다. 이유: 포맷터는
  `IFormatProvider`·`TimeZoneInfo` 를 인자로 요구하고(phase 0 step 3), 유형 이름은 비동기다.
  조립은 `PaneViewModel` 이 한다.

### 5. `src/FlexDir.App/ViewModels/PaneViewModel.cs`

```csharp
namespace FlexDir.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using FlexDir.Core.Locations;

public enum PaneStatus { Idle, Enumerating, Empty, Error }

public sealed partial class PaneViewModel : ObservableObject
{
    public PaneViewModel(
        IFolderSource folderSource,
        ITypeNameProvider typeNames,
        IUiDispatcher dispatcher,
        IFormatProvider culture,
        TimeZoneInfo timeZone);

    public BulkObservableCollection<FileItemViewModel> Items { get; }

    public LocationId? CurrentLocation { get; }
    public string AddressText { get; }        // CurrentLocation.DisplayPath 또는 ""
    public PaneStatus Status { get; }
    public string StatusText { get; }         // StatusSummary / LocationErrorMessages 결과
    public bool CanGoBack { get; }
    public bool CanGoForward { get; }
    public bool CanGoUp { get; }

    /// 지정한 위치를 연다. 히스토리에 기록한다.
    public Task NavigateAsync(LocationId location, CancellationToken ct = default);

    /// 문자열 주소를 파싱해 연다. 파싱 실패는 Error 상태로 간다 (예외를 던지지 않는다).
    public Task NavigateAsync(string address, CancellationToken ct = default);

    public Task GoBackAsync(CancellationToken ct = default);
    public Task GoForwardAsync(CancellationToken ct = default);
    public Task GoUpAsync(CancellationToken ct = default);

    /// 현재 폴더를 다시 읽는다. 히스토리에 기록하지 않는다.
    public Task RefreshAsync(CancellationToken ct = default);
}
```

### 동작 규칙

1. **목록을 비우고 시작하지 않는다.** 새 폴더의 첫 배치가 도착할 때 이전 목록을 교체한다
   (`ReplaceAll`), 이후 배치는 `AddRange` 로 덧붙인다.
   이유: `docs/UI_GUIDE.md` §상태 표현 — "목록을 비우지 않고 점진적으로 채운다."
   먼저 비우면 폴더 전환마다 빈 화면이 번쩍인다.
2. 정렬은 `FileItemComparer.Default` 로 한다. 배치를 덧붙일 때마다 **전체를 다시 정렬하지 말고**
   그 배치를 정렬해 넣는다. 완전한 정렬은 열거 종료 시 한 번 보장한다.
   (폴더별 정렬 상태 연동은 step 3 이 한다. 지금은 기본값만.)
3. 상태 전이:
   - 열거 시작 → `Enumerating`, `StatusText` 는 `StatusSummary.ForEnumerating(누적 개수)`
   - 열거 종료, 항목 있음 → `Idle`, `StatusSummary.ForItems`
   - 열거 종료, 항목 0개 → `Empty`, `StatusSummary.Empty`
   - `LocationAccessException` → `Error`, `LocationErrorMessages.Describe(...)`
4. **권한 없음은 경로를 되돌리지 않는다.** `CurrentLocation` 은 실패한 그 경로로 남고
   상태표시줄에만 사유가 뜬다 (`docs/PRD.md` §4, `docs/UI_GUIDE.md` §상태 표현).
   목록은 비운다 — 이전 폴더 항목이 남으면 잘못된 폴더의 내용으로 보인다.
5. **경로가 사라진 경우**(`LocationErrorKind.NotFound`)만 **상위 폴더로 자동 이동**하고
   사유를 `StatusText` 에 남긴다 (`docs/PRD.md` §4). 상위도 없으면 `Error` 로 멈춘다.
   자동 이동은 **한 단계만** 시도한다 — 연쇄로 올라가면 사용자가 어디로 갔는지 모른다.
6. 목록 변경은 전부 `IUiDispatcher.InvokeAsync` 안에서 한다.
7. `NavigateAsync(string)` 이 `LocationId.TryParse` 에 실패하면 `Error` 상태로 가고
   `LocationParseError` 에 맞는 사유를 남긴다. **예외를 던지지 않는다.**
8. 유형 이름은 확장자마다 한 번만 조회한다. 같은 배치에 같은 확장자가 여러 개면 한 번만 묻는다.

### 테스트가 반드시 덮어야 할 것

`FakeFolderSource`(phase 1)·`FakeTypeNameProvider`·`InlineUiDispatcher` 를 쓴다.

- 항목 3개 폴더를 열면 `Items` 가 3개가 되고 `Status == Idle`
- 빈 폴더 → `Status == Empty`, `StatusText == StatusSummary.Empty`
- 폴더가 폴더보다 먼저 정렬돼 있다
- `FakeFolderSource.FailureInjection` 으로 `AccessDenied` → `Status == Error`,
  **`CurrentLocation` 이 실패한 경로 그대로**, `Items` 가 비어 있다
- `NotFound` → **상위 폴더로 이동**하고 사유가 `StatusText` 에 남는다
- 루트에서 `NotFound` → 자동 이동 없이 `Error`
- 잘못된 주소 문자열 → `Status == Error`, 예외가 던져지지 않는다
- 느린 열거 중 다른 폴더로 `NavigateAsync` → 첫 폴더 항목이 `Items` 에 섞이지 않는다
- 뒤로/앞으로/상위 후 `CanGoBack`·`CanGoForward`·`CanGoUp` 이 맞다
- `RefreshAsync` 는 히스토리를 늘리지 않는다
- 같은 확장자 항목이 10개일 때 `FakeTypeNameProvider` 호출이 1회다
- 열거 중 `StatusText` 가 `ForEnumerating` 형태다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1`)
   - `PaneViewModel` 이 `System.Windows.*`·`Dispatcher` 를 직접 쓰지 않는가? (`IUiDispatcher` 를 통한다)
   - 권한 오류에서 경로가 유지되는가? `NotFound` 에서만 상위로 가는가?
   - 목록을 먼저 비우고 열거를 시작하지 않는가?
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 만든 파일 다섯 개의 경로와 fake 두 개의 경로를 반드시 적어라. 이후 step 이 모두 쓴다.

## 금지사항

- **XAML 이나 `*.xaml.cs` 를 만들지 마라.** 이유: View 는 수동 UI phase 의 일이고(ADR-009),
  코드비하인드는 구조 게이트가 막는다(`CLAUDE.md` §2).
- **`Dispatcher.CurrentDispatcher`·`Application.Current` 를 쓰지 마라.** 이유: 테스트에는
  WPF 애플리케이션이 없다. `IUiDispatcher` 를 주입받는다.
- **`IFolderSource` 를 직접 열거하지 마라.** `EnumerationSession` 을 쓴다. 이유: 폴더 이탈 시
  결과 격리 규칙이 그 클래스에 있다(phase 1 step 1). 직접 부르면 그 보호가 사라진다.
- **권한 오류에서 이전 경로로 되돌아가지 마라.** 이유: `docs/PRD.md` §4 가 명시적으로 금지했다.
- **`Items` 를 `ScrollViewer` 로 감싸거나 가상화를 끄는 것을 전제한 API 를 만들지 마라**
  (예: 전체 항목을 `List<T>` 로 노출하는 속성). 이유: `docs/ARCHITECTURE.md` §5 — 데이터 가상화가
  완전히 해제된다.
- **갱신 경로에 `ReplaceAll`/`AddRange` 를 쓰지 마라.** 이유: `Reset` 알림이 선택을 날린다.
  감시 반영은 step 7 이 개별 알림으로 만든다.
- **폴더 크기를 계산하지 마라.** 이유: v1 범위 밖이고 대용량 폴더에서 UI 가 멈춘다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
