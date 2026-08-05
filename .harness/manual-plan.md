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
| `IFolderSource` | ~~`FindFirstFileExW`~~ → `FileSystemEnumerator<T>` (ADR-014) | §열거 |
| `ITypeNameProvider` | `SHGetFileInfoW`(확장자당 1회) | §아이콘 |
| `IThumbnailSource` | `SHGetFileInfoW`+`SHGetImageList`(아이콘) · `IShellItemImageFactory`(썸네일) | §아이콘 |
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
| `IFolderSource` | ✅ `FileSystemFolderSource` — 계약 6 + 실물 10 |
| `ITypeNameProvider` | ✅ `ShellTypeNameProvider` — 13. 첫 손 P/Invoke |
| `IThumbnailSource` | ✅ `ShellThumbnailSource` — 23. 시스템 이미지 리스트 + `IShellItemImageFactory` |
| `IItemActivator` | ✅ `ShellItemActivator` — 16. `ShellExecuteEx` |
| `IFileOperations` | ✅ `ShellFileOperations` — 48. `IFileOperation` + progress sink |
| `IClipboardBridge` | ✅ `ShellClipboardBridge` — 24. `CF_HDROP` + `Preferred DropEffect` |
| `IContextMenuProvider` | 미착수 — **포트 정의부터 수동** (A 잔여, Core 에 포트도 없다) |

**포트 8개가 끝났다.** 다음은 §C(Host)다 — 아래 §순서 참조.

### SHELL_NOTES 와 다르게 간 곳 → **ADR-014 로 올렸다**

`IFolderSource` 를 `FindFirstFileExW` P/Invoke 가 아니라
`System.IO.Enumeration.FileSystemEnumerator<T>` 로 구현했다. 근거와 포기한 것
(shell 네임스페이스 열거 불가 — v2 에서는 PIDL 경로를 따로 세운다)은 `docs/ADR.md` ADR-014 에
있고, `docs/SHELL_NOTES.md` §열거 에 포인터를 달았다.

`SHGetFileInfoW`·`IShellItemImageFactory`·`IFileOperation` 은 대체 API 가 없으므로
예정대로 손으로 P/Invoke 한다.

### 확인용 프로브 → **`.harness/probe/` 로 확정 (ADR-015)**

sln 밖 콘솔 앱이라 게이트(`dotnet build`·`dotnet test`·`check-structure.ps1`)가 보지 않고,
`harness.config.json` 의 `tdd.exclude` 에 들어가 TDD 가드도 비켜간다.
`docs/ARCHITECTURE.md` §7 이 금지한 것은 **재는** 벤치마크 CLI 이고 프로브는 포트 구현체를
실물에 물려 보는 손잡이라 목적이 다르다 — 그 구분을 ADR-015 가 적어 두었다.

실행: `dotnet run --project .harness/probe -- <command>`

| 커맨드 | 무엇을 확인하는가 | 포트 |
|---|---|---|
| `typeicons` | 확장자·크기별 형식 아이콘 | `IThumbnailSource` |
| `thumbnail <path>` | 한 파일의 실제 썸네일 | `IThumbnailSource` |
| `leak [folder]` | 반복 호출 중 GDI·USER 핸들 수 | `IThumbnailSource` |
| `activate <path>` | 연결 프로그램이 정말 뜨는가 | `IItemActivator` |
| `recycle` | 정말 휴지통에 들어가는가 | `IFileOperations` |
| `clipboard copy\|cut <path>...` · `clipboard paste` | 탐색기와 주고받는가 | `IClipboardBridge` |

뒤의 셋은 **프로그램을 띄우고 · 휴지통에 항목을 남기고 · 사용자의 클립보드를 덮어쓴다.**
자동 테스트가 하지 않는 일이라 프로브에 있다.

**프로브로 확인한 것은 아래 사람 확인 항목에 결과를 적는다.** 기록이 없으면 다음 세션이
같은 확인을 다시 한다.

**사람이 확인해야 하는 것** (자동 테스트가 닿지 않는 자리).

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
- [x] **GDI 핸들 누수** — 새지 않는다. `probe leak` 으로 `C:\Windows\System32` 200개에
      10회차(아이콘+썸네일 2,000 요청)를 돌려 **GDI 40 · USER 12 로 고정**, 이미지 폴더
      (`C:\Windows\Web\Screen`)에서도 같다. `HICON`·`HBITMAP`·DC 를 `StaWorkQueue` 작업 안에서
      만들고 그 안에서 해제한 결과다 (`docs/SHELL_NOTES.md` §아이콘 함정 3).
      **다만 이것은 프로세스 반복 호출을 잰 것이지 UI 스크롤을 잰 것이 아니다** — 화면이
      붙은 뒤(phase B) 대용량 폴더를 오래 스크롤하며 한 번 더 본다.
