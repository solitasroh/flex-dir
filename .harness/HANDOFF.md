# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 갱신: 2026-08-05. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) — 수동 phase 계획과
> 사람 확인 항목(프로브·실물 실측 결과가 거기 있다). 세션 커밋 이력은 `git log` 를 본다.

## 한 줄

A(포트 8/8) · C(Host 뼈대) · B-1(ViewModel 확장) · **B-2 골격(창 + Details)** 까지 끝났다.
**앱이 창을 띄운다** — 실행하면 MainWindow 가 뜨고 Details 로 탐색·선택·조작이 된다(코드상).
**실물 확인은 아직이다** — `manual-plan.md` §B-2 사람 확인 항목이 다음 세션의 첫 일이다.

## 현재 상태

```
브랜치   main  ·  origin/main 보다 앞 (B-1·B-2 작업분, 푸시하지 않았다)
테스트   936 통과   Core 341 · App 363 · Shell 190 · Host 42
게이트   fast (build -warnaserror · test --blame-hang · check-structure) ✅
         full (Release build -warnaserror) ✅
phases/  0-core-model · 1-core-pipeline · 2-viewmodel 모두 completed
```

푸시는 `CLAUDE.md` §7 대로 별도 지시가 있을 때만. 원격은 `origin`
(`git@github.com:solitasroh/flex-dir.git`).

```
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
```

## 포트 현황 — 9/10

| 포트 | 구현 | 테스트 |
|---|---|---|
| `IViewStateStore` | `Shell/ViewState/JsonViewStateStore.cs` | 계약 12 + 고유 10 |
| `IFolderWatcher` | `Shell/Watching/FileSystemFolderWatcher.cs` | 계약 4 + 실물 5 |
| `IFolderSource` | `Shell/Enumeration/FileSystemFolderSource.cs` | 계약 6 + 실물 10 |
| `ITypeNameProvider` | `Shell/Presentation/ShellTypeNameProvider.cs` | 13 |
| `IThumbnailSource` | `Shell/Presentation/ShellThumbnailSource.cs` | 23 |
| `IItemActivator` | `Shell/Activation/ShellItemActivator.cs` | 16 |
| `IFileOperations` | `Shell/Operations/ShellFileOperations.cs` | 48 |
| `IClipboardBridge` | `Shell/Operations/ShellClipboardBridge.cs` | 24 |
| `IUsageLog` | `Shell/Usage/FileUsageLog.cs` | 9 + fake 3 |
| `IUiDispatcher` (App 의 포트) | `App/Threading/WpfUiDispatcher.cs` | 8 |

공용 배관은 `Interop/StaWorkQueue.cs`. **`IContextMenuProvider` 는 `FlexDir.Core` 에
포트 정의부터 없다** — 창 핸들과 메뉴 메시지 펌핑이 필요해 의도적으로 미뤘다(§열린 결정).

확인용 프로브 `.harness/probe/` (ADR-015):
`typeicons` · `thumbnail <경로>` · `leak [폴더]` · `activate <경로>` · `recycle` ·
`clipboard copy|cut|paste`. **뒤의 셋은 프로그램을 띄우고 휴지통에 항목을 남기고 사용자의
클립보드를 덮어쓴다** — 자동 테스트가 하지 않는 일이라 거기 있다.
실측 결과는 `manual-plan.md` 사람 확인 항목에 있다.

## Host 가 지금 하는 일 (phase C 결과)

```
Program.Main  (명시적 Main. App.xaml 로 가지 않았다 — manual-plan §C)
  └ SingleInstanceGate.Acquire       뮤텍스로 판정, 파이프로 인자 전달
      ├ 두 번째 실행 → SendAsync 하고 exit 0
      └ 상주 프로세스 →
          Application { ShutdownMode = OnExplicitShutdown }
          AppComposition.Create(WpfUiDispatcher, %LOCALAPPDATA%\flex-dir)
          ActivationRouter  ← 실행 + 활성화마다 IUsageLog 기록 · 인자의 폴더를 활성 페인에서
          PerformanceLog    ← ColdStart 를 perf.log 에
          application.Run() … 창 없이 상주
          (완전 종료 뒤) composition.DisposeAsync()
```

