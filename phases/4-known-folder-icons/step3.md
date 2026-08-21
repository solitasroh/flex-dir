# Step 3: known-folder-menu-icons

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 **§2(코드비하인드 금지)** · §1(참조 방향)
- `docs/DESIGN.md` §7(아이콘) — *"파일·폴더 아이콘은 디자인 대상이 아니다. Windows Shell
  에서 온다"* 가 원칙이고, 탭 아이콘만 글리프로 예외를 둔 이유가 적혀 있다
- `src/FlexDir.App/ViewModels/PaneViewModel.cs` — **step 2 가 넓힌 것.**
  `public sealed record KnownFolderOption(string Label, LocationId Location, ThumbnailBitmap? Icon = null);`
  와 `KnownFolderOptions` 속성. 아이콘은 배경에서 채워지고 채워질 때 `PropertyChanged` 가
  한 번 더 나간다
- `src/FlexDir.App/Views/ViewConverters.cs` — **`ThumbnailImageConverter`.**
  `IMultiValueConverter` 이고 `values.OfType<ThumbnailBitmap>().FirstOrDefault()` 를
  `BitmapSource` 로 바꾼다. `Pbgra32` 를 쓰는 이유와 얼려서 내는 이유가 주석에 있다.
  **새 변환기를 만들 필요가 없다** — 아래 §2 참조
- `src/FlexDir.App/Views/MainWindow.xaml` — 이 step 이 고치는 파일:
  - **368줄** `<v:ThumbnailImageConverter x:Key="ThumbnailImage" />` — 이미 등록돼 있다
  - **809줄 언저리** `<MultiBinding Converter="{StaticResource ThumbnailImage}">` —
    목록 항목이 이 변환기를 쓰는 본보기
  - **1237~1258줄 언저리** 알려진 폴더 `Menu`(글리프 `E707`) 와 그
    `MenuItem.ItemContainerStyle` — **이 step 이 고치는 자리.** 지금 `Header` 와
    `Command`·`CommandParameter` 만 설정한다
  - **518~562줄** `ToolMenu` 스타일 — 그 안 주석이 **`FontFamily` 를 `MenuItem` 에 걸면
    하위 한글 항목이 두부(□)가 된다**고 경고한다

## 배경

알려진 폴더 메뉴(홈 · 바탕화면 · 문서 · 다운로드 · 사진)의 각 항목에 Windows 의 실제 폴더
아이콘을 보인다 (사용자 요청 2026-08-21). step 0~2 가 포트·구현·ViewModel 을 끝냈다.
**이 step 이 그것을 화면에 붙인다 — 이 step 이 끝나면 아이콘이 실제로 보인다.**

## 작업

### 1. `MenuItem.Icon` 에 그림을 건다

알려진 폴더 메뉴의 `MenuItem.ItemContainerStyle` 안 `Style` 에 `Setter` 를 하나 더한다.
`MenuItem` 은 `Icon` 속성을 이미 갖고 있고 기본 템플릿이 그것을 헤더 왼쪽 칸에 그린다 —
**새 템플릿을 만들지 마라.**

```xml
<Setter Property="Icon">
    <Setter.Value>
        <Image Width="16" Height="16"
               RenderOptions.BitmapScalingMode="HighQuality">
            <Image.Source>
                <MultiBinding Converter="{StaticResource ThumbnailImage}">
                    <Binding Path="Icon" />
                </MultiBinding>
            </Image.Source>
        </Image>
    </Setter.Value>
</Setter>
```

**반드시 지킬 것:**

- **`MultiBinding` 에 `Binding` 하나만 넣는다.** `ThumbnailImageConverter` 는
  `IMultiValueConverter` 라 단일 `Binding` 으로는 못 건다. 하나짜리 `MultiBinding` 은
  정상이고, 이렇게 하면 **새 변환기를 만들지 않아도 된다** — 변환기가 두 벌이 되면
  `Pbgra32` 같은 판단이 한쪽에만 남는다.
- **`Width`·`Height` 는 16 이다.** 원본은 32 로 온다 (step 2). 줄여 그리는 쪽이 150% 배율
  에서 선명하고, `BitmapScalingMode="HighQuality"` 가 그 축소를 맡는다.
- **아이콘이 `null` 일 때 깨지지 않아야 한다.** 변환기가 `null` 을 내면 `Image.Source` 가
  비고 빈칸이 남는다 — 그것이 정상이다. `Icon` 자리를 아예 없애는 트리거를 만들지 마라.
