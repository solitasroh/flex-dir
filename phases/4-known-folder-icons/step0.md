# Step 0: folder-icon-port

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(Core 는 WPF·Windows API 를 모른다) · §3(UI 스레드) ·
  §6(TDD 순서)
- `src/FlexDir.Core/Presentation/IThumbnailSource.cs` — **이 step 이 고치는 파일.**
  지금 `ThumbnailBitmap(int Width, int Height, byte[] Pixels)`(BGRA32, stride `Width * 4`)
  과 메서드 둘이 있다:
  - `GetThumbnailAsync(LocationId item, int requestedSize, CancellationToken ct)`
  - `GetTypeIconAsync(string extension, bool isDirectory, int requestedSize, CancellationToken ct)`
  클래스 주석이 **왜 `ImageSource` 가 아닌지** · **왜 실패를 예외가 아니라 `null` 로 내는지**
  를 적어 두었다. 새 메서드도 같은 계약을 따른다
- `docs/SHELL_NOTES.md` §아이콘 — shell 아이콘 조회의 함정
- `tests/FlexDir.Core.Tests/Presentation/IThumbnailSourceTests.cs` (실제 파일명은 확인하라) —
  이 포트를 채점하는 자리
- `tests/FlexDir.Core.Tests/Fakes/FakeThumbnailSource.cs` — 이 step 이 넓히는 fake

## 배경

알려진 폴더 메뉴(홈 · 바탕화면 · 문서 · 다운로드 · 사진)의 각 항목에 **Windows 가 쓰는 실제
폴더 아이콘**을 붙인다 (사용자 요청 2026-08-21). 다운로드의 화살표, 사진의 그림처럼 알려진
폴더는 저마다 다른 shell 아이콘을 갖는다.

`docs/DESIGN.md` §7 의 원칙 그대로다 — *"파일·폴더 아이콘은 디자인 대상이 아니다.
Windows Shell 에서 온다."* 탭 아이콘만 글리프로 예외를 뒀는데 그 예외의 이유(탭은 8개까지
열리고 배경 탭에서도 저장소 호출이 나간다)가 **여기에는 해당하지 않는다** — 다섯 개 고정이고
프로세스당 한 번이다.

### 왜 기존 메서드 둘로는 안 되는가 — 이것이 이 step 이 존재하는 이유다

- **`GetThumbnailAsync` 는 안 된다.** 구현체가 `SIIGBF_THUMBNAILONLY` 를 주고 있어
  **실제 썸네일이 없으면 일부러 `null` 을 낸다** — 그 계약이 있어야 호출자가 형식 아이콘으로
  대체할지 정할 수 있기 때문이다. 폴더에는 썸네일이 없으므로 항상 `null` 이다.
- **`GetTypeIconAsync` 도 안 된다.** 확장자 + `SHGFI_USEFILEATTRIBUTES` 로 묻기 때문에
  **모든 폴더가 같은 일반 폴더 아이콘**을 받는다. 다운로드와 사진이 갈리지 않는다.

필요한 것은 **실제 경로를 보고 그 항목의 아이콘을 내는 것**이다. 그래서 메서드가 하나 는다.

## 작업

### 1. 메서드를 추가한다

`IThumbnailSource` 에:

```csharp
/// <summary>
/// 그 위치의 <b>아이콘</b>. 썸네일이 아니라 shell 이 그 항목에 붙여 둔 그림이다.
/// 없거나 실패하면 <c>null</c> 이다.
/// </summary>
ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct);
```

이름은 `GetItemIconAsync` 다 — 알려진 폴더 전용이 아니라 **경로 하나의 아이콘**이라는 것이
계약이므로 `KnownFolder` 라는 말을 넣지 마라. 이 포트는 알려진 폴더를 모른다.

### 2. 계약을 XML 주석에 못 박는다

반드시 담을 것 다섯:

1. **`GetThumbnailAsync` 와 다른 점** — 그쪽은 내용의 미리보기이고 없으면 `null` 이다.
   이쪽은 **아이콘**이라 거의 언제나 있다. 위 §배경의 두 문단을 여기에 적어라.
2. **`GetTypeIconAsync` 와 다른 점** — 그쪽은 확장자마다 하나이고 캐시가 잘 듣는다.
   이쪽은 **경로마다** 다를 수 있어(알려진 폴더 · 사용자 지정 아이콘 · `desktop.ini`)
   캐시 키가 경로다. **그래서 목록의 항목마다 부르면 안 된다** — 대용량 폴더에서
   확장자마다 한 번이 가장 큰 승리였던 것이 무너진다. 부르는 쪽은 **개수가 정해진 곳**이다.