**실물에서 잰 것** (`manual-plan.md` §C 사람 확인 항목에 전문):
cold start **358ms**(첫 실행) / **123~134ms**(이후) — 목표 1.5s.
두 번째 실행은 **124ms 에 exit 0** 이고 프로세스는 하나로 유지된다.
상주 프로세스를 죽이면 다음 실행이 새 상주 프로세스가 된다.

**아직 못 잰 것**: 상주 중 창 표시(100ms) · 폴더 전환 후 첫 항목(150ms) ·
"창을 닫아도 프로세스가 사는가" · 완전 종료 경로. **넷 다 창이 있어야 한다 — phase B.**
계측 지점(`MeasurementPoint.WindowShown`·`FirstItem`)과 예산은 이미 코드에 있다.

## 다음 작업 — phase B (View)

### 시안은 이미 있다 — `docs/mockups/v1-two-pane.html`

**248줄짜리 인터랙티브 목업**이다(`docs/DESIGN.md:5` 에서 링크). 뷰 4종 · 상태 5종을
버튼으로 전환할 수 있고 스플리터도 실제로 끌린다. **브라우저로 열어 보고 시작한다.**

`DESIGN.md` §1~§8 + 목업이 이미 확정한 것 — XAML 은 받아쓰면 된다:
뷰 4종 레이아웃·치수 · Details 컬럼 폭 · 색 토큰 전부 · 활성/비활성 페인 표현 ·
상태 5종(일반 · 빈 폴더 · 권한 없음 · 열거 중 진행바 · 썸네일 로딩) · 상태표시줄 · 툴바.

### 선행조건은 없다 — `docs/DESIGN.md` §9 를 채웠다

키보드 맵 · 이름변경 인라인 편집 · 스플리터 드래그 중 · 페인 간 드래그앤드롭이 전부
`DESIGN.md` §9 · §9-1 에 확정돼 있다. **바로 착수할 수 있다.**

착수 전에 읽을 것: `DESIGN.md` §9(키보드·상호작용) · `ARCHITECTURE.md` §5(합성 행) ·
**ADR-016**(왜 합성 행인가, 기각한 셋).

세 가지가 이 phase 의 모양을 정한다.

- **뷰 3종은 합성 행 위에 선다** (ADR-016). `PaneViewModel` 이 `Rows` 를 소유하고 View 는
  `SetViewportSize(width, height)` 만 민다. **Details 는 평평한 `Items` 그대로**다.
- **이동·선택을 `ListView` 에 맡기지 않는다.** `FocusedName` + `MoveFocus(...)` 를 새로
  만든다. 합성 행 위에서 내장 이동은 행 단위로만 움직이기 때문이다.
- **드래그앤드롭에 `FlexDir.Shell` 이 필요 없다.** WPF `System.Windows.DataObject` 가
  CF_HDROP 마샬링을 대신한다(컴파일로 확인). 포트를 늘리지 말고 `PaneViewModel` 에
  `DropAsync(paths, targetFolder, isMove)` 만 더한다.

### B-1 이 만든 표면 — 전부 들어왔고 전부 자동 채점된다

| 표면 | 비고 |
|---|---|
| `SetViewportSize(double w, double h)` | **열 수가 바뀔 때만** `Rows` 를 다시 묶는다. 디바운스 없음 |
| `Rows : IReadOnlyList<RowViewModel>` | wrap 3종 전용. Details 는 평평한 `Items` 그대로 (ADR-016) |
| `FocusedName` · `MoveFocus(...)` | 2D 이동 · Shift 확장 · Ctrl 포커스만. 폴더 전환이 포커스를 접는다 |
| `TypeAhead(char)` | 리셋 시한 1초. `TimeProvider` 는 ctor **선택 주입**(기본 시스템 시계) |
| `DropAsync(paths, targetFolder, isMove, ct)` | 경로 파싱 포함. 제자리 **이동**만 걸러낸다 (복사는 허용) |
| `GoBack`·`GoForward`·`GoUp`·`RefreshCommand` | 네비게이션 넷이 커맨드로도 노출된다. 메서드 표면은 그대로다 |

**B-2 (View) 가 알아야 하는 것**

- 뷰 3종의 `ItemsSource` 는 `Rows`, Details 는 `Items` 다. View 가 미는 것은
  `SetViewportSize` 와 `SetVisibleRange` 둘뿐이다.
