# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 작성 시점: 2026-08-04. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) (수동 phase 계획과
> 사람 확인 항목).

## 한 줄

자율 실행(Core + ViewModel) 3 phase 와 리뷰가 끝났고, 지금은 **수동 phase A — `FlexDir.Shell`
포트 구현** 중이다. 포트 8개 중 5개 완료. **다음은 `IItemActivator`**(`ShellExecuteEx`) 다.
그 뒤 `IFileOperations` · `IClipboardBridge` 로 A 를 닫는다.

## 현재 상태

```
브랜치   main  ·  origin/main 보다 앞서 있다 — 푸시하지 않았다
테스트   671 통과   Core 338 · App 240 · Shell 93
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
| **`IItemActivator`** | **미착수 — 다음** | `ShellExecuteEx` |
| `IFileOperations` | 미착수 | `IFileOperation` + **`FOFX_RECYCLEONDELETE`** |
| `IClipboardBridge` | 미착수 | 탐색기 호환 포맷 |

공용 배관: `Interop/StaWorkQueue.cs` (아래 §반드시 알아야 하는 규칙 1).
확인용 프로브: `.harness/probe/` (ADR-015). 커맨드 `typeicons` · `thumbnail <경로>` · `leak [폴더]`.

**아직 `FlexDir.Core` 에 정의조차 없는 포트 2개** — 자율 phase 가 만들지 않았다.

- `IContextMenuProvider` — 의도적 제외. 창 핸들과 네이티브 메뉴 메시지 펌핑이 필요해
  ViewModel 테스트로 채점할 수 없다(`docs/SHELL_NOTES.md` §컨텍스트 메뉴 의
  `IContextMenu2/3` · `HandleMenuMsg`). 포트 정의부터 수동이다.
- `IUsageLog` — `docs/ARCHITECTURE.md` §2 의 포트 목록에는 있는데 step 설계에서 빠졌다.
  ADR-007 도그푸딩 게이트의 **입력**이다. **phase C(Host)에서 포트·구현·배선을 한 번에
  하기로 정했다** — 기록할 "사용 시간" 이 창 표시 시간인지 프로세스 수명인지가 ADR-003
  상주 프로세스 구조를 짜면서 갈리므로, 지금 인터페이스만 만들면 소비자 없는 추측성
  정의가 된다 (`manual-plan.md` §C).

## 다음 작업 — `IItemActivator`

`src/FlexDir.Core/Activation/` 아래의 포트를 먼저 읽어라. `ShellExecuteEx` 하나로 끝나므로
앞의 다섯보다 작다. 그래도 STA 규칙과 계약 테스트 상속은 같다.

**미리 정해 둘 것**

- `ShellExecuteEx` 는 STA 를 요구한다 — `StaWorkQueue` 를 쓴다 (아래 §규칙 1).
- 실패를 어떻게 낼지: "연결된 프로그램 없음"(`ERROR_NO_ASSOCIATION` 1155)과 사용자가
  '연결 프로그램' 대화상자를 취소한 경우(`ERROR_CANCELLED` 1223)는 오류가 아니다.
  `FileSystemFolderSource.Translate` 가 Win32 코드를 꺼내는 방식을 그대로 쓴다
  (아래 §규칙 3 의 마스크 함정 포함).
- **자동 테스트가 프로세스를 실제로 띄우면 안 된다.** 앞의 구현체들처럼 실행 자체를
  바꿔 끼울 수 있게 열어두고(`internal` 생성자), 실물 확인은 `.harness/probe/` 로 한다.

## 직전에 끝낸 것 — `IThumbnailSource`

`src/FlexDir.Shell/Presentation/ShellThumbnailSource.cs` · 테스트 23개.
**두 메서드가 서로 다른 API 위에 선다.**

| 메서드 | 경로 |
|---|---|
| `GetTypeIconAsync` | `SHGetFileInfoW`+`SHGFI_SYSICONINDEX` → `SHGetImageList` → `IImageList::GetIcon` → `HICON` |
| `GetThumbnailAsync` | `SHCreateItemFromParsingName` → `IShellItemImageFactory::GetImage` → `HBITMAP` |

**`SHGFI_ICON` 을 쓰지 않은 이유**: 그것은 16·32 밖에 내지 못하는데 큰 아이콘 뷰는 96 을
쓴다 (`docs/DESIGN.md` §2). 시스템 이미지 리스트를 크기별로 골라야 한다 —
`<=16` SMALL · `<=32` LARGE · `<=48` EXTRALARGE · 그 위는 JUMBO(256).
프로브 실측으로 16→16 · 32→32 · 48→48 · 96→**256** 을 확인했다.

**남은 것 하나**: 96 요청이 256×256(256KB)을 낸다. 축소는 View 가 하고 스케줄러의 확장자
캐시는 비우지 않으므로 상주 프로세스에서 쌓인다. `manual-plan.md` 사람 확인 항목에 있다.

**이 구현이 고정한 사실** (다음 포트에서도 그대로다)

- COM 선언은 **`[ComImport]` 전통 방식**. `[GeneratedComInterface]` 는 어셈블리 전체에
  `DisableRuntimeMarshalling` 을 요구해 `ShellFileInfo` 같은 런타임 마샬링까지 묶는다.
- **부르지 않는 vtable 슬롯도 선언해야 한다.** COM 호출은 이름이 아니라 순서로 간다 —
  `IImageList.GetIcon` 앞의 일곱 메서드가 인자 없이 선언돼 있는 이유다.
- `Marshal.GetObjectForIUnknown` 은 자기 참조를 따로 잡는다. **원시 포인터의
  `Marshal.Release` 와 RCW 의 `ReleaseComObject` 를 둘 다** 해야 한다.
- `DrawIconEx` 는 레거시 아이콘을 `BitBlt` 로 그리고 **알파 바이트를 건드리지 않는다.**
  DIB 를 0 으로 시작했으므로 그대로 두면 완전 투명이 되어 아이콘이 보이지 않는다 —
  `Opaquify` 가 "알파가 전부 0 이면 불투명" 으로 되돌린다.
- **결과는 premultiplied BGRA 다.** View 는 `PixelFormats.Bgra32` 가 아니라 **`Pbgra32`** 로
  `WriteableBitmap` 을 만들어야 한다. 틀리면 반투명 가장자리가 어둡게 번진다. phase B 주의.
- `GdiFlush()` 를 부르지 않고 DIB 를 읽으면 빈 픽셀을 본다. GDI 는 명령을 모아 둔다.

**실물 확인 결과**는 `manual-plan.md` §A 에 적었다 — GDI 핸들 40 고정(누수 없음),
jpg 썸네일 96×54/96×60 · 34~45ms.

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
5. **`ARCHITECTURE.md` §7 은 별도 CLI 를 금지한다.** 금지 대상은 **재는** 벤치마크 CLI 이고,
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
- [ ] **`IContextMenuProvider` 포트 정의** — A 잔여. 이번 세션에서 다루지 않기로 했다.
- [ ] **푸시** — `main` 이 `origin/main` 보다 앞서 있다. 이번 세션에서도 푸시하지 않았다.

### 닫힌 것 (2026-08-05)

| 항목 | 결론 |
|---|---|
| 썸네일 스케줄러 배선 | `PaneViewModel` 이 직접 소유. `manual-plan.md` §B 에 5개 항목 확정 |
| 확인용 프로브 | `.harness/probe/` 에 sln 밖으로 커밋 — **ADR-015** |
| `FileSystemEnumerator<T>` | **ADR-014** 로 올림. SHELL_NOTES §열거 에 포인터 |
| `IUsageLog` | phase C(Host)에서 포트·구현·배선을 한 번에. `manual-plan.md` §C |
| `DESIGN.md` §9 | 이번 세션 범위 밖 — B 착수 전까지 |

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
A. Shell interop   ← 지금 여기 (5/8)
C. Host 뼈대        진입점 + DI, 화면 없이 실행되는지
B. View            DESIGN §9 를 먼저 채운 뒤
→ 매일 쓰기 → 도그푸딩 게이트 ON (ADR-007, v1 완성 후)
```
