# Step 8: toolbar-buttons

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §2(**코드비하인드 금지**) · §5(자동 채점의 경계)
- `docs/ARCHITECTURE.md` §1 계층 구조 · §2 포트 표 — **참조 방향은 `scripts/check-structure.ps1` 이 기계로 잡지만, 어느 계층에 무엇을 놓는가는 여기 있다**
- `docs/DESIGN.md` §7 — 아이콘. **이 툴바의 계열 규칙**: 선(`E8A4`·`E8FD`)과
  사각(`E8A9`·`E15B`) 둘이 이미 있고, 새 아이콘은 **둘 중 어느 쪽도 아닌 실루엣**이어야
  한다. 그리고 *"툴바 아이콘은 툴바 맥락에서, 실제 크기로, 창 전체에 이미 있는 글리프와
  함께 본다"*
- `docs/ADR.md` ADR-021 — 툴바 오버플로. **무리 단위 접힘 · `»`(현재 `E712`) 서브메뉴는
  따로 선언 · 자연 폭 캐시 · 오른쪽 정렬은 패널이 쥔다.** 대가도 적혀 있다:
  *"같은 항목이 툴바와 `···` 에 두 벌 선언된다"*
- `docs/PRD-v2.md` §20 — 접힘 규칙 표. **`FoldOrder 1` 이 이 step 을 위해 비어 있다**
- `src/FlexDir.App/Views/ToolbarOverflowPanel.cs` — `FoldOrder` · `IsOverflowHead` ·
  `AlignRight` · `FoldedOrders`
- `src/FlexDir.App/Views/MainWindow.xaml` — 특히:
  - `ToolButton` 스타일 (376줄 부근) — **본뜰 크롬.** 32×28 · `CornerRadius 4` ·
    `HoverBrush`/`PressedBrush`/`TextDisabledBrush` 트리거 셋
  - `ToolMenu` 스타일 (519줄 부근)
  - 페인 툴바 선언 (1186줄 부근 `<v:ToolbarOverflowPanel x:Name="PaneToolbar">`) —
    `FoldOrder 1` 자리를 비워 둔 주석이 거기 있다
  - `···` 오버플로 머리와 그 서브메뉴 (1305줄 부근)
  - `FoldedOrderToVisibility` 변환기 사용법
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — **step 5 가 만든 공개 이름 여섯**:
  `Terminal` · `CanOpenInEditor` · `CanOpenInTerminal` · `OpenInEditorCommand` ·
  `OpenInTerminalCommand` · `RefreshExternalTools`
- `tests/FlexDir.App.Tests/Views/ToolbarOverflowPanelTests.cs`

## 배경

`[VS]`·`[>_]` 버튼을 툴바에 세운다. **`FoldOrder 1`** 이고 **가장 먼저 접힌다.**

> **아이콘은 글리프가 아니라 벡터 Path 다** (사용자 확정 2026-08-24).
> `[VS]` 는 진짜 VS Code 리본, `[>_]` 는 벡터 프롬프트다. Segoe Fluent Icons 에는
> 터미널 글리프가 `E756`(C:\ 박스) 하나뿐인데 **테두리 사각이라 `E15B` 와 같은 계열로
> 읽히고 16px 에서 박스 안 글자가 뭉갠다** — `E707`(MapPin)을 기각했던 것과 같은
> 실패 모양이다 (`docs/DESIGN.md` §7).
>
> 그래서 **스타일이 하나 는다.** `ToolButton`·`ToolMenu` 는 글리프 `TextBlock` 기반이라
> `Path` 를 그릴 수 없다.

## 작업

### 1. `ToolPathButton` 스타일 하나

`MainWindow.xaml` 의 `ToolButton` **바로 뒤**에 둔다.

- `ToolButton` 과 **완전히 같은 크롬**: 32×28 · `CornerRadius 4` · `Background` Transparent ·
  `BorderThickness 0` · `Focusable False` · `Foreground` = `{DynamicResource TextBrush}` ·
  `IsMouseOver`/`IsPressed`/`IsEnabled=False` 트리거 셋
- 템플릿 안은 `ContentPresenter` 가 아니라 **`Path` 하나**다:
  - `Data="{TemplateBinding Tag}"` — 지오메트리는 `Tag` 로 온다
  - `Fill="{TemplateBinding Foreground}"` — **이것이 다크/라이트와 비활성 회색을
    기존 글리프 버튼과 한 소스로 따라가게 하는 자리다**
  - `Stretch="Uniform"` · `Width="15"` · `Height="15"` · 가운데 정렬
  - **`Stroke` 를 주지 마라** — 아래 두 지오메트리는 둘 다 **채우기 전용**이다.
    `Stroke` 를 함께 걸면 채워진 모양 둘레에 이유 없는 테두리가 생겨 16px 에서 뭉갠다
