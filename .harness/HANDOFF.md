# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 작성 시점: 2026-08-05. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) (수동 phase 계획과
> 사람 확인 항목).

## 한 줄

자율 실행(Core + ViewModel) 3 phase 와 리뷰가 끝났고, 수동 phase A 의 **포트 8개가 전부
구현됐다**. **다음은 phase C — Host 뼈대**(진입점 + DI + single instance + `IUsageLog`)다.
A 에 남은 것은 `IContextMenuProvider`(Core 에 포트 정의부터 없다)와 썸네일 스케줄러
배선뿐이고 둘 다 아래 §열린 결정 에 있다.

## 현재 상태

```
브랜치   main  ·  origin/main 보다 앞서 있다 — 푸시하지 않았다
테스트   759 통과   Core 338 · App 240 · Shell 181
게이트   fast (build -warnaserror · test --blame-hang · check-structure) ✅
         full (Release build -warnaserror) ✅
phases/  0-core-model · 1-core-pipeline · 2-viewmodel 모두 completed
```

푸시는 `CLAUDE.md` §7 대로 별도 지시가 있을 때만 한다. 원격은 `origin`
(`git@github.com:solitasroh/flex-dir.git`).

### 검증 명령

```
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
```

## 포트 현황 (`FlexDir.Shell`)

| 포트 | 구현 | 비고 |
|---|---|---|
| `IViewStateStore` | `ViewState/JsonViewStateStore.cs` | 계약 12 + 파일 고유 10 |
| `IFolderWatcher` | `Watching/FileSystemFolderWatcher.cs` | 계약 4 + 실물 5 |
| `IFolderSource` | `Enumeration/FileSystemFolderSource.cs` | 계약 6 + 실물 10 |
| `ITypeNameProvider` | `Presentation/ShellTypeNameProvider.cs` | 13. 첫 손 P/Invoke |
| `IThumbnailSource` | `Presentation/ShellThumbnailSource.cs` | 23. 첫 COM(`[ComImport]`) + GDI 변환층 |
| `IItemActivator` | `Activation/ShellItemActivator.cs` | 16. `ShellExecuteEx` |
| `IFileOperations` | `Operations/ShellFileOperations.cs` | 48. `IFileOperation` + progress sink |
| `IClipboardBridge` | `Operations/ShellClipboardBridge.cs` | 24. `CF_HDROP` + `Preferred DropEffect` |

**8/8.** 공용 배관: `Interop/StaWorkQueue.cs` (아래 §반드시 알아야 하는 규칙 1).
확인용 프로브: `.harness/probe/` (ADR-015). 커맨드 `typeicons` · `thumbnail <경로>` ·
`leak [폴더]` · `activate <경로>` · `recycle` · `clipboard copy|cut|paste`.
**뒤의 셋은 프로그램을 띄우고 휴지통에 항목을 남기고 클립보드를 덮어쓴다** — 자동 테스트가
하지 않는 일이라 거기 있다. 실물 결과는 `manual-plan.md` 사람 확인 항목에 적었다.

**아직 `FlexDir.Core` 에 정의조차 없는 포트 2개** — 자율 phase 가 만들지 않았다.

- `IContextMenuProvider` — 의도적 제외. 창 핸들과 네이티브 메뉴 메시지 펌핑이 필요해
  ViewModel 테스트로 채점할 수 없다(`docs/SHELL_NOTES.md` §컨텍스트 메뉴 의
  `IContextMenu2/3` · `HandleMenuMsg`). 포트 정의부터 수동이다.
- `IUsageLog` — `docs/ARCHITECTURE.md` §2 의 포트 목록에는 있는데 step 설계에서 빠졌다.
  ADR-007 도그푸딩 게이트의 **입력**이다. **phase C(Host)에서 포트·구현·배선을 한 번에
  하기로 정했다** — 기록할 "사용 시간" 이 창 표시 시간인지 프로세스 수명인지가 ADR-003
  상주 프로세스 구조를 짜면서 갈리므로, 지금 인터페이스만 만들면 소비자 없는 추측성
  정의가 된다 (`manual-plan.md` §C).

## 다음 작업 — phase C (Host 뼈대)

`manual-plan.md` §C 가 미결 목록이다. **화면 없이 실행되는 것까지**가 이 phase 의 범위다.

