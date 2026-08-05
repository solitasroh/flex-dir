# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 갱신: 2026-08-05. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) — 수동 phase 계획과
> 사람 확인 항목(프로브 실측 결과가 거기 있다). 세션 커밋 이력은 `git log` 를 본다.

## 한 줄

자율 실행(Core + ViewModel) 3 phase 와 수동 phase A(`FlexDir.Shell` 포트 **8/8**)가 끝났다.
**다음은 phase C — Host 뼈대**(진입점 + DI + single instance + `IUsageLog` + 계측)이고,
그 전에 **A 잔여인 썸네일 스케줄러 배선**을 닫는다. 둘 다 아래 §다음 작업 에 있다.

## 현재 상태

```
브랜치   main  ·  origin/main 보다 앞서 있다 — 푸시하지 않았다
테스트   759 통과   Core 338 · App 240 · Shell 181
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

## 포트 현황 — 8/8

| 포트 | 구현 (`src/FlexDir.Shell/`) | 테스트 |
|---|---|---|
| `IViewStateStore` | `ViewState/JsonViewStateStore.cs` | 계약 12 + 고유 10 |
| `IFolderWatcher` | `Watching/FileSystemFolderWatcher.cs` | 계약 4 + 실물 5 |
| `IFolderSource` | `Enumeration/FileSystemFolderSource.cs` | 계약 6 + 실물 10 |
| `ITypeNameProvider` | `Presentation/ShellTypeNameProvider.cs` | 13 |
| `IThumbnailSource` | `Presentation/ShellThumbnailSource.cs` | 23 |
| `IItemActivator` | `Activation/ShellItemActivator.cs` | 16 |
| `IFileOperations` | `Operations/ShellFileOperations.cs` | 48 |
| `IClipboardBridge` | `Operations/ShellClipboardBridge.cs` | 24 |

공용 배관은 `Interop/StaWorkQueue.cs` (§규칙 1). **`IContextMenuProvider` 는 `FlexDir.Core` 에
포트 정의부터 없다** — 창 핸들과 메뉴 메시지 펌핑이 필요해 의도적으로 미뤘다(§열린 결정).

확인용 프로브 `.harness/probe/` (ADR-015):
`typeicons` · `thumbnail <경로>` · `leak [폴더]` · `activate <경로>` · `recycle` ·
`clipboard copy|cut|paste`. **뒤의 셋은 프로그램을 띄우고 휴지통에 항목을 남기고 사용자의
클립보드를 덮어쓴다** — 자동 테스트가 하지 않는 일이라 거기 있다.
실측 결과는 `manual-plan.md` 사람 확인 항목에 있다(휴지통·클립보드 양방향·활성화 모두 통했다).

## 다음 작업

### 1. A 잔여 — `ThumbnailRequestScheduler` 배선

`src/FlexDir.App/ViewModels/ThumbnailRequestScheduler.cs` 는 있는데 **`src/` 안에 만드는 곳이
없다.** 그대로 두면 화면을 붙여도 **썸네일이 한 장도 나오지 않는다.** 배선 방식은
`manual-plan.md` §B 에 다섯 항목으로 확정돼 있다 — `PaneViewModel` 이 직접 소유 ·
`LoadAsync` 에서 `Reset()` · attached behavior 가 `SetVisibleRange` · `ViewMode`→크기는
ViewModel 의 private 매핑 · `DisposeAsync` 가 함께 정리.

**확정문과 코드가 어긋나 있다.** 확정문은 `SetVisibleRange` 가 크기를 인자로 받지 않는다고
적었는데 스케줄러의 현재 시그니처는 `SetVisibleRange(visible, requestedSize)` 다.
배선하면서 어느 쪽이 맞는지 정한다.

### 2. phase C — Host 뼈대

`manual-plan.md` §C 가 미결 목록이다. **화면 없이 조립이 끝나는 것까지**가 범위다.
`src/FlexDir.Host/Program.cs` 는 지금 빈 `Main` 이다.

- **진입점 형태** — `App.xaml`(`ApplicationDefinition`) + 빈 `App.xaml.cs` 로 갈지 명시적
  `Main` 을 유지할지. 어느 쪽이든 **조립 코드는 `*.xaml.cs` 가 아닌 별도 클래스**에 둔다
  (CLAUDE.md §2, `check-structure.ps1` 이 막는다).
- **DI 컨테이너 또는 수동 조립** — `FlexDir.Host` 만 `Core`·`Shell`·`App` 셋을 다 안다.
- **single instance 상주** — 두 번째 실행은 기존 프로세스에 인자를 넘기고 종료, 창을 닫아도
  프로세스 유지 (ADR-003 · `ARCHITECTURE.md` §6).
- **`IUsageLog` 는 포트 정의부터 여기서 한다**(확정). ADR-007 도그푸딩 게이트의 입력이다.
  "사용 시간" 이 창 표시 시간인지 프로세스 수명인지가 상주 구조를 짜면서 갈리므로 지금
  인터페이스만 만들면 소비자 없는 추측성 정의가 된다.
- **계측** — cold start · 상주 중 창 표시 · 폴더 전환 후 첫 항목. 목표치는 `PRD.md` §5
  (1.5s · 100ms · 150ms, 전부 잠정). **별도 벤치마크 CLI 를 만들지 않는다**(`ARCHITECTURE.md` §7).

**조립 대상은 전부 있다.** `PaneViewModel` 생성자가 받는 것: `IFolderSource` ·
`IFolderWatcher` · `ITypeNameProvider` · `IViewStateStore` · `IFileOperations` ·
`IClipboardBridge` · `IItemActivator` · `IUiDispatcher` · `IFormatProvider` · `TimeZoneInfo`
(스케줄러 배선을 하면 `IThumbnailSource` 가 하나 더 붙는다). `WorkspaceViewModel` 은
`PaneViewModel` 둘 + `IViewStateStore` 를 받는다.

**Host 가 풀어야 할 두 가지**

- `IUiDispatcher` 의 실제 구현(WPF `Dispatcher` 기반)이 아직 없다. 인터페이스는
  `src/FlexDir.App/Threading/IUiDispatcher.cs` 다.
- **Shell 구현체 넷은 `IDisposable` 이고 안에 STA 스레드를 든다**(`StaWorkQueue`).
  상주 프로세스라 창이 닫혀도 프로세스가 사는데, 그때 누가 언제 `Dispose` 하는가.

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
5. **shell 이 대화상자를 띄우는 경로는 자동 테스트에서 밟을 수 없다.** 그 호출은 사용자의
   답을 기다리며 블로킹해 `--blame-hang` 에 걸린다 — 실패가 아니라 **매달림**이 된다.
   실행 지점을 `internal` 생성자로 바꿔 끼우고 실물은 프로브로 본다. 부작용도 같은 기준이다:
   프로세스를 띄우거나 휴지통에 넣거나 클립보드를 덮어쓰는 것은 게이트가 돌 때마다 일어나면 안 된다.
6. **`ARCHITECTURE.md` §7 이 금지하는 것은 재는 벤치마크 CLI 다.** 포트 구현체를 실물에 물려
   보는 확인용 프로브는 다르다 — ADR-015 가 선을 긋고 `.harness/probe/` 를 sln 밖에 둔다.

## 구현체가 다음 phase 에 넘긴 사실

**phase B(View)가 틀리기 쉬운 것**

- **썸네일·아이콘은 premultiplied BGRA 다.** `PixelFormats.Bgra32` 가 아니라 **`Pbgra32`** 로
  `WriteableBitmap` 을 만든다. 틀리면 반투명 가장자리가 어둡게 번진다.
- 96 요청이 `SHIL_JUMBO` 의 **256×256(256KB)** 을 낸다. 축소는 View 가 하고 확장자 캐시는
  비우지 않으므로 상주 프로세스에서 쌓인다.
- **`SetOwnerWindow` 를 부르지 않았다.** Shell 계층은 창을 모르므로 shell 대화상자에 소유
  창이 없다. 창이 생긴 뒤 다시 본다 — 소유 창을 주려면 포트에 창 핸들을 흘려야 하고,
  그것 자체가 결정거리다.

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

- [ ] **`docs/DESIGN.md` §9** — 키보드 맵, 상호작용 상태(이름변경 인라인 편집 · 스플리터
      드래그 · 페인 간 드래그앤드롭). **phase B 의 선행조건.** ViewModel 커맨드는 다 있고
      남은 것은 XAML `InputBindings` 의 제스처 매핑이다.
- [ ] **`IContextMenuProvider`** — A 잔여. 포트 정의부터 수동이고 창 핸들이 필요하므로
      창이 생기는 phase B 와 함께 보는 편이 낫다 (`SHELL_NOTES.md` §컨텍스트 메뉴).
- [ ] **클라우드 자리표시자 확인 불가** — 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다. `SHELL_NOTES.md` §열거 함정 3 의 핵심이고
      틀리면 스크롤만으로 수 GB 를 내려받는다. 동기 중인 OneDrive 가 있는 기계가 필요하다.
- [ ] **대화상자가 뜨는 실패 경로** — 활성화(연결 프로그램 없음·취소)와 파일 조작 실패는
      자동 실행으로 확인할 수 없다(블로킹). `manual-plan.md` 사람 확인 항목에 있다.
- [ ] **푸시** — `main` 이 `origin/main` 보다 앞서 있다.

## 순서

```
A. Shell interop   포트 8/8 ✅   잔여: 스케줄러 배선 · IContextMenuProvider
C. Host 뼈대        ← 다음. 진입점 + DI + single instance + IUsageLog, 화면 없이 조립까지
B. View            DESIGN §9 를 먼저 채운 뒤
→ 매일 쓰기 → 도그푸딩 게이트 ON (ADR-007, v1 완성 후)
```