- **`IsEnabled=False` 트리거는 `Foreground` 를 `TextDisabledBrush` 로 바꾼다.**
  `TargetName` 으로 `Path` 의 `Fill` 을 직접 집지 마라 — `TemplateBinding` 을 덮어써서
  다크/라이트 전환이 죽는다 (`CaptionButton` 주석에 같은 함정이 적혀 있다)

**`FontFamily`·`FontSize` 를 두지 마라.** 글자를 그리지 않는다.

### 2. 지오메트리 둘 — `Window.Resources` 에 `Geometry` 리소스로

**아래 문자열을 그대로 쓴다.** 이 기계에서 16px 로 그려 확인했다 (2026-08-24).

`ExternalEditorGeometry` — VS Code 리본. viewBox 0 0 100 100, **`F0`(EvenOdd)** 로
가운데 삼각 구멍이 뚫린다:

```
F0 M70.9 99.3a6.2 6.2 0 0 0 4.9-.2l20.4-9.8a6.2 6.2 0 0 0 3.5-5.6V16.3a6.2 6.2 0 0 0-3.5-5.6L75.8.9a6.2 6.2 0 0 0-7.1 1.2L29.6 37.8 12.6 24.9a4.1 4.1 0 0 0-5.3.2l-5.5 5a4.1 4.1 0 0 0 0 6.1L16.5 50 1.8 63.8a4.1 4.1 0 0 0 0 6.1l5.5 5a4.1 4.1 0 0 0 5.3.2l17-12.9 39.1 35.7a6.2 6.2 0 0 0 2.2 1.4ZM75.2 27.3 45.5 50l29.7 22.7V27.3Z
```

`ExternalTerminalGeometry` — 프롬프트 `>_`. 16 단위 상자, **`F1`(NonZero)**:

```
F1 M3.4,2.4 L9,8 L3.4,13.6 L1,13.6 L6.6,8 L1,2.4 Z M9.6,12 L15,12 L15,13.7 L9.6,13.7 Z
```

- **`F0`/`F1` 접두사를 빼지 마라.** 리본은 `F0` 이 없으면 가운데 구멍이 메워져
  16px 에서 검은 덩어리가 된다
- `x:Key` 를 위 이름 그대로 쓴다 — step 9 가 문서에 그 이름을 적는다
- **`x:Shared="False"` 를 주지 마라.** `Geometry` 는 불변으로 쓰이고 네 페인이 같은
  인스턴스를 공유해도 된다 (`Freeze` 되는 종류다)

### 3. 툴바에 무리 하나를 늘린다

`<v:ToolbarOverflowPanel x:Name="PaneToolbar">` 안, **새로 고침 다음 구분자 바로 뒤**
(= 뷰 모드 무리 앞) 에 넣는다. 지금 그 자리에 `FoldOrder 1` 을 위해 비워 둔 주석이 있다.

```
Rectangle  (구분자)  FoldOrder=1
Button  ToolPathButton  Tag={StaticResource ExternalEditorGeometry}   FoldOrder=1
        ToolTip="VS Code 로 열기"        AutomationProperties.Name="VS Code 로 열기"
        Command={Binding OpenInEditorCommand}
Button  ToolPathButton  Tag={StaticResource ExternalTerminalGeometry} FoldOrder=1
        ToolTip="터미널로 열기"          AutomationProperties.Name="터미널로 열기"
        Command={Binding OpenInTerminalCommand}
```

- **구분자에도 `FoldOrder=1` 을 준다.** 안 주면 무리가 접힌 뒤 구분자만 고아로 남는다
  (`ToolbarOverflowPanel` 주석이 그 이유를 적어 두었다)
- **`IsEnabled` 를 바인딩하지 마라.** `Command` 의 `CanExecute` 가 이미 그것이다
  (step 5 의 `[RelayCommand(CanExecute = ...)]`). 둘을 다 걸면 두 소스가 싸운다
- **`AlignRight` 를 주지 마라.** 왼쪽 흐름에 선다
- `ToolTip` 을 **반드시** 달아라. 그림만으로 뜻이 오지 않는 사람이 있고, 실물 확인의
  UI Automation 이 이 이름으로 버튼을 찾는다

