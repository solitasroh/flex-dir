# Step 1: shell-folder-icon

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §3(UI 스레드) · §6(TDD 순서)
- `docs/SHELL_NOTES.md` — **반드시 읽어라.** §아이콘 · §COM 아파트먼트 · §아이콘 함정
- `src/FlexDir.Core/Presentation/IThumbnailSource.cs` — **step 0 이 늘린 포트.**
  `ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct)`
  — 그 위치의 shell 아이콘, 실패는 `null`, 취소만 예외
- `src/FlexDir.Shell/Presentation/ShellThumbnailSource.cs` — **이 step 이 고치는 파일.**
  step 0 이 `null` 을 내는 최소 구현을 두었고, 이 step 이 그것을 실제 조회로 바꾼다.
  **이 파일 안에 필요한 것이 전부 이미 있다:**
  - `worker` — `StaWorkQueue`. `SHGetFileInfo` 는 STA 를 요구한다 (SHELL_NOTES §COM 아파트먼트)
  - `QueryTypeIcon` — `SHGetFileInfo` 를 `SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES` 로
    불러 **인덱스**를 받고 시스템 이미지 리스트에서 꺼내는 길. `SHGFI_ICON` 을 쓰지 않는
    이유가 주석에 있다 (16·32 밖에 못 낸다)
  - `ImageListFor(requestedSize)` · `FromImageList(list, index)` · `FromIcon` · `Compose` —
    HICON → BGRA32 변환 사슬
  - `Guarded(...)` — 실패를 `null` 로 접는 자리
  - 클래스 주석: **"네이티브 핸들이 이 파일 밖으로 나가지 않는다"**
- `src/FlexDir.Shell/Interop/ShellInfoGate.cs` — `SHGetFileInfo` 직렬화
- `tests/FlexDir.Shell.Tests/Presentation/ShellThumbnailSourceTests.cs` (실제 파일명은
  확인하라) — 이 step 이 늘리는 테스트

## 배경

알려진 폴더 메뉴에 Windows 의 실제 폴더 아이콘을 붙인다 (사용자 요청 2026-08-21).
다운로드의 화살표, 사진의 그림은 **실제 경로**를 보고 물어야 나온다.

## 작업

### 1. `GetItemIconAsync` 를 실제 조회로 바꾼다

step 0 이 둔 `null` 최소 구현을 대체한다. `GetTypeIconAsync` 와 **같은 모양**으로 쓴다:

```csharp
public ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(item);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
    ct.ThrowIfCancellationRequested();

    return new ValueTask<ThumbnailBitmap?>(
        worker.RunAsync(() => Guarded(() => itemIcon(item, requestedSize)), ct));
}
```

**`worker.RunAsync` 를 반드시 거쳐라.** 이유: `SHGetFileInfo` 는 STA 를 요구하고
(SHELL_NOTES §COM 아파트먼트), shell 조회는 동기 블로킹이라 UI 스레드에 두면 안 된다
(CLAUDE.md §3). 스레드풀도 안 된다 — STA 가 아니다.

생성자에 주입 가능한 델리게이트(`Func<LocationId, int, ThumbnailBitmap?>`)로 두어라 —
`typeIcon`·`thumbnail` 이 이미 그 형태이고, 테스트가 실물 shell 없이 사슬을 채점하는 길이다.

### 2. 조회 본체

`QueryTypeIcon` 을 본보기로 삼되 **두 가지가 다르다**:

| | `QueryTypeIcon` (기존) | 이 step |
|---|---|---|
| 무엇을 주나 | 센티널 이름(`"x.txt"`) | **실제 경로** (`LocationId` 의 파일시스템 경로) |
| 플래그 | `SHGFI_SYSICONINDEX \| SHGFI_USEFILEATTRIBUTES` | **`SHGFI_SYSICONINDEX` 만** |

- **`SHGFI_USEFILEATTRIBUTES` 를 주지 마라.** 이유: 그 플래그가 있으면 shell 이 실제 항목을
  보지 않고 확장자 연결 정보만 본다 — 그래서 모든 폴더가 같은 아이콘이 된다. **그것을
  피하는 것이 이 step 의 전부다.** 다운로드·사진의 특화 아이콘은 shell 이 실제 경로를
  봐야 나온다.
- 그 대가를 알고 있어라: **실제 항목을 보므로 네트워크·클라우드 경로에서 느려질 수 있다.**
  그래서 STA 워커 뒤에 있고 호출자는 개수가 정해진 곳이어야 한다 (포트 주석).
  **`SHGFI_ICON` 을 쓰지 마라** — 16·32 밖에 못 낸다는 것이 기존 주석의 판단이다.
