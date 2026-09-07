# ARCHITECTURE — flex-dir

## 1. 프로젝트 구성

```
FlexDir.Core        순수 .NET. UI·COM·Win32 의존 없음
                    모델 · 정렬 · 뷰상태 · 경로 추상화 · 포트 인터페이스

FlexDir.Shell       → Core
                    COM interop 구현체. PIDL · 컨텍스트메뉴 · 썸네일 ·
                    클립보드 · 파일조작 · 폴더감시

FlexDir.App         → Core
                    WPF. View(XAML) + ViewModel. Shell 을 모른다

FlexDir.Host        → Core · Shell · App
                    exe. Application 진입점과 DI 조립만. 로직 없음

FlexDir.Core.Tests  → Core
                    포트의 fake 와 계약 기반 클래스(*Contract)를 둔다

FlexDir.App.Tests   → App, Core, Core.Tests
FlexDir.Shell.Tests → Shell, Core, Core.Tests

                    두 테스트 프로젝트가 Core.Tests 를 참조하는 이유는 하나다 —
                    fake 와 실물 구현체가 같은 계약 클래스를 상속해 같은 검증을 받는 것

FlexDir.Host.Tests  → Host, Core, Shell, App, Core.Tests, App.Tests

                    App.Tests 까지 참조하는 이유는 InlineUiDispatcher 하나다.
                    IUiDispatcher 는 App 의 포트라 그 fake 를 Core.Tests 에 둘 수 없고
                    (Core 는 App 을 모른다), 조립을 WPF Application 없이 세우려면
                    그 fake 가 필요하다. 같은 물건을 두 벌 두지 않는다
```

**`App` 이 `Shell` 을 참조하면 컴파일이 깨진다.** 이것이 구조 게이트다.
`Host` 를 따로 둔 이유가 이것뿐이다 — DI 조립은 두 쪽을 모두 알아야 하므로,
그 지식을 로직 없는 프로젝트 하나에 가둔다.

**`FlexDir.App` 에 Win32 호출이 하나 있다 — 지우지 마라** (사용자 결정 2026-08-06).
`Views/ListInput.cs` 의 `GetDoubleClickTime`. 재클릭 이름변경을 더블클릭과 구분하려면
그 값이 필요한데 WPF 가 `ClickCount` 안에서만 쓰고 밖으로 열지 않는다 —
`SystemParameters.MinimumHorizontalDragDistance` 와 같은 성격의 시스템 메트릭이다.
**금지 대상은 `Core` 이지 `App` 이 아니다.** interop 을 `Shell` 이 맡는다는 것은 관례이고,
이 한 줄은 shell 이 아니라 입력 장치를 묻는다. `LibraryImport` 가 아니라 `DllImport` 인
이유도 거기 적혀 있다 — 호출 하나 때문에 `App` 전체에 `AllowUnsafeBlocks` 를 켜지 않는다.

## 2. 포트 (Core 가 정의, Shell 이 구현)

