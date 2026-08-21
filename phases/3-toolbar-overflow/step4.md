# Step 4: toolbar-wiring

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 **§2(코드비하인드 금지)** · §1(참조 방향)
- `docs/DESIGN.md` §1(레이아웃) · **§7(아이콘)** — 툴바에 계열이 둘 있고(선 `E8A4`·`E8FD` /
  사각 `E8A9`·`E15B`) 새 아이콘은 둘 중 어느 쪽도 아니어야 한다는 규칙, 그리고 그 자리를
  **두 번 틀렸던 기록**
- `src/FlexDir.App/Views/ToolbarOverflowPanel.cs` — **step 2 가 만든 패널.**
  첨부 속성 `FoldOrder`(0=안 접힘, 1부터 작은 번호가 먼저) · `IsOverflowHead` ·
  `AlignRight`, 그리고 `FoldedOrders` 의존 속성
- `src/FlexDir.App/Views/ViewConverters.cs` — step 2 가 추가한 `FoldedOrderToVisibility`
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — step 1 이 추가한
  `KnownFolderOptions`(`IReadOnlyList<KnownFolderOption>`) 와 `OpenKnownFolderCommand`.
  `KnownFolderOption` 은 `record (string Label, LocationId Location)` 이다
- `src/FlexDir.Shell/Locations/KnownFolderList.cs` — step 3 이 만든 구현체.
  **`IDisposable` 이 아니다**
- `src/FlexDir.App/Views/MainWindow.xaml` — 이 step 이 고치는 파일:
  - **1179~1227줄** 툴바 `Border`/`StackPanel` — 이 step 이 바꾸는 자리
  - **518~562줄** `ToolMenu` 스타일 — 툴바 드롭다운 머리의 크롬(32×28)
  - **564~580줄** `GroupOptionItem` 스타일 — 항목 스타일의 본보기
  - **2003~2040줄** 타이틀바 — `AutomationProperties.Name` 을 왜 반드시 주는지가 주석에 있다
- `src/FlexDir.Host/Composition/AppComposition.cs` **110~190줄** — 포트를 만들어
  `PaneViewModel` 팩토리에 넘기는 자리. `driveSpace` 가 어떻게 들어가는지 보라

## 배경

step 0~3 이 만든 것을 화면에 붙인다. **이 step 이 끝나면 기능이 실제로 돈다.**

## 작업

### 1. `AppComposition` 주입

`src/FlexDir.Host/Composition/AppComposition.cs`:

- `driveList`·`systemTheme` 옆에 `var knownFolders = new KnownFolderList();` 를 만든다.
  그 자리의 주석이 "STA 도 정리도 필요 없다" 라고 적어 둔 무리다 — 여기 속한다.
- `PaneViewModel Pane() => new(...)` 의 마지막에 `knownFolders: knownFolders` 를 붙인다.
- **`shellServices` 배열(정리 목록)에 넣지 마라.** 이유: COM 이 아니라 `IDisposable` 이
  아니다 (step 3). 넣으면 컴파일되지 않는다.

### 2. 툴바를 `ToolbarOverflowPanel` 로 바꾼다

1183줄의 `<StackPanel Orientation="Horizontal" Margin="6,0" VerticalAlignment="Center">` 를
`<v:ToolbarOverflowPanel Margin="6,0" VerticalAlignment="Center">` 로 바꾼다.
**자식의 순서와 내용은 그대로 둔다.** 바꾸는 것은 컨테이너와 아래 첨부 속성뿐이다.

`FoldOrder` 를 이렇게 붙인다 (**구분자 `Rectangle` 은 뒤따르는 무리와 같은 번호**를 준다 —
아니면 무리가 접힌 자리에 구분자만 고아로 남는다):

| 자식 | `FoldOrder` |
|---|---|
| 뒤로 `E72B` · 앞으로 `E72A` · 상위 `E74A` | `0` (기본 — 안 적어도 된다) |
| 구분자 `Rectangle` (새로고침 앞) | `0` |
| 새로 고침 `E72C` | `0` |
| 구분자 `Rectangle` (뷰 모드 앞) | `3` |
| 자세히 `E8A4` · 목록 `E8FD` · 타일 `E8A9` · 큰 아이콘 `E15B` | `3` |
| 구분자 `Rectangle` (분류 앞) | `2` |
| 분류 `Menu`(`E8EC`) | `2` |
| **알려진 폴더 `Menu`(`E707`)** | `0` + `AlignRight="True"` |
| **`»` 머리** | `0` + `AlignRight="True"` + `IsOverflowHead="True"` |

> **`1` 번은 비워 둔다.** 2구간의 외부 도구(`[VS]` · `[>_]`)가 가장 먼저 접히는 자리다.

