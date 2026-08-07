# SHELL_NOTES — Windows Shell 실전 노트

[fast-explorer](https://github.com/solitasroh/fast-explorer) 에서 3주간 실제로 부딪힌
것들. 결론과 함정만 남긴다. API 사용법은 MSDN 에 있다.

**원본** 표기는 fast-explorer 저장소 기준 경로다. 막히면 거기를 열어본다.
C++ 코드를 그대로 옮기지 마라 — 스택이 다르다. **판단만** 가져온다.

C# 에서는 대부분 [CsWin32] 또는 [Vanara.Windows.Shell] 로 같은 API 에 닿는다.

> **이 문서는 guardrail 이 아니다.** 매 step 에 자동 주입되지 않는다.
> 경로 정규화 · 열거 · 아이콘 · 컨텍스트메뉴 · 클립보드 · 파일조작 · 감시 · 네트워크를
> 건드리는 step 지시서는 **"먼저 읽을 것" 에 이 파일 경로를 반드시 명시해야 한다.**
> 빠뜨리면 구현 세션은 아래 함정을 하나도 모른 채 시작한다.

---

## COM 아파트먼트

**사실**: `IFileOperation` · `SHGetFileInfo` · `IContextMenu` 는 **STA** 를 요구한다.
워커 스레드에서 쓰려면 그 스레드를 STA 로 초기화해야 한다.

**함정 1**: 호스트가 스레드를 MTA 로 먼저 초기화했으면 `CoInitializeEx` 가
`RPC_E_CHANGED_MODE` 를 낸다. 이때 큐를 막지 말고 계속 비우되 각 명령을 실패로
처리해야 한다 — 안 그러면 큐가 영원히 멈춘다.

**함정 2 — 아파트먼트는 동시성이 아니다.** STA 를 잡았다고 끝난 것이 아니다. 워커가
여럿이면 **같은 API 를 동시에 부르는 상황이 그대로 남는다**, 그리고 shell API 중에는
그것을 견디지 못하는 것이 있다 (§아이콘 함정 4). 새 shell API 를 붙일 때
"STA 인가" 와 "동시에 불려도 되는가" 를 **따로** 묻는다.

**C#**: 전용 워커 스레드에 `SetApartmentState(ApartmentState.STA)`.
`Task.Run`/스레드풀은 MTA 라 그대로 쓰면 안 된다.

**원본**: `src/explorer/shell-worker.cpp` `workerMain`.
함정 2 는 flex-dir 에서 나왔다 — `src/FlexDir.Shell/Interop/StaWorkQueue.cs`(아파트먼트)와
`ShellInfoGate.cs`(직렬화)가 서로 다른 문제를 푼다.

---

## 경로 정규화

**사실**: 내부 표현은 `\\?\` 확장 접두사로 통일한다.
UNC 는 `\\server\share` → `\\?\UNC\server\share`.

**함정 1**: UNC 판정은 **구분자 정규화보다 먼저** 해야 한다. `//server/share` 를
먼저 `\` 로 바꾸면 드라이브 문자가 없어서 상대 경로로 오분류된다.

**함정 2**: 경로 본문의 `:` 는 NTFS **대체 데이터 스트림** 구분자다. 거부한다.
단 드라이브 문자 뒤의 `C:` 는 합법이므로 스캔에서 제외한다.

**함정 3**: `\\?\C:\` 는 UNC 가 **아니다** — 확장 접두사가 붙은 로컬 경로다.
`\\` 로 시작한다고 UNC 로 판정하면 틀린다.

**함정 4**: `\\server` (서버만, 공유 없음)도 유효한 탐색 대상이다. 탐색기는 이걸
공유 목록 폴더로 취급한다. 거부하면 안 된다.

**원본**: `src/core/path-utils.cpp` `toInternal` · `isUncPath`

---

## 위치 표현

**사실**: 경로를 문자열로 다루면 shell 네임스페이스를 표현할 수 없다.
전작은 결국 3종 태그 타입으로 수렴했다 —
`FileSystemPath` / `ShellKnownFolder` / `ShellNamespace`.

**함정**: PIDL 을 파일시스템 경로로 되돌릴 때 순서가 중요하다.

1. `SHGetKnownFolderIDList` + `ILIsEqual` 로 알려진 폴더(ThisPC · Network · NetHood) 먼저 판정
2. `SHGetPathFromIDListW` — 성공하면 실제 경로
3. 실패 시 `SHGetNameFromIDList(SIGDN_DESKTOPABSOLUTEPARSING)`

2번을 먼저 하면 `shell:ThisPC` 같은 위치가 빈 문자열이 되거나 엉뚱한 경로가 된다.

**함정**: `SHParseDisplayName` 이 돌려준 PIDL 은 `CoTaskMemFree` 로 해제한다.
`ILFree` 와 혼동하지 마라 (지금은 같은 동작이지만 계약이 다르다).

**원본**: `src/explorer/shell-pidl-location.cpp` · `src/core/location.h`

---

## 열거

**사실**: `FindFirstFileExW` 를 `FindExInfoBasic` + `FindExSearchNameMatch` +
`FIND_FIRST_EX_LARGE_FETCH` 로 연다. 이 조합이 전작 속도의 핵심이었다.
`FindExInfoBasic` 은 8.3 단축 이름 조회를 건너뛴다.

**함정 1**: `FindFirstFileEx` 가 `ERROR_FILE_NOT_FOUND` 를 내는 건 와일드카드가
0개를 찾았을 때다. `.` / `..` 는 보통 보장되므로, 이건 "순회 불가능한 입력"을 뜻한다.

**함정 2**: `dwReserved0` 의 reparse 태그는 `FILE_ATTRIBUTE_REPARSE_POINT` 가
설정됐을 때만 유효하다. 아니면 값이 미정의다.

**함정 3 — 클라우드 자리표시자**: `FILE_ATTRIBUTE_OFFLINE`,
`0x00400000`(RECALL_ON_DATA_ACCESS), `0x00040000`(RECALL_ON_OPEN) 이 붙은 항목은
OneDrive 등의 미다운로드 파일이다. **내용을 건드리면 다운로드가 트리거된다.**
썸네일·미리보기에서 특히 위험하다. 별도 플래그로 표시하고 접근을 피한다.

**함정 4**: 확장자 판정 시 선행 `.` 은 건너뛴다. `.bashrc` 는 확장자가 없는 것으로
취급해야 한다.

**C#**: `FindFirstFileEx` 를 직접 P/Invoke 하는 편이 낫다. `Directory.EnumerateFiles`
는 위 플래그를 못 주고 예외 기반이라 대용량에서 불리하다.

> **이 지시는 따르지 않았다.** `IFolderSource` 는 `System.IO.Enumeration.FileSystemEnumerator<T>`
> 로 구현돼 있다 — `Directory.EnumerateFiles` 가 아니라 그 밑의 저수준 primitive 이고,
> 여기서 P/Invoke 를 지시한 두 근거(8.3 이름 · 항목별 예외)를 모두 피한다.
> 근거와 포기한 것은 **ADR-014** 에 있다.

**원본**: `src/core/win32-fs-backend.cpp`

---

## 아이콘

**사실**: **파일마다 아이콘을 조회하지 마라. 확장자마다 한 번만 조회한다.**
`SHGetFileInfoW` 에 `SHGFI_USEFILEATTRIBUTES` 를 주고 존재하지 않는 센티널
파일명(`"x" + ".txt"`)을 넘기면 파일 없이 연결 아이콘을 얻는다.
이게 대용량 폴더에서 가장 큰 승리였다.

**함정 1**: `SHGetFileInfoW` 는 **동기 블로킹**이다. 네트워크 경로나 클라우드
자리표시자에서 초 단위로 멈춘다. 반드시 워커에서.

**함정 2**: shell 네임스페이스 위치는 경로가 없으므로 `SHParseDisplayName` 후
`SHGFI_PIDL` 플래그로 조회한다.

**함정 3 — HICON 누수**: 워커가 마지막 UI 드레인 이후에 만든 HICON 은 종료 시
버려진다. 워커를 **명시적으로 join 한 뒤** 남은 결과를 드레인해서 `DestroyIcon`
해야 한다. 메시지 루프 종료가 마지막 post 를 삼킨다.

**함정 4 — 동시 호출이 조용히 실패한다**: `SHGetFileInfoW` 를 두 스레드가 같은 순간에
부르면 한쪽이 **예외도 오류 코드도 없이 0 을 낸다** (`SHGFI_SYSICONINDEX` 면 아이콘 인덱스가
`-1`). 그것만 직렬화하면 이번엔 그 뒤의 **시스템 이미지 리스트**(`SHGetImageList` →
`IImageList.GetIcon`)가 던진다. **형식 아이콘 경로 전체를 프로세스 단위로 직렬화해야 한다.**

- STA 워커를 여럿 두면 바로 재현된다 (§COM 아파트먼트 함정 2).
- 대가는 없다시피 하다: `SHGFI_USEFILEATTRIBUTES` 라 저장소에 닿지 않고 조회는 확장자마다
  한 번뿐이다. 오래 걸리는 **썸네일** 조회(`IShellItemImageFactory`)는 직렬화하지 않는다.
- **실패를 캐시하는 호출자와 만나면 영구 버그가 된다.** 실패도 시도로 세어 재요청을 막는
  정책(`PRD.md` §4)과 겹쳐, 경합에 한 번 진 페인의 아이콘이 끝까지 비어 있었다.
  화면만 보면 "아이콘 기능이 없다" 로 보인다.

**폴백 체인**: 실제 경로 조회 → 실패 시 `SHGFI_USEFILEATTRIBUTES` +
디렉터리/일반 속성으로 재시도 → 실패 시 자리표시자.

**원본**: `src/explorer/icon-provider.cpp` · `icon-cache-coordinator.cpp`.
함정 4 는 flex-dir 에서 나왔다 (2026-08-06, 페인 둘이 동시에 폴더 아이콘을 물었다) —
`src/FlexDir.Shell/Interop/ShellInfoGate.cs` 가 막고 있다.

---

## 컨텍스트 메뉴

전작에서 가장 함정이 많았던 영역이다.

**사실**: 항목 메뉴는 `IShellFolder::GetUIObjectOf(IID_IContextMenu)`,
빈 영역(배경) 메뉴는 `CreateViewObject(IID_IContextMenu)`. **다른 API 다.**

**함정 1 — 오너드로 렌더링**: `IContextMenu2/3` 를 QueryInterface 해서
`WM_INITMENUPOPUP` · `WM_DRAWITEM` · `WM_MEASUREITEM` · `WM_MENUCHAR` 를
`HandleMenuMsg`/`HandleMenuMsg2` 로 전달해야 한다. 안 하면 "Open With" · "공유"
같은 오너드로 항목이 빈칸으로 그려진다. `IContextMenu3` 우선(WM_MENUCHAR 유니코드).

**함정 2 — 메시지 후킹 수명**: 후킹은 `InvokeCommand` **동안에도 유지**해야 한다.
shell verb 가 중첩 메뉴를 펌프하면서 같은 메시지를 또 낸다. 메뉴가 닫혔다고
후킹을 떼면 거기서 깨진다.

**함정 3 — 앱 자체 메뉴 항목 ID**: shell verb ID 범위는 `1 ~ 0x7FFF`.
앱이 추가하는 항목은 반드시 **`0x7FFF` 초과** ID 를 써야 충돌하지 않는다.

**함정 4 — `lpVerbW` 크래시**: ID 로 verb 를 호출할 때 `MAKEINTRESOURCE` 를
**ANSI 필드(`lpVerb`)에만** 넣는다. 일부 확장은 `lpVerbW` 가 non-null 이면
`IS_INTRESOURCE` 인데도 문자열로 역참조해서 **크래시한다.**

**함정 5 — 다중 선택 fan-out**: 일부 verb 는 다중 PIDL 묶음을 무시하고 첫 항목만
처리한다. 확인된 것: `install` · `installallusers`(폰트 설치) · `print` · `printto`.
탐색기는 이 verb 들에 대해 **파일마다 단일 PIDL 메뉴를 새로 만들어 이름으로 호출**한다.
`GetCommandString(GCS_VERBA)` 로 verb 이름을 얻어 판별한다. 각 호출은 동기로 —
비동기로 하면 확장의 `Initialize()` 가 서로 레이스한다.

**함정 6 — 빈 leaf 이름**: 빈 문자열을 `ParseDisplayName` 에 넘기면 일부
네임스페이스에서 **폴더 자신**으로 해석된다. 의도한 적이 없는 결과다. 걸러낸다.

**함정 7 — 낡은 선택**: 방금 삭제된 파일이 선택에 남아 `ParseDisplayName` 이
실패할 수 있다. 그 항목만 건너뛰고 메뉴는 살린다. 하나 때문에 전체를 실패시키면
사용자는 살아있는 항목에도 손을 못 댄다.

**함정 8**: 앱 자체 항목이 선택됐을 때는 `PostMessage` 로 미룬다(`Send` 아님).
`TrackPopupMenuEx` 가 완전히 풀리고 메뉴가 파괴된 뒤에 처리돼야 한다.

**함정 9 — C# 고유. 배열 파라미터의 기본 마샬링이 `SafeArray` 다**: `[ComImport]`
인터페이스에서 `nint[] apidl` 을 그냥 선언하면 런타임이 **SAFEARRAY** 로 마샬링한다
(P/Invoke 의 기본은 `LPArray` 라 습관이 어긋난다). shell 은 그것을 PIDL 포인터 배열로
역참조하고 **프로세스가 죽는다** — `GetUIObjectOf` 안에서 `0xC0000005` 다.
`[MarshalAs(UnmanagedType.LPArray)]` 를 반드시 붙인다.

**자동 테스트가 잡을 수 없다**: 선언이 틀려도 컴파일되고, 실행 지점을 바꿔 끼운 테스트는
진짜 vtable 을 지나지 않는다. flex-dir 에서 우클릭 첫 시도가 이렇게 끝났다 (2026-08-06).

**함정 10 — `as` 로 얻은 `IContextMenu2/3` 는 같은 RCW 다**: `menu as IContextMenu3` 는
새 참조가 아니라 **같은 객체를 다른 타입으로** 돌려준다. 거기에 `ReleaseComObject` 를
부르면 원본이 이미 세고 있는 것을 한 번 더 놓아 과다 해제가 되고, 죽는 자리는 한참 뒤다.
수명은 메뉴를 만든 쪽 하나가 쥔다.

### 메뉴 루프를 어디서 도는가 — flex-dir 의 결정

`TrackPopupMenuEx` 는 **창을 소유한 스레드에서만** 돌 수 있고 오너드로 메시지도 그
스레드의 창 프로시저로 온다. 그런데 `QueryContextMenu`·`InvokeCommand` 는 저장소에
닿는다 (확장 열거 · 네트워크 경로에서 초 단위). WPF 창을 쓰면 그 호출이 UI 스레드로 온다.

**STA 워커에서 전부 돌리고 메뉴의 주인은 자체 숨은 창으로 만든다** (사용자 결정
2026-08-06). WPF 핸들은 shell 확장이 띄우는 대화상자의 부모로만 넘긴다. 숨은 창은
WPF 창을 소유자로 삼고, `SetForegroundWindow` + 뒤이은 `PostMessage`(KB135788)로
메뉴 밖 클릭이 메뉴를 닫게 한다.

**실물로 확인됐다** (2026-08-06): 오너드로 항목(7-Zip·TortoiseSVN·공유 등)이 글자와
아이콘까지 제대로 그려지고, 메뉴 밖을 누르면 닫힌다.

**소유 창은 `FlexDir.Host` 가 쥔다.** `FlexDir.Core` 의 포트에는 창 핸들이 없다
(CLAUDE.md §1) — `Host/Startup/OwnerWindow` 가 창의 `SourceInitialized` 에서 HWND 를
잡아 두고 공급자(`Func<nint>`)를 구현체에 물려 준다. **`IFileOperation.SetOwnerWindow`
도 같은 배선을 쓴다** — shell 의 진행률·충돌 대화상자가 그래야 소유 창을 갖는다.

**원본**: `src/explorer/shell-context-menu.cpp` — 이 파일은 통째로 읽을 가치가 있다.
함정 9·10 과 위 결정은 flex-dir 에서 나왔다 (`src/FlexDir.Shell/Operations/ShellContextMenuProvider.cs`).

---

## 클립보드 (탐색기 호환)

**사실**: `IShellFolder::GetUIObjectOf(IID_IDataObject)` 로 만든 데이터 객체를
`OleSetClipboard` 한다.

**함정 1 — 잘라내기**: `CFSTR_PREFERREDDROPEFFECT` 포맷을
`RegisterClipboardFormatW` 로 등록하고 `DROPEFFECT_MOVE` 를 `SetData(..., TRUE)` 로
심는다. 이게 없으면 잘라내기가 복사로 동작한다.
`SetData` 실패 시 `HGLOBAL` 은 **우리가** 해제해야 한다.

**함정 2 — 프로세스 종료**: `OleFlushClipboard()` 를 호출하지 않으면 앱이 종료될 때
클립보드 내용이 사라진다. flex-dir 는 상주 프로세스지만 그래도 호출한다.

**사실 — 붙여넣기는 직접 구현하지 않는다**: 대상 폴더의
`CreateViewObject(IID_IDropTarget)` 를 얻어 `DragEnter → DragOver → Drop` 순서로
위임한다. **진행률 대화상자와 충돌 처리를 shell 이 알아서 한다.**
직접 복사 루프를 짜면 그 UI 를 전부 다시 만들어야 한다.

**함정 3**: 각 단계마다 `DROPEFFECT_NONE` 을 확인하고 거부 시 `DragLeave`.
단 `Drop` 이 성공하면 `DragLeave` 를 부르면 안 된다 — `Drop` 이 이를 포함한다.

**원본**: `src/explorer/clipboard-ops.cpp`

---

## 파일 조작

**사실**: `IFileOperation` 을 쓴다. `SHCreateItemFromParsingName` 으로 `IShellItem`.

**함정 — 삭제가 조용히 영구 삭제가 되는 경우**: `FOF_ALLOWUNDO` 만으로는 부족하다.
드라이브의 휴지통 할당량을 초과하면 Windows 가 **말없이 영구 삭제로 바꾼다.**
`FOFX_RECYCLEONDELETE` 를 같이 줘야 강제된다.

**주의**: 전작은 `FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT` 로 모든 대화상자를
껐다. **flex-dir 는 반대로 간다** — 이름 충돌 대화상자는 shell 것을 그대로 쓴다
(PRD 엣지케이스). 조용히 실패하면 사용자가 무슨 일이 났는지 모른다.

**원본**: `src/explorer/shell-worker.cpp`

---

## 폴더 감시

**사실**: `ReadDirectoryChangesW` + IOCP + `OVERLAPPED`.
핸들은 `FILE_LIST_DIRECTORY` + `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED`,
공유 플래그에 `FILE_SHARE_DELETE` 포함(없으면 감시 중 폴더 삭제가 막힌다).

**함정 1**: 루프마다 `OVERLAPPED` 를 **다시 0으로 초기화**한다. 커널이 쓴
`Internal`/`InternalHigh` 가 다음 호출로 새어 들어간다.

**함정 2 — 종료**: `CancelIoEx` 만으로는 `GetQueuedCompletionStatus` 가 안 풀릴 수
있다. `PostQueuedCompletionStatus` 로 합성 완료를 하나 넣어 깨운다.

**C#**: `FileSystemWatcher` 가 이걸 감싼다. 대신 **`InternalBufferSize` 를 키우고
`Error` 이벤트(버퍼 오버플로)를 반드시 처리**해야 한다. 오버플로가 나면 이벤트가
유실되므로 그때는 전체 새로고침으로 폴백한다.

**원본**: `src/core/fs-watcher.cpp`

---

## 네트워크 (v2)

**v2 착수됨 (2026-08-07).** UNC 경로는 `LocationId` 가 담는다 — 내부 표현은 확장 UNC
형식 `\\?\UNC\server\share`, 표시형은 `\\server\share` 다 (docs/PRD-v2.md §5 N-1).

### 공유의 여유 용량은 `DriveInfo` 로 못 잰다

**사실**: `DriveInfo` 는 드라이브 문자만 받는다. `new DriveInfo(@"\\server\share")` 는
`ArgumentException` 이다. `GetDiskFreeSpaceEx` 를 직접 부른다 — 그쪽은 UNC 를 받는다.

**함정**: 디렉터리 이름을 받으므로 **후행 구분자를 붙여야** 공유 자체를 가리킨다
(`\\server\share\`). 그리고 실패를 예외로 만들면 안 된다 — 서버가 죽었거나 자격증명이
없을 때 여유 용량 하나 때문에 폴더를 못 여는 일이 생긴다.

> 실물에서 이 자리를 밟았다. UNC 를 열면 상태표시줄의 여유 용량만 **조용히 사라졌다** —
> 예외가 계약대로 "모른다"(null)로 접혀서 원인이 화면에 안 나온다.

아래는 전작에서 건진 것으로, **가장 값비싼 삽질이 여기 있었다.**

### NAS 에서 공유 목록이 안 나오는 문제

**사실**: `\\server` (서버만) 를 열려면 공유 목록을 열거해야 하는데,
**`NetShareEnum` 을 쓰면 안 된다.** `WNetOpenEnum` + `WNetEnumResource` 를 쓴다.

**이유**: `NetShareEnum`(netapi32)은 LANMAN 명명 파이프로 RPC-over-SMB 를 직접
때린다. **현대 NAS / SMB2 전용 호스트는 이 경로를 막아두면서도 일반 SMB 파일
작업은 정상 제공한다.** `WNet*` 은 다중 공급자 라우터(mpr.dll)를 거치므로
`net view \\server` 와 같은 경로를 탄다.

> 실제로 사용자 NAS(`10.10.10.23`)에서 정확히 이 격차에 걸렸다.
>
> **2026-08-07 재확인.** 같은 NAS 에서 `net view \\10.10.10.23` 은 공유 목록을 내고
> `Win32_Share`(NetShareEnum 계열)는 실패한다. `WNetOpenEnum`+`WNetEnumResource` 로
> 구현했고 실물에서 **공유 30개**가 나온다 (`Shell/Enumeration/NetworkShareSource.cs`).
> 버퍼는 `ERROR_MORE_DATA` 로 되돌아오면 늘려 다시 묻는다.

**부수 사실**: `$` 로 끝나는 관리 공유는 숨긴다 (탐색기와 동일).
`WNetEnumResource` 는 `ERROR_NO_MORE_ITEMS` 까지 루프한다.

### 오류 코드 매핑

경로 문제와 인증 문제를 구분해서 보여줘야 한다. 뭉뚱그리면 사용자가 손쓸 방법이 없다.

| 코드 | 의미 | 분류 |
|---|---|---|
| 53 `ERROR_BAD_NETPATH` | 네트워크 경로 없음 | 경로 없음 |
| 67 `ERROR_BAD_NET_NAME` | 공유 이름 없음 | 경로 없음 |
| 64 `ERROR_NETNAME_DELETED` | 작업 중 공유가 사라짐 | 경로 없음 |
| 52 `ERROR_DUP_NAME` | 서버 이름 중복 | 경로 없음 |
| 1231 / 1232 | 네트워크·호스트 도달 불가 | 경로 없음 |
| 1326 `ERROR_LOGON_FAILURE` | 자격증명 거부 | **권한** |
| 1311 `ERROR_NO_LOGON_SERVERS` | 로그온 서버 없음 | **권한** |
| 1219 `ERROR_SESSION_CREDENTIAL_CONFLICT` | 같은 서버에 다른 자격증명 세션 | **권한** |

**함정**: 1219 는 흔하고 헷갈린다. 이미 다른 계정으로 그 서버에 연결돼 있다는 뜻이라
경로나 비밀번호를 고쳐도 안 된다. `net use /delete` 가 필요하다는 걸 알려줘야 한다.

### 알려진 미해결

**사실**: shell link 가 PIDL-only target 이거나 Windows 가 파일시스템/UNC target 을
제공하지 않으면 해석이 제한된다. 전작에서도 완전히 풀지 못했다.

**원본**: `src/core/win32-fs-backend.cpp` `openShareEnum` · `mapWin32Error`

---

## 참고

- [CsWin32](https://github.com/microsoft/CsWin32) — Win32 P/Invoke 소스 생성기
- [Vanara.Windows.Shell](https://github.com/dahall/Vanara) — shell COM 래퍼
