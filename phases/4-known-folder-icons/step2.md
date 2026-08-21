# Step 2: known-folder-icon-option

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(App 은 Shell 을 참조하지 않는다) · §3(UI 스레드) ·
  §6(TDD 순서)
- `src/FlexDir.Core/Presentation/IThumbnailSource.cs` — **step 0 이 늘린 포트.**
  `ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct)`
  — 그 위치의 shell 아이콘. 실패는 `null`, 취소만 예외.
  **주석이 "목록의 항목마다 부르면 안 된다 — 부르는 쪽은 개수가 정해진 곳이다" 라고
  적어 두었다.** 알려진 폴더 다섯이 그 "개수가 정해진 곳" 이다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — **이 step 이 고치는 파일.** 특히:
  - `public sealed record KnownFolderOption(string Label, LocationId Location);`
  - `internal static IReadOnlyList<KnownFolderOption> ToOptions(IReadOnlyList<KnownFolder>)`
    — `Location` 이 `null` 인 항목을 빼고 입력 순서를 지킨다
  - `KnownFolderOptions` 속성과 그것을 채우는 배경 경로 (폴더를 열 때 한 번, 이미 읽었으면
    다시 묻지 않는다)
  - 생성자에 이미 들어 있는 `IThumbnailSource thumbnailSource` — **새로 주입할 것이 없다**
- `tests/FlexDir.App.Tests/ViewModels/PaneKnownFoldersTests.cs` — 이 step 이 늘리는 테스트
- `tests/FlexDir.Core.Tests/Fakes/FakeThumbnailSource.cs` — step 0 이 넓힌 fake
  (경로별 아이콘 주입 · 경로별 호출 횟수)

## 배경

알려진 폴더 메뉴 항목에 Windows 의 실제 폴더 아이콘을 붙인다 (사용자 요청 2026-08-21).
step 0·1 이 포트와 구현을 만들었다. **이 step 은 다섯 폴더의 아이콘을 배경에서 읽어
메뉴 항목에 싣는다.** XAML 은 건드리지 않는다 (step 3 이 한다).

## 작업

### 1. `KnownFolderOption` 에 아이콘을 붙인다

```csharp
public sealed record KnownFolderOption(string Label, LocationId Location, ThumbnailBitmap? Icon = null);
```

**기본값 `null` 인 마지막 위치 매개변수**여야 한다. 이유: 기존 테스트가 두 인자로 만드는
자리가 있고 그것들이 그대로 살아야 한다.

### 2. `ToOptions` 는 그대로 둔다

지금 하는 일(경로 없는 항목 제외 · 입력 순서 유지 · 라벨 그대로)을 **바꾸지 마라.**
아이콘 없이 만든 `KnownFolderOption` 을 그대로 낸다 — 아이콘은 그 다음에 붙는다 (아래 3번).

이유: `ToOptions` 는 순수 변환이고 이미 테스트가 그것을 채점하고 있다. 여기에 비동기 조회를
섞으면 그 판정이 shell 을 요구하게 된다.

### 3. 아이콘을 배경에서 읽어 채운다

`KnownFolderOptions` 를 채우는 **바로 그 배경 경로**에서, 목록을 만든 직후에 이어서 한다.

```
경로 다섯을 얻는다 (IKnownFolderList)          ← 이미 있다
  → ToOptions 로 거른다                        ← 이미 있다
  → KnownFolderOptions 에 넣고 알린다           ← 이미 있다  (아이콘 없이 먼저 뜬다)
  → 남은 항목마다 GetItemIconAsync 를 부른다     ← 이 step
  → 아이콘을 실은 새 목록으로 교체하고 다시 알린다  ← 이 step
```

지켜야 할 것:

- **아이콘 없이 먼저 보여 준다.** 아이콘을 다 읽을 때까지 메뉴를 비워 두지 마라 —
  다섯 번의 shell 조회가 네트워크 리디렉션에서 초 단위로 걸릴 수 있고, 그동안 메뉴가
  비면 기능이 없는 것처럼 보인다.
- **UI 스레드에서 부르지 마라** (CLAUDE.md §3). 이미 배경인 자리에서 `await` 한다.
- **한 번만 읽는다.** 알려진 폴더의 아이콘은 프로세스가 사는 동안 사실상 바뀌지 않고
  이 앱은 상주다(ADR-003). `FakeThumbnailSource` 의 경로별 호출 횟수로 채점한다.