3. **실패는 던지지 않는다.** `null` 이다. 아이콘이 없다고 폴더를 못 여는 사건이 되면 안 된다.
   취소만 예외로 나온다 — 이 포트의 다른 둘과 같다.
4. **UI 스레드에서 부르지 않는다** (CLAUDE.md §3). shell 조회는 동기 블로킹이고
   네트워크·클라우드 항목에서 초 단위로 멈춘다.
5. **크기는 요청일 뿐 보장이 아니다** — shell 이 가진 가장 가까운 크기가 온다.
   `requestedSize` 는 양수여야 한다 (다른 둘과 같은 검증).

### 3. Fake 를 넓힌다

`tests/FlexDir.Core.Tests/Fakes/FakeThumbnailSource.cs` 에 새 메서드를 구현한다.
**기존 사용처가 하나도 안 깨지게 하라** — 이 fake 는 페인 테스트 수백 개가 쓴다.

- 경로별로 낼 아이콘을 주입할 수 있어야 한다
- 주입하지 않은 경로는 `null` 을 낸다
- 취소 토큰을 관측한다
- 경로별 호출 횟수를 셀 수 있으면 좋다 — step 2 가 "한 번만 묻는가" 를 채점한다

### 4. 테스트

포트를 채점하는 기존 테스트 파일에 **추가**한다 (새 파일을 만들지 마라 — TDD 훅은 소스
`IThumbnailSource.cs` 에 대해 `IThumbnailSourceTests.cs` 를 찾는다).

채점할 것:

1. `FakeThumbnailSource` 가 주입한 경로에 그 아이콘을 낸다
2. 주입하지 않은 경로에는 `null` 을 낸다
3. 이미 취소된 토큰이면 `OperationCanceledException` 이 나온다
4. `requestedSize` 가 0 이하이면 `ArgumentOutOfRangeException`
   (구현체 계약이라 fake 도 같게 맞춘다 — 다른 둘이 그렇게 하고 있는지 먼저 보고 맞춰라)

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 이 step 전(2073)보다 커야 한다.

> **인터페이스에 메서드를 늘리면 모든 구현체가 깨진다.** `ShellThumbnailSource`(Shell)는
> step 1 이 채우므로, 이 step 에서는 **컴파일이 되게만** 하라 — 던지는 임시 구현이 아니라
> `NotSupportedException` 도 아니고, **`null` 을 내는 최소 구현**을 두고 step 1 이 그것을
> 실제 조회로 바꾼다. 이유: 던지는 구현을 두면 step 1 이 실패해도 앱이 죽는 형태가 되고,
> `null` 이면 "아이콘이 아직 없다" 라는 이 포트의 정상 상태로 접힌다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `FlexDir.Core` 에 WPF·Windows API 참조가 생기지 않았는가?
   - 기존 페인 테스트 수백 개가 하나도 안 깨졌는가? (fake 를 넓힌 것이지 바꾼 것이 아닌가)
   - 새 메서드 이름에 `KnownFolder` 라는 말이 안 들어갔는가?
3. `phases/4-known-folder-icons/index.json` 의 step 0 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 새 메서드의 정확한 시그니처와 `ShellThumbnailSource` 에 둔 임시 구현의 자리를
적어라 — step 1 이 그 자리를 채운다.

## 금지사항

- **`IKnownFolderList` 를 건드리지 마라.** 이유: 그 포트는 *"어디에 있나"* 를 답하고 이 포트는
  *"어떻게 생겼나"* 를 답한다. 아이콘을 `KnownFolder` 에 실으면 `KnownFolderList` 가 STA
  워커와 `IDisposable` 을 들여야 하고, `ShellThumbnailSource` 의 이미지 리스트·BGRA 변환
  코드가 두 벌이 된다.
- **`PaneViewModel` 이나 XAML 을 건드리지 마라.** 이유: step 2 와 step 3 이 한다.
- **`ShellThumbnailSource` 에 실제 조회를 넣지 마라.** 이유: step 1 이다. 지금 넣으면
  step 1 이 red 상태를 만들 수 없어 TDD 훅이 편집을 거부한다. 여기서는 `null` 최소 구현만.
- **`ImageSource`·`BitmapSource` 를 Core 에 들이지 마라.** 이유: CLAUDE.md §1.
- **`ThumbnailBitmap` 을 새로 만들지 마라.** 이유: 이미 있고 `ViewConverters.ToImage` 가
  그 타입만 안다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
