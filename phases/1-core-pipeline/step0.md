# Step 0: folder-source-port

폴더를 읽는 포트를 정의한다. 구현체는 `FlexDir.Shell` 이 나중에 수동으로 만들고,
지금은 **주입 가능한 fake** 와 **계약 테스트**를 만든다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — phase 0 산출물
- `src/FlexDir.Core/Model/FileItem.cs` — phase 0 산출물
- `src/FlexDir.Core/Errors/LocationAccessException.cs` — phase 0 산출물. 열거 실패는 이것으로 던진다
- `tests/FlexDir.Core.Tests/ViewState/ViewStateStoreContract.cs` — phase 0 산출물.
  **계약 테스트를 이 파일과 같은 방식으로 쓴다**
- `docs/ARCHITECTURE.md` — §2 포트 표(`IFolderSource` — 비동기 스트림, 취소 가능), §3 데이터 흐름
- `docs/SHELL_NOTES.md` — §열거 (구현체가 나중에 지켜야 할 사실. 포트 모양을 정할 때 참고)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다. 순수 인터페이스도 예외가 아니다.

> **테스트 파일 이름 규칙 — 훅이 파일명으로 찾는다.** 소스가 `Foo.cs` 면 테스트는 반드시
> `FooTests.cs` 여야 한다. **인터페이스도 예외가 아니다** — `IFolderSource.cs` 의 테스트 파일은
> `IFolderSourceTests.cs` 다. 파일 안의 클래스 이름은 달라도 되지만 **파일명은 정확히 맞춘다.**

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/Enumeration/FolderSourceContract.cs` (추상 클래스)
   - `tests/FlexDir.Core.Tests/Fakes/FakeFolderSource.cs` (fake)
   - `tests/FlexDir.Core.Tests/Enumeration/IFolderSourceTests.cs`
     — 안에 `FakeFolderSourceTests : FolderSourceContract` 를 둔다
2. 그 다음 `src/FlexDir.Core/Enumeration/IFolderSource.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Enumeration/IFolderSource.cs`

```csharp
namespace FlexDir.Core.Enumeration;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

public interface IFolderSource
{
    /// 폴더의 항목을 비동기 스트림으로 낸다. 열 수 없으면 LocationAccessException.
    /// 스트림 도중에도 실패할 수 있다 (열거 중 폴더가 사라지는 경우).
    IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct);

    /// 단건 조회. 없으면 null (예외가 아니다).
    /// 외부 변경 알림을 받은 뒤 그 항목만 다시 확인하는 데 쓴다.
    Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct);
}
```

- **오류는 예외로 낸다.** 결과 스트림에 오류 항목을 섞지 않는다. 이유: `IAsyncEnumerable` 소비자가
  항목마다 오류 여부를 검사해야 하면 모든 호출부가 오염된다.
- `TryGetItemAsync` 는 **없음을 `null` 로** 낸다. 이유: 감시 알림과 실제 상태가 어긋나는 것은
  정상 상황이다(진실원천은 파일시스템 — `CLAUDE.md` §4). 예외로 만들면 정상 흐름이 예외 경로가 된다.
- 권한 문제는 `TryGetItemAsync` 에서도 `LocationAccessException` 이다. 없음과 구분해야 한다.

### 2. `tests/FlexDir.Core.Tests/Fakes/FakeFolderSource.cs`

이후 모든 열거·ViewModel 테스트가 이 fake 를 쓴다. **주입 가능해야 하는 것**:

```csharp
public sealed class FakeFolderSource : IFolderSource
{
    /// 폴더별 항목 목록.
    public Dictionary<LocationId, List<FileItem>> Folders { get; }

    /// 항목을 하나 낼 때마다 이 만큼 await 한다. 0 이면 즉시.
    public int YieldDelayMilliseconds { get; set; }

    /// N 번째 항목을 낸 뒤 예외를 던진다. null 이면 던지지 않는다.
    public (int AfterItems, LocationErrorKind Kind)? FailureInjection { get; set; }

    /// EnumerateAsync 가 호출된 폴더의 순서. 취소·격리 검증에 쓴다.
    public List<LocationId> EnumerateCalls { get; }

    /// 취소가 관측된 횟수.
    public int CancellationsObserved { get; }
}
```

- 취소는 **항목을 낼 때마다** `ct.ThrowIfCancellationRequested()` 로 관측한다.
  실제 구현체도 그래야 하므로 fake 가 먼저 그 계약을 강제한다.
- 등록되지 않은 폴더는 `LocationErrorKind.NotFound` 로 `LocationAccessException` 을 던진다.

### 3. `tests/FlexDir.Core.Tests/Enumeration/FolderSourceContract.cs`

구현체가 반드시 만족해야 하는 성질이다. **나중에 `FlexDir.Shell` 의 실제 구현체 테스트가
이 클래스를 상속한다.**

```csharp
public abstract class FolderSourceContract
{
    /// 이 폴더와 항목들이 준비된 구현체를 낸다.
    protected abstract IFolderSource CreateSource(LocationId folder, IReadOnlyList<FileItem> items);

    // 아래를 [Fact] 로 구현한다:
    // - 등록된 항목을 전부 낸다
    // - 이미 취소된 토큰을 주면 OperationCanceledException 을 던진다
    // - 열거 중간에 취소하면 그 이후 항목을 내지 않는다
    // - 없는 폴더는 LocationAccessException(NotFound)
    // - TryGetItemAsync 는 없는 항목에 null 을 낸다 (예외가 아니다)
    // - TryGetItemAsync 는 있는 항목의 FileItem 을 낸다
}
```

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 계약 테스트가 `IFolderSource` 만 보고 있는가? fake 의 내부 필드를 들여다보면 재사용이 안 된다
   - `FlexDir.Core` 에 `Directory`·`FileInfo`·P/Invoke 가 없는가? (`check-structure.ps1`)
   - fake 가 `tests/` 아래에 있는가?
3. 결과에 따라 `phases/1-core-pipeline/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `FakeFolderSource` 와 `FolderSourceContract` 의 경로, 그리고 주입 가능한 속성 이름을
반드시 적어라. 이후 모든 step 이 이 fake 를 쓴다.

## 금지사항

- **실제 파일시스템을 읽는 구현체를 만들지 마라.** 이유: `FlexDir.Shell` 의 몫이고 수동 검증
  대상이다(ADR-009). `FindFirstFileEx` P/Invoke 를 여기서 시작하면 자동 채점이 무의미해진다.
- **`Directory.EnumerateFileSystemEntries` 로 임시 구현을 만들지 마라.** 이유: `docs/SHELL_NOTES.md`
  §열거 가 그 API 를 쓰지 말라고 못박았다(플래그를 못 주고 예외 기반이라 대용량에 불리). 임시로 넣으면
  나중에 지우지 않고 남는다.
- **오류를 결과 스트림에 항목으로 섞지 마라.** 이유: 모든 소비자가 항목마다 분기해야 한다.
- **`IEnumerable`(동기)로 만들지 마라.** 이유: 열거는 UI 스레드 밖에서 점진적으로 흘러야 한다
  (`docs/ARCHITECTURE.md` §3, `CLAUDE.md` §3).
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
