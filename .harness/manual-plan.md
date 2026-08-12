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


| 포트                     | 구현                                                                     | SHELL_NOTES 절 |
| ---------------------- | ---------------------------------------------------------------------- | ------------- |
| `IFolderSource`        | `~~FindFirstFileExW~~` → `FileSystemEnumerator<T>` (ADR-014)           | §열거           |
| `ITypeNameProvider`    | `SHGetFileInfoW`(확장자당 1회)                                              | §아이콘          |
| `IThumbnailSource`     | `SHGetFileInfoW`+`SHGetImageList`(아이콘) · `IShellItemImageFactory`(썸네일) | §아이콘          |
| `IFileOperations`      | `IFileOperation` + `**FOFX_RECYCLEONDELETE**`                          | §파일 조작        |
| `IClipboardBridge`     | 탐색기 호환 클립보드 포맷                                                         | §클립보드         |
| `IFolderWatcher`       | `FileSystemWatcher` + `InternalBufferSize` 확대 + `Error` 처리             | §폴더 감시        |
| `IViewStateStore`      | `%APPDATA%\flex-dir\` 파일 (2026-08-07에 옮겼다 — ARCHITECTURE §4)         | —             |
| `IItemActivator`       | `ShellExecuteEx`                                                       | —             |
| `IContextMenuProvider` | `IContextMenu` + `IContextMenu2/3` 메시지 펌핑                              | §컨텍스트 메뉴      |


### 진행


| 포트                     | 상태                                                                      |
| ---------------------- | ----------------------------------------------------------------------- |
| `IViewStateStore`      | ✅ `JsonViewStateStore` — 계약 12 + 파일 고유 10                               |
| `IFolderWatcher`       | ✅ `FileSystemFolderWatcher` — 계약 4 + 실물 5                               |
| `IFolderSource`        | ✅ `FileSystemFolderSource` — 계약 6 + 실물 10                               |
| `ITypeNameProvider`    | ✅ `ShellTypeNameProvider` — 13. 첫 손 P/Invoke                            |
| `IThumbnailSource`     | ✅ `ShellThumbnailSource` — 23. 시스템 이미지 리스트 + `IShellItemImageFactory`   |
| `IItemActivator`       | ✅ `ShellItemActivator` — 16. `ShellExecuteEx`                           |
| `IFileOperations`      | ✅ `ShellFileOperations` — 48. `IFileOperation` + progress sink          |
| `IClipboardBridge`     | ✅ `ShellClipboardBridge` — 24. `CF_HDROP` + `Preferred DropEffect`      |
| `IContextMenuProvider` | ✅ `ShellContextMenuProvider` — 12. `IContextMenu2/3` 펌핑 + 자체 숨은 창 (B-4) |


**포트가 전부 끝났다** (B-4 가 `IContextMenuProvider` 를, 마감이 `IDriveSpace` 를 채웠다).
§C(Host)도 §B(View)도 §마감도 끝났다. 포트 목록의 정본은 `docs/ARCHITECTURE.md` §2 이고
구현체·테스트 수는 `HANDOFF.md` §포트 현황 에 있다 — 여기서 세지 않는다.

### SHELL_NOTES 와 다르게 간 곳 → **ADR-014 로 올렸다**

`IFolderSource` 를 `FindFirstFileExW` P/Invoke 가 아니라
`System.IO.Enumeration.FileSystemEnumerator<T>` 로 구현했다. 근거와 포기한 것
(shell 네임스페이스 열거 불가 — v2 에서는 PIDL 경로를 따로 세운다)은 `docs/ADR.md` ADR-014 에
있고, `docs/SHELL_NOTES.md` §열거 에 포인터를 달았다.

`SHGetFileInfoW`·`IShellItemImageFactory`·`IFileOperation` 은 대체 API 가 없으므로
예정대로 손으로 P/Invoke 한다.

### 확인용 프로브 → `**.harness/probe/` 로 확정 (ADR-015)**

sln 밖 콘솔 앱이라 게이트(`dotnet build`·`dotnet test`·`check-structure.ps1`)가 보지 않고,
`harness.config.json` 의 `tdd.exclude` 에 들어가 TDD 가드도 비켜간다.
`docs/ARCHITECTURE.md` §7 이 금지한 것은 **재는** 벤치마크 CLI 이고 프로브는 포트 구현체를
실물에 물려 보는 손잡이라 목적이 다르다 — 그 구분을 ADR-015 가 적어 두었다.

실행: `dotnet run --project .harness/probe -- <command>`


| 커맨드                                                | 무엇을 확인하는가             | 포트                 |
| -------------------------------------------------- | --------------------- | ------------------ |
| `typeicons`                                        | 확장자·크기별 형식 아이콘        | `IThumbnailSource` |
| `thumbnail <path>`                                 | 한 파일의 실제 썸네일          | `IThumbnailSource` |
| `leak [folder]`                                    | 반복 호출 중 GDI·USER 핸들 수 | `IThumbnailSource` |
| `activate <path>`                                  | 연결 프로그램이 정말 뜨는가       | `IItemActivator`   |
| `recycle`                                          | 정말 휴지통에 들어가는가         | `IFileOperations`  |
| `clipboard copy|cut <path>...` · `clipboard paste` | 탐색기와 주고받는가            | `IClipboardBridge` |


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
- [x] `**SIIGBF_THUMBNAILONLY` 동작** — 자동 테스트로 옮겼다

  (`Thumbnail_ForItemWithoutAHandler_IsNull`·`Thumbnail_ForDirectory_IsNull`).
  처리기가 등록될 리 없는 확장자를 쓰므로 기계에 의존하지 않는다.
- [x] **정말 휴지통에 들어가는가** — 들어간다. `probe recycle` 이 `%TEMP%\flex-dir-probe\`

  에 파일을 만들어 `DeleteAsync` 로 지우면 원본이 사라지고 `C:\$Recycle.Bin` 아래에
  `**$IEXRQRB.txt` · `$REXRQRB.txt` 쌍이 생긴다.** 그 쌍이 휴지통 항목의 실제 형태이므로
  영구 삭제가 아니다 — `FOFX_RECYCLEONDELETE` 가 붙어 있다는 실물 증거다.
  자동 테스트는 플래그가 실렸는지까지만 본다.
- [x] **탐색기와 클립보드를 주고받는가** — 양방향 모두 실물에서 통했다. 판정은 우리 코드가

  아닌 **독립 구현**(WinForms `Clipboard`)으로 했다.
  - 우리가 싣고 → 남이 읽기: `probe clipboard copy` 뒤 포맷이
    `FileDrop, FileNameW, FileName, Preferred DropEffect` 로 보이고
    (`FileNameW`·`FileName` 은 Windows 가 `CF_HDROP` 에서 합성한 것이라, 시스템이 우리
    `DROPFILES` 를 파싱했다는 뜻이다) `GetFileDropList()` 가 경로를 그대로 냈다.
    `**Preferred DropEffect` 는 복사 1 · 잘라내기 2** 로 갈린다.
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
- [x] `**SetOwnerWindow` — B-4 에서 풀렸다.** 포트에 창 핸들을 흘리지 않고 `FlexDir.Host` 의

  `Startup/OwnerWindow` 가 창을 쥐고 공급자(`Func<nint>`)를 구현체에 물려 준다
  (사용자 결정 2026-08-06 · `IContextMenuProvider` 와 같은 결정). shell 대화상자가
  이제 flex-dir 창을 부모로 한다 — 실물 확인됐다.
- [x] **점보 아이콘의 메모리 — B-4 에서 실측했다** (아래 §B-3 사람 확인 항목: WS 417MB) — 96 을 물으면 `SHIL_JUMBO` 가 **256×256(256KB)** 을 준다

  (48 로 내리면 96 자리에서 흐려진다). 축소는 View 가 하고, 스케줄러의 확장자 캐시는
  비우지 않으므로 상주 프로세스(ADR-003)에서 계속 쌓인다. 확장자 100종이면 25MB 수준이고,
  **실제 사용량은 B-4 에서 쟀다** (§B-3 사람 확인 항목) — 그대로 두기로 했다.

- `**IContextMenuProvider` 는 포트 정의부터 수동이었다** — 창 핸들과 네이티브 메뉴 메시지
펌핑이 필요해 ViewModel 테스트로 채점할 수 없다(자율 phase 2 step 5 에서 의도적으로 제외).
**B-4 에서 손으로 끝냈다** (아래 §B-4 · `docs/SHELL_NOTES.md` §컨텍스트 메뉴).
- 각 구현체 테스트는 자율 phase 가 만든 **계약 기반 클래스를 상속**한다 —
`FolderSourceContract` · `FolderWatcherContract` · `ViewStateStoreContract`.
같은 검증을 fake 와 실물이 함께 받는 것이 이 계약 클래스들의 존재 이유다.
- COM 아파트먼트 규칙은 `docs/SHELL_NOTES.md` §COM 아파트먼트 를 따른다.

## B. WPF View (`FlexDir.App`)

**선행조건은 없다 — `docs/DESIGN.md` §9 · §9-1 을 채웠다.**

- [x] **키보드 맵 표** — `DESIGN.md` §9. 충돌하면 탐색기가 이긴다. 뷰 `Ctrl+Shift+1~4` ·

  정렬 `Ctrl+1~4` · 페인 전환 `Tab`/`F6` · 반대편 복사·이동 `Ctrl+Alt+C`/`M`.
  **이동·선택은 `ListView` 내장을 쓰지 않고 ViewModel 이 처리한다** — 합성 행 위에서
  내장 이동은 행 단위로만 움직인다. type-ahead 는 v1 에 넣는다(필터·검색이 v2 라
  큰 폴더에서 항목을 찾는 유일한 수단이다).
- [x] **상호작용 상태** — `DESIGN.md` §9-1. 이름변경은 **포커스를 잃으면 취소**(탐색기와

  다른 유일한 지점 — 2분할이라 반대편 클릭이 일상이고 이름변경은 휴지통이 없다).
  스플리터는 열 수가 바뀔 때만 재배치. 드래그앤드롭은 **기본 복사 · `Shift` 이동**이고
  **양방향**이되 `FlexDir.Shell` 을 쓰지 않는다(WPF `DataObject` 로 충분).
- [x] **뷰 3종의 가상화 → ADR-016.** `WrapPanel` 은 가상화하지 않으므로 합성 행으로

  `VirtualizingStackPanel` 을 지킨다. Details 는 평평한 `Items` 그대로다.

### `ThumbnailRequestScheduler` 배선 — **확정 · 구현됨**

자율 phase 가 이 클래스를 만들었을 때 `src/` 안에 부르는 곳이 없었다 — 생성하는 곳도,
폴더 전환에 `Reset()` 을, 스크롤에 `SetVisibleRange()` 를 부르는 곳도 없었다. 그대로 뒀으면
썸네일이 한 장도 나오지 않았을 것이다. 아래가 그때 확정한 배선이고 **B-1·B-3 에서 전부
들어왔다** (실물에서 네 뷰에 아이콘·썸네일이 붙는 것을 확인했다 — §B-3).

- [x] **소유자 = `PaneViewModel` 이 직접.** 페인당 하나이고, ctor 에 `IThumbnailSource` 를

  하나 더 받아 `new ThumbnailRequestScheduler(source, dispatcher)` 를 필드로 든다.
  얇은 소유 클래스를 따로 두지 않는다 — 스케줄러가 이미 정책을 전부 감싸고 있어
  감쌀 것이 위임 메서드뿐이다. 약 30줄 증가.
  View 가 소유하지 않는 이유는 그대로다: 같은 상태가 두 계층에 갈라진다.
- [x] `**Reset()` = `LoadAsync`.** 폴더를 바꾸는 지점이 거기 하나다 (`PaneViewModel.cs:566`).

  View 가 알 필요 없다. 이전 폴더의 진행 중 요청이 남으면 새 폴더의 행에 옛 그림이 붙는다.
- [x] `**SetVisibleRange()` = attached behavior 가 `PaneViewModel` 의 메서드를 부른다.**

  노출 형태는 `public void SetVisibleRange(IReadOnlyList<FileItemViewModel> visible)` —
  **크기는 인자로 받지 않는다**(아래). 스크롤·뷰 전환·목록 갱신 셋 다 behavior 가
  민다. "무엇이 보이는가" 를 아는 쪽이 View 뿐이기 때문이다.
  `*.xaml.cs` 에 스크롤 핸들러를 두는 것은 금지다 — `scripts/check-structure.ps1` 이 막는다.
- [x] **확정문과 스케줄러 시그니처의 어긋남 → 둘 다 맞다. 층위가 다르다.**

  이 확정문이 말한 "크기를 받지 않는다" 는 `**PaneViewModel` 의 공개 표면**이고,
  `ThumbnailRequestScheduler.SetVisibleRange(visible, requestedSize)` 는 그 아래층이다.
  스케줄러는 크기를 **캐시 키의 일부**로 쓰므로(같은 확장자라도 16 과 96 은 다른 요청이다)
  뺄 수 없고, 동시에 `ViewMode` 를 알아서도 안 된다 — 그것을 알면 `IThumbnailSource` 위의
  정책이 아니라 두 번째 ViewModel 이 된다. 그래서 **스케줄러 시그니처는 그대로 두고**
  `PaneViewModel.SetVisibleRange(visible)` 가 `IconSize(ViewMode)` 를 끼워 넘긴다.
  배선 테스트는 `tests/FlexDir.App.Tests/ViewModels/PaneThumbnailsTests.cs` 다.
- [x] `**ViewMode` → 아이콘 크기 = `PaneViewModel` 의 private 매핑.**

  `docs/DESIGN.md` §2 가 정본이다: Details **16** · 목록 **16** · 타일 **32** · 큰 아이콘 **96**.
  `ViewMode` 를 아는 쪽이 ViewModel 이므로 View 가 크기를 계산해 넘기지 않는다.
  크기가 스케줄러 캐시 키에 들어가므로 뷰를 바꾸면 그 크기로 다시 묻는다.
  별도 파일로 빼지 않는다 — `FakeThumbnailSource` 가 받은 `requestedSize` 로
  `SetVisibleRange` 를 통해 관측되므로 public 표면 없이 테스트된다 (`CLAUDE.md` §6).
- [x] **종료 = `PaneViewModel.DisposeAsync` 가 스케줄러를 함께 `DisposeAsync`.**

  상주 프로세스라 창만 닫히고 프로세스는 남는다 (ADR-003) — 그때 진행 중 요청과
  BGRA 버퍼가 함께 정리되는지 확인한다.

### B-2 (창 + Details) 구현 결과 — 사람 확인 항목

B-1(ViewModel 확장)과 B-2 골격이 끝났다. 창이 뜨고 Details 로 탐색·선택·조작이
가능해야 한다. **아래는 자동 채점이 닿지 않아 사람이 실물로 확인해야 한다.**

- [x] **창이 뜨는가 — 실행 → 창 표시는 확인됐다** (2026-08-05 실물). 페인 둘·Details·

  정렬 화살표·선택 배경·포커스 테두리·활성 주소줄 accent·상태표시줄 요약까지 그려진다.
  창을 닫은 뒤 다시 실행하면 창이 다시 오는 것까지 확인 (2026-08-06 실물).
  **인자 경로는 절반 확인됐다** (2026-08-06): 상주 프로세스가 없을 때
  `FlexDir.Host.exe C:\Users\SOOJANG\Pictures` 로 띄우면 그 폴더가 활성 페인에 열린다.
  **남은 절반**: 상주 프로세스가 **이미 떠 있을 때** 인자를 준 두 번째 실행
  (single instance 파이프 → `ActivationRouter`) 이 그 폴더로 옮겨 가는가.
- [x] **키보드 배선이 실제로 돈다** (2026-08-05 실물). 방향키 이동이 우리 KeyBinding 으로

  오고 (`ListBox` 내장 처리에 먹히지 않았다 — 걱정했던 지점) `Ctrl+C`·`Ctrl+V` 도
  동작했다. Shift/Ctrl 조합·Home/End/PageUp/Down 은 개별 확인이 남아 있다.
- [ ] **클릭·더블클릭** — 선택 3종(평·Ctrl·Shift)·빈 곳 클릭 해제·더블클릭 열기·

  비활성 페인 클릭 시 활성 전환.
- [ ] **주소줄** — 경로 입력 + Enter 로 이동, 오타 시 상태표시줄 사유. 목록 클릭 후

  방향키가 주소를 편집하지 않는가 (`ListInput` 이 포커스를 목록으로 옮긴다).
- [ ] **활성 페인 표현** — 주소줄 accent + 하단 2px, 비활성 선택색 강등 (§6). DESIGN §10

  의 #5(비활성 크롬 강등이 과한가)도 여기서 판단.
- [ ] **가상화 실측** — 뷰 4종이 다 들어왔으므로 §B-3 의 "대용량 폴더 스크롤 체감" 에서

  함께 본다 (같은 항목을 두 곳에 두지 않는다).
- [x] **시작 상태 복원 — 확인됐다** (2026-08-06 실물). 폴더·스플리터 비율·창 위치/크기가

  재실행 후 돌아온다. **이 확인이 잠복 버그를 잡았다**: 배치가 저장된 기계에서만,
  복원의 PropertyChanged(UI 밖 스레드)를 받은 `ResidentWindow` 가 창을 바로 만져
  스레드 친화성 예외 → StartAsync 버림 속으로 삼켜져 **창이 영영 안 떴다**.
  Dispatcher 마샬링으로 수정 (`fix: 창 배치 적용을 UI 스레드로 마샬링한다`).
  날 이벤트 핸들러에는 바인딩 엔진의 스레드 마샬링이 없다 — View 배선이 VM 이벤트를
  직접 받을 때는 항상 이 함정을 본다.
- [x] **트레이 아이콘 — 확인됐다** (2026-08-06 실물). 아이콘 표시·창 열기·완전 종료로

  프로세스 종료까지. Shutdown 이 `Closing` 취소를 무시하는 것도 이때 같이 확인됐다.
- [x] **닫기 = 숨기기 — 확인됐다** (2026-08-06 실물). X 로 닫아도 프로세스가 살고

  재실행하면 같은 창이 다시 온다. 이전에는 이 경로가 `Show()` 에서 던졌다.
- [x] **스플리터 드래그 — 확인됐다** (2026-08-06 실물).
- [x] **계측 실측 수치 — 예산 안이다** (2026-08-06 · perf.log). 상주 중 WindowShown

  **3~9ms**(예산 100) · FirstItem **0~18ms**(예산 150). 첫 표시만 367ms over —
  창 생성 + 첫 레이아웃이 통째로 들어간 값이고, 상주 설계(ADR-003)가 그 비용을
  한 번만 내게 한다는 것이 수치로 확인된 셈이다.
- [ ] **주소줄에 경로 자동완성 드롭다운이 뜬다는 보고** (2026-08-06) — **우리 기능이

  아니다** (당시 주소줄은 순수 TextBox 였고 지금도 편집 상자에 자동완성 코드가 없다 —
  마감에서 breadcrumb 이 얹혔지만 입력 상자는 그대로다). 외부 유틸(Listary 류)이나
  OS 입력 기능으로 추정. 메모장 등 다른 앱의 텍스트 입력에서도 뜨는지로 판별한다.
  해가 없으면 방치, v1 에 자체 자동완성을 넣을지는 별도 결정.
- [x] **아이콘·썸네일 · 뷰 3종 · type-ahead · Ctrl+L → B-3 에서 들어왔다** (아래 §B-3).
- [x] **이름변경 편집기 · 드래그앤드롭 · `IContextMenuProvider` → B-4 에서 들어왔다**

  (아래 §B-4).
- [x] **제품 아이콘 · 커스텀 타이틀바 → 마감에서 들어왔다** (아래 §마감).

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

### B-3 (아이콘·썸네일 + 뷰 3종) 구현 결과 — 사람 확인 항목

뷰 4종이 전부 그려지고 shell 아이콘·썸네일이 붙는다. **실물로 본 것과 못 본 것을 나눈다.**

**실물 확인됐다** (2026-08-06, 창 캡처 `PrintWindow`):

- [x] **Details·목록·타일·큰 아이콘 네 뷰가 그려진다.** 툴바 버튼 4개와 `Ctrl+Shift+1~4`

  로 전환되고, 지금 뷰 버튼이 accent 로 강조된다. Details 컬럼 헤더는 다른 뷰에서 사라진다.
- [x] **shell 아이콘이 네 뷰에 다 붙는다** (16·16·32·96). 사진 폴더에서 **96px 실제 썸네일**이

  나오고 반투명 가장자리가 번지지 않는다 — `Pbgra32` 가 맞았다.
- [x] **합성 행의 열 수가 폭을 따라간다** — 큰 아이콘 3열(뷰포트 372~495), 타일 1열(456 미만).

  `DESIGN.md` §2 의 표와 일치한다.
- [x] **뷰 전환이 `DataTemplate` 교체다** — 목록 컨트롤은 하나이고 `ItemsSource` 만

  `Items`↔`Rows` 로 갈린다.

**실물이 잡은 버그 둘** (자동 채점이 닿지 않던 자리다):

- [x] `**SHGetFileInfo` 경로는 동시 호출에 조용히 실패한다.** 두 페인이 같은 순간에 폴더

  아이콘을 물으면 한쪽이 실패한다 — 예외도 오류 코드도 없이 `SHGetFileInfo` 가 0 을 내고
  (아이콘 인덱스 -1), 그것을 막으면 이번엔 그 뒤의 **시스템 이미지 리스트가 던진다.**
  실패는 호출자 캐시에 남아 다시 묻지 않으므로 (PRD §4) **그 페인의 아이콘이 끝까지
  비어 있었다** — 매 실행 재현됐고, 어느 페인이 지는지만 달랐다.
  → `Interop/ShellInfoGate` 로 형식 아이콘 경로 전체를 프로세스 단위로 직렬화했다.
  `ShellTypeNameProvider` 도 같은 API 라 함께 지난다. 대가는 없다시피 하다: 둘 다
  `SHGFI_USEFILEATTRIBUTES` 라 저장소에 닿지 않고 조회는 확장자마다 한 번이다.
  `**StaWorkQueue` 로는 풀리지 않는다** — 아파트먼트를 보장할 뿐 워커가 넷이다.
- [x] **같은 폴더를 다시 여는 경로에서 썸네일 요청을 끊으면 안 된다.** 시작할 때 복원과

  활성화 라우팅이 같은 폴더를 두 번 연다. 두 번째가 `Reset()` 하면 진행 중 세대가
  죽는데, 항목 인스턴스는 그대로라 (`MergeItems`) View 는 "보이는 것이 바뀌었다" 고
  볼 일이 없어 **다시 밀지 않는다.** → `movedAway` 일 때만 끊는다.
  **교훈**: View 쪽 중복 제거 캐시와 ViewModel 쪽 `Reset()` 은 서로를 모른다. 한쪽이
  상태를 버리면 다른 쪽이 되살릴 신호가 없다.

**사람이 확인했다** (2026-08-06, B-4 세션):

- [x] **type-ahead** — 점프·1초 리셋·주소줄 포커스일 때 주소줄 입력 전부 동작한다.

  **다만 처음에는 화면이 따라오지 않았다** (아래 스크롤 결함).
- [x] `**Ctrl+L`·`Alt+D`** — 좌우 페인이 각자 자기 주소줄로 가고 전체 선택된다.
- [x] **뷰 3종의 키보드 이동** — 목록 뷰의 방향 반전, 타일·큰 아이콘의 2D 이동,

  PageUp/Down 한 화면 전부 맞다. **이것도 스크롤 결함에 가려 있었다.**
- [x] **wrap 뷰의 클릭** — 칸 선택·마지막 줄 빈 칸 해제 둘 다 동작한다.
- [x] **대용량 폴더 스크롤 체감** — `C:\Windows\WinSxS`(24,115개) 큰 아이콘으로 매끄럽다.
- [x] `**CacheLength` 기본값의 메모리 — 실측했다.** WinSxS 를 큰 아이콘으로 한참 훑은 뒤

  **WS 417MB · Private 318MB · 핸들 1,122**. 시작 직후는 263MB 였다. 96px 가 항목당
  최대 256KB 인 것을 감안하면 예상 범위이고 계속 오르지는 않았다. 지금은 두되,
  거슬리면 `CacheLength` 를 줄인다.

**이 확인이 잡은 결함** (B-3 이 남긴 것이다):

- [x] **포커스가 움직여도 목록이 스크롤하지 않았다.** 이동·선택을 `ListView` 내장에 맡기지

  않으므로 (ADR-016) **내장 스크롤 추적도 없다** — 방향키·PageUp/Down·type-ahead 가
  `FocusedName` 만 옮기고 화면은 그대로였다. 위 세 항목이 전부 이 하나에 가려 있었다.
  → `Views/FocusScroll.cs`. 소스가 둘이라 (Details 는 항목, wrap 뷰는 행) 무엇을
  스크롤할지가 판정거리다. 새 폴더처럼 **아직 목록에 없는 이름**은 목록이 채워질 때
  다시 본다.
- [x] `**DESIGN.md` §7 의 큰 아이콘 글리프 `E8A6` 은 틀렸다** — 그 자리는 잠긴 문서

  (ProtectedDocument)다. 큰 사각 하나(`E15B`)로 바꿨고 §7 표도 고쳤다 (사용자 결정
  2026-08-06). 남은 판단: `E8A4`(자세히)와 `E8FD`(목록)가 서로 비슷해 보인다 —
  실물에서 네 버튼을 나란히 보고 거슬리면 그때 넷을 함께 다시 고른다.
- [ ] **타일 칸은 `UniformGrid` 로 늘린다** (§2 의 "최소 폭 220, 가변"). 마지막 줄이 윗줄과

  같은 칸 너비여야 해서 `RowViewModel.Capacity` 를 더했다 — 실물에서 어색하지 않은지.

### B-4 (상호작용) 구현 결과 — 사람 확인 항목

이름변경 인라인 편집 · 드래그앤드롭 · `IContextMenuProvider` 셋이 들어왔다.
**완료 기준을 "게이트 4종 + 순수 판정 테스트 + 사람 확인 체크리스트 전부"로 정했고**
(사용자 결정 2026-08-06) 아래가 그 체크리스트다. 전부 닫혔다.

**결정 둘** (다시 정하지 않는다)

- [x] **컨텍스트 메뉴 포트에 창 핸들을 두지 않는다.** `FlexDir.Core` 는 HWND 를 모르고

  (CLAUDE.md §1) `Host/Startup/OwnerWindow` 가 창을 쥐고 공급자를 물려 준다.
  대가는 "어느 창 위에"를 호출자가 못 정한다는 것이고 창이 하나라는 전제에 기댄다.
  `**IFileOperation.SetOwnerWindow` 도 같은 배선으로 풀렸다** — §A 의 열린 항목이 닫혔다.
- [x] **메뉴 루프는 STA 워커에서 돌고 주인은 자체 숨은 창이다.** WPF 창을 쓰면

  `QueryContextMenu`·`InvokeCommand` 가 UI 스레드로 온다 (CLAUDE.md §3).
  전문은 `SHELL_NOTES.md` §컨텍스트 메뉴.

**실물 확인됐다** (2026-08-06)

- [x] **이름변경** — `F2`·선택 항목 재클릭으로 시작, `Enter` 확정, `Esc` 취소,

  **포커스 상실 취소**. 초기 선택 범위 4종(`a.txt`→`a` · `report.tar.gz`→`report.tar` ·
  `.gitignore`/`noext`→전체 · 폴더 `folder.v2`→전체)이 전부 맞다.
  `**Ctrl+Shift+N` 이 새 폴더를 만들고 화면이 거기로 스크롤되며 편집기가 열린다** —
  "아직 목록에 없는 이름" 경로가 실물에서 통했다.
- [x] **편집기 안의 마우스** — 클릭으로 캐럿 이동 · 드래그로 글자 선택 · 더블클릭으로

  단어 선택 · 우클릭으로 TextBox 자체 메뉴. 넷 다 목록이 손대지 않는다.
- [x] **드래그앤드롭** — 기본 복사 · `Shift` 이동 · **여러 개** · 폴더 항목 위 · 파일 항목

  위 · 탐색기 ↔ flex-dir 양방향 · `Alt` 는 아무 일도 하지 않는다.
- [x] **컨텍스트 메뉴** — 항목 메뉴 · 배경 메뉴 · 선택 밖 우클릭이 선택을 옮기고 다중 선택

  우클릭은 선택을 유지한다 · 항목 실행 시 대화상자가 flex-dir 창을 부모로 한다.
  **오너드로 항목(7-Zip·TortoiseSVN·Kaspersky·공유 등)이 글자와 아이콘까지 그려지고**
  (함정 1 이 맞았다) **메뉴 밖을 누르면 닫힌다** (숨은 창 설계가 성립한다).
- [x] **클릭 확정이 마우스 업으로 바뀐 뒤에도** 평 클릭·`Ctrl`·`Shift` 선택과 더블클릭

  열기가 그대로다 (B-2 의 미확인 항목도 여기서 함께 닫혔다).

**실물이 잡은 버그 셋** (자동 채점이 닿지 않던 자리다)

- [x] `**KeyDown` 을 전부 `Handled` 로 표시하면 타이핑이 죽는다.** 방향키가 목록으로 새는

  것을 막으려 무조건 삼켰더니 WPF 가 그 키에서 `TextInput` 을 만들지 않았다 —
  **캐럿·선택은 멀쩡한데 글자만 안 들어간다.** → 삼킬 키를 명시한다
  (`RenameEditor.Swallows`). `Delete` 는 창이 휴지통에 걸어 두었으므로 삼키는 쪽이다.
- [x] **목록의 터널링 핸들러가 편집기 클릭을 먼저 가져간다.** `PreviewMouseLeftButtonDown`

  은 목록에서 편집기로 내려가므로 목록이 `Focus()` 를 하면 편집이 취소된다 — 캐럿을
  옮기려고 누른 것뿐인데. → `ListInput.IsEditing`. 같은 함정이 더블클릭(파일이 열린다) ·
  우클릭(shell 메뉴가 뜬다) · 드래그(파일이 딸려 나간다)에도 있었다.
- [x] **마우스 다운이 선택을 즉시 접으면 여러 개를 끌 수 없다.** 탐색기가 선택 확정을

  마우스 업으로 미루는 이유가 이것이다. → `ListInput.DefersSelection`.
- [x] **COM 인터페이스의 배열은 기본이 `SafeArray` 다** — `GetUIObjectOf` 가 PIDL 배열을

  SAFEARRAY 로 받아 **프로세스가 죽었다**(`0xC0000005`). `SHELL_NOTES.md` §컨텍스트
  메뉴 함정 9 에 올렸다. 자동 테스트가 절대 못 잡는 종류다.

## 마감 (v1) — 구현 결과 · 사람 확인 항목

제품 아이콘 · 32px 커스텀 타이틀바 · 주소줄 breadcrumb · 상태표시줄 여유 용량 ·
도그푸딩 게이트 스크립트가 들어왔다 (2026-08-06). 시안(`docs/mockups/v1-two-pane.html`)과
실물을 픽셀로 대조한 결과가 근거다.

**실물 확인됐다** (창 캡처 `PrintWindow`)

- [x] **제품 아이콘이 창·작업표시줄·exe 셋에 붙는다.** 타이틀바 좌측, 작업표시줄 버튼,

  `ExtractAssociatedIcon` 으로 꺼낸 exe 아이콘 모두 직접 그린 32px 프레임이다.
  `.ico` 는 16·32·48·256 네 프레임을 담고 WPF 디코더로 픽셀까지 다시 읽었다.
- [x] **32px 커스텀 타이틀바.** "flex-dir" 좌측 · 캡션 버튼 46×32 우측 · 창 배경이 이어진다.

  **최대화하면 글리프가 복원(`E923`)으로 바뀌고** 닫기 버튼이 화면 오른쪽 끝에 붙는다.
- [x] **최대화가 작업영역과 정확히 맞는다.** 창 rect 는 사방 8px 나가지만 보이는 내용은

  화면 좌표 `(0,0)-(2560,1392)` 다 — 잘리지 않는다 (HISTORY.md §최대화 여백에 전문).
- [x] **주소줄 breadcrumb.** `C: › Users › SOOJANG › dev › github`, 구분자는 칸 사이에만,

  활성 페인 accent 테두리는 그대로다.
- [x] **상태표시줄 여유 용량.** 좌측 `항목 3개` · 우측 `여유 공간 252.8 GB` — 목업 그대로.
- [x] **페인 테두리·라운드는 처음부터 있었다.** 시안 차이인 줄 알았으나 오독이었다 —

  캡처의 바깥 8px 은 창의 보이지 않는 리사이즈 테두리다 (HISTORY.md §시안 대조).

**사람이 손으로 확인했다** (2026-08-06 · 사용자 smoke 테스트, 다섯 항목 전부 통과)

- [x] **트레이 아이콘** — 알림 영역 오버플로(`^`) 안에 제품 아이콘이 있고 우클릭 메뉴와

  완전 종료가 동작한다. (자동 캡처로는 오버플로가 접혀 있어 못 봤던 항목이다.)
- [x] **타이틀바 조작감** — 끌어서 옮기기 · 더블클릭 최대화 · Aero Snap · Win+방향키 ·

  화면 위로 밀어 최대화. 전부 `WindowChrome.CaptionHeight` 가 OS 에 넘긴 것이라
  우리 코드가 없다 — 그래서 손으로 만져 봐야 했고, 그대로 온다.
- [x] **다른 배율·다른 모니터에서의 최대화** — 이상 없다. 훅 없이 두기로 한 결정

  (HISTORY.md §최대화 여백)이 이 기계 구성에서는 맞았다.
- [x] **breadcrumb 상호작용** — 칸 클릭으로 상위 이동 · 빈 자리 클릭으로 편집 ·

  `Ctrl+L`/`Alt+D` 진입 + 전체 선택 · `Esc` 복귀 · `Enter` 이동.
- [x] **여유 용량이 드라이브를 따라간다.**

> 되돌릴 일이 생기면 breadcrumb 은 커밋 `feat(core,app): 주소줄을 breadcrumb 으로 바꾼다`
> 하나만 되돌리면 된다. 일부러 단독 커밋으로 뒀다.

**남은 확인 잔여** — 이 기계에서 못 보는 것뿐이다: 클라우드 자리표시자(§A) ·
네트워크 경로(§A, v2) · 연결 프로그램 없는 확장자와 파일 조작 실패 대화상자(§A).

## C. Host — 진입점과 DI 조립 (`FlexDir.Host`) — **끝났다**

`tests/FlexDir.Host.Tests/` 가 새로 생겼고 `FlexDir.sln` 에 들어 있다.
아래는 결정과 그 근거다.

- [x] **WPF 진입점 = 명시적 `Main`.** `App.xaml`(`ApplicationDefinition`)로 가지 않았다.

  이유 둘: (1) **두 번째 실행은 WPF 초기화 비용을 내기 전에 끝나야 하는데**
  `ApplicationDefinition` 이 만드는 `Main` 은 곧바로 `Application` 을 세우므로 그 판정을
  앞에 둘 자리가 없다 — 상주 프로세스를 고른 이유가 그 비용이다(ADR-003).
  (2) 그 길로 가면 조립을 넣을 곳이 `App.xaml.cs` 밖에 없어진다.
  `Program.cs` 는 TDD 가드의 검사 대상이 아니므로 **판단은 한 줄도 두지 않았다** —
  순서만 정하고 판단은 전부 `AppComposition` · `ActivationRouter` · `SingleInstanceGate` 에 있다.
- [x] **DI 컨테이너 없음 — 수동 조립 (`Composition/AppComposition.cs`).**

  조립 대상이 열둘 남짓이고 그래프가 하나다. 컨테이너는 (1) cold start 에 리플렉션
  비용을 얹는데 그것이 ADR-003 이 상주 프로세스를 고른 바로 그 비용이고, (2) 정말
  어려운 수명(STA 를 든 shell 구현체)을 컨테이너 규약 뒤로 숨긴다.
  **포트 인스턴스는 두 페인이 나눠 쓴다** — 페인마다 만들면 STA 워커가 두 배가 되고
  `ShellTypeNameProvider` 의 확장자 캐시까지 두 벌이 된다.
- [x] **Dispose 는 조립이, 완전 종료 시점에.** `AppComposition.DisposeAsync` 가

  **페인 둘을 먼저 접고 그 다음 shell 구현체**를 닫는다 — 뒤집으면 진행 중 요청이 닫힌
  STA 큐에 들어가 관측되지 않는 예외가 된다. 창이 닫힐 때는 부르지 않는다: 상주
  프로세스는 창 없이 살아 있고 그때 STA 워커까지 접으면 다음 창이 그 비용을 다시 낸다.
  **STA 를 든 구현체는 넷이 아니라 다섯이다** — 이 문서와 `HANDOFF.md` 가 빠뜨린 것은
  `ShellTypeNameProvider` 다. `AppCompositionTests` 가 다섯 전부를 세고 각각이 정말
  닫혔는지 본다.
- [x] **single instance = 이름 있는 뮤텍스(판정) + 이름 있는 파이프(전달).**

  파이프 서버 생성만으로 판정하지 않는다 — 서버는 요청 하나마다 닫고 다시 열어야 하는데
  그 틈에 들어온 두 번째 실행이 자기를 상주 프로세스로 착각한다. 뮤텍스는 기다리지도
  놓지도 않는다(`createdNew` 하나로 판정이 끝나므로 스레드 친화성 문제가 없고, 프로세스가
  죽으면 이름이 사라져 다음 실행이 새 상주 프로세스가 된다).
  활성화는 `ActivationRouter` 가 받아 **사용을 기록하고 인자의 폴더를 활성 페인에서 연다** —
  왼쪽에 못박지 않는다.
- [x] **계측 = `Diagnostics/PerformanceLog.cs` → `%APPDATA%\flex-dir\perf.log`.**
      (2026-08-07에 `%LOCALAPPDATA%` 에서 옮겼다 — ARCHITECTURE §4)

  지점 셋과 예산(`docs/PRD.md` §5)이 코드 안에 있고 줄마다 예산을 함께 적는다 —
  나중에 보는 사람이 문서를 찾지 않아도 판정이 서야 하고, 수치가 전부 잠정이라 예산이
  바뀌면 옛 줄과 갈리는 것도 보여야 한다. phase C 시점에 기록되던 것은 `ColdStart`
  하나였고, `**WindowShown`·`FirstItem` 은 B-2 가 창을 만들면서 붙었다** — 셋 다
  실측 수치가 있다 (위 §B-2 사람 확인 항목).
  별도 벤치마크 CLI 는 만들지 않았다(`docs/ARCHITECTURE.md` §7).
- [x] `**IUsageLog` — 포트(`Core/Usage`) · 구현(`Shell/Usage/FileUsageLog.cs`) · 배선을 한 번에.**

  **"사용" 은 사용자가 창을 요구한 순간으로 정했다.** 프로세스 수명도 창 표시 시간도
  아니다: 전자는 로그인 후 계속 사는 프로세스가 **7일 중 7일**을 만들어 게이트를
  무력화하고, 후자는 띄워 놓고 자리를 비운 시간을 사용량으로 세며 ADR-007 이 묻는 숫자도
  아니다(며칠이지 몇 시간이 아니다). 그래서 남기는 것은 시각 하나이고 게이트는 **서로
  다른 날짜 수**를 센다. 파일은 **한 줄에 하루, 앞 10자가 `yyyy-MM-dd`** — 이것을 읽는
  것은 우리 코드가 아니라 게이트 스크립트라 파일의 모양 자체가 계약이다.
  부르는 곳은 지금 `ActivationRouter` 하나(실행 + 활성화)이고, 창이 생기면 창 표시가
  같은 자리를 부른다. 게이트 자체는 **v1 완성 후** 켠다.

### 사람 확인 항목 — phase C

실물은 Release 빌드 `src/FlexDir.Host/bin/Release/net9.0-windows/FlexDir.Host.exe` 다
(framework-dependent). 단일 파일·self-contained 로 게시하면 수치가 달라진다.

- [x] **두 번째 실행이 정말 기존 프로세스로 가는가** — 간다. 상주 프로세스가 뜬 상태에서

  `FlexDir.Host.exe C:\Windows` 를 실행하면 **exit code 0 으로 124ms 만에 끝나고**
  프로세스 수는 1 그대로다. exit 0 은 파이프에 붙어 인자를 다 쓴 경우에만 나온다 —
  붙을 상대가 없으면 1 이다. **다만 상주 프로세스가 그 인자로 정말 폴더를 열었는지는
  창이 없어 밖에서 볼 수 없다.** 그 경로는 `ActivationRouterTests` 가 자동으로 잡고,
  눈으로 보는 것은 phase B 다.
- [x] **상주 프로세스를 끝내면 다음 실행이 새 상주 프로세스가 되는가** — 된다.

  `Stop-Process` 뒤 세 번 연속 실행이 모두 살아남았다(뮤텍스 이름이 제대로 사라진다).
  이것이 안 되면 앱이 두 번 다시 뜨지 않는다.
- [x] **cold start** — `perf.log` 기준 **358ms**(첫 실행, 디스크 캐시가 찬 상태가 아님) ·

  이후 **134 / 128 / 123ms**. `docs/PRD.md` §5 의 잠정 목표 1.5s 대비 4~12배 여유다.
  재는 구간은 `Process.StartTime` 부터 조립이 끝난 시점까지다.
- [x] **사용 기록이 하루 한 줄인가** — 그렇다. 같은 날 실행 4회 + 활성화 1회에

  `usage.log` 는 한 줄이었다.

> 아래 넷은 "창이 없어 잴 수 없다" 로 열려 있었다. **B-2 가 창을 만들면서 전부 닫혔다**
> (2026-08-06). 수치와 정황은 위 §B-2 사람 확인 항목이 정본이므로 여기서 되풀이하지 않는다.

- [x] **창을 닫아도 프로세스가 사는가** — 산다 (§B-2 "닫기 = 숨기기").
- [x] **상주 중 창 표시 (≤100ms)** — 예산 안 (§B-2 계측 실측 수치).
- [x] **폴더 전환 후 첫 항목 (≤150ms)** — 예산 안 (§B-2 계측 실측 수치).
- [x] **완전 종료 경로** — 트레이 메뉴의 "완전 종료" 가 그 조작이다 (§B-2 트레이 아이콘).

  `Shutdown()` 이 `Closing` 취소를 무시하는 것까지 실물로 확인됐다.

## 순서

```
phases/ 자율 실행 (Core + ViewModel)          ✅
   → A. Shell interop  (포트 11/11)           ✅
   → C. Host 뼈대       (진입점 + DI + single instance + IUsageLog + 계측)  ✅
   → B. View            (DESIGN §9 를 먼저 채운 뒤)   ✅
   → 마감               (아이콘 · 타이틀바 · breadcrumb · 여유 용량 · 게이트 스크립트)  ✅
   → 매일 쓰기 → 도그푸딩 게이트 ON   ← 다음
   → v1.1 (Details 그룹화 ✅) → 게이트 PASS 후 v2 (네트워크)   `docs/PRD-v2.md` (승인 2026-08-06)