- PageUp/Down 의 '한 화면' 은 `DESIGN.md` §2 치수에서 유도했다 — Details 24 ·
  목록 열 폭 212(200+12) · 타일 60(56+4) · 큰 아이콘 148(140+8). §2 가 바뀌면
  `PaneViewModel` 의 `*Pitch` 상수도 같이 바꿔야 한다.
- 포커스가 없을 때 첫 방향키는 첫 항목에서 시작한다 (`End` 만 마지막). `Ctrl+Space` 는
  View 가 `Selection.Toggle(FocusedName)` 로 잇는다 (§9 키보드 맵 그대로).

### 그 다음 (`manual-plan.md` §B 가 목록이다)

- **창을 만들고 `WorkspaceViewModel` 을 `DataContext` 로 건다.** 조립은 이미 있다 —
  `AppComposition.Create` 가 `Workspace` 를 낸다. `Program.RunResident` 에 창을 띄우는
  줄을 더하고, 활성화 때 창을 앞으로 가져오는 경로를 `ActivationRouter` 에 붙인다.
- **`SetVisibleRange` 를 미는 attached behavior.** ViewModel 쪽 배선은 끝났다 —
  `PaneViewModel.SetVisibleRange(IReadOnlyList<FileItemViewModel>)` 를 스크롤·뷰 전환·
  목록 갱신 셋에서 부르면 된다. `*.xaml.cs` 에 스크롤 핸들러를 두는 것은
  `scripts/check-structure.ps1` 이 막는다.
- **계측 두 개를 붙인다.** `PerformanceLog.RecordAsync(MeasurementPoint.WindowShown, …)` 와
  `FirstItem`. 예산은 `PerformanceLog.Budget` 이 정본이다.
- **`IContextMenuProvider`** — 포트 정의부터. 창 핸들이 필요해 여기로 미뤘다.

## 반드시 알아야 하는 규칙 (값을 치르고 배운 것)

1. **shell 호출은 STA 에서만.** `SHGetFileInfo`·`IFileOperation`·`IContextMenu` 는 STA 를
   요구하고 `Task.Run`·스레드풀은 MTA 다 (`SHELL_NOTES.md` §COM 아파트먼트).
   `Interop/StaWorkQueue.cs` 를 쓴다. `ShellTypeNameProvider` 를 처음 `Task.Run` 으로 썼고
   테스트 60개가 다 초록이었지만 규칙 위반이었다 — 지금은 아파트먼트를 테스트가 고정한다.
2. **계약 기반 클래스가 경로를 박아두면 실물이 만족할 수 없다.**
   `FolderWatcherContract`·`FolderSourceContract` 가 `C:\Temp\Docs` 를 고정해 둬서
   `WatchedFolder`·`SourceFolder` 추상 멤버를 추가했다. fake 만 상속하는 동안에는 안 드러난다.
3. **`HResult` 에서 Win32 코드를 꺼낼 때 마스크를 `int` 로 못박아라.** `0xFFFF0000` 은 uint
   리터럴이라 그대로 쓰면 양쪽이 long 으로 승격되고 음수 HResult 가 부호 확장돼 **비교가
   영원히 거짓**이 된다. 테스트는 예외 타입 폴백 때문에 통과했고 실물에서야 드러났다
   (`FileSystemFolderSource.Translate`·`ShellFileOperations.Translate` 의 `FacilityMask`).
4. **게이트의 테스트에 `--blame-hang` 이 붙어 있다.** hang 을 실패로 바꾼다. 뺐다가
   `dotnet test` 가 무한 대기하면 실행기까지 교착된다 (실제로 그랬다).
   같은 이유로 Host 테스트의 파이프·Dispatcher 대기에는 전부 시한이 붙어 있다.