### 4. `···` 서브메뉴에 두 벌째를 선언한다

ADR-021 의 대가다 — 같은 항목이 툴바와 `···` 에 **두 벌 선언된다.** 동작은 같은
`Command` 를 보므로 진실원천은 하나다.

`···` 서브메뉴에서 **'보기 모드' 앞**(= 접힘 순서대로 맨 위)에:

```
MenuItem  Header="VS Code 로 열기"  Command={Binding OpenInEditorCommand}
          Visibility={Binding FoldedOrders, ElementName=PaneToolbar,
                      Converter={StaticResource FoldedOrderToVisibility}, ConverterParameter=1}
MenuItem  Header="터미널로 열기"    Command={Binding OpenInTerminalCommand}
          Visibility=(같음, ConverterParameter=1)
```

- **`MenuItem.Icon` 을 쓰지 마라.** 이 앱의 암시적 `MenuItem` 템플릿에 `Icon` 프리젠터가
  없어 **무엇을 넣든 예외도 경고도 없이 안 그려진다** (v0.8.1 이 그렇게 나갔다 —
  `docs/PRD-v2.md` §20 §값을 치르고 배운 것). 메뉴 항목은 **글자만** 둔다
- **`ConverterParameter=1`** 이다 — 뷰 모드가 3, 분류가 2, 외부 도구가 1

### 5. 접힘 판정 테스트

`tests/FlexDir.App.Tests/Views/ToolbarOverflowPanelTests.cs` 에 추가 —
`ToolbarOverflowPanel.Folded` 는 순수 값 함수라 자동 채점된다:

1. **`FoldOrder 1` 이 2·3 보다 먼저 접힌다** — 폭을 조금만 줄이면 1 만 접히고 2·3 은 남는다
2. 더 줄이면 1+2 가 접히고 3 이 남는다
3. 더 줄이면 1+2+3 이 다 접힌다
4. **`FoldOrder 0` 은 어떤 폭에서도 안 접힌다** (이동 셋 · 새로 고침 · 알려진 폴더)
5. 같은 번호끼리는 **함께** 접힌다 (구분자 포함)
6. 폭이 충분하면 아무것도 안 접히고 `···` 도 안 뜬다

**실제 폭 수치는 `docs/PRD-v2.md` §20 실측(툴바 347 · `···` 32 · 여백 12)에서 온다.**
외부 도구 무리가 늘었으므로 그 합이 `347 + 9 + 32 + 32 = 420` 이 된다 —
**step 9 가 §20 표를 그 값으로 갱신한다. 이 step 은 코드만 맞춘다.**

### 6. 실물 확인 — 이 step 안에서 한다

> 사용자 결정 2026-08-24: **UI step 은 화면을 실제로 본 것까지가 완료 조건이다.**
> 게이트 4종이 1구간에서 화면 결함 둘을 그대로 통과시켰다 —
> `MenuItem.Icon` 미표시(v0.8.2)와 `»` 두부(v0.8.3). **둘 다 "그려졌는가" 의 문제였다.**

`scripts/capture-toolbar.ps1` 을 만든다. 하는 일:

```
1. Start-Process 로 저장소 Debug 빌드를 띄운다
     src\FlexDir.Host\bin\Debug\net9.0-windows\FlexDir.Host.exe
2. 창이 뜰 때까지 기다린다 (시한 20초. 안 뜨면 실패)
     ⚠ 상주 앱이라 두 번째 실행은 즉시 종료된다. 먼저
        Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue
3. UI Automation 으로 AutomationProperties.Name 이
     "VS Code 로 열기" · "터미널로 열기" 인 요소를 찾는다
     → 없으면 실패 (버튼이 트리에 없다)
     → BoundingRectangle 을 받는다
4. PrintWindow 로 창을 찍는다
5. 각 버튼 사각형 안에서 **배경색과 다른 픽셀**을 센다
     → 임계값 미만이면 실패 ("버튼 자리에 아무것도 안 그려졌다")
6. 캡처 PNG 를 artifacts\toolbar-<타임스탬프>.png 로 남긴다
7. finally 로 Stop-Process 한다   ← 반드시
```