| 포트 | 책임 |
|---|---|
| `IFolderSource` | 경로를 받아 항목을 **비동기 스트림**으로 낸다. 취소 가능 |
| `IThumbnailSource` | 항목의 썸네일/아이콘. 비동기, 취소 가능. 메서드 셋이 각각 다른 것을 답한다 — `GetThumbnailAsync`(내용 미리보기) · `GetTypeIconAsync`(확장자별 형식 아이콘) · `GetItemIconAsync`(경로별 항목 아이콘 — 알려진 폴더 메뉴가 쓴다, v0.8.1 · docs/PRD-v2.md §20) |
| `IContextMenuProvider` | shell 컨텍스트 메뉴 표시와 명령 실행 |
| `IClipboardBridge` | 탐색기 호환 복사/잘라내기/붙여넣기 |
| `IFileOperations` | 복사·이동·삭제(휴지통)·이름변경·새 폴더 |
| `IFolderWatcher` | 외부 변경 알림 |
| `IViewStateStore` | 폴더별 뷰 모드·정렬 저장/복원 |
| `IUsageLog` | 실행·사용 시간 기록 |
| `IItemActivator` | 더블클릭 시 연결 프로그램 실행 |
| `IDriveSpace` | 위치가 속한 볼륨의 여유/전체 용량 (상태표시줄) |
| `IKnownFolderList` | 알려진 폴더 다섯(홈·바탕화면·문서·다운로드·사진)의 위치. **조회가 저장소에 닿는다** — 리디렉션된 폴더(OneDrive·도메인 로밍)에서는 네트워크로 내려간다. 구현 `Shell/Locations/KnownFolderList.cs` 는 COM 이 아니라 STA 도 정리도 필요 없다 (`AppComposition` 정리 목록에 없다) |
| `IExternalToolCatalog` | 설치된 외부 도구를 찾는다 — `FindEditorAsync(ct)` · `FindTerminalAsync(preset, ct)`, 둘 다 `ValueTask<string?>`. **실패는 `null`, 취소만 예외.** **캐시하지 않는다** — 상주 앱이라 창이 다시 보일 때마다 다시 묻는다 (v0.8.5 · docs/PRD-v2.md §21). 구현 `Shell/Tools/AppPathsToolCatalog.cs` (COM 이 아니라 STA 도 정리도 없다) |
| `IExternalToolLauncher` | 외부 도구를 그 폴더를 작업 디렉터리로 띄운다 — `LaunchAsync(executable, arguments, workingFolder, ct)` → `ValueTask<LocationErrorKind>`. **실패는 `LocationErrorKind`, 던지지 않는다** — 실패가 정상 상황이라 호출자가 상태표시줄 한 줄로 만든다 (`None` 이 성공). 구현 `Shell/Tools/ProcessToolLauncher.cs` |

포트는 **경로를 문자열이 아니라 `LocationId` 로** 주고받는다. v1 은 로컬
파일시스템 경로만 담지만, v2 의 UNC·shell 네임스페이스(PIDL)를 같은 타입으로
표현할 수 있어야 한다. 이 추상화가 없으면 v2 에서 전면 수정이 된다.

## 3. 데이터 흐름

```
사용자 입력
   → ViewModel (App)
   → 포트 인터페이스 (Core)
   → 구현체 (Shell)          ← 여기부터 UI 스레드 밖
   → 결과를 비동기로 ViewModel 에 반영
   → 데이터 바인딩으로 View 갱신
```

**UI 스레드에서 shell 을 부르지 않는다.** 파일 탐색기가 멈추는 원인은 렌더링이
아니라 동기 shell 호출이다.

## 4. 상태

| 상태 | 소유자 | 수명 |
|---|---|---|
| **탭별** 현재 경로·히스토리 | `PaneViewModel` (탭 하나가 인스턴스 하나다) | 프로세스 |
| 항목 목록 · 선택 | `PaneViewModel` | 폴더 전환까지 |
| **탭 목록 · 활성 탭** | `PaneTabsViewModel` (페인마다 하나) | 프로세스 (목록 자체는 영구 저장) |
| 뷰 모드 · 정렬 | `IViewStateStore` | 영구 (**폴더별** — 탭이 갖지 않는다) |
| **분할 수 · 비율 둘** · 창 위치 · **페인별 탭 목록·컬럼 폭** | `IViewStateStore` | 영구 (전역) |
| 썸네일 | shell 캐시 | **우리가 소유하지 않는다** |
| 사용 로그 | `IUsageLog` | 영구 (append) |

**진실원천은 파일시스템이다.** 위 항목 중 목록·썸네일은 캐시이며, 어긋나면
파일시스템을 믿는다.

### 탭이 소유 구조를 한 단 깊게 한다 (docs/PRD-v2.md §17 · ADR-018 · §18 · ADR-019)

```
WorkspaceViewModel ──> PaneTabsViewModel ×1~4   (AllPanes — 접힌 것까지)
                              │                  Panes = 앞에서부터 SplitCount 개가 화면에
                              ├─ Tabs   : PaneViewModel ×N   전부 살아 있다
                              └─ Active : PaneViewModel      이 중 하나

WorkspaceViewModel.ActivePane : PaneTabsViewModel   활성 페인 (인스턴스로 든다)
WorkspaceViewModel.ActiveTab  == ActivePane.Active  (파생 속성 — 아래 참조)
WorkspaceViewModel.OtherPane  : PaneTabsViewModel?  직전에 활성이던 <b>보이는</b> 페인 (MRU)
```