5. **shell 이 대화상자를 띄우는 경로는 자동 테스트에서 밟을 수 없다.** 그 호출은 사용자의
   답을 기다리며 블로킹해 `--blame-hang` 에 걸린다 — 실패가 아니라 **매달림**이 된다.
   실행 지점을 `internal` 생성자로 바꿔 끼우고 실물은 프로브로 본다. 부작용도 같은 기준이다:
   프로세스를 띄우거나 휴지통에 넣거나 클립보드를 덮어쓰는 것은 게이트가 돌 때마다 일어나면 안 된다.
   **파일을 쓰는 테스트는 `Path.GetTempPath()` 아래에서만 쓴다** — 실제
   `%LOCALAPPDATA%\flex-dir\` 를 건드리면 도그푸딩 게이트가 자기 테스트 실행을 사용으로 센다.
6. **`ARCHITECTURE.md` §7 이 금지하는 것은 재는 벤치마크 CLI 다.** 포트 구현체를 실물에 물려
   보는 확인용 프로브는 다르다 — ADR-015 가 선을 긋고 `.harness/probe/` 를 sln 밖에 둔다.
   계측은 앱 안(`Host/Diagnostics/PerformanceLog.cs`)에 있고 결과는 `perf.log` 로 나온다.
7. **`Program.cs` 는 TDD 가드의 검사 대상이 아니다.** 그래서 판단을 한 줄도 두지 않았다.
   조립·활성화·single instance 는 전부 채점되는 클래스 안에 있다 — 그 선을 넘기면
   채점되지 않는 자리에 로직이 자란다.

## 구현체가 다음 phase 에 넘긴 사실

**phase B(View)가 틀리기 쉬운 것**

- **썸네일·아이콘은 premultiplied BGRA 다.** `PixelFormats.Bgra32` 가 아니라 **`Pbgra32`** 로
  `WriteableBitmap` 을 만든다. 틀리면 반투명 가장자리가 어둡게 번진다.
- 96 요청이 `SHIL_JUMBO` 의 **256×256(256KB)** 을 낸다. 축소는 View 가 하고 확장자 캐시는
  비우지 않으므로 상주 프로세스에서 쌓인다.
- **`SetOwnerWindow` 를 부르지 않았다.** Shell 계층은 창을 모르므로 shell 대화상자에 소유
  창이 없다. 창이 생긴 뒤 다시 본다 — 소유 창을 주려면 포트에 창 핸들을 흘려야 하고,
  그것 자체가 결정거리다.
- **`ViewMode` → 아이콘 크기는 `PaneViewModel` 안에 있다** (16·16·32·96, `DESIGN.md` §2).
  View 가 크기를 계산해 넘기지 않는다 — 그 표가 두 계층으로 갈린다.

**Host 를 고칠 때**

- **STA 를 든 shell 구현체는 다섯이다** — `ShellTypeNameProvider`·`ShellThumbnailSource`·
  `ShellFileOperations`·`ShellClipboardBridge`·`ShellItemActivator`. 이 문서가 한동안 넷이라고
  적고 있었고 빠진 것은 첫 번째다. `AppCompositionTests` 는 **소유 타입 집합**으로 다섯을
  고정하고, 종료는 **부작용 없는 조회 둘**(`ShellThumbnailSource`·`ShellTypeNameProvider`)로만
  확인한다 — 나머지 셋으로 물어보면 정리가 안 됐을 때 프로그램이 뜨고 휴지통에 항목이
  남고 클립보드가 덮인다 (§규칙 5).
- **정리 순서는 페인 → shell 구현체다.** 뒤집으면 진행 중 요청이 닫힌 STA 큐에 들어가
  관측되지 않는 예외가 된다.
- **`WpfUiDispatcher` 는 종료 중인 `Dispatcher` 에서 조용히 물러난다.** 완전 종료 경로가
  ViewModel 정리를 지나며 이 자리를 밟기 때문이다 — 예외로 만들면 STA 워커가 남는다.

**COM 을 또 쓸 때**

- 선언은 **`[ComImport]` 전통 방식**. `[GeneratedComInterface]` 는 어셈블리 전체에
  `DisableRuntimeMarshalling` 을 요구해 런타임 마샬링에 기대는 다른 interop 까지 묶는다.
- **부르지 않는 vtable 슬롯도 순서대로 선언해야 한다.** COM 호출은 이름이 아니라 순서로 간다.
  단, **shell 이 우리를 부르는 인터페이스(CCW)는 전부 실제로 구현해야 한다** —
  `ShellFileOperations.NewItemSink` 의 16개 메서드가 그래서 다 있다.
- `Marshal.GetObjectForIUnknown` 은 자기 참조를 따로 잡는다. **원시 포인터의
  `Marshal.Release` 와 RCW 의 `ReleaseComObject` 를 둘 다** 한다
  (`ShellFileOperations.ComRef<T>` 가 그 둘을 묶는다).

**되돌릴 수 없는 것을 막고 있는 자리**

- 삭제는 `FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE` 다. 앞의 것만으로는 휴지통 할당량을 넘을 때
  Windows 가 말없이 영구 삭제로 바꾼다. `Delete_ForcesTheRecycleBin` 이 이것을 지킨다.
- 잘라내기 표시는 `Preferred DropEffect` 4바이트뿐이다. 없으면 탐색기가 복사로 붙여넣는다.
  읽을 때 표시가 없으면 복사로 본다 — 이동으로 오해하면 남의 파일이 사라진다.

## 열린 결정 · 미완

- [ ] **`IContextMenuProvider`** — A 잔여. 포트 정의부터 수동이고 창 핸들이 필요하므로
      창이 생기는 phase B 와 함께 본다 (`SHELL_NOTES.md` §컨텍스트 메뉴).
- [ ] **완전 종료 조작이 없다.** `ShutdownMode = OnExplicitShutdown` 만 걸려 있고
      `Application.Shutdown()` 을 부르는 자리가 아직 없다 — phase C 의 프로세스는
      `Stop-Process` 로 끝냈다. 메뉴가 생기는 phase B 에서 그 경로를 실제로 밟는다.
- [ ] **클라우드 자리표시자 확인 불가** — 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다. `SHELL_NOTES.md` §열거 함정 3 의 핵심이고
      틀리면 스크롤만으로 수 GB 를 내려받는다. 동기 중인 OneDrive 가 있는 기계가 필요하다.
- [ ] **대화상자가 뜨는 실패 경로** — 활성화(연결 프로그램 없음·취소)와 파일 조작 실패는
      자동 실행으로 확인할 수 없다(블로킹). `manual-plan.md` 사람 확인 항목에 있다.
- [ ] **Host 테스트가 한 번 간헐 실패했다 — 원인 미확정.** 전체 솔루션 실행 1회에서
      `FlexDir.Host.Tests` 40개 중 1개가 실패했으나 **이름을 잡지 못했다**(출력 필터가
      `[FAIL]` 줄을 걸러냈다). 그 실행에 있던
      `DisposeAsync_DisposesEveryShellImplementationThatHoldsAnStaThread` 는 정리 여부를
      **활성화·파일 조작·클립보드 호출로** 물어보고 있었다 — 정리가 안 된 경우에 프로그램이
      뜨고 휴지통에 항목이 남고 사용자의 클립보드가 덮이는, §규칙 5 위반이다. 그래서
      조회 둘(`ShellThumbnailSource`·`ShellTypeNameProvider`) + 소유 타입 집합 단정으로
      바꿨다. 그 뒤 전체 실행 **14회가 전부 초록**이지만 재현되지 않았으므로 **그것이
      원인이었다고 적지 않는다.** 다시 나오면 `--logger trx` 로 이름부터 잡는다.

## 순서

```
A. Shell interop   포트 8/8 ✅   잔여: IContextMenuProvider
C. Host 뼈대        ✅  진입점 + DI + single instance + IUsageLog + 계측, 화면 없이 조립까지
B. View            ← 진행 중. B-1 완료 — 다음은 B-2 (창 + Details)
→ 매일 쓰기 → 도그푸딩 게이트 ON (ADR-007, v1 완성 후)
```

**phase B 를 쪼갠다면 이 순서를 권한다** (자율 실행 대상이 아니므로 `phases/` 에 넣지 않는다):

```
B-1  ViewModel 확장   ✅ Rows · FocusedName · MoveFocus · TypeAhead · DropAsync
                     + 네비게이션 커맨드 4개.  화면 없이 전부 채점됐다 (App 258→321)
B-2  창 + Details     ◐ 골격 완료 — MainWindow(Views/) · 입력 커맨드 · ListInput ·
                     PaneChrome · 활성화가 창 표시. 남은 것: 사람 확인(manual-plan §B-2) ·
                     WindowShown/FirstItem 계측 · 완전 종료 메뉴 · 스플리터 저장 배선
                     → 여기서 이미 매일 쓸 수 있다. 계측 둘을 붙인다
B-3  나머지 뷰 3종     합성 행 템플릿 · attached behavior(SetVisibleRange·SetViewportSize)
B-4  상호작용         이름변경 인라인 편집 · 드래그앤드롭 · IContextMenuProvider
```

B-1 을 먼저 두는 이유: 그 덩이가 **화면 없이 전부 자동 채점된다.** 화면부터 붙이면
2D 이동 규칙의 버그와 XAML 버그가 섞여서 나온다 (`CLAUDE.md` §5).
