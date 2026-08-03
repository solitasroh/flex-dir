# Step 1: enumeration-session

폴더를 빠르게 전환해도 이전 폴더의 결과가 새 폴더에 섞이지 않게 만든다.
전작에서 잔버그가 살던 자리다.

## 읽어야 할 파일

- `src/FlexDir.Core/Enumeration/IFolderSource.cs` — 이전 step 산출물
- `tests/FlexDir.Core.Tests/Fakes/FakeFolderSource.cs` — 이전 step 산출물.
  지연·예외 주입으로 경합을 재현한다
- `src/FlexDir.Core/Errors/LocationAccessException.cs` — phase 0 산출물
- `src/FlexDir.Core/Model/FileItem.cs` — phase 0 산출물
- `docs/PRD.md` — §4 엣지케이스 중 **열거 중 폴더 이탈** ("이전 결과가 새 폴더에 섞이면 안 된다"),
  **대용량 폴더** (백그라운드 열거 + 점진적 표시)
- `docs/UI_GUIDE.md` — §상태 표현 (열거 중: 목록을 비우지 않고 점진적으로 채운다)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

1. 먼저 `tests/FlexDir.Core.Tests/Enumeration/EnumerationSessionTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.Core/Enumeration/EnumerationSession.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

`src/FlexDir.Core/Enumeration/EnumerationSession.cs`

```csharp
namespace FlexDir.Core.Enumeration;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

/// 한 페인의 열거를 관리한다. 새 열거를 시작하면 이전 것을 취소한다.
public sealed class EnumerationSession : IAsyncDisposable
{
    public EnumerationSession(IFolderSource source);

    /// 현재 세대 번호. Start 마다 증가한다.
    public int Generation { get; }

    /// 이전 run 을 취소하고 새 run 을 시작한다.
    public EnumerationRun Start(LocationId folder);

    public ValueTask DisposeAsync();
}

public sealed class EnumerationRun
{
    public LocationId Folder { get; }

    /// 이 run 이 시작될 때의 세대 번호.
    public int Generation { get; }

    /// 이 run 이 이미 낡았는가 (더 새로운 Start 가 있었는가).
    public bool IsStale { get; }

    /// 항목을 배치로 낸다. 낡은 run 에서는 아무것도 내지 않는다.
    public IAsyncEnumerable<IReadOnlyList<FileItem>> BatchesAsync(CancellationToken ct);
}
```

### 배치 정책 — 숫자를 그대로 쓴다

시간을 쓰지 않는다. **항목 수만으로** 배치를 끊는다.

| 배치 | 크기 |
|---|---|
| 첫 배치 | 항목 **1개** — 첫 항목을 최대한 빨리 보인다 (`docs/PRD.md` §5 첫 항목 150ms 목표) |
| 이후 배치 | 항목 **256개** |
| 스트림 종료 시 | 남은 항목을 마지막 배치로 낸다 (0개면 배치를 내지 않는다) |

> 시간 기반 플러시(예: 50ms 마다)를 쓰지 마라. 테스트가 실행 속도에 따라 들쭉날쭉해지고,
> 자율 실행에서 간헐적 실패는 원인을 찾는 비용이 가장 크다.

### 격리 규칙 — 이 step 의 핵심

1. `Start` 는 **즉시** 이전 run 의 `CancellationTokenSource` 를 취소한다. 반환 전에 취소한다.
2. `BatchesAsync` 는 배치를 내기 **직전마다** 자기 `Generation` 이 세션의 현재 `Generation` 과
   같은지 확인한다. 다르면 그 자리에서 열거를 끝낸다 — **예외를 던지지 않고 조용히 끝낸다.**
   이유: 낡은 run 의 소비자에게 예외를 주면 UI 가 오류 상태로 넘어간다. 낡은 것은 오류가 아니다.
3. 낡은 run 이 이미 받아둔 항목은 **버린다.** 절대 소비자에게 내지 않는다.
4. `LocationAccessException` 은 **현재 세대의 run 에서만** 전파한다. 낡은 run 에서 발생한 실패는
   삼킨다. 이유: 이미 떠난 폴더의 권한 오류를 새 폴더의 오류로 표시하면 안 된다.
5. `DisposeAsync` 는 진행 중인 run 을 취소하고 완료를 기다린다.

### 테스트가 반드시 덮어야 할 것

`FakeFolderSource.YieldDelayMilliseconds` 로 느린 열거를 만들고 경합을 재현한다.

- 항목 3개 폴더 → 배치가 `[1개, 2개]` 로 나온다 (첫 배치 1개 규칙)
- 항목 300개 폴더 → 배치가 `[1, 256, 43]` 로 나온다
- 항목 0개 폴더 → 배치가 하나도 나오지 않고 정상 종료된다
- 느린 열거 중에 다른 폴더로 `Start` → **첫 run 의 나머지 배치가 하나도 나오지 않는다**
- 낡은 run 은 예외 없이 끝난다 (`OperationCanceledException` 도 아니다)
- 낡은 run 의 `IsStale == true`, 현재 run 은 `false`
- 낡은 run 에서 주입된 `LocationAccessException` 이 소비자에게 전파되지 않는다
- 현재 run 에서 주입된 `LocationAccessException` 은 전파된다
- `FakeFolderSource.CancellationsObserved` 가 1 이상 — 이전 열거가 실제로 취소됐다
- `DisposeAsync` 후 진행 중 run 이 끝난다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 배치 경계가 `1, 256, ...` 인가? 시간 기반 로직이 없는가?
   - 낡은 run 이 예외 없이 조용히 끝나는가?
   - `FlexDir.Core` 가 여전히 순수 .NET 인가? (`check-structure.ps1`)
3. 결과에 따라 `phases/1-core-pipeline/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **시간 기반 배치 플러시를 넣지 마라** (`Task.Delay`·타이머·`Stopwatch`). 이유: 테스트가
  실행 속도에 의존하면 간헐적으로 실패하고, 자율 실행에서 그 원인을 찾는 비용이 가장 크다.
- **낡은 run 에서 예외를 던지지 마라.** 이유: 폴더를 빠르게 넘기는 것은 정상 조작이다.
  오류로 만들면 상태표시줄이 유령 오류로 덮인다.
- **낡은 run 의 항목을 "혹시 쓸모 있을까" 하고 캐시에 남기지 마라.** 이유: 진실원천은
  파일시스템이고(`CLAUDE.md` §4), 섞인 목록은 전작의 잔버그 원천이었다.
- **UI 나 `ObservableCollection` 을 이 클래스에 끌어들이지 마라.** 이유: `FlexDir.Core` 는
  WPF 를 모른다. 목록 반영은 ViewModel 의 일이다.
- **`lock` 으로 열거 전체를 감싸지 마라.** 이유: 열거는 초 단위로 걸릴 수 있고, 그 사이
  `Start` 가 막히면 폴더 전환이 멈춘다. 세대 번호는 `Interlocked` 로 다룬다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
