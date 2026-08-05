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

**포트 8개가 끝났다.** §C(Host)도 끝났다 — 다음은 §B(View)다. 아래 §순서 참조.

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
- [x] **확정문과 스케줄러 시그니처의 어긋남 → 둘 다 맞다. 층위가 다르다.**
      이 확정문이 말한 "크기를 받지 않는다" 는 **`PaneViewModel` 의 공개 표면**이고,
      `ThumbnailRequestScheduler.SetVisibleRange(visible, requestedSize)` 는 그 아래층이다.
      스케줄러는 크기를 **캐시 키의 일부**로 쓰므로(같은 확장자라도 16 과 96 은 다른 요청이다)
      뺄 수 없고, 동시에 `ViewMode` 를 알아서도 안 된다 — 그것을 알면 `IThumbnailSource` 위의
      정책이 아니라 두 번째 ViewModel 이 된다. 그래서 **스케줄러 시그니처는 그대로 두고**
      `PaneViewModel.SetVisibleRange(visible)` 가 `IconSize(ViewMode)` 를 끼워 넘긴다.
      배선 테스트는 `tests/FlexDir.App.Tests/ViewModels/PaneThumbnailsTests.cs` 다.
- [x] **`ViewMode` → 아이콘 크기 = `PaneViewModel` 의 private 매핑.**
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

- [ ] **창이 뜨는가** — 실행 → 창 표시. 두 번째 실행(`FlexDir.Host.exe C:\Windows`)이
      창을 앞으로 가져오고 그 폴더를 여는가. 창을 닫은 뒤 다시 실행하면 창이 다시 오는가
      (상주 프로세스 경로의 핵심).
- [ ] **키보드 배선이 실제로 도는가** — 방향키·Home/End/PageUp/Down·Shift/Ctrl 조합이
      `ListBox` 의 내장 키 처리에 먹히지 않고 우리 KeyBinding 으로 오는가. **내장 처리가
      이기면** `ListInput` 에 키 훅을 옮겨야 한다 (설계상 예상 지점).
- [ ] **클릭·더블클릭** — 선택 3종(평·Ctrl·Shift)·빈 곳 클릭 해제·더블클릭 열기·
      비활성 페인 클릭 시 활성 전환.
- [ ] **주소줄** — 경로 입력 + Enter 로 이동, 오타 시 상태표시줄 사유. 목록 클릭 후
      방향키가 주소를 편집하지 않는가 (`ListInput` 이 포커스를 목록으로 옮긴다).
- [ ] **활성 페인 표현** — 주소줄 accent + 하단 2px, 비활성 선택색 강등 (§6). DESIGN §10
      의 #5(비활성 크롬 강등이 과한가)도 여기서 판단.
- [ ] **가상화 실측** — `C:\Windows\WinSxS` 같은 대용량 폴더에서 스크롤이 매끄러운가.
- [ ] **아직 없는 것 (다음 세션)** — 아이콘·썸네일 (`SetVisibleRange` behavior, B-3) ·
      type-ahead 배선 (TextInput behavior) · Ctrl+L 주소줄 포커스 · 이름변경 편집기 (B-4) ·
      드래그앤드롭 (B-4) · 스플리터 비율 저장 배선 · WindowShown/FirstItem 계측 ·
      완전 종료 메뉴 · 커스텀 타이틀바(지금은 OS 기본 크롬 — DESIGN §1 의 32px 와 다르다).

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
- [x] **계측 = `Diagnostics/PerformanceLog.cs` → `%LOCALAPPDATA%\flex-dir\perf.log`.**
      지점 셋과 예산(`docs/PRD.md` §5)이 코드 안에 있고 줄마다 예산을 함께 적는다 —
      나중에 보는 사람이 문서를 찾지 않아도 판정이 서야 하고, 수치가 전부 잠정이라 예산이
      바뀌면 옛 줄과 갈리는 것도 보여야 한다. **지금 기록되는 것은 `ColdStart` 하나다.**
      `WindowShown`·`FirstItem` 은 창이 있어야 잴 수 있어 phase B 에서 붙인다.
      별도 벤치마크 CLI 는 만들지 않았다(`docs/ARCHITECTURE.md` §7).
- [x] **`IUsageLog` — 포트(`Core/Usage`) · 구현(`Shell/Usage/FileUsageLog.cs`) · 배선을 한 번에.**
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
- [ ] **창을 닫아도 프로세스가 사는가** — **판정 불가. 아직 창이 없다.**
      확인된 것은 "창이 하나도 없는 상태로 프로세스가 계속 산다" 와
      `ShutdownMode = OnExplicitShutdown` 이 걸려 있다는 것까지다. 창을 닫는 조작 자체가
      phase B 에 생기므로 그때 다시 본다.
- [ ] **상주 중 창 표시 (≤100ms)** — 창이 없어 잴 수 없다. phase B.
      계측 지점(`MeasurementPoint.WindowShown`)과 예산은 이미 코드에 있다.
- [ ] **폴더 전환 후 첫 항목 (≤150ms)** — 앱 안에서는 아직 잴 수 없다. phase B.
      다만 열거 자체는 프로브로 쟀다(§A: `WinSxS` 24,115개에서 첫 항목 14.8ms).
- [ ] **완전 종료 경로** — `Application.Shutdown()` 을 부르는 조작이 아직 없다.
      `AppComposition.DisposeAsync` 가 그 뒤에 도는 것은 코드로만 확인했고, phase C 의
      프로세스는 `Stop-Process` 로 끝냈다. 메뉴가 생기는 phase B 에서 실제 경로를 밟는다.

## 순서

```
phases/ 자율 실행 (Core + ViewModel)          ✅
   → A. Shell interop  (포트 8/8)             ✅  잔여: IContextMenuProvider
   → C. Host 뼈대       (진입점 + DI + single instance + IUsageLog + 계측)  ✅
   → B. View            (DESIGN §9 를 먼저 채운 뒤)   ← 다음
   → 매일 쓰기 → 도그푸딩 게이트 ON
```

B 를 마지막에 두는 이유: 화면이 붙기 전에 Shell 구현체가 실물 폴더에서 동작하는지
확인할 수 있고, 그 단계의 버그를 UI 버그와 섞지 않을 수 있다.

§B 의 **`ThumbnailRequestScheduler` 배선**은 **ViewModel 쪽이 끝났다**(위 §B) —
`PaneViewModel` 이 소유하고, `LoadAsync` 가 `Reset()` 을, `DisposeAsync` 가 함께 정리하며,
`public void SetVisibleRange(IReadOnlyList<FileItemViewModel>)` 가 열려 있다.
**B 에 남은 것은 그 메서드를 미는 attached behavior 하나다.** 코드비하인드 금지와 부딪히는
항목이라 View 를 짜기 시작한 뒤에 정하면 이미 `*.xaml.cs` 에 스크롤 핸들러가 들어가 있게 된다.

**아직 열려 있는 것**: `IContextMenuProvider` 포트 정의 — A 잔여, 창 핸들이 필요해 B 와 함께 본다.
`DESIGN.md` §10 의 #4·#5 — 실물을 보고 판단한다. UIA 접근성 — v2 로 미뤘다 (ADR-016).
