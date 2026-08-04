# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 작성 시점: 2026-08-04. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) (수동 phase 계획과
> 사람 확인 항목).

## 한 줄

자율 실행(Core + ViewModel) 3 phase 와 리뷰가 끝났고, 지금은 **수동 phase A — `FlexDir.Shell`
포트 구현** 중이다. 포트 8개 중 4개 완료. **다음은 `IThumbnailSource`** 이며, 그 앞에서
멈춘 이유가 아래 §다음 작업 에 있다.

## 현재 상태

```
브랜치   main (clean)  ·  origin/main 보다 11 커밋 앞서 있다 — 푸시하지 않았다
테스트   648 통과   Core 338 · App 240 · Shell 70
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
| **`IThumbnailSource`** | **미착수 — 다음** | GDI 변환층 + COM 필요 |
| `IItemActivator` | 미착수 | `ShellExecuteEx` |
| `IFileOperations` | 미착수 | `IFileOperation` + **`FOFX_RECYCLEONDELETE`** |
| `IClipboardBridge` | 미착수 | 탐색기 호환 포맷 |

공용 배관: `Interop/StaWorkQueue.cs` (아래 §반드시 알아야 하는 규칙 1).

**아직 `FlexDir.Core` 에 정의조차 없는 포트 2개** — 자율 phase 가 만들지 않았다.

- `IContextMenuProvider` — 의도적 제외. 창 핸들과 네이티브 메뉴 메시지 펌핑이 필요해
  ViewModel 테스트로 채점할 수 없다(`docs/SHELL_NOTES.md` §컨텍스트 메뉴 의
  `IContextMenu2/3` · `HandleMenuMsg`). 포트 정의부터 수동이다.
- `IUsageLog` — `docs/ARCHITECTURE.md` §2 의 포트 목록에는 있는데 step 설계에서 빠졌다.
  ADR-007 도그푸딩 게이트의 **입력**이므로 v1 완성 전에 채워야 한다. 지금은 구멍이다.

## 다음 작업 — `IThumbnailSource`

`src/FlexDir.Core/Presentation/IThumbnailSource.cs` 를 먼저 읽어라. 메서드 둘이 성격이 다르다.

| 메서드 | 경로 |
|---|---|
| `GetTypeIconAsync(extension, isDirectory, size, ct)` | `SHGetFileInfoW` + `SHGFI_ICON` → `HICON` |
| `GetThumbnailAsync(item, size, ct)` | `SHCreateItemFromParsingName` → `IShellItemImageFactory::GetImage` → `HBITMAP` |

**이미 결정된 것** (다시 논의하지 말 것):

- COM 선언은 **`[ComImport]` 전통 방식**. 사용자가 골랐다. `[GeneratedComInterface]` 는
  어셈블리 전체에 `DisableRuntimeMarshalling` 을 요구해 다른 interop 까지 묶는다.
- P/Invoke 는 손으로 쓴 `LibraryImport`. `FlexDir.Shell` 에만 `AllowUnsafeBlocks` 가 켜져 있다.
- 고정 길이 문자 버퍼는 `[InlineArray]` + **원소 `ushort`**. `char` 는 런타임 마샬링에서
  blittable 이 아니라 `SYSLIB1051` 이 난다. 읽을 때 `MemoryMarshal.Cast<ushort, char>` 로 캐스팅한다.
  `ShellTypeNameProvider` 의 `ShellFileInfo` 가 그 예다.
- 결과는 `ThumbnailBitmap(Width, Height, byte[] Pixels)` — 위에서 아래로 채운 BGRA32,
  길이는 `Width * Height * 4` (record 가 검사한다).

**새로 필요한 것**: `HICON`/`HBITMAP` → BGRA32 변환층. 예상 경로는

- `HBITMAP`: `GetObject` 로 크기 → `GetDIBits` 에 32bpp `BI_RGB` **top-down(음수 height)**
  헤더를 주고 바이트로 받는다. 알파가 보존된다.
- `HICON`: `CreateCompatibleDC` + `CreateDIBSection`(32bpp top-down) + `SelectObject` +
  `DrawIconEx(DI_NORMAL)` 로 합성한 뒤 `Marshal.Copy`. 레거시 1비트 마스크 아이콘의
  투명도가 이 경로에서 제대로 처리된다. `GetIconInfo` + `GetDIBits` 는 `hbmColor`·`hbmMask`
  **복사본을 새로 만들어** 해제 대상이 늘어난다 — 피하는 편이 낫다.

**함정** — `docs/SHELL_NOTES.md` §아이콘 을 반드시 다시 읽어라.

- 함정 3 (HICON 누수): 해제 대상이 `HICON`(`DestroyIcon`) · `HBITMAP`(`DeleteObject`) ·
  DC 로 늘어난다. **`StaWorkQueue` 작업 안에서 만들고 그 안에서 해제하라** — 핸들을 큐 밖으로
  내보내지 않는 것이 전작이 종료 시점에 흘린 그 함정을 없애는 방법이고, `StaWorkQueue` 의
  주석에도 그렇게 적혀 있다.
- 함정 1: 동기 블로킹. 반드시 STA 워커에서.
- `GetThumbnailAsync` 는 **`SIIGBF_THUMBNAILONLY`(0x8)** 를 줘야 계약("실제 썸네일. 없으면
  null")과 맞는다. 주지 않으면 썸네일이 없을 때 shell 이 아이콘을 대신 돌려주고, 그러면
  형식 아이콘 경로와 겹쳐 `ThumbnailRequestScheduler` 의 "실패 시 재시도 없음" 이 무의미해진다.
- 캐시를 우리가 만들지 않는다 (`docs/UI_GUIDE.md` §금지 목록). shell 캐시를 쓴다.
  확장자별 아이콘 캐시는 **호출자**(`ThumbnailRequestScheduler`)가 들고 있다.

**테스트 전략** (앞의 셋과 같은 틀): `tests/FlexDir.Shell.Tests/Presentation/` 에
`ShellThumbnailSourceTests.cs`. 문구·픽셀 내용은 기계마다 다르니 **성질**을 재라 —
크기가 요청한 값 이하인가, `Pixels.Length == W*H*4` 인가, 없는 파일은 `null` 인가,
아이콘이 확장자마다 갈리는가, STA 에서 도는가, 취소가 통하는가. 누수는 자동으로 재지 못하니
`manual-plan.md` 의 사람 확인 항목에 추가하라(대용량 폴더를 오래 스크롤하며 GDI 핸들 수 관찰).

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
5. **`ARCHITECTURE.md` §7 은 별도 CLI 를 금지한다.** 확인용 도구는 저장소 밖(scratchpad)에
   만들고 커밋하지 않았다 — 아래 §열린 결정 참조.

## 열린 결정 · 미완

- [ ] **`docs/DESIGN.md` §9** — 키보드 맵, 상호작용 상태(이름변경 인라인 편집 · 스플리터
      드래그 · 페인 간 드래그앤드롭). **phase B(View)의 선행조건**이다. ViewModel 커맨드는
      이미 다 있고 남은 것은 XAML `InputBindings` 의 제스처 매핑이다.
- [ ] **썸네일 스케줄러 배선** — `ThumbnailRequestScheduler` 를 `src` 어디에서도 부르지
      않는다. 그대로 두면 썸네일이 한 장도 나오지 않는다. 결정 항목은 `manual-plan.md` §B 에
      정리돼 있고(소유자 · `Reset` 시점 · `SetVisibleRange` 시점 · `ViewMode`→크기 매핑 · 종료),
      코드비하인드 금지와 부딪히므로 **View 를 짜기 시작하기 전에** 정해야 한다.
- [ ] **클라우드 자리표시자 확인 불가** — 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다(`%OneDrive%` 가 비어 있음). SHELL_NOTES
      §열거 함정 3 의 핵심이고 틀리면 스크롤만으로 수 GB 를 내려받는다. 동기 중인 OneDrive
      내용이 있는 기계에서 확인해야 한다.
- [ ] **`IUsageLog` 포트가 없다** (위 §포트 현황).
- [ ] **`FileSystemEnumerator<T>` 선택을 ADR 로 올릴지** — `IFolderSource` 를
      `FindFirstFileExW` P/Invoke 가 아니라 그것으로 구현했다. 근거와 포기한 것은
      `manual-plan.md` §A 에 있다. guardrail 문서(SHELL_NOTES)와 어긋난 선택이라 나중에
      찾을 수 있어야 한다. 리뷰가 MVVM 의존을 ADR-013 으로 올린 선례가 있다.
- [ ] **확인용 프로브를 어떻게 할지** — scratchpad 에 `probe` 콘솔 앱을 만들어 실물 검증에
      썼다(명령: `enumerate` `watch` `viewstate` `denied` `overflow` `vanish` `typenames`).
      커밋하지 않았고 scratchpad 는 세션마다 달라 **다음 세션에는 없다.** 남은 포트 4개도
      전부 수동 검증이 필요하므로(ADR-009) 매번 다시 만들 것인지, `.harness/probe/` 로
      들여 sln 밖에 둘 것인지 정해야 한다. 후자는 `ARCHITECTURE.md` §7 과 부딪히는지
      판단이 필요하다 — §7 이 막으려던 것은 "실사용과 무관한 것을 재는 벤치마크 CLI" 였다.
- [ ] **푸시** — `main` 이 `origin/main` 보다 11 커밋 앞서 있다.

## 이번 세션 커밋 (리뷰 이후 11개)

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
A. Shell interop   ← 지금 여기 (4/8)
C. Host 뼈대        진입점 + DI, 화면 없이 실행되는지
B. View            DESIGN §9 를 먼저 채운 뒤
→ 매일 쓰기 → 도그푸딩 게이트 ON (ADR-007, v1 완성 후)
```