```

## v2 네트워크 — 사람 확인 항목

**실물 확인됨** (2026-08-07 · NAS `\\10.10.10.23`, Z: 로도 매핑돼 있다)

- [x] **UNC 경로를 연다.** `\\10.10.10.23\11_연구개발부_…` 에서 폴더 10개가 열거되고
      형식 아이콘이 붙는다. breadcrumb 이 `\\10.10.10.23 › 11_연구개발부_…` 로 서버를
      첫 칸으로 쪼갠다.
- [x] **매핑 드라이브는 v1 때부터 됐다.** `Z:\` 는 드라이브 문자라 파서가 막지 않았다 —
      열거 185ms, 여유 용량 1,760.7 GB. **막혀 있던 것은 UNC 표기뿐이다.**
- [x] **공유의 여유 용량.** `GetDiskFreeSpaceEx` 로 바꾼 뒤 1,760.6 GB 가 나온다.
      그전에는 `DriveInfo` 가 던지고 "모른다" 로 접혀 조용히 사라졌다.
- [x] **`\\server` 공유 목록** — `\\10.10.10.23` 에서 **공유 30개**가 나온다
      (`01_대표이사` … `CI`). `$` 관리 공유는 숨는다. 크기·수정 시각은 빈 칸이다 —
      공유에 없는 값이라 그것이 "모른다" 다.
      `net view` 는 되고 `Win32_Share` 는 실패하는 격차를 이 NAS 에서 재확인했다.

**감시와 조작도 확인됐다** (2026-08-07 · `\\10.10.10.23\home\` — 사용자가 지정한 자리)

- [x] **SMB 에서의 `IFolderWatcher` — 돈다.** 다른 프로세스가 만든 파일이 뜨고
      (`항목 5개`→`6개`), **robocopy `/MT:32` 로 300개를 10.3초에 부어도** 목록이
      파일시스템과 정확히 일치했다(306개). 폴더를 rename 으로 치웠다 되돌려도 감시가
      살아남는다 — 핸들을 따라가기 때문이다. **ADR-011 의 자동 갱신 전제는 SMB 에서
      깨지지 않았다.** 폴백(주기적 재열거)은 넣지 않는다.
- [x] **파일 조작 넷이 UNC 에서 돈다.** 이름변경(F2) · 복사(`Ctrl+Alt+C`) ·
      이동(`Ctrl+Alt+M`) · 삭제(`Delete`) 전부 서버에 반영됐다. 조작 결과를 **반대편
      페인의 감시가 곧바로 잡는다.**
- [x] **대화상자 둘이 UNC 에서 뜨고 flex-dir 을 부모로 한다** — 이름 충돌("파일 바꾸기 또는
      건너뛰기")과 **영구 삭제 확인**("이 파일을 완전히 삭제하시겠습니까?"). 네트워크에는
      휴지통이 없어 후자가 뜨는 것이 정상이다. §열린 결정 의 "대화상자가 뜨는 실패 경로"
      중 조작 쪽을 여기서 밟았다.

**아직 사람이 봐야 하는 것**

- [ ] **감시가 연결 끊김을 견디는가.** 랜선 분리·절전 복귀는 확인하지 못했다 —
      어댑터를 끊으려면 관리자 권한이 필요하다. **위에서 본 것은 "서버가 살아 있는 동안"
      까지다.** 절전에서 깨어난 뒤 목록이 낡아 있으면 그때 폴백을 정한다.
- [ ] **느린/끊긴 서버에서의 조작감.** 이 NAS 는 185ms 로 빠르다. **없는 서버는 한 번에
      42초 블로킹한다**(실측 2026-08-07 · `\\10.10.10.199`) — 그 사이 UI 가 어떻게 보이는지는
      아직 안 봤다 (CLAUDE.md §3 이 겨냥한 자리).
- [ ] **탐색기 ↔ flex-dir 클립보드에 UNC 경로.** 읽는 쪽은 테스트로 고정했지만
      (`TryGetPaste_ReadsUncPaths`) 실물 왕복은 아직이다.
- [ ] **1219 를 앱 안에서 재현하지 못했다.** `net use` 로는 즉시 뜨지만(다른 사용자 이름으로
      같은 서버에 붙을 때) 앱의 열거는 기존 세션을 재사용하므로 그 창을 안 지난다.
      **안내 문구는 테스트로만 고정돼 있다** — 실물로 볼 기회가 오면 확인한다.

**드라이브가 뜬 김에 본 것 — 확인 대상이 아니었다**

- 스플리터 비율과 창 크기가 **조작 중에 저절로 바뀌는 것을 여러 번 봤다** (좌 페인이
  330px→595px→890px). 재현 조건을 못 잡았고 이번 변경과 무관해 보인다 (오류 분류만
  건드렸다). `SplitterSync`·`ResidentWindow` 근처를 볼 것.

  > **원인 하나를 찾아 고쳤다** (2026-08-07 · 커밋 `6617eb8`). `Thumb.DragCompleted` 가
  > 버블링이라 **페인 안의 스크롤바 썸**이 올린 것도 `SplitterSync` 의 핸들러에 닿았고
  > (실물로 확인: `Grid` 가 스크롤바 Thumb 과 GridSplitter 를 똑같이 받는다), 그것이
  > 실측 폭을 비율로 되썼다. 열에는 `MinWidth="320"` 이 걸려 있어서 **좁은 창에서는 실측
  > 폭이 사용자가 고른 비율이 아니라 벽에 막힌 폭**이다 — 목록을 한 번 스크롤하면 그
  > 값이 정본이 되고 넓은 창으로 돌아와도 원래 자리로 오지 않는다.
  > 이 기계는 **2560 과 1080(세로) 두 모니터**를 오간다: 1080 폭에서 도달 가능한 비율은
  > 0.30~0.70 뿐인데 VM 의 클램프는 0.15~0.85 라 **레이아웃이 자르는 구간이 넓다.**
  > 관찰된 330px 이 `MinWidth` 320 + 페인 안쪽 여백과 맞는다.
  >
  > **다만 이것이 관찰된 그 현상이라고 단정하지 않았다.** 원래 관찰(330→595→890)을
  > 재현하지는 못했다. 재현을 시도해 **아닌 것으로 밝혀진 것 둘**:
  > 닫기(=숨기기)→다시 열기 왕복은 **일반 창에서도 최대화 상태에서도 고정점이었다**
  > (각 6회, `rect`·`normalPosition`·DPI 가 한 픽셀도 안 변했다). `ResidentWindow` 의
  > `Closing`→`WindowPlacement`→`Apply` 되먹임은 적어도 그 두 경로에서 표류하지 않는다.

## 확인 도구 — 포그라운드를 뺏지 않고 창을 읽는 법 (2026-08-07)

합성 입력의 함정(포그라운드를 못 잡으면 남의 창으로 간다)을 아예 피하는 방법이다.
**읽기와 버튼 누르기는 UI Automation 으로 되고, 그것은 포그라운드를 요구하지 않는다.**

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# 화면의 글자 전부 — 알림 바·상태표시줄·breadcrumb 가 여기 다 나온다
$c = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
$root.FindAll('Descendants', $c) | ForEach-Object { $_.Current.Name }

# 버튼 누르기 (포그라운드 불필요)
$btn = $root.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, '나중에')))
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
```