**`PaneSide { Left, Right }` 는 없다** (분할이 들어온 2026-08-12에 사라졌다). 자리는 번호이고,
어느 번호가 화면 어느 칸에 앉는지는 **View 만 안다** (`Views/SplitLayout.cs`) — ViewModel 이
화면의 기하를 알면 배치를 바꿀 때마다 두 계층을 함께 고쳐야 한다.

**`OtherPane` 이 nullable 인 것은 1분할 때문이다.** 갈 곳이 없다는 것을 타입으로 말한다 —
자기 자신을 내면 '다른 페인으로 복사' 가 제자리 복사가 되고 그것은 조용히 파일을 부른다.

**View 가 무는 자리는 2단이다** (탭 줄이 선 2026-08-11에 이렇게 됐다):

```
ContentControl  Content={Binding Pane0}  Template=PaneWithTabs   ← 탭 줄 + 페인 크롬
  └ ContentControl  Content={Binding Active}  Template=PaneTemplate ← 페인 하나
```

**슬롯 넷을 `Panes[n]` 이 아니라 속성 넷(`Pane0`~`Pane3`)으로 문다.** 인덱서 바인딩은 목록이
짧을 때 **조용히** 실패한다 — 이것이 아래 교훈의 두 번째 적용이다.

**`PaneTemplate` 의 계약은 그대로다** — 여전히 `PaneViewModel` 을 물고, 그 안의 바인딩 수십
개가 손대지 않은 채 산다. 타입을 바꾼 것은 `ActivePane` 하나뿐이고 (탭 → 페인), **그래서
XAML 의 `ActivePane.*` 열둘을 `ActiveTab.*` 로 함께 옮겼다**: WPF 바인딩은 런타임 조회라
타입을 바꾸면 템플릿 안 바인딩이 **컴파일 에러 없이 조용히 죽는다**
(docs/PRD-v2.md §17 값을 치르고 배운 것).

**바깥이 한 겹 늘어난 이유는 수명이다.** 활성 탭이 바뀌면 안쪽 `ContentControl` 이 무는
`PaneViewModel` 이 바뀌어 **페인 트리가 통째로 다시 선다.** 탭 줄을 `PaneTemplate` 안에 두면
지금 누르고 있는 그것이 매 전환마다 새로 서고 가로 스크롤 위치와 드래그 상태가 함께 사라진다
— 그래서 줄은 탭보다 오래 사는 바깥 층에 있다.

**`ActiveTab` 을 지나는 배선 일곱** — 트리 따라가기(양방향) · 즐겨찾기 고정 · 설정의 시작
폴더 지정 · 페인 간 복사 · 이동 · 다른 페인 폴더 열기 · 마우스 뒤로/앞으로. 페인이 런타임에
생기므로 **페인별 구독은 만드는 자리 하나(`Wire`)에서만 건다** — 다만 트리의
`NavigationRequested` 는 창에 하나뿐이라 생성자에 남는다 (여기를 헷갈려 한 번 끊었고
테스트가 잡았다).

**감시는 활성 탭만 든다.** 배경 탭은 목록을 메모리에 지닌 채 `IFolderWatcher` 를 놓고,
다시 활성이 될 때 새로 고침 한 번을 돌린 뒤 감시를 건다 — 근거는 docs/PRD-v2.md §13
(감시 오버플로 폭주)이다. **접힌 페인도 같다**: 화면에 없는 페인이 감시를 들면 그 폭주가
상태표시줄 문구조차 못 보는 자리에서 돈다 (§18). 그래서 접기(`SuspendAsync`)와
펴기(`ResumeAsync`)가 짝이고, 탭 전환과 **같은 여는 경로**(`OpenAsync`)를 나눠 쓴다.
**정리는 `AllPanes` 를 지난다**: `AppComposition.DisposeAsync` 가 **접힌 페인까지** 모든
탭을 접은 뒤에 shell 구현체를 닫는다 (순서를 뒤집으면 진행 중인 썸네일 요청이 닫힌
STA 큐에 들어가고, 접힌 것을 빼먹으면 그 탭들이 그대로 남는다).