**임계값**: 16×16 안에서 배경과 다른 픽셀이 **40개 이상**. 이유: 빈 버튼은 0 이고,
`>_` 는 대략 90~110, 리본은 120 넘는다. 40 은 "무언가 그려졌다" 를 가르는 자리이지
"제대로 그려졌다" 를 가르는 자리가 아니다 — **후자는 사람이 본다** (CLAUDE.md §5).

⚠ **`Stop-Process` 를 빼먹으면 다음 게이트가 파일 잠금으로 깨진다**
(`.harness/HANDOFF.md` §배포 절차 ①). `try/finally` 로 감싸라.

⚠ **`PrintWindow` 로 "잘렸는가" 는 판정할 수 없다** — 검은 테두리가 잘린 자리를 덮는다.
여기서 재는 것은 **"그려졌는가" 뿐이다.**

⚠ **합성 마우스 입력을 쓰지 마라.** 포그라운드를 못 잡으면 조용히 실패하고 클릭이 남의
창으로 간다 (`.harness/HANDOFF.md` §규칙 13). UI Automation 조회는 포그라운드가 필요 없다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
pwsh -File scripts/capture-toolbar.ps1
```

**넷 다 exit 0**, `dotnet build` 는 **경고 0**. 테스트 총계가 step 7 직후보다 커야 한다.
`capture-toolbar.ps1` 이 끝난 뒤 `Get-Process FlexDir.Host` 가 비어 있어야 한다.

## 검증 절차

1. 위 AC 커맨드 넷을 순서대로 실행한다. **`capture-toolbar.ps1` 은 마지막이다** —
   앞의 셋이 산출물을 만든 뒤에야 띄울 것이 있다.
2. 체크리스트:
   - `*.xaml.cs` 에 `InitializeComponent()` 외의 것이 없는가?
   - 리본 지오메트리에 `F0` 접두사가 있는가? (없으면 가운데 구멍이 메워진다)
   - `Path` 에 `Stroke` 를 안 걸었는가?
   - 구분자에도 `FoldOrder=1` 을 줬는가?
   - `···` 서브메뉴에 `MenuItem.Icon` 을 안 썼는가?
   - **캡처가 두 버튼 자리에서 픽셀을 셌는가?** 남긴 PNG 를 실제로 열어 확인해라
   - **`FlexDir.Host` 프로세스를 죽였는가?**
   - 기존 툴바 테스트가 하나도 안 깨졌는가?
3. `phases/5-external-tools/index.json` 의 step 8 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 스타일 이름 · 지오메트리 리소스 키 둘 · **캡처가 센 픽셀 수 두 개** ·
남긴 PNG 경로를 적어라. step 9 가 문서에 옮기고, 사람 확인 9·10번이 그 PNG 에서 시작한다.

## 금지사항

- **코드비하인드를 만들지 마라.** 이유: CLAUDE.md §2.
- **`MenuItem.Icon` 을 쓰지 마라.** 이유: 이 앱의 암시적 템플릿에 프리젠터가 없어
  예외도 경고도 없이 안 그려진다. v0.8.1 이 그렇게 배포됐다.
- **`ToolMenu` 의 `Header` 에 Segoe Fluent Icons 밖의 글자를 넣지 마라.** 이유: 그
  스타일이 폰트를 하드코딩해 두부(□)가 된다. v0.8.3 이 `»` 로 그랬다.
  (이 step 은 `ToolMenu` 를 안 건드리지만, 손대고 싶어지면 이 이유를 먼저 읽어라)
- **`ToolButton`·`ToolMenu` 스타일을 고치지 마라.** 이유: 툴바의 다른 버튼 열 개가
  전부 그것을 쓴다 (CLAUDE.md §2.3 — 수술적 변경).
- **`IsEnabled` 와 `CanExecute` 를 둘 다 걸지 마라.** 이유: 가시성/활성의 소스가
  둘이 되면 서로 덮어쓴다.
- **`ToolbarOverflowPanel.cs` 를 고치지 마라.** 이유: 접힘 판정은 이미 `FoldOrder 1` 을
  지원한다. 자리가 비어 있었을 뿐이다.
- **캡처 뒤 `FlexDir.Host` 를 살려 두지 마라.** 이유: 저장소 Debug 빌드는 산출물을
  잠그고 다음 게이트가 깨진다.
- **캡처로 "예쁜가·구분되는가" 를 판정하려 들지 마라.** 이유: CLAUDE.md §5 —
  자동 채점할 수 없다. 재는 것은 "그려졌는가" 하나다.
- **`Directory.Build.props`·문서를 건드리지 마라.** 이유: step 9 다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