- **페인 폭(= 스플리터 위치)** 은 `ControlType.List` 의 `BoundingRectangle` 로 잰다.
  페인마다 List 가 **둘씩** 잡히므로 X 로 정렬해 양 끝을 쓴다.
  둘의 정체는 **breadcrumb 과 항목 목록**이다 — `Height` 로 가른다(breadcrumb 은 20px).
  breadcrumb 의 자식 이름은 `PathSegment { Name = ..., ... }` 로 나오므로 `Name = ([^,]+),`
  를 뽑아야 폴더 이름이 된다. **어느 페인이 어느 폴더인지는 이것으로만 확정된다** —
  X 위치로 넘겨짚으면 창이 움직였을 때 조용히 반대편을 잰다.

**UIA 로 목록 선택은 만들 수 없다** (2026-08-11 · 여기서 없는 결함을 보고했다).
선택은 `PaneSelection` 이 **이름 집합**으로 들고 행에는 `IsSelected` 가 없다
(ADR-011 · `Views/ViewConverters.cs` 의 `MultiBinding`). 그래서
`SelectionItemPattern.Select()` 는 `ListBoxItem` 자체 플래그만 세우고 **앱의 선택 모델에
닿지 않으며**, 다음 갱신에 바인딩이 덮는다. 화면상으로는 "갱신이 선택을 날렸다" 로 보여
**멀쩡한 코드를 결함으로 보고하게 된다.**
→ 선택이 필요한 확인은 **사람이 클릭해서 본다.** 판정 신호는 상태표시줄인데 문구가
`항목 N개` 와 `N개 선택` 두 갈래이므로, 정규식을 `^항목 \d` 로 좁히면 선택 중을 놓친다.

