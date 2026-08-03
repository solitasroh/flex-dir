# Step 6: thumbnail-requests

썸네일을 요청하는 정책을 만든다. 형식 아이콘을 먼저 보이고, 스크롤 밖은 취소하고,
**클라우드 자리표시자는 건드리지 않는다.**

## 읽어야 할 파일

- `src/FlexDir.App/ViewModels/FileItemViewModel.cs` — 이전 step 산출물. 여기에 이미지 속성을 붙인다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이전 step 산출물
- `src/FlexDir.App/Threading/IUiDispatcher.cs` — 이전 step 산출물
- `src/FlexDir.Core/Model/FileItem.cs` — phase 0 산출물.
  **`IsContentAccessRisky` 가 이 step 의 핵심 분기다**
- `docs/SHELL_NOTES.md` — **§아이콘** (확장자마다 한 번 · 동기 블로킹 · 폴백 체인),
  **§열거 함정 3** (클라우드 자리표시자를 건드리면 다운로드가 트리거된다)
- `docs/UI_GUIDE.md` — §상태 표현 (썸네일 로딩 중: 형식 아이콘을 먼저, 자리를 비워두지 않는다),
  §금지 목록 (자체 아이콘·썸네일 캐시 구축 금지)
- `docs/PRD.md` — §2 썸네일 (shell 캐시 재사용 · 비동기 · 스크롤 중 취소), §4 (실패 시 재시도 없음)
- `docs/DESIGN.md` — §2 뷰 모드별 아이콘 크기 (16 / 16 / 32 / 96)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

> **테스트 파일 이름·위치 규칙.** 소스가 `Foo.cs` 면 테스트 파일명은 정확히 `FooTests.cs` 이고,
> **소스가 속한 프로젝트의 `.Tests`** 에 둔다. `IThumbnailSource` 는 `FlexDir.Core` 의 파일이므로
> 테스트와 fake 는 `FlexDir.Core.Tests` 에 있어야 한다.

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/Fakes/FakeThumbnailSource.cs` (fake)
   - `tests/FlexDir.Core.Tests/Presentation/IThumbnailSourceTests.cs`
     — `ThumbnailBitmap` 의 픽셀 길이 검증과 fake 의 계약
   - `tests/FlexDir.App.Tests/ViewModels/ThumbnailRequestSchedulerTests.cs`
2. 그 다음 소스를 쓴다:
   - `src/FlexDir.Core/Presentation/IThumbnailSource.cs`
   - `src/FlexDir.App/ViewModels/ThumbnailRequestScheduler.cs`
3. `FileItemViewModel` 에 속성 추가는 기존 소스 수정이므로 훅이 막지 않는다.
4. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Presentation/IThumbnailSource.cs`

`FlexDir.Core` 는 WPF 를 모르므로 `ImageSource` 를 쓸 수 없다. **BGRA32 픽셀로 넘긴다.**

```csharp
namespace FlexDir.Core.Presentation;

using FlexDir.Core.Locations;

/// 위에서 아래로 채워진 BGRA32 픽셀. Stride 는 Width * 4 다.
/// Pixels 길이는 Width * Height * 4 여야 하며, 아니면 생성 시 ArgumentException.
public sealed record ThumbnailBitmap(int Width, int Height, byte[] Pixels);

public interface IThumbnailSource
{
    /// 항목의 실제 썸네일. 없거나 실패하면 null (예외가 아니다).
    ValueTask<ThumbnailBitmap?> GetThumbnailAsync(LocationId item, int requestedSize, CancellationToken ct);

    /// 확장자에 대응하는 형식 아이콘. extension 은 점 없는 소문자, 빈 문자열은 확장자 없음.
    ValueTask<ThumbnailBitmap?> GetTypeIconAsync(string extension, bool isDirectory, int requestedSize, CancellationToken ct);
}
```