- 인덱스를 받은 뒤는 기존 사슬을 **그대로 재사용한다**: `ImageListFor(requestedSize)` →
  `FromImageList(list, index)`. 새로 쓰지 마라.
- `SHGetFileInfo` 호출은 `ShellInfoGate.Query` 로 감싼다 — 기존 자리가 어떻게 하는지 보고
  같게 하라.

### 3. 실패와 핸들

- `SHGetFileInfo` 가 0 을 내면 `null` 이다. **던지지 마라.**
- **네이티브 핸들이 이 파일 밖으로 나가지 않는다** (클래스 주석). `HICON` 을 얻었으면
  모든 경로에서 `DestroyIcon` 한다 — 예외가 나도. 기존 `FromImageList` 가 `try/finally` 로
  그렇게 하고 있으니 그것을 쓰면 저절로 지켜진다.
- `Guarded(...)` 로 감싸 실패를 `null` 로 접는다.

### 4. 테스트

`tests/FlexDir.Shell.Tests/Presentation/` 의 기존 `ShellThumbnailSource` 테스트 파일에
**추가**한다 (새 파일을 만들지 마라 — TDD 훅이 소스 이름으로 찾는다).

채점할 것:

1. **주입한 델리게이트가 불린다** — `LocationId` 와 `requestedSize` 가 그대로 넘어간다
2. 델리게이트가 `null` 을 내면 `null` 이 나온다
3. 델리게이트가 던져도 **`null` 이 나오고 예외가 새지 않는다** (`Guarded`)
4. 이미 취소된 토큰이면 `OperationCanceledException`
5. `requestedSize` 가 0 이하이면 `ArgumentOutOfRangeException`
6. **실물 조회 하나** — 이 기계에 반드시 있는 경로(예: `Environment.GetFolderPath(
   Environment.SpecialFolder.UserProfile)`)로 불러 **`null` 이 아닌 `ThumbnailBitmap`** 이
   오고 `Width`·`Height` 가 양수이며 `Pixels.Length == Width * Height * 4` 인지 본다.
   **특정 픽셀 값을 단정하지 마라** — 테마·Windows 버전·DPI 에 따라 그림이 다르다.
7. **다른 두 알려진 폴더의 아이콘이 서로 다르다** — 예를 들어 다운로드와 사진.
   `Pixels` 가 바이트로 같지 않은지 본다. **이것이 이 step 의 진짜 판정이다**:
   `SHGFI_USEFILEATTRIBUTES` 를 실수로 남기면 둘이 같아지고 이 테스트만 잡아낸다.
   두 폴더 중 하나라도 이 기계에 없으면 `Skip` 이 아니라 **그 경우 단정을 건너뛰되
   테스트는 통과**시켜라 (다른 기계에서 게이트가 깨지면 안 된다).

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 0 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - **`SHGFI_USEFILEATTRIBUTES` 를 안 줬는가?** (주면 모든 폴더가 같은 아이콘이 된다 —
     위 §4 의 7번 테스트가 그것을 본다)
   - `worker.RunAsync` 를 거치는가? (STA)
   - `HICON` 을 모든 경로에서 해제하는가?
   - 실물 픽셀 값을 단정한 테스트가 없는가? (다른 기계·테마에서 깨진다)
3. `phases/4-known-folder-icons/index.json` 의 step 1 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 쓴 플래그 조합과, 다운로드/사진 아이콘이 실제로 갈렸는지를 적어라.

## 금지사항

- **`SHGFI_USEFILEATTRIBUTES` 를 주지 마라.** 이유: 위 §2. 그러면 이 step 이 만든 것이
  기존 `GetTypeIconAsync` 와 같아져 아무것도 달라지지 않는다.
- **`SHGFI_ICON` 을 쓰지 마라.** 이유: 16·32 밖에 못 낸다 (기존 주석의 판단).
  시스템 이미지 리스트를 거쳐야 요청 크기를 존중한다.
- **HICON·HBITMAP·DC 를 이 파일 밖으로 내보내지 마라.** 이유: 클래스 주석의 계약이다.
  밖으로 나가면 누가 해제할지가 흐려지고 GDI 핸들이 샌다.
- **`IKnownFolderList` 나 `KnownFolderList` 를 건드리지 마라.** 이유: 그 포트는 경로를
  답하고 이 포트는 그림을 답한다.
- **`PaneViewModel` 이나 XAML 을 건드리지 마라.** 이유: step 2·3 이 한다.
- **BGRA 변환 코드를 새로 쓰지 마라.** 이유: `FromImageList`→`FromIcon`→`Compose` 사슬이
  이미 있고, 레거시 아이콘(32bpp 알파 없음) 처리까지 들어 있다. 두 벌이 되면 어긋난다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