### 3. 알려진 폴더 메뉴 — 글리프 `E707`

분류 메뉴(1213~1224줄 · `<Menu>`…`</Menu>`) 바로 뒤, `»` 앞에 넣는다.

```xml
<Menu Background="Transparent" VerticalAlignment="Center" Padding="0"
      v:ToolbarOverflowPanel.AlignRight="True">
    <MenuItem Style="{StaticResource ToolMenu}" Header="&#xE707;"
              AutomationProperties.Name="알려진 폴더" ToolTip="알려진 폴더"
              ItemsSource="{Binding KnownFolderOptions}">
        <MenuItem.ItemContainerStyle>
            <Style TargetType="MenuItem" BasedOn="{StaticResource {x:Type MenuItem}}">
                <Setter Property="Header" Value="{Binding Label}" />
                <Setter Property="Command"
                        Value="{Binding DataContext.OpenKnownFolderCommand,
                                RelativeSource={RelativeSource AncestorType=Menu}}" />
                <Setter Property="CommandParameter" Value="{Binding}" />
            </Style>
        </MenuItem.ItemContainerStyle>
    </MenuItem>
</Menu>
```

**반드시 지킬 것:**

- **글리프는 `E707` 이다. `E700` 을 쓰지 마라.** 이유: `E700` 은 **2008줄 타이틀바의 폴더
  트리 토글**이 이미 쓰고 있고, 4분할이면 같은 글리프가 창에 다섯 개가 된다. 게다가 ☰ 는
  `docs/DESIGN.md` §7 이 "새 아이콘은 둘 중 어느 쪽도 아니어야 한다" 고 못 박은 **선 계열**
  이다. `E707`(MapPin)은 물방울+원 실루엣이라 선·사각 어느 쪽도 아니다.
  (사용자 확정 2026-08-21.)
- **`AutomationProperties.Name` 을 반드시 준다.** 이유: 없으면 UIA 에 글리프 문자가 이름으로
  잡힌다 — 2036줄 주석에 같은 자리가 적혀 있다. 사람 확인이 UIA 로 이 메뉴를 찾는다.
- **`MenuItem` 에 `FontFamily` 를 걸지 마라.** 이유: `FontFamily` 는 상속되므로 하위 메뉴의
  한글 항목("바탕화면")이 전부 두부(□)가 된다 — `ToolMenu` 스타일 안 주석(520줄 언저리)에 이미
  적혀 있는 함정이다. 글리프 폰트는 `ToolMenu` 템플릿의 `TextBlock` 에만 걸려 있다.
- **단축키를 만들지 마라** (사용자 확정). `InputBindings` 를 추가하지 마라.

### 4. `»` 오버플로 머리

알려진 폴더 메뉴 **뒤**에 넣는다 (선언 순서가 곧 오른쪽 무리의 좌→우 순서다).

```xml
<Menu Background="Transparent" VerticalAlignment="Center" Padding="0"
      v:ToolbarOverflowPanel.AlignRight="True"
      v:ToolbarOverflowPanel.IsOverflowHead="True">
    <MenuItem Style="{StaticResource ToolMenu}" Header="&#x00BB;"
              AutomationProperties.Name="더 보기" ToolTip="더 보기">
        <!-- 뷰 모드 (FoldOrder 3) -->
        <MenuItem Header="보기 모드"
                  Visibility="{Binding FoldedOrders, ElementName=PaneToolbar,
                               Converter={StaticResource FoldedOrderToVisibility},
                               ConverterParameter=3}">
            ... 뷰 모드 4종을 MenuItem 으로 ...
        </MenuItem>
        <!-- 분류 (FoldOrder 2) -->
        <MenuItem Header="분류 방법"
                  ItemsSource="{Binding GroupOptions}"
                  Visibility="{Binding FoldedOrders, ElementName=PaneToolbar,
                               Converter={StaticResource FoldedOrderToVisibility},
                               ConverterParameter=2}">
            ... ItemContainerStyle 은 1217~1222줄 `MenuItem.ItemContainerStyle` 을 그대로 ...
        </MenuItem>
    </MenuItem>
</Menu>
```

**반드시 지킬 것:**

- `ToolbarOverflowPanel` 에 `x:Name="PaneToolbar"` 를 준다 — `ElementName` 바인딩의 대상이다.
  이 툴바는 `ItemsPanelTemplate` 안이 **아니므로** 이름 범위가 같고 `ElementName` 이 닿는다
  (`TabStripPanel` 이 오버레이를 못 쓴 이유가 그 반대 경우다).