**스크롤은 입력 없이 걸 수 있다** — `ScrollPattern.SetScrollPercent`. 선택을 건드리지 않고
`VerticalScrollPercent` 로 되읽을 수 있어, 갱신이 스크롤을 튀게 하는지는 이것만으로 판정된다.

**컨테이너 재생성(=깜박임)을 잴 때는 비율로 본다.** 실현된 항목의 `GetRuntimeId()` 집합을
통째로 비교하면 30개 중 하나만 바뀌어도 "재생성" 으로 찍힌다 — `Reset` 이면 **전부** 바뀌고
`MergeItems` 면 바뀐 줄만 바뀐다. 판정 폴더도 고른다: `C:\Users\SOOJANG` 이나
`home\soojang` 은 진짜 변경이 섞여 들어오고, `/etc` 는 아무도 안 건드려 병합조차 0 이어야
하므로 깨끗하다. 갱신주기가 30초이므로 **최소 3주기(100초 이상)** 를 봐야 의미가 있다.

**트리에는 합성 우클릭이 닿는다** (2026-08-10). 목록에서 한 번도 닿지 않아 "우클릭은 사람에게
맡긴다" 로 적어 두었는데, 트리 노드에서는 `SetCursorPos` + `mouse_event(RIGHTDOWN/UP)` 이
그대로 통했다 — 포그라운드만 확보하면 된다. 메뉴는 **별도 최상위 창**이라 주 창을
`PrintWindow` 해도 안 나오므로, `EnumWindows` 로 같은 PID 의 보이는 창 중 주 창이 아닌 것을
찾아 그것을 찍는다. **찍는 것까지 한 스크립트 안에서 해야 한다** — 명령을 나누면 그 사이
포커스가 옮겨져 메뉴가 닫힌다.