- **실패는 삼킨다.** 어느 하나가 `null` 이거나 던져도(취소 제외) 나머지는 그대로 싣고
  그 항목만 아이콘 없이 남는다. **아이콘이 없다고 항목을 빼지 마라** — 거르는 기준은
  여전히 경로 하나다.
- **목록을 통째로 새로 만들어 교체한다.** `record` 는 불변이므로 `with` 로 새 인스턴스를
  만들어 새 목록에 담고, `OnPropertyChanged(nameof(KnownFolderOptions))` 를 낸다.
  **인스턴스를 그대로 두고 속성만 바꾸면 View 가 다시 그릴 신호를 못 받는다** —
  `GroupOptions` 가 "기준이 바뀌면 통째로 새로 만든다" 고 적어 둔 것과 같은 자리다.
- **요청 크기**: `32` 를 쓴다. 메뉴 아이콘 슬롯은 16 DIP 지만 150% 배율에서는 24 장치
  픽셀이라 16 원본은 뭉갠다. 32 를 받아 View 가 줄이는 쪽이 선명하다.
  **크기를 매직 넘버로 흩뿌리지 말고 `private const int` 하나로 두고 이유를 주석에 적어라.**

### 4. 테스트

`tests/FlexDir.App.Tests/ViewModels/PaneKnownFoldersTests.cs` 에 **추가**한다.

채점할 것:

1. 폴더를 연 뒤 `KnownFolderOptions` 의 항목들이 주입한 아이콘을 들고 있다
2. **아이콘이 `null` 인 경로의 항목도 목록에 남는다** (거르는 기준은 경로다)
3. `IThumbnailSource` 가 던져도 **폴더 열기가 성공하고** 나머지 항목은 그대로다
4. **두 번 이상 탐색해도 경로마다 아이콘 조회는 한 번이다** (`FakeThumbnailSource` 의
   경로별 호출 횟수)
5. **경로가 없어 걸러진 항목에는 아이콘을 묻지 않는다** — 다섯 중 둘이 `null` 이면
   조회는 셋이다. 갈 곳 없는 자리에 shell 조회를 내보내지 않는다
6. 아이콘이 채워지면 `KnownFolderOptions` 에 대한 `PropertyChanged` 가 **한 번 더** 나온다
   (아이콘 없이 한 번, 채운 뒤 한 번)
7. `ToOptions` 의 기존 판정 넷이 그대로 통과한다 (아이콘을 섞지 않았다는 증거)

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 1 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가?
   - 아이콘 조회가 **UI 스레드 밖**인가?
   - **걸러진 항목에 조회를 안 내보내는가?** (5번 테스트)
   - `ToOptions` 가 여전히 순수 변환인가? (비동기가 섞이지 않았는가)
   - 기존 페인 테스트가 하나도 안 깨졌는가?
3. `phases/4-known-folder-icons/index.json` 의 step 2 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `KnownFolderOption` 의 새 시그니처와 요청 크기 상수 이름을 적어라 —
step 3 이 XAML 에서 `Icon` 으로 바인딩한다.

## 금지사항

- **XAML 을 건드리지 마라.** 이유: 배선은 step 3 이다.
- **`FlexDir.Shell` 을 건드리지 마라.** 이유: step 1 이 끝냈다.
- **`IKnownFolderList` 를 건드리지 마라.** 이유: 그 포트는 경로를 답한다. 아이콘은
  `IThumbnailSource` 가 답한다 — 이 구간이 그렇게 가른 이유가 step 0 지시서에 있다.
- **아이콘이 없는 항목을 걸러 내지 마라.** 이유: 아이콘은 곁다리다. 경로가 있으면 갈 수
  있고, 못 그린다고 갈 수 있는 자리를 없애면 기능이 사라진다.
- **아이콘을 다 읽을 때까지 메뉴를 비워 두지 마라.** 이유: 위 §3. 네트워크 리디렉션에서
  초 단위로 걸린다.
- **목록 항목마다 `GetItemIconAsync` 를 부르는 코드를 다른 곳에 만들지 마라.** 이유:
  포트 주석의 계약이다 — 그것은 경로마다 조회라 대용량 폴더에서 확장자마다 한 번이라는
  기존 승리를 무너뜨린다. 부르는 곳은 개수가 정해진 여기뿐이다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