- WPF 진입점 형태 — 조립 코드는 `*.xaml.cs` 가 아닌 별도 클래스에 (CLAUDE.md §2).
- DI 컨테이너 또는 수동 조립. `FlexDir.Host` 만 `Core`·`Shell`·`App` 셋을 다 안다.
- single instance 상주 — 창을 닫아도 프로세스 유지 (ADR-003).
- **`IUsageLog` 는 포트 정의부터 여기서 한다** (확정). "사용 시간" 이 창 표시 시간인지
  프로세스 수명인지가 상주 구조를 짜면서 갈리므로 지금 인터페이스만 만들면 소비자 없는
  추측성 정의가 된다.

조립할 구현체는 전부 있다 — `FileSystemFolderSource` · `FileSystemFolderWatcher` ·
`JsonViewStateStore` · `ShellTypeNameProvider` · `ShellThumbnailSource` ·
`ShellItemActivator` · `ShellFileOperations` · `ShellClipboardBridge`.
**넷은 `IDisposable` 이고 안에 STA 스레드를 든다** (`StaWorkQueue`) — 상주 프로세스의
종료 지점에서 누가 `Dispose` 하는지가 Host 의 숙제다.

## 직전에 끝낸 것 — 포트 셋 (`IItemActivator` · `IFileOperations` · `IClipboardBridge`)

세 구현체 모두 **실행 지점을 `internal` 생성자로 바꿔 끼운다.** 실물은 프로그램을 띄우고
파일을 지우고 사용자의 클립보드를 덮어쓰므로 자동 테스트가 밟아서는 안 되는 자리다.

| 구현 | 자동으로 재는 것 | 프로브로만 되는 것 |
|---|---|---|
| `ShellItemActivator` | shell 이 낸 Win32 코드 → 오류 분류 | 프로그램이 뜨는가 |
| `ShellFileOperations` | 무엇을 어떤 **플래그**로 넘기는가 · HRESULT → 오류 | 휴지통에 들어가는가 |
| `ShellClipboardBridge` | `DROPFILES` **바이트 배치** | 탐색기가 받아들이는가 |

**이 셋이 고정한 사실**

- **`ShellExecuteEx` 의 오류 대화상자를 끄지 않는다.** `SEE_MASK_FLAG_NO_UI` 를 주면
  연결 프로그램이 없을 때 조용히 1155 로 돌아와 **더블클릭이 아무 일도 안 한 것처럼**
  보인다. 1155(`ERROR_NO_ASSOCIATION`)와 1223(`ERROR_CANCELLED`)은 실패가 아니다.
- **`IFileOperation` 은 실패도 자기 대화상자로 알린다.** 그래서 자동 테스트에서 실패
  경로를 실물로 밟으면 **답을 기다리며 블로킹**한다(`--blame-hang` 에 걸린다). 다만
  **항목을 여는 단계**(`SHCreateItemFromParsingName`)는 UI 없이 HRESULT 만 내므로
  없는 원본은 실물로 잡을 수 있다 — 성공 경로도 `%TEMP%` 안에서는 실물로 잰다.
- **폴더 생성의 이름은 shell 만 안다.** 겹치면 `새 폴더 (2)` 가 되므로
  `IFileOperationProgressSink` 를 구현해 `PostNewItem` 에서 되받는다. **shell 이 우리를
  부르는 인터페이스라 16개 메서드를 전부 구현해야 한다** — 자리만 채운 선언으로는 CCW 가
  만들어지지 않는다(호출하는 쪽 인터페이스와 다르다).
- **`SetClipboardData` 는 소유 창 없이도 통한다.** `OpenClipboard(NULL)` 로 열고
  `HGLOBAL`(`GMEM_MOVEABLE`)을 넘기면 시스템이 가져가므로 `OleFlushClipboard` 가 필요 없다
  (SHELL_NOTES §클립보드 함정 2 는 OLE 경로 이야기다). 실패하면 그 `HGLOBAL` 은 우리 것이다.
- **`Preferred DropEffect` 는 등록 포맷이라 번호가 고정이 아니다.** `CF_HDROP`(15)와 달리
  `RegisterClipboardFormatW` 로 매번 받아야 한다.
- **`SetOwnerWindow` 를 부르지 않았다.** Shell 계층은 창을 모른다 — shell 대화상자에 소유
  창이 없다. phase B 에서 창이 생긴 뒤 다시 본다 (`manual-plan.md`).

## 그 앞 — `IThumbnailSource` 가 phase B 에 남긴 주의

- **결과는 premultiplied BGRA 다.** View 는 `PixelFormats.Bgra32` 가 아니라 **`Pbgra32`** 로
  `WriteableBitmap` 을 만들어야 한다. 틀리면 반투명 가장자리가 어둡게 번진다.