> **왜 인코딩된 스트림이 아닌가**: shell 은 `HBITMAP` 을 준다. PNG 로 인코딩해 넘기면
> App 이 다시 디코딩해야 하고 96×96 이미지에 왕복 비용을 두 번 낸다. BGRA 는 한 번 복사하면
> `WriteableBitmap` 에 그대로 들어간다. 대가는 항목당 약 36KB(96×96×4)이므로
> **보이는 범위 밖의 썸네일은 반드시 해제해야 한다** (아래 규칙 6).
>
> `GetTypeIconAsync` 와 `ITypeNameProvider.GetTypeNameAsync` 는 실제 구현에서 같은
> `SHGetFileInfoW` 호출로 함께 얻을 수 있다. 그 최적화는 수동 Shell phase 의 일이다.

### 2. `src/FlexDir.App/ViewModels/ThumbnailRequestScheduler.cs`

```csharp
namespace FlexDir.App.ViewModels;

using FlexDir.App.Threading;

public sealed class ThumbnailRequestScheduler : IAsyncDisposable
{
    public const int MaxConcurrentRequests = 4;

    public ThumbnailRequestScheduler(IThumbnailSource source, IUiDispatcher dispatcher);

    /// 화면에 보이는 항목이 바뀔 때마다 부른다.
    /// 범위에 없어진 항목의 진행 중 요청은 취소되고, 썸네일은 해제된다.
    void SetVisibleRange(IReadOnlyList<FileItemViewModel> visible, int requestedSize);

    /// 폴더를 옮길 때 부른다. 진행 중 요청을 모두 취소한다.
    void Reset();

    public ValueTask DisposeAsync();
}
```

`FileItemViewModel` 에 추가하는 속성:

```csharp
public ThumbnailBitmap? Icon { get; internal set; }        // 형식 아이콘 (자리표시자 역할)
public ThumbnailBitmap? Thumbnail { get; internal set; }   // 실제 썸네일. null 이면 Icon 을 쓴다
public bool ThumbnailAttempted { get; internal set; }      // 실패도 시도로 센다
```

### 동작 규칙

1. **형식 아이콘을 먼저 채운다.** 보이는 항목이 정해지면 그 항목들의 확장자 아이콘을 먼저
   요청해 `Icon` 에 넣는다. **자리를 비워두지 않는다** (`docs/UI_GUIDE.md` §상태 표현).
2. **형식 아이콘은 확장자마다 한 번만 요청한다.** 스케줄러 안에 확장자 → 아이콘 사전을 둔다.
   같은 확장자 파일이 1000개여도 요청은 1회다 (`docs/SHELL_NOTES.md` §아이콘 —
   "이게 대용량 폴더에서 가장 큰 승리였다"). 디렉터리는 별도 키 하나로 취급한다.
3. **`item.Item.IsContentAccessRisky` 가 `true` 면 `GetThumbnailAsync` 를 부르지 않는다.**
   형식 아이콘만 쓰고 `ThumbnailAttempted` 를 `true` 로 둔다.
   이유: OneDrive 미다운로드 파일의 내용을 건드리면 **다운로드가 트리거된다**
   (`docs/SHELL_NOTES.md` §열거 함정 3). 파일 목록을 스크롤한 것만으로 수 GB 를 내려받게 된다.
4. **디렉터리는 썸네일을 요청하지 않는다.** 형식 아이콘만 쓴다.
5. **실패하면 재시도하지 않는다.** `null` 을 받거나 예외가 나면 `ThumbnailAttempted = true` 로
   두고 다시 묻지 않는다 (`docs/PRD.md` §4). 같은 항목이 다시 보이게 되어도 재요청하지 않는다.
6. **보이는 범위를 벗어난 항목의 `Thumbnail` 을 `null` 로 되돌린다.** `Icon` 은 남긴다.
   이유: BGRA 버퍼가 항목당 36KB 다. 10만 항목 폴더를 훑으면 유지할 수 없다.
   `ThumbnailAttempted` 는 그대로 유지해 재요청을 막는다.
7. **동시 요청은 `MaxConcurrentRequests` 개로 제한한다.** shell 호출은 동기 블로킹이고
   네트워크·클라우드 항목에서 초 단위로 멈춘다(`docs/SHELL_NOTES.md` §아이콘 함정 1).
   제한이 없으면 워커가 전부 막힌다.