> 그러므로 "우클릭은 확인할 수 없다" 는 **목록에 대해서만** 참이다. 무엇이 다른지는
> 밝히지 못했다 (트리는 우리 WPF 메뉴, 목록은 shell 의 `TrackPopupMenuEx` 다).

**"포그라운드만 확보하면 된다" 로는 부족하다** (2026-08-11 에 좁혔다). `AttachThreadInput`
recipe 로 포그라운드를 잡고 `GetForegroundWindow()` 까지 확인한 뒤 좌클릭을 보냈는데,
**좌표를 재고 누르는 사이에 창이 491px 옆으로 움직여** 클릭이 목록이 아니라 폴더 트리로
갔다 — 활성 페인이 엉뚱한 곳으로 튀었고, 그 사실은 몇 분 뒤에야 드러났다.
→ **좌표는 `SetCursorPos` 직전에 다시 잰다.** 그리고 클릭 뒤 `GetWindowRect` 를 다시 재
창이 움직였는지 본다. `LEFTDOWN` 뒤 마우스가 움직이면 타이틀바를 끌게 되는 것도 같은
자리다. 사용자의 창을 흔드는 확인이므로 **안 써도 되면 안 쓴다.**

**트리 노드는 `ControlType.TreeItem` 으로 잡힌다.** 다만 자식을 읽기 전에는
`ExpandCollapseState` 가 `LeafNode` 라 `ExpandCollapsePattern.Expand()` 가 통하지 않는다
(지연 로딩의 대가 — docs/PRD-v2.md §10). 펼치기는 화살표를 좌클릭한다.
- 창 핸들은 `EnumWindows` 로 **제목이 정확히 `flex-dir`** 인 것을 고른다 — shell 대화상자가
  뜨면 `MainWindowHandle` 이 그쪽으로 옮겨간다 (아래 §열린 결정 의 주의와 같은 이유).