- 96 요청이 `SHIL_JUMBO` 의 **256×256(256KB)** 을 낸다. 축소는 View 가 하고 확장자 캐시는
  비우지 않으므로 상주 프로세스에서 쌓인다 (`manual-plan.md` 사람 확인 항목).
- COM 선언은 **`[ComImport]` 전통 방식**. `[GeneratedComInterface]` 는 어셈블리 전체에
  `DisableRuntimeMarshalling` 을 요구해 `ShellFileInfo` 같은 런타임 마샬링까지 묶는다.
- **부르지 않는 vtable 슬롯도 순서대로 선언해야 한다.** COM 호출은 이름이 아니라 순서로 간다.
- `Marshal.GetObjectForIUnknown` 은 자기 참조를 따로 잡는다. **원시 포인터의
  `Marshal.Release` 와 RCW 의 `ReleaseComObject` 를 둘 다** 해야 한다
  (`ShellFileOperations.ComRef<T>` 가 그 둘을 묶는다).

## 반드시 알아야 하는 규칙 (이 세션에서 값을 치르고 배운 것)

1. **shell 호출은 STA 에서만.** `docs/SHELL_NOTES.md` §COM 아파트먼트 — `SHGetFileInfo` ·
   `IFileOperation` · `IContextMenu` 는 STA 를 요구하고 `Task.Run`·스레드풀은 MTA 다.
   `Interop/StaWorkQueue.cs` 를 쓰라. `ShellTypeNameProvider` 를 처음 `Task.Run` 으로 썼고
   테스트 60개가 다 초록이었지만 규칙 위반이었다 — 지금은 아파트먼트를 테스트가 고정한다
   (`Lookup_RunsOnAnStaThread`).
2. **계약 기반 클래스가 경로를 고정하고 있으면 실물이 만족할 수 없다.**
   `FolderWatcherContract`·`FolderSourceContract` 가 `C:\Temp\Docs` 를 박아둬서
   `WatchedFolder`·`SourceFolder` 추상 멤버를 추가했다. fake 만 상속하는 동안에는 드러나지
   않는다. 남은 포트에 계약을 새로 쓸 때 같은 실수를 하지 마라.
3. **`HResult` 에서 Win32 코드를 꺼낼 때 마스크를 `int` 로 못박아라.**
   `error.HResult & 0xFFFF0000` 은 `0xFFFF0000` 이 uint 리터럴이라 양쪽이 long 으로 승격되고
   음수 HResult 가 부호 확장돼 **비교가 영원히 거짓**이 된다. 분류는 예외 타입 폴백이
   맞춰줘서 테스트가 통과했고, 실물을 돌려보고서야 드러났다
   (`FileSystemFolderSource.Translate` 의 `FacilityMask`).
4. **게이트의 테스트에 `--blame-hang` 이 붙어 있다.** hang 을 실패로 바꾼다. 뺐다가
   `dotnet test` 가 무한 대기하면 실행기까지 교착된다 (실제로 그랬다).
5. **shell 이 대화상자를 띄우는 경로는 자동 테스트에서 밟을 수 없다.** 그 호출은 사용자의
   답을 기다리며 블로킹하므로 게이트의 `--blame-hang` 에 걸린다 — 실패가 아니라 **매달림**이
   되어 진단이 어렵다. 실행 지점을 `internal` 생성자로 바꿔 끼우고 실물은 프로브로 본다.
   부작용도 같은 기준이다: 프로세스를 띄우거나 휴지통에 넣거나 사용자의 클립보드를 덮어쓰는
   것은 게이트가 돌 때마다 일어나서는 안 된다.
6. **`ARCHITECTURE.md` §7 은 별도 CLI 를 금지한다.** 금지 대상은 **재는** 벤치마크 CLI 이고,
   포트 구현체를 실물에 물려 보는 확인용 프로브는 다르다 — ADR-015 가 그 선을 긋고
   `.harness/probe/` 를 sln 밖에 둔다.

## 열린 결정 · 미완

2026-08-05 세션에서 다섯 건을 닫았다. 아래는 남은 것이다.

- [ ] **`docs/DESIGN.md` §9** — 키보드 맵, 상호작용 상태(이름변경 인라인 편집 · 스플리터
      드래그 · 페인 간 드래그앤드롭). **phase B(View)의 선행조건**이다. ViewModel 커맨드는
      이미 다 있고 남은 것은 XAML `InputBindings` 의 제스처 매핑이다.
      이번 세션에서는 B 착수 전까지 미루기로 했다.
- [ ] **클라우드 자리표시자 확인 불가** — 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다(`%OneDrive%` 가 비어 있음). SHELL_NOTES
      §열거 함정 3 의 핵심이고 틀리면 스크롤만으로 수 GB 를 내려받는다. 동기 중인 OneDrive
      내용이 있는 기계에서 확인해야 한다.
