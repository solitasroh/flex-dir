# Step 2: toolbar-overflow-panel

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §2(코드비하인드 금지) · §6(TDD 순서)
- `docs/DESIGN.md` §1(레이아웃 · 툴바 36 · 버튼 32×28 · 아이콘 16) · §7(아이콘)
- `src/FlexDir.App/Views/TabStripPanel.cs` — **이 step 이 그대로 따라 쓰는 선례다.**
  `Panel` 을 직접 상속하고, `MeasureOverride`/`ArrangeOverride` 를 쓰고,
  **판정만 `internal static` 으로 갈라 채점한다** (`Widths` · `Reveal`). 파일 머리의
  클래스 주석이 "판정 둘만 채점한다 — 나머지는 그 둘을 부르는 배관이다" 라고 적어 둔
  그 수를 따른다
- `tests/FlexDir.App.Tests/Views/TabStripPanelTests.cs` — 그 판정을 어떻게 채점했는지
- `src/FlexDir.App/Views/ViewConverters.cs` 와
  `tests/FlexDir.App.Tests/Views/ViewConvertersTests.cs` — 이 step 이 컨버터 하나를
  **추가**하는 자리
- `src/FlexDir.App/Views/MainWindow.xaml` **1179~1227줄** — 이 패널이 대체할 툴바
  `StackPanel`. 지금 무엇이 어떤 순서로 들어 있는지 확인하라. **이 step 에서 고치지는 않는다**

## 배경 — 무엇을 만드는가

페인은 1~4분할이고(ADR-019) 최소 폭이 좁다. 좁은 페인에서 툴바 버튼이 잘려 나가는 것을
막기 위해, 폭이 모자라면 **덜 중요한 버튼 무리를 접고 `»` 메뉴로 보낸다.**

확정된 규칙 (사용자 확정 2026-08-21):

| | |
|---|---|
| **절대 안 접힘** | 뒤로 `E72B` · 앞으로 `E72A` · 상위 `E74A` · 새로 고침 `E72C` · **알려진 폴더 `E707`** |
| **접힘 순서** | ① 외부 도구(2구간에 들어온다 — 지금은 없다) → ② 분류 `E8EC` → ③ 뷰 모드 4종 |

## 이 step 의 핵심 설계 — 반드시 지켜라

### 살아 있는 `UIElement` 를 `»` 메뉴로 옮기지 마라

WPF 의 논리 트리 부모는 하나뿐이다. `Children.Remove(button)` 뒤
`menuItem.Items.Add(button)` 을 하면 스타일·`DynamicResource`·바인딩이 전부 재평가되고,
`ToolButton` 크롬을 쓴 `Button` 이 `MenuItem` 안에서 깨진다. 그리고 되돌릴 때 순서가
보장되지 않는다.

**패널은 접기만 한다.** `»` 메뉴는 step 4 가 XAML 에 **따로 선언**하고, 접힌 무리에 해당하는
서브메뉴만 보이게 한다. 두 벌이 생기지만 `Command` 가 같은 ViewModel 을 보므로 진실원천은
하나다.

### 접기는 버튼 하나가 아니라 무리 단위다

뷰 모드 4개가 하나씩 사라지면 남은 2개가 무슨 뜻인지 알 수 없고, 앞의 구분자
`Rectangle` 이 고아로 남는다. **같은 `FoldOrder` 를 가진 자식은 한 덩어리로 접힌다.**

### 자연 폭을 캐시하지 않으면 진동한다

`Visibility.Collapsed` 인 자식은 `Measure` 후 `DesiredSize` 가 `(0,0)` 이다. 다음 측정
패스에서 그 0 을 자연 폭으로 읽으면 "자리가 남네" 로 판단해 펼치고, 펼치면 다시 모자라
접고 — **무한 진동한다.** 자연 폭은 **접기 전에** 재서 캐시한다 (아래 4번).

## 작업

### 1. 첨부 속성 둘

`src/FlexDir.App/Views/ToolbarOverflowPanel.cs` 를 새로 만든다.
`src/FlexDir.App/Views/` 는 **이미 존재한다.**