8. **범위를 벗어난 항목의 진행 중 요청은 취소한다** (`docs/PRD.md` §2 — 스크롤 중 취소).
9. 속성 대입은 전부 `IUiDispatcher.InvokeAsync` 안에서 한다.
10. `Reset` 은 진행 중 요청을 모두 취소하되 **확장자 아이콘 사전은 비우지 않는다.**
    확장자 아이콘은 폴더와 무관하다.

### 테스트가 반드시 덮어야 할 것

`FakeThumbnailSource` 는 호출 기록(항목별·확장자별), 지연, `null` 반환, 예외를 주입할 수 있어야 한다.

- 보이는 항목의 `Icon` 이 채워진다
- 같은 확장자 항목 10개 → `GetTypeIconAsync` 호출이 **1회**
- 디렉터리 10개 → 디렉터리 아이콘 요청이 **1회**, `GetThumbnailAsync` 는 **0회**
- **`IsContentAccessRisky` 항목에 `GetThumbnailAsync` 가 호출되지 않는다** ← 핵심
- 그 항목의 `Icon` 은 채워지고 `ThumbnailAttempted == true`
- 일반 파일은 `GetThumbnailAsync` 가 호출되고 `Thumbnail` 이 채워진다
- `null` 을 받으면 `ThumbnailAttempted == true` 이고 `Thumbnail == null`
- 예외가 나도 스케줄러가 죽지 않고 `ThumbnailAttempted == true`
- 실패한 항목이 다시 보이게 되어도 **재요청하지 않는다**
- 범위를 벗어나면 `Thumbnail` 이 `null` 로 돌아가고 `Icon` 은 남는다
- 범위를 벗어난 항목의 진행 중 요청이 취소된다 (fake 가 취소를 관측한다)
- 동시 요청이 `MaxConcurrentRequests` 를 넘지 않는다 (fake 가 최대 동시 수를 기록한다)
- `Reset` 후 진행 중 요청이 취소되지만 확장자 아이콘 사전은 유지된다
  (`Reset` 후 같은 확장자를 다시 보여도 `GetTypeIconAsync` 가 다시 불리지 않는다)
- `ThumbnailBitmap` 의 `Pixels` 길이가 맞지 않으면 `ArgumentException`

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 클라우드 자리표시자에 썸네일 요청이 가지 않는 테스트가 실제로 있는가?
   - 확장자당 1회 요청이 지켜지는가?
   - 범위를 벗어난 썸네일이 해제되는가?
   - `FlexDir.Core` 에 WPF 타입이 들어가지 않았는가? (`check-structure.ps1`)
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **클라우드 자리표시자·오프라인 항목에 썸네일을 요청하지 마라.** 이유: 내용 접근이 다운로드를
  트리거한다(`docs/SHELL_NOTES.md` §열거 함정 3). 목록을 스크롤한 것만으로 수 GB 를 내려받는다.
- **자체 썸네일 캐시를 디스크에 만들지 마라.** 이유: `docs/UI_GUIDE.md` §금지 목록 — shell 캐시를
  쓴다. 중복 캐시는 어긋나고, 어긋난 썸네일은 원인 추적이 어렵다.
- **실패한 썸네일을 재시도하지 마라.** 이유: `docs/PRD.md` §4. 실패하는 항목은 계속 실패하고,
  스크롤할 때마다 다시 시도하면 워커가 그것으로 막힌다.
- **`ImageSource`·`BitmapSource`·`WriteableBitmap` 을 `FlexDir.Core` 에 넣지 마라.** 이유:
  `CLAUDE.md` §1 — Core 는 WPF 를 참조하지 않는다. 변환은 View 계층의 일이다.
- **동시 요청 제한을 없애지 마라.** 이유: shell 호출은 동기 블로킹이고 항목마다 초 단위로
  멈출 수 있다(`docs/SHELL_NOTES.md` §아이콘 함정 1).
- **보이는 범위 밖의 썸네일을 계속 들고 있지 마라.** 이유: 항목당 36KB. 대용량 폴더에서
  메모리가 선형으로 늘어난다.
- **파일마다 형식 아이콘을 조회하지 마라.** 이유: 확장자마다 한 번이 전작 성능의 핵심이었다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