- 창을 닫는(=숨기는) 것은 `PostMessage(hwnd, WM_CLOSE)` 로 한다. 이것도 포그라운드가 없다.
- **그림이 필요할 때만** `PrintWindow` 로 캡처한다. 그 캡처의 바깥 8px 은 창이 아니다
  (§시안 대조 참조).

**트레이 아이콘도 UIA 로 읽힌다 — 다만 이 기계는 숨김 버킷에 둔다** (2026-08-11 · §14 확인).
`Shell_TrayWnd` 를 훑으면 **보이는** 알림 아이콘만 나오고 `flex-dir` 은 거기 없다.
숨겨진 것을 보려면 `숨겨진 아이콘 표시` 버튼을 `InvokePattern` 으로 누른 뒤 오버플로 창을
읽고, 다시 눌러 접는다 (포그라운드 불필요).

```powershell
# 오버플로 창은 열려 있는 동안만 존재한다. 클래스는 TopLevelWindowForOverflowXamlIsland.
# FindWindow 로는 놓쳤고 EnumWindows 로 잡았다 — 열고 나서 EnumWindows 로 클래스를 찾는다.
$chev = $tray.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, '숨겨진 아이콘 표시')))
$chev.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
# … EnumWindows 로 TopLevelWindowForOverflowXamlIsland 핸들 → FromHandle → Button 열거 …
$chev.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()   # 접는다
```