```csharp
/// 접히는 순번. 0(기본) 은 절대 안 접힌다. 1 부터는 작은 번호가 먼저 접힌다.
/// 같은 번호는 한 덩어리로 함께 접힌다 — 구분자 Rectangle 도 앞 무리와 같은 번호를 준다.
public static int GetFoldOrder(DependencyObject element)
public static void SetFoldOrder(DependencyObject element, int value)

/// 이 자식이 » 머리인가. 접힌 것이 있을 때만 보이고, 그 폭만큼 예산에서 뺀다.
public static bool GetIsOverflowHead(DependencyObject element)
public static void SetIsOverflowHead(DependencyObject element, bool value)

/// 오른쪽 끝에 붙는가. 알려진 폴더 📍 와 » 머리가 이것을 켠다.
/// 켠 자식끼리는 선언 순서대로 왼쪽에서 오른쪽으로 놓인다.
public static bool GetAlignRight(DependencyObject element)
public static void SetAlignRight(DependencyObject element, bool value)
```

셋 다 `FrameworkPropertyMetadataOptions.AffectsParentMeasure` 를 준다.

> **`DockPanel` 로 오른쪽 정렬을 풀지 마라.** `DockPanel` 은 `Dock=Right` 자식을 **먼저**
> 재고 남은 폭을 가운데 자식에게 준다. 그런데 `»` 가 보일지는 패널이 접기를 판정한 **뒤에야**
> 정해지므로, 예산이 한 프레임 늦게 반영되고 넓힐 때와 좁힐 때가 서로 다른 지점에서
> 뒤집힌다 — 진동한다. **오른쪽 정렬까지 이 패널이 쥔다.**

### 2. 채점할 판정 — `internal static`

```csharp
/// <param name="foldOrders">자식마다의 FoldOrder. » 머리는 포함하지 않는다.</param>
/// <param name="widths">자식마다의 자연 폭. foldOrders 와 같은 길이·같은 순서.</param>
/// <param name="available">줄에 주어진 폭. 무한이면 아무것도 접지 않는다.</param>
/// <param name="overflowWidth">» 머리의 폭. 하나라도 접히면 이만큼이 예산에서 빠진다.</param>
/// <returns>자식마다 접혔는가. 길이는 foldOrders 와 같다.</returns>
internal static bool[] Folded(
    IReadOnlyList<int> foldOrders,
    IReadOnlyList<double> widths,
    double available,
    double overflowWidth)
```

알고리즘:

1. `foldOrders` 와 `widths` 의 길이가 다르면 `ArgumentException`.
2. 전부 더한 값이 `available` 이하이거나 `available` 이 무한이면 → 전부 `false`.
3. 아니면 `FoldOrder > 0` 인 값들을 **오름차순 distinct** 로 훑으며 한 무리씩 접는다.
   접기 시작한 뒤의 예산은 `available - overflowWidth` 다.
4. 남은 폭이 예산 이하가 되면 멈춘다. 접을 무리가 떨어지면 **더 못 접고 그대로 넘친다** —
   `FoldOrder == 0` 인 것은 어떤 경우에도 접지 않는다.

`TabStripPanel.Widths` 가 "최소에서 멈추고 넘치게 둔다" 를 고른 것과 같은 판단이다.

### 3. 접힌 무리 노출

```csharp
/// 지금 접혀 있는 FoldOrder 들. » 안의 서브메뉴가 이것을 보고 자기를 보일지 정한다.
public IReadOnlyList<int> FoldedOrders { get; }   // DependencyProperty. 기본 []
```

**의존 속성이어야 한다** — step 4 가 `ElementName` 으로 바인딩한다. 일반 CLR 속성이면
`INotifyPropertyChanged` 가 없어 서브메뉴가 영원히 안 뜬다.

값이 실제로 바뀔 때만 새 인스턴스를 넣어라. 매 측정마다 새 목록을 넣으면 바인딩이 매
프레임 재평가된다.

### 4. `MeasureOverride`

순서를 지켜라:

1. `»` 머리 자식(`IsOverflowHead == true`)을 찾아 따로 둔다. 없어도 동작해야 한다.
2. 나머지 자식마다:
   - `Visibility != Collapsed` 이면 `Measure(new Size(∞, availableSize.Height))` 하고
     `DesiredSize.Width` 를 **캐시에 기록**한다.
   - `Collapsed` 이면 **캐시된 자연 폭을 그대로 쓴다** (다시 재지 마라 — 0 이 나온다).
   - 캐시는 자식 인스턴스를 키로 잡아라. 자식이 바뀌면(`OnVisualChildrenChanged`) 비운다.
3. `»` 머리를 `Measure` 해 `overflowWidth` 를 얻는다. 머리가 없으면 0.
4. `Folded(...)` 를 부른다.
5. 결과대로 `Visibility` 를 정한다. 접힌 것은 `Collapsed`, 아닌 것은 `Visible`.
6. `»` 머리는 **하나라도 접혔을 때만** `Visible` 이다.
7. `FoldedOrders` 를 갱신한다.
8. 보이는 자식의 폭 합(+ 보이면 `»` 머리 폭)을 낸다. **`availableSize.Width` 를 넘겨
   요구하지 마라** — 그러면 줄이 페인 밖으로 자란다 (`TabStripPanel.MeasureOverride` 의
   마지막 주석과 같은 이유).

높이는 툴바가 정한다 — `availableSize.Height` 가 무한이면 자식 중 가장 높은 값을 쓴다.

### 5. `ArrangeOverride`

자식을 두 무리로 가른다:

- `AlignRight == false` — **왼쪽 끝(x=0)부터** 선언 순서대로
- `AlignRight == true` — **오른쪽 끝에 붙여서** 선언 순서대로. 즉 이 무리의 폭 합을
  `finalSize.Width` 에서 뺀 자리에서 시작해 왼쪽에서 오른쪽으로 놓는다

`Collapsed` 인 자식은 건너뛴다 (`Arrange` 를 부르지 않아도 된다 — WPF 가 `Collapsed` 를
배치에서 뺀다). 두 무리가 겹칠 만큼 좁으면 **왼쪽 무리가 이긴다** — 이동·새로고침이
가려지느니 📍 가 잘리는 쪽이 낫다. `ClipToBounds = true` 로 두어 잘려 나간 것이 툴바
밖으로 삐져나오지 않게 한다.

`MeasureOverride` 의 예산 계산에서 `AlignRight` 자식을 **빼지 마라** — 같은 줄에 있고 같은
폭을 나눠 쓴다. `Folded` 에 넘기는 `widths` 에는 `»` 머리만 빠지고 📍 는 들어간다.

### 6. 컨버터

`src/FlexDir.App/Views/ViewConverters.cs` 에 **추가**한다 (새 파일을 만들지 마라 —
`ViewConvertersTests.cs` 가 이미 그 파일을 채점하고 있다).

```csharp
/// FoldedOrders 안에 ConverterParameter 로 준 번호가 있으면 Visible, 없으면 Collapsed.
/// » 안의 서브메뉴가 "내 무리가 접혔나" 를 묻는 데 쓴다.
public sealed class FoldedOrderToVisibility : IValueConverter
```

- `value` 가 `IReadOnlyList<int>` 가 아니면 `Collapsed`
- `parameter` 가 `int` 로 파싱되지 않으면 `Collapsed`
- `ConvertBack` 은 `NotSupportedException`

**색이나 크기를 코드에 박지 마라** (`docs/DESIGN.md` §5). 이 컨버터는 `Visibility` 만 낸다.

### 7. 테스트

`tests/FlexDir.App.Tests/Views/ToolbarOverflowPanelTests.cs` 를 새로 만든다 —
**파일명은 정확히 이것이어야 한다** (CLAUDE.md §6-2).
컨버터 테스트는 기존 `tests/FlexDir.App.Tests/Views/ViewConvertersTests.cs` 에 **추가**한다.

`Folded` 를 채점할 것 (최소):

