# Step 0: pane-history

페인 하나의 뒤로/앞으로/상위 히스토리다. 순수 로직이며 WPF 를 쓰지 않는다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — phase 0 산출물. 히스토리 항목 타입
- `docs/ARCHITECTURE.md` — §4 상태 표(페인별 현재 경로·히스토리 → `PaneViewModel`, 수명은 프로세스)
- `docs/PRD.md` — §2 탐색 (주소 입력 · 더블클릭 진입 · 상위로 · 뒤로/앞으로, **페인별 히스토리**)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

이 저장소에는 TDD Guard 훅이 걸려 있다. 대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.
`FlexDir.App` 의 테스트는 `tests/FlexDir.App.Tests/` 아래 같은 하위 경로에 둔다.

1. 먼저 `tests/FlexDir.App.Tests/Navigation/PaneHistoryTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.App/Navigation/PaneHistory.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

`src/FlexDir.App/Navigation/PaneHistory.cs`

```csharp
namespace FlexDir.App.Navigation;

using FlexDir.Core.Locations;

/// 한 페인의 방문 기록. 브라우저 히스토리와 같은 규칙이다.
public sealed class PaneHistory
{
    public const int MaxEntries = 100;

    /// 아직 아무 곳도 방문하지 않았으면 null.
    public LocationId? Current { get; }

    public bool CanGoBack { get; }
    public bool CanGoForward { get; }

    /// 새 위치로 이동한다. 앞으로 기록은 버려진다.
    public void Navigate(LocationId location);

    /// 이동 후의 위치. 갈 수 없으면 null 을 내고 상태를 바꾸지 않는다.
    public LocationId? GoBack();
    public LocationId? GoForward();
}
```

규칙:

1. `Navigate` 는 **앞으로 기록을 버린다.** 뒤로 간 뒤 다른 곳으로 가면 앞으로 갈 곳이 없다.
2. **현재 위치와 같은 곳으로 `Navigate` 하면 아무 일도 일어나지 않는다.**
   히스토리에 중복이 쌓이지 않고, 앞으로 기록도 버려지지 않는다.
   이유: 새로 고침이나 같은 폴더 재선택으로 앞으로 기록을 잃으면 사용자가 놀란다.
   비교는 `LocationId` 동등성(`OrdinalIgnoreCase`)을 쓴다.
3. 기록이 `MaxEntries` 를 넘으면 **가장 오래된 것부터** 버린다. 그만큼 `CanGoBack` 이 먼저 막힌다.
4. `GoBack`/`GoForward` 는 갈 수 없을 때 **예외를 던지지 않는다.** `null` 을 낸다.
   이유: 커맨드의 `CanExecute` 와 실제 실행 사이에 경합이 있을 수 있고, 정상 조작이 예외가 되면
   ViewModel 이 오류 상태로 넘어간다.
5. "상위로" 는 별도 개념이 아니다. 부모 위치로 `Navigate` 하는 것이며 기록에 남는다.
   부모 계산은 `LocationId.TryGetParent` 가 한다 — 이 클래스는 부모를 계산하지 않는다.

### 테스트가 반드시 덮어야 할 것

- 초기 상태: `Current == null`, `CanGoBack == false`, `CanGoForward == false`
- A → B → 뒤로 → `Current == A`, `CanGoForward == true`
- A → B → 뒤로 → C → `CanGoForward == false` (앞으로 기록이 버려졌다)
- 같은 위치로 두 번 `Navigate` → 뒤로 가면 그 이전 위치로 간다 (중복이 쌓이지 않는다)
- 뒤로 간 뒤 **현재와 같은 위치**로 `Navigate` → 앞으로 기록이 살아 있다
- 대소문자만 다른 경로는 같은 위치로 취급된다
- `MaxEntries + 10` 번 이동 후 뒤로를 계속 눌러도 예외 없이 멈춘다
- 갈 수 없을 때 `GoBack`/`GoForward` 가 `null` 을 내고 `Current` 를 바꾸지 않는다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `PaneHistory` 가 WPF 타입(`System.Windows.*`)을 쓰지 않는가?
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가? (`check-structure.ps1` 이 검사한다)
   - 파일 위치가 `src/FlexDir.App/Navigation/` 인가?
3. 결과에 따라 `phases/2-viewmodel/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **여기서 열거를 시작하지 마라.** 이유: 히스토리는 위치만 기억한다. 열거는 `PaneViewModel` 의
  일이며 다음 step 이다. 섞으면 히스토리 테스트가 I/O 에 묶인다.
- **`INotifyPropertyChanged` 를 구현하지 마라.** 이유: 이 클래스는 `PaneViewModel` 이 소유하는
  내부 상태다. View 는 이것을 직접 바인딩하지 않고 ViewModel 의 `CanGoBack` 등을 본다.
- **갈 수 없을 때 예외를 던지지 마라.** 이유: 위 규칙 4.
- **부모 경로를 문자열로 계산하지 마라.** 이유: `LocationId.TryGetParent` 가 이미 정규화 규칙
  (`\\?\` 접두사·루트 처리)을 담고 있다. 두 곳에서 계산하면 어긋난다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