- [x] **실물 썸네일** — `C:\Windows\Web\Screen` 의 jpg 6개가 96 요청에 **96×54 · 96×60**
      으로 온다. 비율이 유지되고 요청 크기를 넘지 않으며 항목당 **34~45ms** 다.
- [x] **`SIIGBF_THUMBNAILONLY` 동작** — 자동 테스트로 옮겼다
      (`Thumbnail_ForItemWithoutAHandler_IsNull`·`Thumbnail_ForDirectory_IsNull`).
      처리기가 등록될 리 없는 확장자를 쓰므로 기계에 의존하지 않는다.
- [x] **정말 휴지통에 들어가는가** — 들어간다. `probe recycle` 이 `%TEMP%\flex-dir-probe\`
      에 파일을 만들어 `DeleteAsync` 로 지우면 원본이 사라지고 `C:\$Recycle.Bin` 아래에
      **`$IEXRQRB.txt` · `$REXRQRB.txt` 쌍이 생긴다.** 그 쌍이 휴지통 항목의 실제 형태이므로
      영구 삭제가 아니다 — `FOFX_RECYCLEONDELETE` 가 붙어 있다는 실물 증거다.
      자동 테스트는 플래그가 실렸는지까지만 본다.
- [x] **탐색기와 클립보드를 주고받는가** — 양방향 모두 실물에서 통했다. 판정은 우리 코드가
      아닌 **독립 구현**(WinForms `Clipboard`)으로 했다.
      - 우리가 싣고 → 남이 읽기: `probe clipboard copy` 뒤 포맷이
        `FileDrop, FileNameW, FileName, Preferred DropEffect` 로 보이고
        (`FileNameW`·`FileName` 은 Windows 가 `CF_HDROP` 에서 합성한 것이라, 시스템이 우리
        `DROPFILES` 를 파싱했다는 뜻이다) `GetFileDropList()` 가 경로를 그대로 냈다.
        **`Preferred DropEffect` 는 복사 1 · 잘라내기 2** 로 갈린다.
      - 남이 싣고 → 우리가 읽기: WinForms `SetFileDropList` 로 실은 것을
        `probe clipboard paste` 가 읽었고, 그쪽은 `Preferred DropEffect` 를 싣지 않아
        **표시 없음 → 복사** 경로도 실물에서 확인됐다.
      - 남은 것: **탐색기 창에서의 Ctrl+V 자체**는 사람이 눌러야 한다. 위는 같은 포맷을
        읽는 다른 구현이지 탐색기가 아니다.
- [x] **연결 프로그램이 실제로 뜨는가** — 뜬다. `probe activate <temp.txt>` 가 **174ms** 에
      돌아오고 그 직후 `notepad` 프로세스가 0 → 2 로 늘었다. `SEE_MASK_NOASYNC` 를 줬지만
      프로그램의 종료를 기다리지 않는다(프로세스 핸들을 요청하지 않는다).
- [ ] **연결 프로그램이 없는 확장자 · 대화상자 취소** — 확인하지 않았다. 그 경로는 shell 이
      '연결 프로그램' 대화상자를 띄우고 **답을 기다리며 블로킹**하므로 자동 실행에서 부를 수
      없다. `ERROR_NO_ASSOCIATION`(1155)·`ERROR_CANCELLED`(1223)를 오류로 만들지 않는다는
      분기는 자동 테스트가 고정하고 있으니, 사람은 **대화상자가 실제로 뜨는지**만 보면 된다.
- [ ] **파일 조작의 실패 경로** — 확인하지 않았다. 같은 이유다: 권한 없는 대상에 복사하면
      shell 이 오류 대화상자를 띄워 블로킹한다. 다만 **없는 원본**은 shell 에 넘기기 전
      (`SHCreateItemFromParsingName`)에 걸리므로 자동 테스트가 실물로 잡는다
      (`Really_MissingSource_IsNotFound`).
- [ ] **`SetOwnerWindow` 를 부르지 않는다** — `FlexDir.Shell` 은 창을 모른다(CLAUDE.md §1).
      그래서 shell 의 진행률·충돌 대화상자에 소유 창이 없고 별도 작업표시줄 항목으로 뜬다.
      **phase B 에서 창이 생긴 뒤 다시 본다** — 소유 창을 주려면 포트에 창 핸들을 흘려야
      하므로 그 자체가 결정거리다.
- [ ] **점보 아이콘의 메모리** — 96 을 물으면 `SHIL_JUMBO` 가 **256×256(256KB)** 을 준다
      (48 로 내리면 96 자리에서 흐려진다). 축소는 View 가 하고, 스케줄러의 확장자 캐시는
      비우지 않으므로 상주 프로세스(ADR-003)에서 계속 쌓인다. 확장자 100종이면 25MB 수준이라
      지금은 두지만, phase B 에서 실제 사용량을 한 번 본다.

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

### `ThumbnailRequestScheduler` 배선 — **확정**

자율 phase 가 이 클래스를 만들었지만 `src/` 안에 **부르는 곳이 없다** — 생성하는 곳도,
폴더 전환에 `Reset()` 을, 스크롤에 `SetVisibleRange()` 를 부르는 곳도 없다. 그대로 두면
썸네일이 한 장도 나오지 않는다. 배선 방식을 아래로 확정했다 (구현은 이 문서의 순서대로
`IThumbnailSource` 다음).

- [x] **소유자 = `PaneViewModel` 이 직접.** 페인당 하나이고, ctor 에 `IThumbnailSource` 를
      하나 더 받아 `new ThumbnailRequestScheduler(source, dispatcher)` 를 필드로 든다.
      얇은 소유 클래스를 따로 두지 않는다 — 스케줄러가 이미 정책을 전부 감싸고 있어
      감쌀 것이 위임 메서드뿐이다. 약 30줄 증가.
      View 가 소유하지 않는 이유는 그대로다: 같은 상태가 두 계층에 갈라진다.
- [x] **`Reset()` = `LoadAsync`.** 폴더를 바꾸는 지점이 거기 하나다 (`PaneViewModel.cs:566`).
      View 가 알 필요 없다. 이전 폴더의 진행 중 요청이 남으면 새 폴더의 행에 옛 그림이 붙는다.
- [x] **`SetVisibleRange()` = attached behavior 가 `PaneViewModel` 의 메서드를 부른다.**
      노출 형태는 `public void SetVisibleRange(IReadOnlyList<FileItemViewModel> visible)` —
      **크기는 인자로 받지 않는다**(아래). 스크롤·뷰 전환·목록 갱신 셋 다 behavior 가
      민다. "무엇이 보이는가" 를 아는 쪽이 View 뿐이기 때문이다.
      `*.xaml.cs` 에 스크롤 핸들러를 두는 것은 금지다 — `scripts/check-structure.ps1` 이 막는다.
- [x] **`ViewMode` → 아이콘 크기 = `PaneViewModel` 의 private 매핑.**
      `docs/DESIGN.md` §2 가 정본이다: Details **16** · 목록 **16** · 타일 **32** · 큰 아이콘 **96**.
      `ViewMode` 를 아는 쪽이 ViewModel 이므로 View 가 크기를 계산해 넘기지 않는다.
      크기가 스케줄러 캐시 키에 들어가므로 뷰를 바꾸면 그 크기로 다시 묻는다.
      별도 파일로 빼지 않는다 — `FakeThumbnailSource` 가 받은 `requestedSize` 로
      `SetVisibleRange` 를 통해 관측되므로 public 표면 없이 테스트된다 (`CLAUDE.md` §6).
- [x] **종료 = `PaneViewModel.DisposeAsync` 가 스케줄러를 함께 `DisposeAsync`.**
      상주 프로세스라 창만 닫히고 프로세스는 남는다 (ADR-003) — 그때 진행 중 요청과
      BGRA 버퍼가 함께 정리되는지 확인한다.

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
- [ ] **`IUsageLog` — 포트 정의부터 여기서 한다 (확정).** `docs/ARCHITECTURE.md` §2 의 포트
      목록에는 있으나 `FlexDir.Core` 에 아직 없다. 자율 phase 의 구멍이 아니라 **여기로
      미룬 것**이다: 기록할 내용이 프로세스 수명에 달려 있어 — ADR-003 상주 프로세스라
      "사용 시간" 이 창 표시 시간인지 프로세스 수명인지 Host 를 짜면서 갈린다 — 지금
      인터페이스만 만들면 소비자 없는 추측성 정의가 된다. 포트 · 구현 · 배선을 한 번에 한다.
      도그푸딩 게이트(ADR-007) 자체는 **v1 완성 후** 켠다.

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

§B 의 **`ThumbnailRequestScheduler` 배선**은 **결정을 끝냈다**(위 §B). 코드비하인드 금지와
부딪히는 항목이라 View 를 짜기 시작한 뒤에 정하면 이미 `*.xaml.cs` 에 스크롤 핸들러가 들어가
있게 되기 때문이다. ViewModel 쪽 배선(`PaneViewModel` 소유·`Reset`·`SetVisibleRange`·
`DisposeAsync`)은 `IThumbnailSource` 구현 직후 A 안에서 하고, attached behavior 만 B 에서 한다.

**아직 열려 있는 것**: `docs/DESIGN.md` §9 (키보드 맵 · 상호작용 상태) — B 착수 전까지.
`IContextMenuProvider` 포트 정의 — A 잔여.
