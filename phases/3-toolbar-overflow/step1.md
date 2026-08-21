# Step 1: known-folder-menu

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §3(UI 스레드에서 저장소 호출 금지) · §6(TDD 순서)
- `docs/ARCHITECTURE.md`
- `src/FlexDir.Core/Locations/IKnownFolderList.cs` — **step 0 이 만든 포트.**
  `KnownFolderKind`(Home · Desktop · Documents · Downloads · Pictures 다섯, 이 순서) ·
  `KnownFolder(Kind, Label, LocationId?)` · `ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken)`
- `tests/FlexDir.Core.Tests/Fakes/FakeKnownFolderList.cs` — step 0 이 만든 fake.
  낼 목록을 주입할 수 있고 호출 횟수를 센다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — 이 step 이 고치는 파일. 특히:
  - **86줄** `public sealed record GroupOption(string Label, SortKey? Key, bool IsSelected);`
    — 이 step 이 만들 레코드의 본보기
  - **609~618줄** `GroupOptions` 와 `BuildGroupOptions()` — 캐시하고 무효화하는 수
  - **327~343줄** 생성자. 마지막 매개변수가 `IDriveSpace? driveSpace = null` 이다 —
    **선택 주입의 본보기이고 이 step 이 그대로 따라 쓴다**
  - **655~662줄** `NavigateAsync(LocationId, CancellationToken)` — 히스토리에 기록하고 연다
  - **2120~2145줄 언저리** `driveSpace` 를 배경에서 부르는 자리 — 선택 포트를 언제
    어떻게 부르는가의 본보기
- `tests/FlexDir.App.Tests/ViewModels/PaneViewModelTests*.cs` (실제 파일명은 확인하라) —
  기존 페인 테스트가 `PaneViewModel` 을 어떻게 세우는지

## 배경

페인 툴바 오른쪽 끝에 **알려진 폴더 메뉴**(글리프 `E707` 핀)가 들어간다. 이 step 은 그
메뉴가 쓸 **ViewModel 쪽 전부**를 만든다. XAML 은 건드리지 않는다 (step 4 가 한다).

## 작업

### 1. 메뉴 항목 레코드

`src/FlexDir.App/ViewModels/PaneViewModel.cs` 안에 `GroupOption` **바로 옆**에 선언한다.
새 파일을 만들지 마라 — `GroupOption` 이 이미 그 파일에 있고, 같은 성격의 선언이다.

```csharp
public sealed record KnownFolderOption(string Label, LocationId Location);
```

`LocationId` 가 **nullable 이 아니다.** 경로가 없는 항목은 여기까지 오지 않기 때문이다
(아래 2번). 그것이 이 레코드가 존재하는 이유다 — `KnownFolder` 를 그대로 바인딩하면 View 가
`null` 검사를 해야 하고, WPF 바인딩은 런타임 조회라 그 검사가 조용히 실패한다
(`docs/PRD-v2.md` §17 §값을 치르고 배운 것).

### 2. 변환 — 이 step 의 핵심 판정

```csharp
internal static IReadOnlyList<KnownFolderOption> ToOptions(IReadOnlyList<KnownFolder> folders)
```

`TabStripPanel.Widths` 와 같은 수로 **판정만 `internal static` 으로 갈라 채점한다**
(`src/FlexDir.App/Views/TabStripPanel.cs` 의 주석에 그 이유가 있다).

규칙:

- `Location` 이 `null` 인 항목은 **뺀다.** 이 기계에 그 폴더가 없다는 뜻이고, 눌러도 갈 곳이
  없는 항목을 메뉴에 두면 `Command` 가 `null` 인 `MenuItem` 과 같은 자리가 된다 — 그것은
  **정상으로 뜨고 눌리기까지 한다** (`docs/PRD-v2.md` §17).
- **순서는 입력 순서를 그대로 지킨다.** 다시 정렬하지 마라. 순서의 정본은
  `KnownFolderKind` 의 선언 순서이고 그것이 화면 순서다.
- 입력이 비면 빈 목록을 낸다 (`null` 이 아니다).
- 라벨은 손대지 않는다 — 잘라내거나 접미사를 붙이지 마라.

### 3. 생성자 — 선택 주입

`PaneViewModel` 생성자 맨 뒤에 매개변수 하나를 **선택으로** 붙인다:

```csharp
        IDriveSpace? driveSpace = null,
        IKnownFolderList? knownFolders = null)
```

**반드시 선택(기본값 `null`)이어야 한다.** 이유: `driveSpace` 바로 위 주석이 적어 둔 것과
같다 — 필수로 만들면 기존 페인 테스트 수백 개를 전부 고쳐야 하고, 이 포트 하나 때문에 그
값을 치를 자리가 아니다. `ArgumentNullException.ThrowIfNull` 을 걸지 마라.

### 4. 노출과 적재

```csharp
/// <summary>알려진 폴더 메뉴에 올릴 항목. 포트를 주지 않았거나 아직 못 읽었으면 빈 목록이다.</summary>
public IReadOnlyList<KnownFolderOption> KnownFolderOptions { get; private set; } = [];
```

**언제 읽는가**: 폴더를 여는 배경 경로(`OpenAsync`)에서 **딱 한 번** 읽는다.
`driveSpace` 를 부르는 자리 바로 옆이다.

지켜야 할 것:

- **UI 스레드에서 부르지 마라** (CLAUDE.md §3). 리디렉션된 알려진 폴더(OneDrive · 도메인
  로밍 프로필)는 네트워크로 내려가 초 단위로 블로킹한다. `OpenAsync` 는 이미 배경이므로
  거기서 `await` 하면 된다.