- **`Style` 의 `Setter.Value` 안 `Image` 는 공유된다.** WPF 에서 `Style` 의 값은 여러
  컨테이너가 나눠 쓰므로 같은 `Image` 인스턴스가 여러 `MenuItem` 에 들어가면 마지막 하나만
  보인다. **그것을 피하려면 `x:Shared="False"` 를 `Image` 에 준다.** 이것을 빠뜨리면
  **다섯 중 하나에만 아이콘이 뜨거나 아무 데도 안 뜬다** — 붙인 뒤 반드시 눈으로 세어라.
- **`FontFamily` 를 건드리지 마라** — `ToolMenu` 스타일 주석의 함정이다.

### 2. `»` 안에는 넣지 않는다

알려진 폴더는 `FoldOrder 0` 이라 접히지 않으므로 `»` 서브메뉴에 알려진 폴더가 없다.
**`»` 쪽 XAML 을 건드리지 마라.**

### 3. 코드비하인드 금지

`src/FlexDir.App/Views/MainWindow.xaml.cs` 에 **한 줄도 넣지 마라.**
`check-structure.ps1` 의 A 항목이 검사하고 위반하면 게이트가 깨진다.

### 4. 테스트

이 step 은 XAML 이라 새 단위 테스트가 꼭 필요하지는 않다. **기존 테스트가 전부 그대로
통과해야 한다.**

`ViewConverters` 쪽에 한 줄 늘릴 수 있으면 늘려라 — `ThumbnailImageConverter.Convert` 에
**값 하나짜리 배열**(`[ThumbnailBitmap]`)을 넣었을 때 그림이 나오고, `[null]` 이면
`null` 이 나오는지. 하나짜리 `MultiBinding` 이 실제로 도는지를 채점하는 유일한 자리다.
기존 `tests/FlexDir.App.Tests/Views/ViewConvertersTests.cs` 에 **추가**한다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 2 직후보다 **작아지면 안 된다.**

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. **XAML 은 컴파일된다고 도는 것이 아니다.** 눈으로 확인하라:
   - `Binding Path="Icon"` 이 `KnownFolderOption.Icon` 과 **글자 그대로** 같은가?
     **WPF 바인딩은 런타임 조회라 오타가 컴파일 에러 없이 조용히 죽는다**
     (`docs/PRD-v2.md` §17).
   - **`x:Shared="False"` 를 줬는가?** (없으면 다섯 중 하나에만 뜬다)
   - `MultiBinding` 안에 `Binding` 이 하나인가?
3. 체크리스트:
   - `MainWindow.xaml.cs` 가 안 바뀌었는가? (`git diff --stat`)
   - 새 변환기를 만들지 않았는가?
   - `»` 쪽 XAML 이 안 바뀌었는가?
4. `phases/4-known-folder-icons/index.json` 의 step 3 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 `x:Shared="False"` 를 줬는지와 아이콘 표시 크기를 적어라 — step 4 의 문서가
그것을 옮긴다.

## 금지사항

- **`MainWindow.xaml.cs` 를 건드리지 마라.** 이유: CLAUDE.md §2.
- **새 변환기를 만들지 마라.** 이유: `ThumbnailImageConverter` 가 이미 `Pbgra32`(알파가
  곱해진 픽셀)와 얼리기를 판단해 두었다. 두 벌이 되면 한쪽에만 그 판단이 남고, 반투명
  가장자리가 어둡게 번지는 결함이 한쪽에서만 되살아난다.
- **`MenuItem` 템플릿을 새로 만들지 마라.** 이유: 기본 템플릿에 이미 `Icon` 칸이 있다.
  새로 만들면 체크 표시·단축키 칸·키보드 이동이 전부 다시 짜야 할 것이 된다.
- **`x:Shared="False"` 를 빼지 마라.** 이유: 위 §1. `Style` 의 값은 컨테이너들이 나눠 쓴다.
- **아이콘이 없을 때 항목을 숨기는 트리거를 만들지 마라.** 이유: 아이콘은 곁다리다.
  경로가 있으면 갈 수 있고, 못 그린다고 갈 수 있는 자리를 없애면 기능이 사라진다.
- **버전이나 문서를 고치지 마라.** 이유: step 4 가 한다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