저장 위치: `%APPDATA%\flex-dir\`

> **`%LOCALAPPDATA%` 가 아니다** (2026-08-07에 옮겼다). 인스톨러(Velopack)가
> `%LOCALAPPDATA%\flex-dir\` 에 **설치**하는데 그것이 예전 저장 위치와 같은 경로였고,
> 첫 설치가 여기 있던 `usage.log` 를 실제로 지웠다 — 그것은 도그푸딩 게이트(ADR-007)의
> 입력이다. **사용자 데이터를 설치기가 관리하는 폴더에 두지 않는다** (docs/PRD-v2.md §9).

## 5. 리스트 가상화 — 깨뜨리면 안 되는 것

WPF 데이터 가상화는 쉽게 무력화된다. 다음을 금지한다:

- `ItemsControl` 을 `ScrollViewer` 로 감싸기 → 가상화 완전 해제
- `VirtualizingPanel.IsVirtualizing="False"`
- 가변 행 높이 (`VirtualizationMode=Recycling` 과 상충)
- `ItemsSource` 에 `IEnumerable` 전체 열거를 강제하는 LINQ 체인

필수: `VirtualizingStackPanel` + `VirtualizationMode="Recycling"` +
`ScrollUnit="Item"`.

### wrap 뷰 3종은 합성 행으로 이 규칙을 지킨다 (ADR-016)

뷰 4종 중 셋이 wrap 레이아웃인데(목록·타일·큰 아이콘) **WPF 가 기본 제공하는 가상화
패널은 `VirtualizingStackPanel` 하나뿐이고 `WrapPanel`·`UniformGrid` 는 가상화하지
않는다.** 그래서 한 줄에 N개를 담은 **행 항목**을 만들어 세로(목록 뷰는 가로)만
가상화한다.

- **열 수는 `PaneViewModel` 이 정한다.** View 는 `SetViewportSize(width, height)` 로
  뷰포트 크기만 민다 — 항목 폭 표(`DESIGN.md` §2)를 아는 쪽이 ViewModel 이기 때문이고,
  그래야 묶음 규칙 전체가 테스트로 채점된다. `SetVisibleRange`·`IconSize` 와 같은 경계다.
- **Details 는 합성 행을 지나지 않는다.** 평평한 `Items` 를 그대로 쓴다. 10만 항목의 주
  경로에 래퍼를 두지 않기 위해서이고, `MergeItems` 의 증분 갱신(ADR-011)이 그대로
  살아야 하기 때문이다. **그룹화를 켰을 때만 예외다** — 아래 참조.
- **목록 뷰만 가로 스크롤이다.** 세로로 채우고 다음 열로 넘어가므로 청크 단위가 행이
  아니라 열이고, 개수는 폭이 아니라 **높이**로 정해진다.
- **열 수가 바뀔 때만 다시 만든다.** 폭이 변해도 열 수가 그대로면 아무 일도 하지 않는다.
  시간 디바운스를 쓰지 않는다.
- **크기·보이는 범위를 미는 배선은 목록 컨트롤이 아니라 `ItemsPanel` 에 건다.** 가상화
  패널이 스크롤 주인(`IScrollInfo`)이라 그 `RenderSize` 가 곧 픽셀 뷰포트다.
  `ListBox` 의 크기에는 스크롤바가 들어 있어 마지막 칸이 잘리고, `ScrollViewer` 의
  `Viewport*` 는 `ScrollUnit="Item"` 아래에서 **스크롤 축이 픽셀이 아니라 항목 수**다 —
  PageUp/Down 이 그 축의 픽셀을 쓰므로 거기서 읽으면 조용히 틀린다.
- **반대로 스크롤을 *시키는* 배선은 목록 컨트롤에 건다** (`Views/FocusScroll.cs`).
  `ScrollIntoView` 가 목록 컨트롤의 API 이고 받는 것도 **목록의 원소**다 — Details 는
  항목, wrap 뷰 3종은 행. 바로 위 규칙을 기계적으로 적용해 이것까지 `ItemsPanel` 로
  옮기면 안 된다. **이것이 없으면 이동·선택을 내장에 맡기지 않는 대가로 화면이 포커스를
  따라오지 않는다** — B-3 이 그 상태로 끝났고 B-4 실물에서 드러났다.

### 그룹화도 같은 수로 지킨다 (ADR-017)

`PRD.md` §3 이 그룹핑을 미뤄 둔 사유가 **"전작에서 데이터 가상화를 깨뜨린 기능"** 이었다.
그 사유는 기능이 아니라 WPF 의 `CollectionView.GroupDescriptions` + `GroupStyle` 이라는
**구현 방식**을 가리킨다. **헤더도 행으로 만들면** 목록은 여전히 평평한 한 겹이라 위 규칙이
그대로 산다.

- **그룹 키가 정렬 1차 키다.** 그래야 같은 그룹이 붙어 있고, 그룹 경계를 **정렬된 목록의
  인접 비교**만으로 잡을 수 있다. 그룹마다 순서 번호를 두면 그 번호가 비교기와 어긋나는
  순간 **같은 라벨의 헤더가 목록의 두 자리에** 생긴다.
- **그룹화가 꺼져 있으면 Details 는 평평한 `Items` 그대로다.** 래퍼 비용은 켰을 때만 낸다.

### 목록의 원소 종류가 늘면 View 배선 셋을 함께 고친다

같은 목록 컨트롤에 소스가 셋이다 — 평평한 항목 · 합성 행(ADR-016) · 그룹 행(ADR-017).
**원소 종류로 분기하는 자리가 세 곳 있고, 새 종류를 추가하면서 하나라도 빠뜨리면 조용히
잘못 동작한다.** 그룹화를 넣을 때 뒤의 둘을 실제로 빠뜨렸다.

| 자리 | 빠뜨리면 |
|---|---|
| `Views/ViewConverters.cs` `ViewSourceConverter` | 그 뷰가 아무것도 표시하지 않는다 (바로 보인다) |
| `Views/FocusScroll.cs` `Target` | **포커스는 움직이는데 화면이 따라오지 않는다** |
| `Views/VisibleRangeSync.cs` `Flatten` | 보이는 범위가 늘 비어 **아이콘이 끝까지 빈칸**이다 (실패가 캐시된다 — `PRD.md` §4) |

뒤의 둘은 **화면을 봐도 원인이 안 보인다** — 앞의 하나만 맞으면 목록은 정상으로 보인다.

전작은 속도를 위해 `LVS_OWNERDATA` 를 골랐고, 그 대가로 뷰 모드가 Details
하나로 고정됐다. WPF 에서는 `DataTemplate` 교체로 뷰 모드가 바뀌고 가상화는
유지된다 — **그 이점을 잃지 않는 것이 위 규칙의 목적이다.**

## 6. 상주 프로세스

- single instance. 두 번째 실행은 기존 프로세스에 인자를 넘기고 종료한다.
- 창을 닫아도 프로세스는 유지한다 (트레이 또는 무창 상태).
- 완전 종료는 명시적 메뉴로만.
- 이유: WPF 의 유일한 실질 약점이 cold start 다. 한 번만 낸다.

## 7. 계측

별도 벤치마크 CLI 를 만들지 않는다. 전작은 그걸 만들고 **실사용과 무관한 것을**
쟀다. 계측은 앱 안에 두고 실제 사용 경로에서 측정한다.

측정 대상: cold start · 상주 중 창 표시 · 폴더 전환 후 첫 항목 표시.

금지 대상은 **재는 도구**다. Shell 포트를 실물에 물려 보는 수동 확인용 프로브는 여기
해당하지 않으며 `.harness/probe/` 에 sln 밖으로 둔다 (ADR-015).