- **한 번만 묻는다.** 이미 읽었으면 다시 묻지 않는다. 알려진 폴더는 프로세스가 사는 동안
  바뀌지 않고, 이 앱은 상주다(ADR-003) — 탐색할 때마다 물으면 폴더를 열 때마다 저장소
  조회가 한 번씩 더 붙는다. `FakeKnownFolderList` 의 호출 횟수로 채점한다.
- **실패해도 삼킨다.** 포트가 던지면(취소 제외) 빈 목록으로 두고 폴더 열기는 그대로
  진행한다 — 알려진 폴더를 못 읽는 것이 폴더를 못 여는 사건이 되면 안 된다.
- 값이 채워지면 `OnPropertyChanged(nameof(KnownFolderOptions))` 를 낸다. 이것이 없으면
  메뉴가 영원히 비어 있다 — **WPF 바인딩은 런타임 조회라 컴파일 에러 없이 조용히 죽는다.**

### 5. 명령

```csharp
[RelayCommand]
public Task OpenKnownFolderAsync(KnownFolderOption? option, CancellationToken ct = default)
```

- `option` 이 `null` 이면 아무것도 하지 않는다 (`Task.CompletedTask`).
- 아니면 `NavigateAsync(option.Location, ct)` 를 부른다.
  **`NavigateAsync(LocationId)` 여야 한다** — 그것이 히스토리에 기록하는 쪽이고
  (655~662줄), 알려진 폴더로 간 뒤 **뒤로가기가 원래 자리로 돌아와야 한다**.
  `OpenAsync` 를 직접 부르면 히스토리에 안 쌓인다.
- **현재 탭에서 이동한다.** 새 탭을 열지 마라 — 사용자 확정 사항이다.

### 6. 테스트

`tests/FlexDir.App.Tests/ViewModels/PaneKnownFoldersTests.cs` 를 새로 만든다.

> **파일명 주의**: TDD 훅은 `src/FlexDir.App/ViewModels/PaneViewModel.cs` 에 대해
> `tests/FlexDir.App.Tests/ViewModels/PaneViewModelTests.cs` 를 찾는다. 그 파일이 이미
> 있으면 훅은 만족한다. 새 파일을 따로 두는 것은 2,997줄짜리 테스트 파일을 더 키우지
> 않기 위해서다. **훅이 거부하면 기존 `PaneViewModelTests.cs` 에 테스트를 추가하는 쪽으로
> 돌려라** — 파일을 가르는 것보다 훅을 우회하는 것이 나쁘다.

채점할 것 (최소):

1. `ToOptions` — `Location` 이 `null` 인 항목이 빠진다
2. `ToOptions` — 남은 항목의 **순서가 입력 순서 그대로**다
3. `ToOptions` — 다섯 전부 `null` 이면 빈 목록이다
4. `ToOptions` — 다섯 전부 있으면 다섯이 그대로 나온다
5. 포트를 주지 않으면(`knownFolders: null`) `KnownFolderOptions` 가 빈 목록이고 예외가 없다
6. 폴더를 연 뒤 `KnownFolderOptions` 가 채워진다
7. **두 번 이상 탐색해도 포트 호출은 한 번이다** (`FakeKnownFolderList` 의 호출 횟수)
8. 포트가 던져도 폴더 열기가 성공하고 `KnownFolderOptions` 는 빈 목록이다
9. `OpenKnownFolderCommand` 가 그 위치로 이동시키고 **`CanGoBack` 이 참이 된다**
   (히스토리에 쌓였다는 증거)
10. `OpenKnownFolderCommand` 에 `null` 을 넘겨도 예외가 없고 위치가 안 바뀐다

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
   - `PaneViewModel` 생성자의 새 매개변수가 **선택(기본값 `null`)** 인가? 기존 페인
     테스트가 하나도 안 깨졌는가?
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1` 이 본다)
   - 포트를 UI 스레드에서 부르는 자리가 없는가?
   - `OnPropertyChanged(nameof(KnownFolderOptions))` 가 실제로 나가는가?
3. `phases/3-toolbar-overflow/index.json` 의 step 1 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 새 공개 멤버 이름(`KnownFolderOptions` · `OpenKnownFolderCommand`)과 테스트
파일 경로를 적어라 — step 4 가 XAML 에서 그 이름으로 바인딩한다.

## 금지사항

- **XAML 을 건드리지 마라** (`src/FlexDir.App/Views/MainWindow.xaml`). 이유: 배선은 step 4 다.
  여기서 바인딩을 걸면 step 2 가 만들 패널이 없는 상태로 툴바가 깨진다.
- **`FlexDir.Shell` 에 구현체를 만들지 마라.** 이유: step 3 이 만든다. 지금 만들면 step 3 이
  red 상태를 만들 수 없어 TDD 훅이 편집을 거부한다.
- **단축키를 만들지 마라.** 이유: 사용자 확정 사항이다 (ADR-017 이 분류에 내린 것과 같은
  판단 — 조작은 툴바 메뉴 하나다). `InputBindings` 나 `KeyGesture` 를 추가하지 마라.
- **선택 항목을 보지 마라.** 이유: 알려진 폴더 이동은 현재 탭의 위치만 바꾼다. 선택은
  갱신 중에도 유지되는 별개의 것이다 (CLAUDE.md §4).
- **`ToOptions` 에서 정렬하지 마라.** 이유: 순서의 정본은 `KnownFolderKind` 선언 순서이고,
  다시 정렬하면 정본이 둘이 된다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
