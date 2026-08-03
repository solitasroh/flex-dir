# Step 2: folder-watcher-port

외부 변경 알림 포트를 정의한다. 구현체는 `FlexDir.Shell` 이 나중에 만들고,
지금은 fake 와 계약 테스트를 만든다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — phase 0 산출물
- `src/FlexDir.Core/Enumeration/IFolderSource.cs` — 이전 step 산출물. 포트 모양을 맞춘다
- `tests/FlexDir.Core.Tests/Enumeration/FolderSourceContract.cs` — 이전 step 산출물.
  **계약 테스트를 같은 방식으로 쓴다**
- `docs/ARCHITECTURE.md` — §2 포트 표(`IFolderWatcher`)
- `docs/ADR.md` — ADR-011 (외부 변경은 감시로 자동 갱신, 갱신 중에도 선택 유지)
- `docs/SHELL_NOTES.md` — **§폴더 감시**. C# 항목의 오버플로 규칙이 이 step 의 요구사항이다

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

> **테스트 파일 이름 규칙 — 훅이 파일명으로 찾는다.** 소스가 `Foo.cs` 면 테스트는 반드시
> `FooTests.cs` 여야 한다. **인터페이스도 예외가 아니다.** 파일 안의 클래스 이름은 달라도 되지만
> 파일명은 정확히 맞춘다.

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/Watching/FolderChangeTests.cs`
   - `tests/FlexDir.Core.Tests/Watching/FolderWatcherContract.cs` (추상 클래스)
   - `tests/FlexDir.Core.Tests/Fakes/FakeFolderWatcher.cs` (fake)
   - `tests/FlexDir.Core.Tests/Watching/IFolderWatcherTests.cs`
     — 안에 `FakeFolderWatcherTests : FolderWatcherContract` 를 둔다
2. 그 다음 소스 **두 개**를 쓴다:
   - `src/FlexDir.Core/Watching/FolderChange.cs` (열거형 + 레코드)
   - `src/FlexDir.Core/Watching/IFolderWatcher.cs` (인터페이스만)
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Watching/FolderChange.cs`

```csharp
namespace FlexDir.Core.Watching;

using FlexDir.Core.Locations;

public enum FolderChangeKind
{
    Added,
    Removed,
    Changed,
    Renamed,

    /// 감시 버퍼가 넘쳐 개별 이벤트가 유실됐다. 소비자는 전체 새로고침으로 폴백해야 한다.
    Overflow,
}

/// Name·OldName 은 폴더 안의 이름이며 전체 경로가 아니다.
/// Renamed 만 OldName 을 가진다. Overflow 는 둘 다 빈 문자열이다.
public sealed record FolderChange(FolderChangeKind Kind, string Name, string? OldName = null)
{
    public static FolderChange Overflowed { get; }
}
```

`src/FlexDir.Core/Watching/IFolderWatcher.cs`

```csharp
namespace FlexDir.Core.Watching;

using FlexDir.Core.Locations;

public interface IFolderWatcher
{
    /// 폴더의 변경을 스트림으로 낸다. 취소되면 정상 종료한다.
    IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct);
}
```

### 오버플로가 별도 종류인 이유

`docs/SHELL_NOTES.md` §폴더 감시 의 C# 항목: `FileSystemWatcher` 는 `InternalBufferSize` 를
넘으면 `Error` 이벤트를 내고 **그 사이 이벤트를 유실한다.** 유실된 이벤트를 개별 변경으로
흉내내면 목록이 파일시스템과 어긋난 채로 남는다. 진실원천은 파일시스템이므로
(`CLAUDE.md` §4) 그때는 전체를 다시 읽어야 한다. 그 신호가 `Overflow` 다.

- `Overflow` 는 **삼키지 않는다.** 구현체가 조용히 무시하면 목록이 stale 인 채로 남고,
  그게 전작 잔버그의 주요 원천이었다.

### 2. `tests/FlexDir.Core.Tests/Fakes/FakeFolderWatcher.cs`

테스트가 변경을 임의 시점에 밀어넣을 수 있어야 한다.

```csharp
public sealed class FakeFolderWatcher : IFolderWatcher
{
    /// 감시 중인 소비자에게 변경을 밀어넣는다.
    public void Push(FolderChange change);

    /// Overflow 를 밀어넣는다.
    public void PushOverflow();

    /// 스트림을 정상 종료시킨다.
    public void Complete();

    /// WatchAsync 가 호출된 폴더들.
    public List<LocationId> WatchCalls { get; }

    public int CancellationsObserved { get; }
}
```

`Channel<FolderChange>` 로 구현하면 간단하다. 소비자가 없을 때 `Push` 한 변경은 버퍼에 남아
다음 소비자가 받는다.

### 3. `tests/FlexDir.Core.Tests/Watching/FolderWatcherContract.cs`

```csharp
public abstract class FolderWatcherContract
{
    protected abstract IFolderWatcher CreateWatcher(LocationId folder);

    // 아래를 [Fact] 로 구현한다:
    // - 이미 취소된 토큰을 주면 항목을 내지 않고 끝난다
    // - 취소하면 스트림이 정상 종료된다 (무한 대기하지 않는다)
    // - Overflow 를 낼 수 있다
    // - Renamed 는 OldName 을 가진다
}
```

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 취소 시 스트림이 **무한 대기 없이** 끝나는가? 테스트에 타임아웃을 걸어 확인한다
   - `Overflow` 가 열거형에 있고 계약 테스트가 그것을 덮는가?
   - `FlexDir.Core` 에 `FileSystemWatcher` 사용이 없는가? 그것은 `FlexDir.Shell` 의 몫이다
3. 결과에 따라 `phases/1-core-pipeline/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `FakeFolderWatcher` 의 경로와 `Push`·`PushOverflow`·`Complete` 를 적어라.
phase 2 의 감시 통합 step 이 이 fake 를 쓴다.

## 금지사항

- **`FileSystemWatcher` 를 `FlexDir.Core` 에서 쓰지 마라.** 이유: `CLAUDE.md` §1 — Core 는
  Windows API 를 참조하지 않는다. 실제 감시는 `FlexDir.Shell` 이 하고 수동 검증 대상이다.
- **`Overflow` 를 일반 `Changed` 로 뭉개지 마라.** 이유: 유실된 이벤트를 개별 변경으로 흉내내면
  목록이 파일시스템과 어긋난 채 남는다(`docs/SHELL_NOTES.md` §폴더 감시).
- **`FolderChange` 에 전체 경로를 넣지 마라.** 이유: 감시 대상 폴더는 소비자가 이미 알고 있고,
  경로를 중복으로 실으면 폴더 이름이 바뀔 때 두 곳이 어긋난다.
- **디바운스·병합 로직을 포트에 넣지 마라.** 이유: 얼마나 모아서 처리할지는 소비자(ViewModel)의
  정책이다. 포트가 정하면 테스트가 불가능해진다.
- **이벤트(`event EventHandler`)로 만들지 마라.** 이유: 취소와 수명 관리가 스트림보다 어렵고,
  구독 해제를 놓치면 폴더를 옮길 때마다 감시가 누적된다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