- **`Header` 는 한글 글자다** — `보기 모드` · `분류 방법`. 글리프가 아니다. 이 `MenuItem` 들은
  `ToolMenu` 스타일을 쓰지 않는다 (그 스타일은 32×28 아이콘 머리 전용이다).
- 뷰 모드 4종의 `Command`·`CommandParameter` 는 툴바 버튼(1194~1207줄, `E8A4`·`E8FD`·`E8A9`·`E15B`)의 것을 **그대로
  복사한다.** `ChangeViewModeCommand` 에 `{x:Static state:ViewMode.Details}` 등을 넘긴다.
  체크 표시가 필요하면 `IsCheckable`/`IsChecked` 를 쓰되, 없어도 이 step 은 통과다.
- **`Command` 가 `null` 인 `MenuItem` 을 만들지 마라.** 이유: 그것은 정상으로 뜨고
  **눌리기까지 한다** (`docs/PRD-v2.md` §17 §값을 치르고 배운 것). 바인딩 경로를 틀리면
  조용히 죽는다 — 만든 뒤 **일곱 개를 실제로 눌러 봐야 한다**는 기록이 거기 있다.
- 컨버터를 `Window.Resources` 에 등록하는 것을 잊지 마라 (`ViewConverters.cs` 의 다른
  컨버터가 어떻게 등록돼 있는지 보고 같은 자리에 같은 방식으로 넣어라).

### 5. 코드비하인드 금지

`src/FlexDir.App/Views/MainWindow.xaml.cs` 에 **한 줄도 넣지 마라.** `Menu`/`MenuItem` 은
눌러서 펴지는 것이 내장이라 코드가 필요 없다 (503줄 주석). `check-structure.ps1` 의 A 항목이
이것을 검사하고, 위반하면 게이트가 깨진다.

### 6. 테스트

이 step 은 XAML 과 조립이라 새 단위 테스트가 꼭 필요하지는 않다. **다만 기존 테스트가 전부
그대로 통과해야 한다.**

`AppComposition` 을 고쳤으므로 `tests/FlexDir.Host.Tests/` 에 조립 테스트가 있으면
**`knownFolders` 가 실제로 페인에 닿는지** 한 줄 늘려라 (있는 테스트의 형태에 맞춰라).

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 3 직후보다 **작아지면 안 된다.**

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. **XAML 은 컴파일된다고 도는 것이 아니다.** 아래를 눈으로 확인하라:
   - `Binding` 경로 셋이 실제 멤버 이름과 **글자 그대로** 같은가 —
     `KnownFolderOptions` · `OpenKnownFolderCommand` · `FoldedOrders`.
     **WPF 바인딩은 런타임 조회라 오타가 컴파일 에러 없이 조용히 죽는다**
     (`docs/PRD-v2.md` §17).
   - `»` 안의 모든 `MenuItem` 에 `Command` 가 실제로 걸려 있는가?
   - 글리프가 `E707` 인가? (`E700` 이면 실패다)
   - `x:Name="PaneToolbar"` 가 있고 `ElementName` 이 그것을 가리키는가?
3. 체크리스트:
   - `MainWindow.xaml.cs` 가 안 바뀌었는가? (`git diff --stat` 으로 확인)
   - `FlexDir.App` 이 `FlexDir.Shell` 을 참조하지 않는가?
   - `AppComposition` 의 `shellServices` 배열에 `knownFolders` 를 넣지 않았는가?
4. `phases/3-toolbar-overflow/index.json` 의 step 4 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 **`FoldOrder` 배정표**(어느 자식이 몇 번인지)를 적어라 — step 5 의 문서가
그것을 옮긴다.

## 금지사항

- **`MainWindow.xaml.cs` 를 건드리지 마라.** 이유: CLAUDE.md §2 —
  `InitializeComponent()` 호출만 둔다. WPF 에서 god object 가 자라는 자리가 여기고,
  `check-structure.ps1` 이 매 게이트마다 검사한다.
- **`E700` 을 쓰지 마라.** 이유: 위 §3. 타이틀바 트리 토글과 겹치고 DESIGN §7 의 선 계열이다.
- **`Menu`/`MenuItem` 에 `FontFamily` 를 걸지 마라.** 이유: 상속되어 하위 한글 항목이
  두부(□)가 된다.
- **툴바 자식의 순서를 바꾸지 마라.** 이유: 선언 순서가 곧 배치 순서다. 순서를 바꾸면
  step 2 의 시나리오 테스트가 채점한 화면과 실물이 갈린다.
- **외부 도구 버튼(`[VS]`·`[>_]`)을 만들지 마라.** 이유: 2구간(v0.9.0)이다. `FoldOrder 1`
  자리만 비워 둔다.
- **버전이나 문서를 고치지 마라.** 이유: step 5 가 한다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