- [ ] **`IContextMenuProvider` 포트 정의** — A 잔여. 창 핸들과 메뉴 메시지 펌핑이 필요해
      범위 밖으로 두었다.
- [ ] **`ThumbnailRequestScheduler` 배선이 아직 안 됐다** — A 잔여. `src/` 안에 이 클래스를
      **만드는 곳이 없다.** 배선 방식은 `manual-plan.md` §B 에 확정돼 있다(`PaneViewModel` 이
      직접 소유 · `LoadAsync` 에서 `Reset` · behavior 가 `SetVisibleRange`). 그대로 두면
      **썸네일이 한 장도 나오지 않는다.** 확정문은 `SetVisibleRange` 가 크기를 인자로 받지
      않는다고 적었는데 현재 시그니처는 `SetVisibleRange(visible, requestedSize)` 다 —
      배선하면서 어느 쪽이 맞는지 정해야 한다.
- [ ] **파일 조작·활성화의 대화상자 경로** — 자동 실행으로 확인할 수 없다(블로킹).
      사람 확인 항목으로 남겼다 (`manual-plan.md`).
- [ ] **푸시** — `main` 이 `origin/main` 보다 앞서 있다. 이번 세션에서도 푸시하지 않았다.

### 닫힌 것 (2026-08-05)

| 항목 | 결론 |
|---|---|
| 썸네일 스케줄러 배선 | `PaneViewModel` 이 직접 소유. `manual-plan.md` §B 에 5개 항목 확정 |
| 확인용 프로브 | `.harness/probe/` 에 sln 밖으로 커밋 — **ADR-015** |
| `FileSystemEnumerator<T>` | **ADR-014** 로 올림. SHELL_NOTES §열거 에 포인터 |
| `IUsageLog` | phase C(Host)에서 포트·구현·배선을 한 번에. `manual-plan.md` §C |
| `DESIGN.md` §9 | 이번 세션 범위 밖 — B 착수 전까지 |
| 휴지통 · 클립보드 · 활성화 실물 확인 | 셋 다 통했다. 근거는 `manual-plan.md` 사람 확인 항목 |
| 폴더 생성의 이름 | `IFileOperationProgressSink` 로 shell 이 만든 이름을 되받는다 |
| 클립보드 포맷 | OLE 가 아니라 Win32 + `CF_HDROP`. 이유는 위 §직전에 끝낸 것 |

## 2026-08-05 세션 커밋 (포트 셋 · 해시는 `git log`)

```
feat(shell): IItemActivator 를 ShellExecuteEx 로 구현한다
feat(shell): IFileOperations 를 IFileOperation 으로 구현한다
feat(shell): IClipboardBridge 를 CF_HDROP 로 구현한다
chore(harness): 남은 포트 셋의 실물 확인 커맨드를 프로브에 붙인다
docs: 포트 8/8 과 실물 확인 결과를 인수인계에 옮긴다
```

## 2026-08-04 세션 커밋 (리뷰 이후 11개)

```
5ed002e fix(shell): shell 호출을 STA 워커에서 돌린다
a267348 feat(shell): ITypeNameProvider 를 SHGetFileInfoW 로 구현한다
33588f7 docs: 실물 확인 결과를 사람 확인 항목에 기록한다
b22192a fix(shell): HResult 에서 Win32 코드를 꺼내지 못하던 마스크를 고친다
9dd368c feat(shell): IFolderSource 를 파일시스템 열거로 구현한다
fb3c552 test(core): 열거 계약이 열거할 폴더를 구현체가 정하게 한다
1f1f2ad feat(shell): IFolderWatcher 를 FileSystemWatcher 로 구현한다
2e0f301 test(core): 감시 계약이 감시할 폴더를 구현체가 정하게 한다
eb66bfc feat(shell): IViewStateStore 를 파일 기반으로 구현한다
835e7e6 chore(shell): 계약 상속용 테스트 프로젝트를 추가한다
4495e99 docs: ADR-006 의 강제 수단을 사실에 맞게 정정한다
```

## 순서 (변경 없음)

```
A. Shell interop   포트 8/8 ✅  (잔여: IContextMenuProvider · 스케줄러 배선)
C. Host 뼈대        ← 다음. 진입점 + DI, 화면 없이 실행되는지
B. View            DESIGN §9 를 먼저 채운 뒤
→ 매일 쓰기 → 도그푸딩 게이트 ON (ADR-007, v1 완성 후)
```