**UI 스레드 예외는 임시 트리거로만 던질 수 있다** (2026-08-11 · docs/PRD-v2.md §14).
밖에서 남의 프로세스 UI 스레드에 예외를 넣을 길이 없다. 던지는 `[RelayCommand]` 와 그것을
부르는 툴바 버튼(`AutomationProperties.Name` 을 꼭 준다 — 없으면 UIA 에 글리프 문자가
이름으로 잡힌다)을 임시로 붙였다가 확인 후 걷어낸다. **저장소 Debug 빌드로 해야 하므로
설치본을 먼저 죽인다** (뮤텍스가 같다). 끝나면 `error.log` 를 지우고 설치본을 되살린다 —
그 파일에 남은 스택은 걷어낸 메서드를 가리킨다.

> **판정의 정본은 프로세스 생존이 아니라 `error.log` 다.** 프로세스는 `fatal` 을 적은
> 뒤에도 잠깐 살아 있어서, 500ms 간격으로 생존을 보는 루프는 한 번 늦게 읽는다.

## 배포 — 사람 확인 항목

정본은 `docs/PRD-v2.md` §9. 여기는 **사람이 봐야 하는 것**만이다.

**확인됨** (2026-08-07)

- [x] **설치 → 실행.** `flex-dir-win-Setup.exe` 가 `%LOCALAPPDATA%\flex-dir\` 에 설치하고
      앱이 뜬다. 시작 메뉴·바탕화면 바로가기가 생긴다. 관리자 권한을 묻지 않는다.
- [x] **자동 업데이트 전체 흐름.** 0.1.0 설치 → 0.1.1 릴리스 → 알림 바
      (`새 버전 0.1.1 이 준비됐습니다.`) → `지금 설치` → 패키지 교체 후 재시작.
- [x] **상태가 설치를 견딘다.** 상태를 `%APPDATA%` 로 옮긴 뒤 재설치해도
      `usage.log`·`perf.log` 가 남는다. **옮기기 전에는 첫 설치가 그것을 지웠다.**
- [x] **설치본을 켠 채 게이트 4종 통과.** 저장소 산출물을 잠그지 않는다.

- [x] **제거 → 재설치** (2026-08-07 · 0.1.2). 제어판에 등록된 명령 그대로
      (`Update.exe --uninstall`) **상주 중인 채로** 돌렸다: 프로세스가 종료되고
      `%LOCALAPPDATA%\flex-dir` 가 통째로 사라지고 바로가기 둘(시작 메뉴·바탕화면)과
      제어판 등록도 지워진다. **`%APPDATA%\flex-dir` 셋은 해시까지 그대로 남았다**
      (`usage.log`·`perf.log`·`view-state.json`). 재설치 뒤 `usage.log`·`view-state.json`
      해시가 여전히 같고, **창이 저장된 자리(세로 모니터 최대화)로 복원됐다.**
- [x] **`나중에` 를 누른 뒤의 동작** (2026-08-07 · 0.1.1 → 0.1.2). 알림 바가 접히고 버튼
      둘이 사라진다. **그런데 다음 실행에 다시 뜨지 않는다** — 이 문서와 주석이 기대하던
      것과 다르다. 받아 둔 패키지를 다음 실행의 `VelopackApp.Build().Run()` 이 시작
      지점에서 적용하므로 **그 실행이 이미 새 버전**이고 피드에는 더 새 것이 없다.
      결론: **미루는 대상은 재시작이지 설치가 아니다.** 결정이 지키려던 것("상주 앱을
      마음대로 재시작하지 않는다")은 지켜진다. 정본은 `docs/PRD-v2.md` §9.

**아직 사람이 봐야 하는 것**

- [ ] **SmartScreen 경고.** 코드 서명이 없어 처음 받는 기계에서 "Windows의 PC 보호" 가
      뜬다. **이 기계에서 재현을 시도했고 실패했다** (2026-08-07): 릴리스에서 받은
      `Setup.exe` 에 브라우저가 붙이는 것과 같은 MOTW
      (`Zone.Identifier` / `ZoneId=3`)를 달아 실행했는데 **경고가 뜨지 않고 그냥
      설치됐다.** 이 기계의 SmartScreen 설정 레지스트리 값은 비어 있고(기본값)
      `Get-MpPreference` 는 실패해 켜짐 여부를 확정하지 못했다.
      → **차단 요인은 "이미 실행한 기계" 가 아니라 이 기계의 평판/설정일 수 있다.**
      여전히 다른 기계가 필요하다.
      주의: 그 시도가 **상주 앱을 끄고 재설치했다** — setup 은 기존 인스턴스를 먼저 죽인다.
- [ ] **피드에 못 닿을 때.** 사내망 밖·서버 꺼짐에서 조용히 지나가는지. 실패를 삼키는
      경로라 **틀려도 화면에 아무것도 안 나온다** — 그래서 사람이 봐야 한다.
      **2026-08-07 세션에서 건너뛰기로 했다** (어댑터 제어에 관리자 권한이 필요하다).
      볼 때는 오프라인에서 재시작해 화면·대화상자와 **`perf.log` 의 ColdStart 수치**를
      함께 본다 — 확인이 시작을 붙잡으면 그 수치가 먼저 말한다.

## 탭 — 사람 확인 항목 (2026-08-11 · 정본은 `docs/PRD-v2.md` §17)

**화면이 섰고 사용자가 밟았다** (2026-08-12 · 저장소 Debug 빌드). 기계로 닿는 데까지는
그 전날 확인했고 (§17 §화면이 선 뒤 기계로 확인한 것 — 탭 줄 실측 · 균등 축소 · 키 일곱 ·
메뉴 일곱), **기계가 못 밟던 넷을 포함해 아홉 항목이 전부 정상이었다.**
정본 표는 `docs/PRD-v2.md` §17 §아직 사람이 봐야 하는 것 이다.

**이미 닫힌 것 하나**: *"활성 탭이 구분되지 않는다"* (사용자 · 2026-08-11). 배경만으로는
대비가 6/255 였다 — **위쪽 2px accent 라인**이 들어갔다 (`docs/DESIGN.md` §1-1).

**밟는 순서** — 아래 ⭐ 넷이 기계가 못 밟던 것이다 (드래그 둘은 모달 루프,
'보내기' 는 고친 직후였다).

- [x] **`Ctrl+T` 를 눌러 탭을 늘렸다 줄여 본다.** 폭이 180 에서 균등하게 줄고 90 에서 멈춘 뒤
      넘치는지. 넘친 상태에서 **휠이 탭 줄 위에서 가로로 구르는지**, `Ctrl+Tab` 으로 화면 밖
      탭으로 가면 **그것이 끌려오는지**.
- [x] ⭐ **탭을 끌어 순서를 바꾼다.** 삽입선(2px accent)이 놓을 자리에 뜨는지, 끌리는 탭이 원래
      자리에 반투명으로 남는지, **놓은 자리에 정확히 서는지**(한 칸 밀리면 `Target` 보정이
      틀린 것이다).
- [x] ⭐ **탭을 반대편 페인 탭 줄로 끈다.** 줄 전체가 `--sel` 로 물드는지, 건너간 탭이 도착
      페인의 활성 탭 **바로 오른쪽**에 서고 활성이 되는지, **활성 페인은 원래 자리에 남는지**.
- [x] ⭐ **컨텍스트 메뉴 '반대편 페인으로 보내기'** — 활성이 **아닌** 탭을 우클릭해 보낸다.
      우클릭한 그 탭이 건너가야 한다 (활성 탭이 아니라).
- [x] **목록에서 파일을 끌어 탭 줄 위로 가 본다.** 아무 표시도 뜨지 않아야 한다 (MVP 제외).
- [x] **NAS(`\\10.10.10.23`) 탭과 로컬 탭을 오간다.** 전환 직후 목록이 눈에 걸리게 흔들리는지.
      이 구간이 만든 동작이라 화면이 서는 지금 처음 보인다.
- [x] **보조 모니터(1080 세로)에서 스플리터를 끝까지 민다.** 320px 페인에서 탭 3개의 폴더
      이름이 구분되는지. **긴 이름**(`Program Files (x86)` 같은)으로 봐야 진짜다.
- [x] ⭐ **탭 제목 이름 바꾸기.** 편집기가 열리고 전체가 선택되는지, 다 지우고 확정하면 폴더
      이름으로 돌아가는지. **확정 뒤 방향키가 한 번 안 듣는 것은 알고 있는 자리다** (§17).
- [x] **탭 8개 이상에서 메모리.** 큰 폴더 여럿을 열어 두고 잰다.
      (**수치는 남기지 않았다** — 사용자가 거슬리지 않는다고 판정했다. 숫자가 필요해지면
      다시 잰다.)
- [ ] **고정 탭이 실제로 쓰이는가** — 즐겨찾기와 겹치는 자리다 (§17 §겹치는 자리).
      **이것만 열려 있다. 한 번 밟아서 닫히는 종류가 아니라** 며칠 써야 한쪽이 안 쓰이는
      것이 보이는 관측 항목이고, §17 이 애초에 *"도그푸딩이 판정한다"* 로 두었다.

## v1.1 — 사람 확인 항목

### Details 그룹화 (ADR-017 · 2026-08-06)

**실물 확인됐다** (`PrintWindow` 캡처 + 합성 클릭)

- [x] **그룹마다 헤더가 하나씩 선다.** 유형 기준에서 `폴더 2 · 확장자 없음 1 · MD 2 ·

  PNG 2 · TXT 3` — 폴더가 한 그룹이고 확장자 없는 항목이 따로 선다.
- [x] **접기.** 헤더를 누르면 항목이 사라지고 **개수는 남는다.** 글리프가 `E70D`(▼)에서

  `E70E`(▲)로 뒤집힌다. 다시 누르면 펴진다.
- [x] **헤더 클릭이 선택을 풀지 않는다.** 목록의 터널링 핸들러가 그 클릭을 먼저 가져가는

  자리다 (B-4 함정 2 와 같다). 항목을 하나 고른 뒤 헤더를 눌러도 상태표시줄이
  `10개 중 1개 선택 · 20 KB` 그대로였다.
- [x] **그룹화가 꺼진 페인은 v1 경로 그대로다.** 같은 창의 오른쪽 페인으로 함께 확인했다.
- [x] **저장 호환.** 그룹화를 쓴 적 없는 기존 `view-state.json` 8개 폴더 기록이 그대로 읽힌다.

**아직 사람이 봐야 하는 것**

- [ ] **대용량 폴더(10만+)에서 그룹 투영의 체감.** 투영은 O(n) 이고 접기마다 다시 만든다 —

  정렬과 같은 비용이지만 실측이 없다. `C:\Windows\WinSxS` 급 폴더에서 유형 기준으로
  켜고 접기를 몇 번 해 본다.
- [ ] **키보드 이동이 접힌 그룹을 건너뛰는 체감.** ViewModel 테스트가 규칙은 고정했지만

  (`PaneGroupingTests`), 스크롤이 따라오는지는 손으로 봐야 한다.
  **여기서 결함 둘이 나왔다** — `FocusScroll.Target` 과 `VisibleRangeSync.Flatten` 이 그룹
  행을 몰라 스크롤이 포커스를 안 따라오고 아이콘이 안 붙었다. 고쳤고 재현 테스트가 있지만
  **실물에서 긴 폴더를 스크롤해 본 적은 아직 없다.**
- [ ] **PageUp/Down 의 한 화면.** 헤더 28 · 항목 24 라 `*Pitch` 상수와 어긋난다 — 근사인
      것이 의도다 (`docs/UI_GUIDE.md` §가변 행 높이의 예외). 얼마나 어긋나는지는 긴
      폴더에서 봐야 한다.
- [x] **툴바 분류 드롭다운** — 사용자가 확인했다 (2026-08-07). 다섯 항목이 뜨고 `없음` 에
      체크가 붙어 있다. **이것이 주 진입점이다** — 헤더 우클릭 하나만 두었을 때 사용자가
      찾지 못했고 실제로 뜨지도 않았다 (HISTORY.md §진입점을 하나만 둔 것이 틀렸다).
- [x] **헤더 우클릭 "분류 방법" 메뉴** — 뜬다 (2026-08-07 · 사용자 확인). `이름` 글자 위,
      즉 `HeaderButton` 위에서 뜨고 현재 기준(`유형`)에 체크가 붙어 있다.
      **이것이 처음에 안 뜨던 자리다** — 메뉴를 부모 `Grid` 에만 달아 두었었다.
- [x] **툴바 드롭다운의 한글** — 정상 (2026-08-07). 처음에는 전부 두부(□)였다.
      `ToolMenu` 스타일이 아이콘 폰트를 걸었고 `FontFamily` 가 상속되기 때문이었다.
- [x] **그룹 헤더와 접기** — 유형 기준에서 `폴더 17 · 확장자 없음 2 · JSON 1 · MD 2 · TXT 1`
      이 서고, 전부 접으면 헤더와 개수만 남으며 글리프가 뒤집힌다 (2026-08-07 · 사용자 확인).
      폴더가 한 그룹인 것과 확장자 대문자 라벨도 설계 그대로다.

  자동 캡처로는 컨텍스트 메뉴를 못 잡는다.

B 를 마지막에 두는 이유: 화면이 붙기 전에 Shell 구현체가 실물 폴더에서 동작하는지
확인할 수 있고, 그 단계의 버그를 UI 버그와 섞지 않을 수 있다.

§B 의 `**ThumbnailRequestScheduler` 배선**은 **ViewModel 쪽이 끝났다**(위 §B) —
`PaneViewModel` 이 소유하고, `LoadAsync` 가 `Reset()` 을, `DisposeAsync` 가 함께 정리하며,
`public void SetVisibleRange(IReadOnlyList<FileItemViewModel>)` 가 열려 있다.
**B 에 남은 것은 그 메서드를 미는 attached behavior 하나다.** 코드비하인드 금지와 부딪히는
항목이라 View 를 짜기 시작한 뒤에 정하면 이미 `*.xaml.cs` 에 스크롤 핸들러가 들어가 있게 된다.

**아직 열려 있는 것**: `DESIGN.md` §10 의 #4·#5 — 실물을 보고 판단한다. UIA 접근성 — v2 로 미뤘다 (ADR-016).