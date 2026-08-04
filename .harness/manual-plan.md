# 수동 작업 계획 — Shell interop · View · Host

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 일부는 harness `guardrails` 로
> 매 step 프롬프트에 주입되고, 이 문서는 자율 실행이 참조할 대상이 아니다.
>
> `phases/` 에는 자율 실행 가능한 phase 만 있다(ADR-009). 아래 세 덩이는 **자동 채점할
> 커맨드가 존재하지 않으므로** `phases/` 에 넣지 않는다 — 넣으면 `/harness:run` 이
> 집어가고, 통과했다는 사실이 아무것도 보증하지 않는 상태가 된다.

## 자율 phase 가 남기는 것

`phases/` 의 3 phase · 18 step 이 끝나면 `FlexDir.Core` 와 `FlexDir.App` 의 ViewModel 이
완성되고, 모든 외부 의존은 **포트 인터페이스와 fake** 로만 존재한다. 아래는 그 포트에
실물을 끼우고 화면을 붙이는 일이다.

## A. Shell interop 구현체 (`FlexDir.Shell`)

`docs/SHELL_NOTES.md` 가 이 작업의 지도다. 절마다 함정이 정리돼 있으니 착수 전에 해당 절을 읽는다.

| 포트 | 구현 | SHELL_NOTES 절 |
|---|---|---|
| `IFolderSource` | `FindFirstFileExW` + `FindExInfoBasic` + `FIND_FIRST_EX_LARGE_FETCH` | §열거 |
| `ITypeNameProvider` · `IThumbnailSource` | `SHGetFileInfoW`(확장자당 1회) · `IShellItemImageFactory` | §아이콘 |
| `IFileOperations` | `IFileOperation` + **`FOFX_RECYCLEONDELETE`** | §파일 조작 |
| `IClipboardBridge` | 탐색기 호환 클립보드 포맷 | §클립보드 |
| `IFolderWatcher` | `FileSystemWatcher` + `InternalBufferSize` 확대 + `Error` 처리 | §폴더 감시 |
| `IViewStateStore` | `%LOCALAPPDATA%\flex-dir\` 파일 | — |
| `IItemActivator` | `ShellExecuteEx` | — |
| `IContextMenuProvider` | `IContextMenu` + `IContextMenu2/3` 메시지 펌핑 | §컨텍스트 메뉴 |

### 진행

| 포트 | 상태 |
|---|---|
| `IViewStateStore` | ✅ `JsonViewStateStore` — 계약 12 + 파일 고유 10 |
| `IFolderWatcher` | ✅ `FileSystemFolderWatcher` — 계약 4 + 실물 5 |
| `IFolderSource` | ✅ `FileSystemFolderSource` — 계약 6 + 실물 9 |
| 나머지 다섯 | 미착수 |

### SHELL_NOTES 와 다르게 간 곳 — ADR 로 올릴지 판단 필요

`IFolderSource` 를 `FindFirstFileExW` P/Invoke 가 아니라
`System.IO.Enumeration.FileSystemEnumerator<T>` 로 구현했다. §열거 가 P/Invoke 를 지시한
근거는 둘이었고 — 8.3 단축 이름 조회를 건너뛰는 것, `Directory.EnumerateFiles` 가 플래그를
못 주고 예외 기반이라는 것 — 이 API 는 `Directory.EnumerateFiles` 가 아니라 **그 밑의 저수준
primitive** 로, Windows 에서 `NtQueryDirectoryFile` 로 내려가 8.3 이름을 아예 묻지 않고
항목별 예외도 없다. 즉 같은 목표를 unsafe 코드와 150줄 interop 없이 얻는다.

**포기한 것**: shell 네임스페이스(내 PC · 네트워크)는 이 API 로 열거할 수 없다. v1 은
로컬만 다루므로(ADR-010) 지금은 손해가 아니지만, v2 에서 PIDL 열거가 필요해지면 그때는
P/Invoke 를 **따로** 세워야 한다 — 이 클래스를 확장하는 것이 아니라.

`SHGetFileInfoW`·`IShellItemImageFactory`·`IFileOperation` 은 대체 API 가 없으므로
예정대로 손으로 P/Invoke 한다.

**사람이 확인해야 하는 것** (자동 테스트가 닿지 않는 자리).
scratchpad 의 확인용 프로브로 실물에서 돌린 결과다 — 프로브는 저장소에 남기지 않는다
(`docs/ARCHITECTURE.md` §7 은 별도 벤치마크 CLI 를 금지한다).

- [x] **실제 버퍼 오버플로** — 재현됨. 버퍼를 최소값(4096)으로 낮추고 200자 이름의 파일
      20,000개를 여러 스레드에서 동시에 만들면 `Overflow` 6회, `Added` 17,959개 유실.
      **다만 순차 생성 6,000개(초당 약 2,350건)에서는 오버플로가 나지 않았다** — 이벤트
      핸들러가 채널에 `TryWrite` 만 하고 빠지므로 배수가 빠르다. 실사용에서 드문 경로라는
      뜻이고, 그래서 자동 테스트로 재현하려 하면 간헐적 실패가 된다.
- [x] **감시 중 폴더 삭제** — `Removed a.txt` 뒤 `Overflow` 가 오고 스트림은 **예외 없이
      정상 종료**한다. 그 뒤 재열거는 `NotFound`(Win32 3)이므로 `PaneViewModel` 이 상위로
      한 단계 올라간다 (`docs/PRD.md` §4 대로).
- [x] **권한 없는 폴더** — `C:\System Volume Information` · `C:\Windows\CSC` ·
      `C:\Windows\System32\config` 셋 모두 `AccessDenied`(Win32 5), 문구는
      "액세스가 거부되었습니다 — {경로}". 이 확인 과정에서 `Win32Error` 가 0 으로 유실되던
      결함을 찾아 고쳤다 (HResult 마스크의 부호 확장).
- [x] **대용량 열거** — `C:\Windows\WinSxS` 24,115개에서 첫 항목 **14.8ms** / 전체 73.6ms,
      `C:\Windows\System32` 4,889개에서 첫 항목 11.3ms / 전체 23.9ms.
      `docs/PRD.md` §5 의 첫 항목 150ms 대비 10배 여유다. 10만 항목은 아직 재지 않았지만
      전체 시간이 항목 수에 선형이라 위험 신호는 없다.
- [ ] **클라우드 자리표시자** — **이 기계에서는 확인 불가.** `%OneDrive%` 아래 항목이
      `desktop.ini` 하나뿐이고, 사용자 프로필 전체에서 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다. 동기 중인 OneDrive 내용이 있는 기계에서
      다시 확인해야 한다 — SHELL_NOTES §열거 함정 3 의 핵심이고, 틀리면 스크롤만으로
      수 GB 를 내려받는다.
- [ ] **네트워크 경로에서의 감시·열거** — v2 범위. 실패 방식만이라도 봐 두는 편이 낫다.

- **`IContextMenuProvider` 는 포트 정의부터 수동이다.** 창 핸들과 네이티브 메뉴 메시지
  펌핑이 필요해 ViewModel 테스트로 채점할 수 없다(자율 phase 2 step 5 에서 의도적으로 제외).
- 각 구현체 테스트는 자율 phase 가 만든 **계약 기반 클래스를 상속**한다 —
  `FolderSourceContract` · `FolderWatcherContract` · `ViewStateStoreContract`.
  같은 검증을 fake 와 실물이 함께 받는 것이 이 계약 클래스들의 존재 이유다.
- COM 아파트먼트 규칙은 `docs/SHELL_NOTES.md` §COM 아파트먼트 를 따른다.

## B. WPF View (`FlexDir.App`)

**착수 전에 `docs/DESIGN.md` §9 를 채워야 한다.** 지금 비어 있는 것:

- [ ] **키보드 맵 표** — 단축키 확정 + 탐색기 기본 단축키 충돌 검토.
      `docs/UI_GUIDE.md` §키보드 의 최소선을 실제 키 조합으로 확정한다.
      ViewModel 커맨드는 자율 phase 에서 이미 다 만들어져 있으므로,
      남은 것은 XAML `InputBindings` 의 제스처 매핑뿐이다.
- [ ] **상호작용 상태** — 이름변경 인라인 편집 · 스플리터 드래그 중 · 페인 간 드래그앤드롭

**`ThumbnailRequestScheduler` 를 부를 주체를 정해야 한다.** 자율 phase 가 이 클래스를 만들었지만
`src/` 안에 **부르는 곳이 없다** — 생성하는 곳도, 폴더 전환에 `Reset()` 을, 스크롤에
`SetVisibleRange()` 를 부르는 곳도 없다. 그대로 두면 썸네일이 한 장도 나오지 않는다.
`SetVisibleRange` 를 부르는 것이 스크롤 이벤트인데 `*.xaml.cs` 에는 로직을 둘 수 없어
(`CLAUDE.md` §2) 배선 지점이 자명하지 않다. 그래서 착수 전에 정한다:

- [ ] **소유자** — `ThumbnailRequestScheduler` 는 ViewModel 계층의 물건이다
      (`FlexDir.App/ViewModels/`, `IThumbnailSource`·`IUiDispatcher` 를 받고
      `FileItemViewModel.Icon`·`Thumbnail` 을 채운다). 페인당 하나를 페인의 ViewModel 쪽이
      소유한다 — View 가 소유하면 같은 상태가 두 계층에 갈라진다.
      `PaneViewModel` 에 직접 넣을지, 얇은 소유 클래스를 하나 둘지는 그때 정한다
      (`PaneViewModel` 은 이미 1,244줄이다).
- [ ] **`Reset()` 시점** — 폴더 전환. View 가 알 필요 없다: ViewModel 이 폴더를 바꾸는 지점을
      이미 안다(`LoadAsync`). 이전 폴더의 진행 중 요청이 남으면 새 폴더의 행에 옛 그림이 붙는다.
- [ ] **`SetVisibleRange()` 시점** — 스크롤·뷰 전환·목록 갱신. 코드비하인드 금지를 지키려면
      스크롤을 **attached behavior**(별도 클래스)로 받아 ViewModel 의 메서드/커맨드로 넘긴다.
      `*.xaml.cs` 에 스크롤 핸들러를 두는 것은 금지다 — `scripts/check-structure.ps1` 이 막는다.
- [ ] **`ViewMode` → 아이콘 크기 매핑** — `SetVisibleRange(visible, requestedSize)` 의
      `requestedSize` 를 정하는 규칙이 아직 어디에도 없다. `docs/DESIGN.md` §2 의 값이 정본이다:
      Details **16** · 목록 **16** · 타일 **32** · 큰 아이콘 **96**.
      `ViewMode` 를 아는 쪽이 ViewModel 이므로 매핑도 ViewModel 에 둔다. 크기가 스케줄러
      캐시 키에 들어가므로(같은 확장자도 크기마다 따로 조회한다) 뷰를 바꾸면 그 크기로 다시 묻는다.
- [ ] **종료** — 창을 닫을 때 `DisposeAsync`. 상주 프로세스라 창만 닫히고 프로세스는 남는다
      (ADR-003) — 그때 진행 중 요청과 BGRA 버퍼가 함께 정리되는지 확인한다.

그리고 `docs/DESIGN.md` §10 의 미결 두 건은 실물을 보고 판단한다:

- [ ] #4 밀도 옵션(24/28)을 설정으로 — v1 범위 밖으로 두었으나 실물 확인 후 재검토
- [ ] #5 비활성 페인 크롬 배경 강등이 과한지

구현 시 지켜야 할 것:

- `*.xaml.cs` 에는 `InitializeComponent()` 만. `scripts/check-structure.ps1` 이 막는다.
- 뷰 모드 4종은 `DataTemplate` 교체로 바꾼다. 목록 컨트롤을 갈아치우지 않는다(ADR-002).
- `VirtualizingStackPanel` + `VirtualizationMode="Recycling"` + `ScrollUnit="Item"`.
  `ScrollViewer` 로 감싸지 않는다(`docs/ARCHITECTURE.md` §5).
- 행 높이는 뷰 안에서 고정. 수치는 `docs/DESIGN.md` §2.
- `ThumbnailBitmap`(BGRA32) → `WriteableBitmap` 변환은 View 계층에서 한다.
- `IUiDispatcher` 의 실제 구현(`Dispatcher` 기반)을 여기서 만든다.

## C. Host — 진입점과 DI 조립 (`FlexDir.Host`)

현재 `src/FlexDir.Host/Program.cs` 는 **빌드를 통과시키기 위한 빈 진입점**이다.
아래가 미결이며, 결정하면서 채운다.

- [ ] **WPF 진입점 형태** — `App.xaml`(`ApplicationDefinition`) + 빈 `App.xaml.cs` 로 갈지,
      명시적 `Main` 을 유지할지. 전자는 관례적이지만 DI 조립을 어디서 할지 정해야 하고,
      `App.xaml.cs` 에 로직을 넣는 것은 `CLAUDE.md` §2 위반이다.
      → 조립 코드는 `*.xaml.cs` 가 아닌 별도 클래스에 둔다.
- [ ] **DI 컨테이너 선택** — 또는 수동 조립. `FlexDir.Host` 만 `Core`·`Shell`·`App` 셋을
      모두 참조하며, 그 지식을 이 프로젝트 하나에 가둔다(`docs/ARCHITECTURE.md` §1).
- [ ] **single instance 상주** — 두 번째 실행은 기존 프로세스에 인자를 넘기고 종료.
      창을 닫아도 프로세스 유지(ADR-003, `docs/ARCHITECTURE.md` §6).
- [ ] **계측** — cold start · 상주 중 창 표시 · 폴더 전환 후 첫 항목.
      별도 벤치마크 CLI 를 만들지 않는다(`docs/ARCHITECTURE.md` §7).
- [ ] `IUsageLog` 구현 — 도그푸딩 게이트(ADR-007)의 입력. 게이트 자체는 **v1 완성 후** 켠다.

## 순서

```
phases/ 자율 실행 (Core + ViewModel)
   → A. Shell interop  (포트에 실물 끼우기, 계약 테스트 상속)
   → C. Host 뼈대       (진입점 + DI, 화면 없이 실행되는지)
   → B. View            (DESIGN §9 를 먼저 채운 뒤)
   → 매일 쓰기 → 도그푸딩 게이트 ON
```

B 를 마지막에 두는 이유: 화면이 붙기 전에 Shell 구현체가 실물 폴더에서 동작하는지
확인할 수 있고, 그 단계의 버그를 UI 버그와 섞지 않을 수 있다.

단, §B 의 **`ThumbnailRequestScheduler` 배선**은 A 착수 전에 결정해 둔다. 코드비하인드 금지와
부딪히는 항목이라 View 를 짜기 시작한 뒤에 정하면 이미 `*.xaml.cs` 에 스크롤 핸들러가 들어가
있게 된다. 결정만 앞으로 당기는 것이고 구현은 B 에서 한다.