1. 자리가 남으면 아무것도 안 접힌다
2. 딱 모자라면 `FoldOrder` **가장 작은 무리**가 먼저 접힌다
3. 그래도 모자라면 **다음으로 작은 무리**가 접힌다 — 즉 `1 → 2 → 3` 순서다
4. **`FoldOrder == 0` 인 자식은 아무리 좁아도 안 접힌다** (이동 4개 + 📍 가 남는다)
5. **같은 `FoldOrder` 는 한 덩어리로 함께 접힌다** — 하나만 접히는 결과가 나오면 실패다
6. 접히기 시작하면 `overflowWidth` 가 예산에서 빠진다 — `»` 폭을 무시해 한 무리 덜 접는
   결과가 나오면 실패다
7. `available` 이 `double.PositiveInfinity` 면 아무것도 안 접힌다
8. 접을 무리가 다 떨어지면 남은 것을 그대로 두고 넘친다 (예외를 던지지 않는다)
9. 길이가 다른 두 목록을 주면 `ArgumentException`
10. 빈 목록이면 빈 결과다

**실제 툴바 구성으로 한 번 채점하라** — 1구간의 자식 구성을 그대로 쓴 시나리오 테스트:
`뒤로(0,32) · 앞으로(0,32) · 상위(0,32) · 구분자(0,9) · 새로고침(0,32) · 구분자(3,9) ·
자세히(3,32) · 목록(3,32) · 타일(3,32) · 큰아이콘(3,32) · 구분자(2,9) · 분류(2,32) ·
📍(0,32)`, `overflowWidth = 32`.

- 넓을 때(예: 400) → 아무것도 안 접힘
- 중간(예: 260) → 뷰 모드 무리(3)만 접힘, 분류는 남음
- 좁을 때(예: 200) → 뷰 모드(3)와 분류(2) 둘 다 접히고, **이동 4개 + 구분자 + 📍 는 남음**

숫자는 위 폭 값으로 직접 계산해 확정하라 — 대충 넣고 통과하는 값을 찾지 마라.

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
   - `Folded` 가 `internal static` 이고 WPF 타입 없이 순수 값으로만 판정하는가?
     (그래야 테스트가 창을 띄우지 않는다)
   - `Visibility.Collapsed` 인 자식의 자연 폭을 **다시 재지 않는가**? (진동의 원인)
   - `FoldedOrders` 가 `DependencyProperty` 인가?
   - `AlignRight` 자식의 폭이 `Folded` 의 예산에 **들어가** 있는가? (빼면 좁은 페인에서
     📍 가 이동 버튼을 덮는다)
   - `*.xaml.cs` 를 새로 만들지 않았는가? (CLAUDE.md §2)
3. `phases/3-toolbar-overflow/index.json` 의 step 2 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 첨부 속성 이름 셋(`FoldOrder` · `IsOverflowHead` · `AlignRight`) ·
`FoldedOrders` · 컨버터 이름을 적어라 — step 4 가 XAML 에서 그 이름을 쓴다.

## 금지사항

- **`Children` 을 옮기거나 지우지 마라.** 이유: 위 §핵심 설계. 논리 트리 부모가 하나뿐이라
  스타일과 바인딩이 재평가되며 깨진다.
- **`MainWindow.xaml` 을 건드리지 마라.** 이유: 배선은 step 4 다. 여기서 패널을 끼우면
  `»` 메뉴가 없는 상태로 버튼이 사라지기만 한다.
- **`ScrollViewer` 를 쓰지 마라.** 이유: 가로 스크롤을 켠 `ScrollViewer` 는 내용을 무한
  폭으로 측정해 "자리가 모자란가" 판정이 영원히 발동하지 않는다
  (`TabStripPanel.cs` 클래스 주석에 같은 이유가 적혀 있다).
- **접힌 자식을 화면 밖 좌표로 배치해 감추지 마라.** 이유: 여전히 히트테스트에 걸려
  안 보이는 버튼이 눌린다. `Visibility.Collapsed` 를 써라.
- **폭·색·글리프를 코드에 박지 마라.** 이유: `docs/DESIGN.md` §5 — 하드코딩 색은 없고,
  버튼 폭은 스타일이 정한다. 패널은 잰 값만 쓴다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
