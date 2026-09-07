# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> **여기에는 살아 있는 것만 둔다** (2026-08-12 에 갈랐다). 얼어붙은 기록 — 배포 기록 ·
> 지난 구간이 만든 표면 · 시안 대조 · 최대화 여백 — 은 [`HISTORY.md`](./HISTORY.md) 에 있다.
> 이 파일이 1,094줄까지 자라 **살아 있는 것이 역사에 묻혔던 것**이 이유다.
>
> 갱신: 2026-08-12. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) — 수동 phase 계획과
> 사람 확인 항목(프로브·실물 실측 결과가 거기 있다). 세션 커밋 이력은 `git log` 를 본다.

## 한 줄

**v1 이 나갔고, 그 뒤로는 도그푸딩이 무엇을 만들지 정하고 있다** (ADR-007 §폐기가 노린
자리다). 쓰다가 걸린 것이 그대로 작업이 됐고, 그렇게 들어온 것이 **폴더 트리 · 네트워크
위치 · 즐겨찾기 · 컬럼 폭 · 설정 창 · 마우스 보조 버튼**이다.

**도그푸딩은 결함도 끌어냈다.** 셋은 사용자가 신고해서 닫혔다 — 감시 오버플로 폭주
(§13 · 처방이 증상을 다시 만드는 고리까지 밟았다) · UI 스레드 예외 하나가 상주 프로세스를
통째로 날리던 것(§14) · 활성화 채널이 조용히 죽던 것(§16 · 게이트가 잡았다).
**전부 배포까지 닫혔다.**

**그리고 탭이 들어왔다** (§17 · 2026-08-11~12). 이 문서가 가장 강하게 막아 뒀던 기능이고
(§2 가 *"전작이 갖추고도 실패한 6개"* 의 첫 항목으로 적어 두었다), 사용자 지시로 들였다.
소유 구조 · 저장 포맷 · 탭 줄 화면 · 드래그까지 서고 **사람 확인 아홉 항목이 닫혔다** —
그중 넷은 기계가 한 번도 못 밟던 것이다. **v0.5.0 으로 나갔고 실물에서 돈다.**

**그 다음이 분할이다** (§18 · 2026-08-12). 탭과 **정확히 같은 자리**를 지났다 — `ADR-004`
가 4분할을 v2+ 로 미뤄 두었고 §17 이 *"페인 접기(1분할)는 범위 밖"* 이라고 적어 둔 것을
사용자 지시가 뒤집었다. **프리셋 네 단계(1·2·3·4)이고 기본은 1분할**이며, 줄이는 것은
**접는 것**이라 다시 펴면 그대로다. `PaneSide { Left, Right }` 열거형이 사라졌다 —
자리는 이제 번호다 (`ADR-019`). **v0.6.0 으로 나갔고 설치본에서 돈다** (2026-08-12).

**그리고 분할이 다음 작업을 직접 낳았다 — 툴바 오버플로·알려진 폴더** (§20 · 2026-08-21 ·
1구간 v0.8.0~v0.8.4). 좁은 페인에서 툴바가 잘리던 것을 `FoldOrder` 무리 접힘 +
**`···`(`E712`) 메뉴**로 받고 (`ADR-021` · `Views/ToolbarOverflowPanel.cs`), 알려진 폴더
다섯(홈·바탕화면·문서·다운로드·사진, **`E8B7`**)이 드롭다운으로 들어왔다. 포트가 하나 늘어 **17/17** (`IKnownFolderList`).
게이트 4종 ✅ · **패키지는 구웠고 업로드는 안 했다** (`-NoUpload`). **사람 확인은 전부
닫혔다** (2026-08-21 · 아래 §다음 작업 0-0).

**그리고 2구간이 그 자리를 채웠다 — 외부 도구** (§21 · 2026-08-24 · v0.8.5). `FoldOrder 1`
에 `[VS]`(VS Code)·`[>_]`(터미널)가 들어왔고 포트가 둘 늘어 **19/19**
(`IExternalToolCatalog` 탐지 · `IExternalToolLauncher` 실행). 이 툴바에서 **처음으로
글리프가 아니라 Path 를 썼다** — 폰트 안에 뜻이 오는 터미널 글자가 없었다(`ADR-022` ·
`DESIGN.md` §7). 게이트 4종 ✅ · 테스트 **2280** · 서명본을 굽고 설치까지 했다
(`-NoUpload`). **사람 확인 일곱이 전부 닫혔다** (2026-08-24 · 사용자 *"모두 정상"*).
⚠ **계획은 v0.9.0 이었고 사용자가 0.8.5 로 정했다** — 실행이 끝난 step 지시서의 `v0.9.0`
표기는 얼어붙은 기록이라 그대로 뒀다 (`docs/PRD-v2.md` §21 머리).

> **값을 치른 자국 셋** (전부 `docs/PRD-v2.md` §17 §값을 치르고 배운 것에 전문이 있다):
> WPF 바인딩은 런타임 조회라 **타입을 바꾸면 템플릿 안 바인딩이 컴파일 에러 없이 조용히
> 죽는다** · `Command` 가 `null` 인 `MenuItem` 은 **정상으로 뜨고 눌리기까지 한다**(그래서
> 메뉴는 일곱 개를 실제로 눌러 봐야 한다) · **근거가 무너진 스펙 값은 조용히 남는다**
> (활성 탭 배경색이 그랬고, 대비가 6/255 였다).

**진단 도구**: `dotnet-stack report -p <pid>` 가 UI 스레드의 관리 스택을 준다 (§13 의 범인을
이것이 좁혔다). `Get-Process` 의 스레드별 `TotalProcessorTime` 으로 "누가 태우나" 를 먼저
좁히면 빠르다. **받기·네트워크가 진행 중인지는 `Get-NetTCPConnection -OwningProcess <pid>`**
로 본다 — 파일 크기는 버퍼 때문에 점프한다 (`HISTORY.md` §v0.5.0 전문).

## 현재 상태

```
브랜치   feat-5-external-tools  ·  **origin/main 은 7b6bcc2** (2026-08-13 푸시).
                 푸시는 사용자 지시가 있을 때만 한다
작업트리 **2구간 외부 도구(§21)가 step 단위 커밋으로 전부 들어갔다** (step 0~9).
                 포트 둘 · Shell 구현 둘 · 설정 항목 셋 · 툴바 버튼 둘 · 문서와 버전.
                 step 목록의 정본은 `phases/5-external-tools/index.json` 이고
                 그 앞은 `phases/4-known-folder-icons` · `phases/3-toolbar-overflow`
                 ✅ **조립 누락이 하나 있었고 배포 전에 닫혔다** (커밋 `ecbb175`).
                 `AppComposition` 이 `AppPathsToolCatalog`·`ProcessToolLauncher` 를
                 만들지도 `Pane()` 과 `SettingsViewModel` 에 넘기지도 않아 실물에서
                 버튼 둘과 `[실행해 보기]` 가 회색이었다. 주입이 선택 인자(`= null`)라
                 컴파일러도 게이트 넷도 못 잡았고, **잡은 것은 step 8 의 실물 캡처와
                 step 9 의 `blocked`** 다. 이제 `AppCompositionTests` 의
                 `Create_GivesThePaneWorkingExternalTools` 가 막는다 — 배선을 되돌리면
                 실패하는 것을 확인했다. 교훈은 §규칙 19 에 남겼다
버전     0.8.5   릴리스는 v0.1.0 ~ v0.7.0 까지 나갔다. **v0.8.0~v0.8.5 은 패키지만
                 굽는다** (`pack.ps1 -NoUpload -SignThumbprint` · 서명본).
                 ✅ **0.8.5 는 굽고 설치까지 했다** (2026-08-24 · `-NoUpload`).
                 이 기계 설치본이 `0.8.5+c87bb09…` 이고 창이 뜬다.
                 ⚠ **계획은 v0.9.0 이었다** — 사용자가 0.8.5 로 정했고, 실행이 끝난 step
                 지시서의 `v0.9.0` 표기는 얼어붙은 기록이라 그대로 뒀다 (PRD §21 머리).
                 ⚠ **0.8.1 을 다시 굽지 않고 0.8.2 로 올렸다** — 0.8.1 이 이미 이 기계에
                 설치돼 있어 같은 번호로 다시 구우면 자동 업데이트 규칙(설치된 버전 <
                 피드의 최신)이 깨지고 설치 관리자도 갱신으로 보지 않는다.
                 업로드·푸시는 사용자 지시가 있을 때만 한다 — 서명 설치본이 실제로
                 뜨는지(사람 확인 12번)를 이 설치본으로 밟는다.
                 그 앞은 0.7.0(다크모드 §19 · 첫 서명본 · 알림 경로 확인) ·
                 0.6.1(창 결함 둘) · 0.6.0(분할 §18).
                 ⚠ `Get-AuthenticodeSignature` 는 `UnknownError` 를 내는데, **서명은 붙어
                 있고 이 기계의 신뢰 루트에 자체 서명 인증서가 없어서**다 — 결함이 아니다
                 (`HISTORY.md` §v0.7.0)
테스트   2280 통과   Core 654 · Shell 394 · App 1137 · Host 95
                 (외부 도구 §21 이 +184: Core +61 · Shell +61 · App +61 · **Host +1**.
                 **그 Host 하나가 조립이 페인에 닿는지 보는 유일한 테스트다** —
                 step 0~8 이 그것을 0 으로 두었고, 그래서 조립 누락이 게이트 넷을
                 전부 통과했다 (§규칙 19). 그 앞은 2093 이었고
                 알려진 폴더 아이콘 +20 · 툴바 오버플로 +45 · 다크모드 +38)
게이트   fast (build -warnaserror · test --blame-hang · check-structure) ✅
         full (Release build -warnaserror) ✅ **2026-08-24 · v0.8.5 문서·버전과
         조립 수정(`ecbb175`) 뒤에 넷 다 다시 돌렸다.**
         ⚠ **게이트가 1구간에서 화면 결함 둘을 연속으로 통과시켰다** — `MenuItem.Icon` 이
         안 그려지던 것(v0.8.2)과 `»` 가 두부로 뜨던 것(v0.8.3). 빌드·테스트·구조 게이트가
         전부 초록인 채로 화면이 틀렸다. **자동 채점의 경계다** (CLAUDE.md §5)
         ⚠ **2구간은 그 경계를 한 겹 더 밀었다 — 조립 누락도 초록이었다.**
         `AppComposition` 이 포트 둘을 안 넘긴 채로 테스트 2276개가 전부 통과했다.
         ViewModel 테스트는 포트를 fake 로 직접 넣어 만들고, 그때의 Host 테스트 94개는
         조립의 *정리 순서*만 봤다 — **"실제로 주입됐는가" 를 보는 테스트가 없었다.**
         잡은 것은 게이트가 아니라 **step 8 의 실물 캡처**(버튼이 회색으로 떴다)이고,
         지금은 `AppCompositionTests.Create_GivesThePaneWorkingExternalTools` (95번째
         Host 테스트)가 그 자리를 막는다. **경계 자체는 그대로다** — 다음에 포트를
         선택 인자로 열 때 같은 테스트를 함께 만들어야 한다 (§규칙 19)
         ✅ `PaneTabsViewModelTests.Reactivating_RefreshesOnceThenWatchesAgain` 플레이키를
         닫았다 (2026-08-24). 전체 병렬 실행에서 **사흘 만에 두 번** 5초 상한을 넘었고
         단독으로는 59~142ms 에 통과한다. `WaitForAsync` 의 상한을 **5초 → 30초**로 올렸다.
         단언은 그대로라 테스트가 약해지지 않는다 — **그 상한은 마감이 아니라 매달림을
         실패로 바꾸는 장치이고**(헬퍼 주석), 진짜 매달림은 `--blame-hang-timeout 120s` 가
         다시 받는다. **게이트가 이유 없이 빨개지면 사람이 빨간 것을 무시하는 법을 배운다** —
         그것이 고친 이유다
         ⚠ `SetSplit_Folding_ReleasesTheWatch` 가 한 번 플레이키했다 —
         `NavigateAsync` 가 감시 루프가 서는 것을 기다리지 않아서다. `watcher.Current` 를
         기다린 뒤 `WatchStream.Finished` 를 보도록 고쳤고 3회 반복 안정이다
         ⚠ **저장소 Debug 빌드로 앱을 띄우면 산출물을 잠근다** — 확인이 끝나면 반드시
         죽인다. 설치본은 잠그지 않는다 (위 §배포 절차 의 경고)
         ⛔ 도그푸딩 게이트는 없다 — 스크립트를 지웠다 (ADR-007 §폐기)
phases/  0-core-model · 1-core-pipeline · 2-viewmodel · 3-toolbar-overflow ·
         4-known-folder-icons · 5-external-tools 모두 step 완료
```


### 배포 절차 — 다음 배포도 이 순서다

**다섯 번 밟았고 다섯 번 다 이 순서였다** (v0.4.1 에서 굳었다). 개별 배포 기록은
[`HISTORY.md`](./HISTORY.md) §배포 기록 에 있다.

```
Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue   # ① 반드시 먼저
dotnet build --nologo -warnaserror                                     # ② 게이트 4종
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
#                                        ③ Directory.Build.props <Version> 을 올린다
#                                        ④ git push  →  pwsh -File scripts/pack.ps1
```

> **①을 빼먹으면 ②가 파일 잠금으로 깨진다.** 도그푸딩 중인 `FlexDir.Host` 는 자기가
> 띄워진 산출물을 잠그고 그 빌드는 오류 수십 개를 낸다 — **Debug 든 Release 든
> 마찬가지다.** 한때 이 문서는 "Release 실행 파일로 띄우면 된다" 고 적었지만 그러면
> 게이트 4번째가 대신 깨진다.
>
> **이것은 저장소 산출물로 띄웠을 때의 이야기다. 인스톨러가 그 교착을 풀었다** —
> 설치본은 `%LOCALAPPDATA%\flex-dir\current\` 에서 돌아 저장소를 잠그지 않는다.
> **설치본을 켜 둔 채 게이트 4종이 전부 통과하는 것을 확인했다.** 그러므로 도그푸딩은
> 설치본으로 하고, `dotnet run`·`bin\Debug` 실행은 확인용으로만 쓰고 게이트 전에 죽인다.

**③을 빼먹으면 아무도 갱신되지 않는다.** 자동 업데이트는 *"설치된 버전 < 피드의 최신
버전"* 하나로 돈다 (`scripts/pack.ps1` 머리 주석).

**XAML 루트를 건드린 배포는 "설치본이 실제로 뜨는 것" 까지 봐야 끝난다.** §10 의
`StaticResource` 크래시가 컴파일과 테스트를 다 통과하고 **처음 우클릭할 때** 터졌던
자리다. 실패한다면 **창이 안 뜨는 모양**이고, 그것은 새 버전이 시작되는 것을 보는
것으로만 배제된다.

## 포트 현황 — 19/19

2026-08-10 에 넷이 늘었다. 셋은 트리와 즐겨찾기가, 하나는 설정 창이 요구한 것이다
(docs/PRD-v2.md §10·§10-2·§12). 2026-08-12 에 다크모드가 하나를 더 늘렸고 (§19),
2026-08-21 에 알려진 폴더가 하나를 더 늘렸다 (§20). **2026-08-24 에 외부 도구가 둘을
늘렸다** (§21 · ADR-022) — 아래 둘은 **구현체는 있는데 조립이 아직 없다** (위 §현재 상태
§작업트리).

| 포트 | 구현 | 왜 따로인가 |
|---|---|---|
| `ISettingsStore` | `Shell/Settings/JsonSettingsStore.cs` | 설정은 캐시가 아니라 **사용자의 의도**다 — 뷰 상태가 깨져 기본값으로 접히는 사건이 "숨김 파일을 보겠다" 를 함께 뒤집으면 안 된다. 즐겨찾기와 같은 판단이고 파일도 따로 쓴다 (`settings.json`) |
| `IDriveList` | `Shell/Storage/SystemDriveList.cs` | 드라이브 목록. COM 이 아니라 `DriveInfo`+`mpr.dll` 이라 STA 도 정리도 없다 |
| `INetworkPlaceList` | `Shell/Storage/ShellNetworkPlaceList.cs` | '네트워크 위치 추가' 는 드라이브 문자를 만들지 않아 `WNetGetConnection` 에 안 잡힌다. **COM 이라 STA 를 들고 정리 목록에 들어간다** |
| `IFavoriteStore` | `Shell/Favorites/JsonFavoriteStore.cs` | 즐겨찾기는 캐시가 아니라 사용자 데이터다 — 뷰 상태와 **다른 파일**이어야 한다 |
| `ISystemThemeSource` | `Shell/Settings/RegistrySystemThemeSource.cs` | OS 가 라이트/다크인가. 레지스트리 값 하나라 COM 도 STA 도 필요 없지만, 다른 시스템 조회 포트와 같은 이유로 비동기다 (ADR-020) |
| `IKnownFolderList` | `Shell/Locations/KnownFolderList.cs` | 알려진 폴더 다섯의 위치. **조회가 저장소에 닿는다** — 리디렉션된 폴더(OneDrive·도메인 로밍)에서는 네트워크로 내려간다. COM 이 아니라 STA 도 정리도 필요 없다 (`AppComposition` 정리 목록에 없다 — `RegistrySystemThemeSource`·`SystemDriveList` 와 같은 자리) |
| `IExternalToolCatalog` | `Shell/Tools/AppPathsToolCatalog.cs` | 설치된 외부 도구를 찾는다. **실패는 `null`, 취소만 예외.** **캐시하지 않는다** — 상주 앱이라 창이 다시 보일 때마다 다시 묻는다. COM 이 아니라 STA 도 정리도 없다 (ADR-022) |
| `IExternalToolLauncher` | `Shell/Tools/ProcessToolLauncher.cs` | 외부 도구를 그 폴더를 작업 디렉터리로 띄운다. **실패는 `LocationErrorKind`, 던지지 않는다** — 실패가 정상 상황이라 호출자가 상태표시줄 한 줄로 만든다. 탐지와 가른 이유가 이 칸의 차이다 (ADR-022) |

아래 표는 v1 의 열하나다.

## 포트 현황 (v1) — 11/11

`IDriveSpace` 는 마감에서 늘었다 (여유 용량). 이름이 `Shell*` 이 아닌 이유는 COM 이
아니어서다 — `DriveInfo` 는 `GetDiskFreeSpaceEx` 로 내려가므로 STA 도 정리도 필요 없고
`AppComposition` 의 정리 목록에도 들어가지 않는다. **그래도 UI 스레드에서는 못 부른다**
(네트워크·이동식 볼륨에서 초 단위 블로킹 — CLAUDE.md §3). 아파트먼트와 블로킹은 다른
문제라는 것이 여기서도 그대로다 (§규칙 8).

| 포트 | 구현 | 테스트 |
|---|---|---|
| `IViewStateStore` | `Shell/ViewState/JsonViewStateStore.cs` | 계약 12 + 고유 10 |
| `IFolderWatcher` | `Shell/Watching/FileSystemFolderWatcher.cs` | 계약 4 + 실물 5 |
| `IFolderSource` | `Shell/Enumeration/RoutingFolderSource.cs` → `FileSystemFolderSource` · `NetworkShareSource` | 계약 6 + 실물 10 + 라우팅 5 + 공유 5 |
| `ITypeNameProvider` | `Shell/Presentation/ShellTypeNameProvider.cs` | 13 |
| `IThumbnailSource` | `Shell/Presentation/ShellThumbnailSource.cs` — 메서드 셋: `GetThumbnailAsync`(내용 미리보기) · `GetTypeIconAsync`(확장자별 형식 아이콘) · `GetItemIconAsync`(경로별 항목 아이콘 · v0.8.1). **포트 개수는 안 늘었다** — 메서드가 는 것이다 | 23 |
| `IItemActivator` | `Shell/Activation/ShellItemActivator.cs` | 16 |
| `IFileOperations` | `Shell/Operations/ShellFileOperations.cs` | 48 |
| `IClipboardBridge` | `Shell/Operations/ShellClipboardBridge.cs` | 24 |
| `IUsageLog` | `Shell/Usage/FileUsageLog.cs` | 9 + fake 3 |
| `IContextMenuProvider` | `Shell/Operations/ShellContextMenuProvider.cs` | 12 + fake 6 |
| `IDriveSpace` | `Shell/Storage/FileSystemDriveSpace.cs` | 4 + fake 3 |
| `IUiDispatcher` (App 의 포트) | `App/Threading/WpfUiDispatcher.cs` | 8 |

공용 배관은 `Interop/StaWorkQueue.cs`(아파트먼트)와 `Interop/ShellInfoGate.cs`
(`SHGetFileInfo` 직렬화 — 둘은 다른 문제다, §규칙 8).

**소유 창은 `Host/Startup/OwnerWindow` 가 쥔다** (사용자 결정 2026-08-06). Core 포트에는
창 핸들이 없고(CLAUDE.md §1) Host 가 공급자(`Func<nint>`)를 물려 준다 — `IContextMenuProvider`
와 `IFileOperations`(`SetOwnerWindow`) 둘이 그것을 쓴다. 대가는 "어느 창 위에"를 호출자가
못 정한다는 것이고, 창이 하나라는 전제(ADR-003)에 기댄다.

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
          AppComposition.Create(WpfUiDispatcher, %APPDATA%\flex-dir)
          ActivationRouter  ← 실행 + 활성화마다 IUsageLog 기록 · 인자의 폴더를 활성 페인에서
          PerformanceLog    ← ColdStart 를 perf.log 에
          application.Run() … 창 없이 상주
          (완전 종료 뒤) composition.DisposeAsync()
```

**실물에서 잰 것** (`manual-plan.md` §C 사람 확인 항목에 전문):
cold start **358ms**(첫 실행) / **123~134ms**(이후) — 목표 1.5s.
두 번째 실행은 **124ms 에 exit 0** 이고 프로세스는 하나로 유지된다.
상주 프로세스를 죽이면 다음 실행이 새 상주 프로세스가 된다.

**계측 셋이 전부 붙었고 실물 수치도 봤다** (2026-08-06 · perf.log): ColdStart(Program) ·
WindowShown(`Startup/WindowPresenter`, 매 활성화) · FirstItem(`Diagnostics/FirstItemMeter`,
**0번 페인 하나**). **상주 중 WindowShown 3~9ms**(예산 100) ·
**FirstItem 0~18ms**(예산 150) — 첫 표시만 367ms(창 생성 포함, 상주가 한 번만 내는 비용).
닫기 = 숨기기 · 트레이 완전 종료 · 시작 상태 복원도 실물 확인됐다 (manual-plan §B-2).


## 다음 작업 (2026-08-24 갱신)

### 0. 2구간(v0.8.5) 외부 도구 — **나갔다. 사람 확인 일곱이 전부 닫혔다**

게이트 4종 ✅ · 테스트 **2280** ✅ (2026-08-24). 전문은 `docs/PRD-v2.md` §21 · `ADR-022` ·
step 목록은 `phases/5-external-tools/index.json`. **서명본을 굽고 설치까지 했다**
(`-NoUpload` · `HISTORY.md` §v0.8.5). **업로드·푸시는 안 했다.**

✅ **조립 누락은 닫혔다** (커밋 `ecbb175`). `AppComposition.Create` 가
`AppPathsToolCatalog`·`ProcessToolLauncher` 를 만들어 `Pane()` 과 `SettingsViewModel` 에
넘긴다 (정리 목록에는 안 넣었다 — COM 이 아니다). 그 전까지는 버튼 둘과 `[실행해 보기]` 가
실물에서 회색이었고 게이트 넷은 전부 초록이었다. 되돌리면
`AppCompositionTests.Create_GivesThePaneWorkingExternalTools` 가 실패한다 — 확인했다.
**아래 일곱은 이제 그대로 밟을 수 있다.**

**✅ 닫힌 다섯** (2026-08-24 · UI Automation + 프로세스 명령줄로 밟았다. 합성 마우스는
안 썼다 — 포그라운드를 못 잡으면 조용히 실패한다, §규칙 13):

1. **`C:\` 에서 `[>_]`** — WezTerm 이 `start --cwd "C:\"` 로 떴다 (사용자 지정:
   `C:\Program Files\WezTerm\wezterm-gui.exe` · `start --cwd "{path}"`).
2. **NAS** — `\\10.10.10.23\home` 에서 둘 다 활성이고 `start --cwd "\\10.10.10.23\home"`
   이 나갔다 (**표시형 그대로다 — `\\?\UNC\` 가 안 샌다**). `\\10.10.10.23` 로 한 단
   올라가니 **둘 다 `IsEnabled=False`**. 막히는 것은 서버 루트 하나뿐이다.
3. **`[VS]`** — `Code.exe "C:\Users\SOOJANG\orca"` (따옴표 하나). HKCU App Paths 의
   `code.exe` 를 지우고 **창을 숨겼다 다시 보이자 네 페인의 `[VS]` 가 전부 비활성**이
   됐고 `[>_]` 는 활성을 유지했다(도구를 따로 묻는다는 증거). **같은 PID 였다** — 상주
   프로세스에서 재탐지가 돈다. 레지스트리는 복구했고 복구 뒤 넷 다 다시 활성이다.
6. **접힘** — 창 **900 DIP** 4분할에서 `[VS]`·`[>_]` 가 툴바에서 사라지고(0개)
   `···` 가 네 페인에 뜨며(4개) **알려진 폴더는 남았다**(4개). 1094 DIP 에서는 좁은
   페인만 접혔다. ⚠ **최대화에서 접히는지는 아직 실측 전이다** — 확인한 최대화는 넓은
   모니터라 페인이 약 1044 였고 아무것도 안 접혔다. §21 의 420 은 여전히 계산값이다.
7. **서명 설치본이 실제로 뜬다** — `Setup.exe --silent` 로 0.8.4 → 0.8.5, 창이 떴고
   `error.log` 마지막 항목은 닷새 전 것이다 (`HISTORY.md` §v0.8.5).

**✅ 사용자가 닫은 나머지** (2026-08-24 · *"모두 정상"*):

4. **실루엣이 갈린다** — `[VS]`·`[>_]` 가 서로, 그리고 기존 계열(선 `E8A4`·`E8FD` ·
   사각 `E8A9`·`E15B` · 폴더 `E8B7` · 점 `E712`)과 구분된다.
   **이 툴바의 계열이 셋이 됐다: 선 · 사각 · 채우기** (`DESIGN.md` §7).
5. **다크·라이트 대비와 비활성 회색이 양쪽에서 읽힌다.**
   ⚠ 확인하려고 `theme` 을 `Light` 로 바꿨다가 **`Dark` 로 되돌려 놓았다.**
7-b. **페인 우클릭이 정상이다** — §10 의 `StaticResource` 크래시가 컴파일·테스트를
   다 통과하고 **첫 우클릭에서** 터졌던 자리다. 이번 구간은 `MainWindow.xaml` 의 리소스를
   셋(`ToolPathButton`·지오메트리 둘) 늘렸으므로 같은 위험이 있었다.
   ⚠ **합성 입력으로 못 밟는다** — 사람이 직접 눌러야 하는 항목이다.
8. **설정의 `[실행해 보기]` 가 돈다** (곁항목 · 2026-08-24). 상태 폴더에서 WezTerm 이
   뜨고 패널에 `사용자 지정 을(를) 열었습니다` 가 찍힌다.
   ⚠ 문구가 어색하다 — 라벨이 *"사용자 지정"* 이라 조사가 붙으면 읽히지 않는다.
   **관측만 해 두고 안 고쳤다** (프리셋 다섯은 자연스럽다).

**2구간에 열린 항목은 없다.** §곁에 남은 구멍이라 적어 뒀던 것은 격리 리뷰(2026-08-24)가
진단을 정정하고 코드로 닫았다 — 아래 참조.

✅ **격리 리뷰가 크래시 사슬 하나와 진단 오류 하나를 잡았다** (2026-08-24 · `harness-reviewer`
서브에이전트가 서버 과부하로 여섯 번 죽었지만 남긴 재현 테스트를 같은 worktree 에서 직접
돌려 확인했다):

1. **범위 밖 `TerminalPreset` 값이 앱을 세 갈래로 크래시시켰다.** `Enum.TryParse` 는
   정의되지 않은 값이어도 숫자 문자열이면 성공한다 — `"terminalPreset": "99"` 가 든 파일을
   읽으면 `(TerminalPreset)99` 가 그대로 실렸다. 그 값이 `SettingsViewModel.SelectedTerminal`
   (XAML 바인딩 getter — **설정 패널을 여는 것만으로 터진다**) · `[실행해 보기]` ·
   `PaneViewModel.OpenInTerminalAsync`(커맨드 밖 예외라 §규칙 10 대로 **프로세스 전체가
   죽는다**) 세 곳에서 던졌다. `JsonSettingsStore.Parse`(`Shell/Settings/JsonSettingsStore.cs`)
   에 `Enum.IsDefined` 검사를 더해 막았다.
2. **진단 정정 — "새 탭이 탐지를 안 돈다" 는 절반만 맞았다.** 진짜 원인은 `Adopt` 가 아니라
   `PaneViewModel.Terminal`(그리고 `PaneTabsViewModel.Terminal`) setter 의 **record 값 비교
   조기 반환**이었다. 새 페인의 필드 기본값이 이미 앱 전체 기본값(`WindowsTerminal`)과
   같아서 — **프리셋을 한 번도 안 바꾼 대다수 사용자**의 새 탭·분할 페인은 영원히 탐지가
   안 돌았다. `PowerShell7` 로 바꾼 사람만 우연히 됐다. `externalToolsCts is null`(아직
   한 번도 탐지를 안 돈 상태)을 값 비교의 예외로 둬서 첫 대입은 항상 돌게 고쳤다.
   **기존 테스트 둘(`ANewTab_StartsWithTheTerminalThePaneIsAlreadyUsing`·
   `APaneBornFromSplitting_StartsWithTheSameTerminal`)이 이 자리를 놓친 이유도 확인했다** —
   둘 다 `PowerShell7`·`CommandPrompt` 를 써서 우연히 결함을 비켜 갔다. 기본 프리셋으로
   실제 탐지가 도는지 보는 테스트를 새로 넣었다.

테스트 2277 → **2280**. 게이트 4종 재확인 ✅.

### 0-0. 1구간(v0.8.0→v0.8.4) 툴바 오버플로·알려진 폴더·항목 아이콘 — **닫혔다**

사람 확인이 끝났다 (2026-08-21). **전문은 `HISTORY.md` §1구간 사람 확인 으로 옮겼다** —
닫힌 항목 여덟과 그때의 경고(`Setup.exe` 의 `--silent` · 합성 클릭으로 못 밟는 우클릭 ·
`theme` 되돌리기)가 거기 그대로 있다. **열린 항목이었던 라이트 테마 대비는 2026-08-24 에
2구간 확인과 함께 닫혔다** (§다음 작업 0 의 5번 — 사용자가 양쪽을 보고 *"모두 정상"*).
**1구간에 열린 항목은 없다.**




### 창 결함 둘 — 끝났고 나갔다 (v0.6.1). **열린 항목 없음**

사용자가 쓰다가 찾은 것 셋을 고쳤다. 커밋 `63eba31` · `ad5b06b` · `4b10c15` · `e0b2c03`.

- **트리 가로 스크롤을 껐다** (`Disabled`). 탐색기 탐색창에 없는 막대였다. `Hidden` 이
  아닌 이유는 `MainWindow.xaml` 의 `TreeView` 주석에 있다 — `Hidden` 은 내용을 여전히
  무한 폭으로 재서 `TreeScroll` 의 `BringIntoView` 가 노드를 가로로 미는 것을 못 막는다.
- **최대화하면 바깥 8px 이 화면 밖으로 나가던 것을 고쳤다** (`Views/MaximizedFrame.cs`).
  **§표 의 §최대화 여백 행이 뒤집힌 자리다** — 2026-08-06 판정이 틀렸다.
- **최대화 복원이 주 모니터로 가던 것을 고쳤다** (`ResidentWindow.FirstState`).
  창이 뜨기 전에 `WindowState=Maximized` 를 걸면 WPF 가 주 모니터 기준으로 크기를
  정하고 저장한 좌표를 버린다. `Loaded` 뒤로 미룬다.

**배포까지 끝났다** — 세 커밋 → 0.6.1 → push → `pack.ps1` → 릴리스 v0.6.1.
사용자가 실물에서 **"정상동작하는거 봤어"** 로 닫았다.

⚠ **v0.6.1 은 무서명이다.** 서명 배선(`e0b2c03`)이 그 뒤에 들어갔으므로 **0.6.2 부터**
서명본이 나간다 — 아래 §서명 참조.

### 서명 — 사내용 자체 서명을 선택으로 달았다 (2026-08-12)

`scripts/new-signing-cert.ps1` 이 인증서를 만들고, `pack.ps1 -SignThumbprint <지문>` 이
서명해 굽는다. **인자를 주지 않으면 지금처럼 서명 없이 굽는다** — 인증서가 없는 기계에서
굽는 길이 막히지 않게 한 것이다.

- 이 기계의 인증서: `CN=Rootech, O=Rootech, C=KR` · 만료 2031-08-12. 지문은 저장소에
  적지 않는다 (`Get-ChildItem Cert:\CurrentUser\My` 로 찾는다).
- `signing/` 은 `.gitignore` 가 막는다. **개인 키 백업(`.pfx`)은 아직 없다** — 잃으면
  배포한 신뢰가 새 인증서와 안 맞아 사내 기계를 전부 다시 돌아야 한다.
- ⚠ **SmartScreen 은 이걸로 안 없어질 수 있다.** 사내에서만 신뢰하는 인증서에는
  마이크로소프트 평판이 없다. 사라지는 것은 '알 수 없는 게시자' 표기다. GitHub 다운로드는
  MOTW 가 붙어 검사를 타므로, 없애려면 사내 공유·Intune 배포로 바꾸거나 공개 CA 로 간다.

### 0-1. 분할 — 끝났고 나갔다 (v0.6.0). **열린 항목 없음**

**§18 이 정본이다** (`docs/PRD-v2.md` · `ADR-019`). 결정 일곱은 전부 사용자 인터뷰로 굳었고
게이트 4종이 통과했다. **기계 확인 다섯 + 사람 확인 일곱이 전부 닫혔다**
(2026-08-12 · 사용자: *"모두 정상동작해"*). 뼈대가 되는 셋:

- **v0.5.0 저장 파일이 2분할로 뜬다** (옛 형식 그대로인 파일에서 확인). 마이그레이션이
  실물에서 돈다 — 쓰던 사람의 화면이 갱신 한 번으로 반쪽이 되지 않는다.
- **접힌 페인이 재시작을 건너 산다.** 3분할로 저장했는데 `panes` 는 넷이었고 4번째 폴더가
  그대로였다. "접기는 닫기가 아니다" 가 파일까지 성립한다.
- **늘려도 자리가 안 튄다.** 슬롯 순서를 고른 이유가 실물에서 성립했다 — 1→2→3→4 어느
  단계에서도 이미 있던 페인이 안 움직인다.

**메뉴 넷과 탭 줄 우클릭 둘을 실제로 눌렀다** — `Command` 가 `null` 인 `MenuItem` 은 정상으로
뜨고 눌리기까지 한다는 §17 의 함정을 이것으로 배제했다. '이 페인 닫기' 는 `Tag.Tag` 를 두 단
타고 올라가는 바인딩이라 더 그랬다.

**분할에 열린 항목은 없다.** 매일 쓰며 판정할 것 셋만 남았다 (§18 §며칠 써야 보이는 것):
**1080 세로에서의 4분할** · 고정 탭·즐겨찾기·분할의 겹침 · 분할 단축키 자리.

**배포까지 끝났다** — 네 커밋 → 0.6.0 → push → `pack.ps1` → **설치본이 4분할로 뜨는 것까지
확인**했다 (§10 의 `StaticResource` 크래시가 그 자리였다). 전문은 `HISTORY.md` §v0.6.0.

⚠ **다음 배포가 갚아야 할 것 하나**: 이번에는 자동 업데이트 경로(알림 → `지금 설치`)를
안 밟았다. 0.5.0 이 새 저장 형식을 못 읽어 기억을 덮어쓰기 때문이다. **0.6.0 → 다음
버전은 같은 포맷이라 알림 경로로 가면 되고, 그때 그 경로가 함께 검증된다.**

### 0-2. 탭 — 끝났다. v0.5.0 으로 나갔고 실물에서 돈다 (2026-08-12 갱신)

**사용자가 실물에서 아홉 항목을 밟았고 전부 정상이었다** (2026-08-12 · 저장소 Debug 빌드).
그중 넷은 **기계가 한 번도 못 밟던 것**이다 — 드래그 둘(모달 루프) · '반대편 페인으로
보내기'(고친 직후) · 제목 이름 바꾸기. `manual-plan.md` §탭 의 체크박스가 정본이다.

**열려 있는 것은 하나뿐이고, 그것은 한 번 밟아서 닫히는 종류가 아니다** —
*"고정 탭이 실제로 쓰이는가"* (즐겨찾기와 겹치는 자리 · §17 §겹치는 자리). 며칠 써야
한쪽이 안 쓰이는 것이 보인다.

**그리고 그 릴리스가 나갔다 — v0.5.0** (2026-08-12 · `HISTORY.md` §v0.5.0 전문). §16(활성화 채널)도
함께 실어 나갔다 (사용자 결정 2026-08-11). 이 기계 설치본이 0.5.0 으로 교체되고 탭 줄이
그려진 것까지 확인했다. **탭에 열린 것은 없다.**

**분할이 끝난 뒤의 축은 도그푸딩이 고른다** (ADR-007 §폐기의 자리). 다크모드는 도그푸딩이
고른 것이 아니라 **사용자가 직접 지시했다** — 아래 §0-3. 남은 후보 셋:

| 후보 | 근거 |
|---|---|
| **9P(WSL) 감시** | `자동 갱신이 느려졌습니다 — 새로 고침(F5)` 을 사용자가 실제로 보고 거슬린다고 했다. 이론상의 미결이 아니라 매일 보이는 자국이다 (docs/PRD-v2.md §13 마지막) |
| **고정 탭 vs 즐겨찾기 vs 분할** | 겹치는 자리에 하나가 더 늘었다 (§17 §겹치는 자리 · §18). 3분할에 탭 셋씩이면 무엇이 안 쓰이는지 며칠이면 보인다 |
| **델타 패키지** | 갱신마다 74MB 다. 이번에 받는 데 8분 걸렸다 — 세 번째 값이고, 편차가 크다는 것이 확정됐다 |

**분할 단축키는 후보에서 빠졌다** — 일부러 안 붙인 결정(사용자 결정 2026-08-12)이지
"막힌 작업" 이 아니라서, 축으로 고를 대상이 아니다.

### 0-3. 다크모드 — 끝났고 나갔다 (v0.7.0). **열린 항목 둘은 사람이 판정한다**

**`.harness/ui-design-request.md` 가 "전부 제외" 로 못 박았던 유일한 항목이다** — 탭·분할과
같은 자리를, 도그푸딩이 아니라 **사용자 지시가 직접** 뒤집었다. 정본은
`docs/PRD-v2.md` §19 · `docs/ADR.md` ADR-020.

**들어온 것**: 테마 3택(시스템·라이트·다크, 기본 시스템) · OS 값 추종(`WM_SETTINGCHANGE`) ·
`ScrollBar`·`ContextMenu`·`MenuItem`·`CheckBox`·`RadioButton`·`ToolTip`·`Separator` 재도색 ·
설정 패널에 라디오 셋. 포트 16/16(`ISystemThemeSource`), 테스트 +38. 게이트 4종 ✅ ·
**실물 확인 여덟 항목 ✅** (§19 §실물 확인).

> ⚠ **게이트 4종을 통과한 뒤 실물에서 결함 셋이 나왔다** (전문은 §19). 이 구간에서 가장
> 값이 큰 기록이다:
> 1. **`AccessViolationException` 으로 창이 아예 안 떴다** — 훅이 모든 메시지에서 `lParam` 을
>    문자열로 읽었다. **이것은 §14 핸들러가 못 잡는다**(corrupted state exception) —
>    `error.log` 에도 안 남으므로 **이벤트 로그의 `.NET Runtime` 항목**을 봐야 했다.
> 2. **`JsonSettingsStore` 가 테마를 저장/복원하지 않았다** — 저장 형식이 Core 타입과 분리돼
>    자체 `Document` record 를 쓴다. **`AppSettings` 에 항목을 늘리면 그 record ·
>    `SaveAsync` · `Parse` 셋을 함께 고친다.** `FakeSettingsStore` 는 객체째로 들고 있어
>    계약 테스트가 통과한다 (§규칙 2 와 같은 계열).
> 3. **팔레트가 안 입혀졌다** — **XAML/BAML 로 로드된 브러시는 frozen 이다.** ADR-020 이
>    처음에 그 반대를 전제로 결정을 내렸고 실물이 뒤집었다. §14 가 예외를 삼켜 화면만
>    조용히 라이트로 남았다.
>
> **그리고 다크가 라이트 시절의 잠재 결함을 드러냈다**: `PaneList` 에 `Foreground` 가 없어
> 항목 텍스트가 WPF 시스템 기본색(검정)을 상속하고 있었다 — 라이트에서 우연히 맞아 v1
> 부터 몰랐다.

**배포까지 끝났다** — 다섯 커밋 → 0.7.0 → push → `pack.ps1 -SignThumbprint` → 릴리스
v0.7.0. **설치본이 자동 업데이트 알림으로 교체되고 다크로 뜨는 것까지 확인했다**
(`HISTORY.md` §v0.7.0 전문).

**열린 것 둘 — 한 번 밟아서 닫히는 종류가 아니다**:

- **OS 테마를 바꿔 시스템 추종을 밟지 않았다** (이 기계 OS 가 라이트다). 사용자 OS 설정을
  건드리는 확인이라 남겼다 — Windows 설정에서 라이트↔다크를 바꿔 앱이 **재시작 없이**
  따라오는지 보면 닫힌다. **`WM_SETTINGCHANGE` 경로가 실물에서 한 번도 안 밟힌 유일한
  자리다** (그 훅이 §규칙 16 의 크래시를 냈던 곳이라 특히 그렇다).
- **접근성 대비를 수치로 재지 않았다.** 눈으로는 읽힌다. §값을 치르고 배운 것(활성 탭
  배경색 대비가 6/255 였던 사건)과 같은 함정이 남아 있을 수 있다.
- 그리고 **매일 쓰며 판정할 것**: 다크에서 shell 아이콘·썸네일이 밝은 배경째로 오는 자리가
  거슬리는가 (OS 가 그리는 것이라 우리 제어 밖이다 — 탐색기도 같다).


### 1. 사람이 손으로 볼 것 — 매일 쓰며 판정한다

자동으로 닿는 데까지는 확인했다. 남은 것은 **조작감**이고, 매일 쓰면서 판정한다.

| 확인 | 왜 사람이 봐야 하나 |
|---|---|
| ~~**WSL 에서 깜박임이 멎었는가**~~ | **끝났다** (2026-08-11 · 설치본 0.4.1). 사용자가 눈으로 확인했다 — **완전히 멎었고 선택도 그대로**였다. 기계로도 같은 자리에서 140초(갱신주기 5회) 스크롤 변동 0 · 컨테이너 교체 0/30. 전문은 docs/PRD-v2.md §13 |
| 설정 패널의 생김새·간격 | 픽셀은 자동 채점되지 않는다 (CLAUDE.md §5) |
| 트리 따라가기의 **체감** | `C:\Users\SOOJANG` 처럼 형제가 90개인 폴더를 지날 때 화면이 튀지 않는가 |
| 느린 경로에서의 따라가기 | NAS·WSL 에서 단계마다 열거가 나간다. 지금은 로컬만 확인했다 |
| 감시 백오프 문구 | `자동 갱신이 느려졌습니다 — 새로 고침(F5)` 이 거슬리는 자리인가 |

### 2. 사람 손이 필요한 확인 (그대로 남아 있다)

| 확인 | 왜 아직 못 봤나 |
|---|---|
| 감시가 연결 끊김을 견디는가 | 어댑터 제어에 관리자 권한이 필요하다 |
| 피드에 못 닿을 때 조용한가 | 〃 (2026-08-07 세션에서 건너뛰기로 했다) |
| 느린/끊긴 서버에서의 조작감 | 이 NAS 는 185ms 로 빠르다. 없는 서버는 한 번에 42초다 |
| SmartScreen 경고 | **MOTW 를 붙여 재현을 시도했지만 안 떴다** — 다른 기계가 필요하다 |

### 3. 곁에서 드러났고 손대지 않은 것 — 하나가 닫혔다

**~~`DispatcherUnhandledException` 핸들러가 없다~~ — 2026-08-11 에 들어왔다.**
정본은 `docs/PRD-v2.md` §14. 1분에 5회까지 삼키고 넘으면 오늘처럼 죽으며, 삼킨 것은
`%APPDATA%\flex-dir\error.log` 에 스택째 남고, 알림 바가 예외 이름과 함께 한 번 말한다.
실물 확인 여섯 항목이 전부 닫혔다 (§14 §실물 확인). **v0.4.2 로 나갔다** (`HISTORY.md` §배포 기록) —
이 기계 설치본은 받아 둔 상태이고 '지금 설치' 를 누르면 그 위에서 돈다.

남은 둘은 그대로다.

- **9P(WSL) 경로에서 shell 아이콘/썸네일 조회가 항목당 250ms 다** (로컬 78ms) — 그리고
  결과는 `null` 이다. §13 폭주의 원인은 **아니었지만**(그것은 감시였다) 큰 폴더에서 아이콘이
  늦게 붙는다. 프로브로 실측한 값이고 손대지 않았다.
- **델타 패키지를 만들지 않는다.** 갱신마다 74MB 를 받는다. **걸리는 시간은 때에 따라
  크게 다르다** — 사내망에서 약 15분(2026-08-07) · 0.4.2·0.4.3 은 30초 안쪽(2026-08-11) ·
  **0.4.3→0.5.0 은 8분**(2026-08-12). 세 자리수가 나왔으므로 편차가 크다는 것이 확정이다.
  변하지 않는 것은 **받는 동안 화면에 아무 표시가 없어 "알림이 안 뜬다" 로 보인다**는 쪽이고,
  **진행을 `.partial` 파일 크기로 재려 하면 안 된다** (버퍼 때문에 점프한다 — `HISTORY.md` §v0.5.0 전문).
  `vpk pack` 전에 이전 릴리스 `.nupkg` 를 `artifacts/packages` 에 두면 델타가 켜지는데,
  지금은 같은 버전 재패킹을 위해 그 폴더를 매번 비운다 (`scripts/pack.ps1` 주석).
  **내려받을 필요는 없다** — 그 폴더를 비우는 시점에 직전 릴리스 `.nupkg` 가 이미 거기
  있다(지난 실행이 구웠다). 그래서 이 작업은 **어느 릴리스에서든 난이도가 같다**.

**다음 세션 후보 — 9P 감시(C)의 근거가 세졌다.** 2026-08-11 에 사용자가
`자동 갱신이 느려졌습니다 — 새로 고침(F5)` 을 **실제로 보고 거슬린다**고 했고, 같은 날
§14 확인 중 상태표시줄에 그것이 떠 있는 것을 읽었다
(`항목 182개 · 자동 갱신이 느려졌습니다 — 새로 고침(F5)` · `\\wsl.localhost\Ubuntu-26.04\etc`).
**이론상의 미결이 아니라 매일 보이는 자국이다.** 판정 방법은 `docs/PRD-v2.md` §13 마지막
항목에 있고, **못 밝히면 "밝히지 못했다" 로 닫고 시도한 것을 거기 적는 것까지가 결과다.**

**도그푸딩은 설치본으로 한다.** 시작 메뉴의 `flex-dir` 이 그것이고, 저장소 산출물을
잠그지 않아 **켜 둔 채로 게이트가 통과한다.** 다만 **저장소 Debug 빌드로 띄우면 잠근다** —
확인이 끝나면 반드시 죽인다.


## 옮겨 간 절 — `HISTORY.md`

**아래 이름으로 이 파일을 찾아온 사람을 위한 표다** (2026-08-12 에 갈랐다). 전문은 전부
[`HISTORY.md`](./HISTORY.md) 에 그대로 있고, 지운 것은 없다.

| 찾던 이름 | 지금 어디 | 한 줄 요약 |
|---|---|---|
| §시안 대조 | `HISTORY.md` §시안 대조 | **캡처의 바깥 8px 은 창이 아니다** — 리사이즈 테두리를 페인 가장자리로 읽어 "테두리가 없다" 고 오판했다. 클라이언트 원점은 `(8, 31)` |
| §최대화 여백 | `HISTORY.md` §최대화 여백 | ⛔ **그 절의 결론은 2026-08-12 에 뒤집혔다** — 실제로는 바깥 8px 이 화면 밖이고 캡션 줄이 24px 만 보였다. `Views/MaximizedFrame.cs` 가 덜어낸다 (`ad5b06b`). `PrintWindow` 로는 이 잘림을 볼 수 없다 (`manual-plan.md` §확인 도구) |
| §진입점을 하나만 둔 것이 틀렸다 | `HISTORY.md` §진입점을 하나만 둔 것이 틀렸다 | **만든 사람이 설명해야 찾을 수 있으면 잘못 고른 자리다.** 부모에 단 컨텍스트 메뉴가 자식 위에서 안 뜬 것도 여기 |
| §0.4.1~0.4.3 배포 | `HISTORY.md` §배포 기록 | 표로 접었다. **절차는 여기 위 §배포 절차 에 남겼다** |
| §B-1 / §B-3 / §B-4 표면 | `HISTORY.md` | v1 View 구간이 만든 표면과 배선 |
| §마감이 만든 표면 | `HISTORY.md` §마감이 만든 표면 | 아이콘·타이틀바·breadcrumb·여유 용량 |
| §다음 작업 0-0 (1구간 사람 확인 전문) | `HISTORY.md` §1구간 사람 확인 | 2026-08-24 에 옮겼다. 닫힌 항목 여덟과 그때의 경고들(`Setup.exe --silent` · 합성 클릭으로 못 밟는 우클릭 · `theme` 되돌리기). **열린 것은 라이트 테마 대비 하나**이고 그 한 줄은 여기 남겼다 |

**소스 주석이 거는 것은 옮기지 않았다** — `§규칙 1~15` · `§phase B` · `§현재 상태` ·
`§포트 현황` · `§열린 결정` 은 전부 이 파일에 그대로 있다. **규칙은 순번 리스트라 번호가
곧 API 다**: `ShellContextMenuProvider.cs`·`WpfUiDispatcherTests.cs` 등이 `§규칙 4`·`§규칙 5`
를 번호로 건다. **중간에서 하나를 지우면 그 뒤가 전부 밀리고 컴파일러는 못 잡는다.**

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
   `%APPDATA%\flex-dir\` 를 건드리면 게이트가 돌 때마다 사용자의 사용 기록과 폴더별 뷰
   설정이 테스트 실행으로 덮인다.
6. **`ARCHITECTURE.md` §7 이 금지하는 것은 재는 벤치마크 CLI 다.** 포트 구현체를 실물에 물려
   보는 확인용 프로브는 다르다 — ADR-015 가 선을 긋고 `.harness/probe/` 를 sln 밖에 둔다.
   계측은 앱 안(`Host/Diagnostics/PerformanceLog.cs`)에 있고 결과는 `perf.log` 로 나온다.
7. **`Program.cs` 는 TDD 가드의 검사 대상이 아니다.** 그래서 판단을 한 줄도 두지 않았다.
   조립·활성화·single instance 는 전부 채점되는 클래스 안에 있다 — 그 선을 넘기면
   채점되지 않는 자리에 로직이 자란다.
8. **아파트먼트와 동시성은 다른 문제다.** STA 를 잡았다고 끝난 것이 아니다 — 워커가 넷이면
   동시 호출이 그대로 남고 `SHGetFileInfo` 경로는 거기서 조용히 무너진다.
   전문은 `SHELL_NOTES.md` §COM 아파트먼트 함정 2 · §아이콘 함정 4 에 올렸다.
   **새 shell API 를 붙일 때 "STA 인가" 와 "동시에 불려도 되는가" 를 따로 묻는다.**
9. **네트워크는 오류 코드의 "언제" 를 바꾼다** (2026-08-07). 로컬과 같은 코드를 지나도
   SMB 는 삭제 중인 디렉터리를 짧게 `ACCESS_DENIED` 로 내고(실측 367ms→414ms 에 3 으로 바뀜)
   로컬 NTFS 에는 그 중간 상태가 없다. **분류가 옳은지는 "무엇이 오는가" 만으로 정해지지
   않는다.** `FileSystemFolderSource.ShouldRetryOpen` 이 그 창을 넘긴다.
10. **커맨드 안에서 던지는 것은 전부 프로세스를 죽인다** (2026-08-07 실물).
   `AsyncRelayCommand` 밖으로 나간 예외는 잡을 사람이 없다. 열거 경로(`FillAsync`)는
   "예외 종류로 가르지 않는다" 를 이미 지키고 있었지만 **조작 경로(`RunAsync`)는 주석만
   그렇게 적고 `LocationAccessException` 만 잡고 있었다.** 같은 교훈이 두 자리에 필요하면
   한 자리에만 적용돼 있을 수 있다 — **주석이 약속한 것을 코드가 지키는지 본다.**
11. **사용자가 친 문자열을 던지는 API 에 그대로 넣지 않는다.** `LocationId.Combine` 은
   던지고 `TryCombine` 은 사유를 낸다. 입력을 받는 자리는 실패가 **정상 상황**이라
   무엇이 잘못됐는지 말할 수 있어야 한다 (`TryParse` 와 같은 이유).
12. **사용자 데이터를 설치기가 관리하는 폴더에 두지 않는다** (2026-08-07). Velopack 은
   `%LOCALAPPDATA%\<packId>` 에 설치하는데 그것이 예전 상태 폴더와 같은 경로였고
   **첫 설치가 `usage.log` 를 지웠다.** 상태는 `%APPDATA%\flex-dir` 다.
   경로를 옮기면 **그것을 읽는 스크립트를 함께 고친다** — `check-dogfooding.ps1` 이
   조용히 빈 폴더를 보고 "판정 유보" 를 냈다. (그 스크립트는 이후 폐기됐다 — ADR-007
   §폐기. 교훈은 남는다: **데이터의 자리를 옮기는 변경은 그 데이터를 읽는 쪽까지가 범위다.**)
13. **합성 입력 전에 포그라운드를 확인한다** (2026-08-07). `SetForegroundWindow` 는
   다른 앱이 포그라운드면 **조용히 false 를 낸다.** 확인하지 않으면 클릭과 키가 남의
   창으로 가고(이 세션에서 Slack 으로 갔다) 화면 캡처는 `PrintWindow` 라 여전히 flex-dir
   을 보여줘서 **아무것도 안 되는 것처럼 보인다.** `AttachThreadInput` 으로 잡고,
   못 잡으면 **입력을 보내지 않는다.**

14. **실패를 캐시하는 정책은 조용한 버그를 영구화한다.** `ThumbnailRequestScheduler` 는
   실패도 시도로 세어 재요청을 막는다 (PRD §4, 옳다). 그래서 §8 의 경합에 한 번 지면
   그 페인의 아이콘이 **끝까지** 비어 있었다. 캐시하는 실패는 원인을 반드시 그 자리에서
   봐야 한다 — 화면만 보면 "아이콘 기능이 없다" 로 보인다.

15. **버블링 라우티드 이벤트를 컨테이너에서 받으면 그 아래 모든 것이 걸린다** (2026-08-07).
   `SplitterSync` 는 `Thumb.DragCompletedEvent` 를 **페인 Grid 에** 걸어 두고 "스플리터를
   끌었다" 로 읽었는데, **페인 안의 스크롤바 썸이 올린 것도 거기 닿는다** (ScrollBar 는 그
   이벤트를 삼키지 않는다 — 실물로 확인했다). `e.OriginalSource` 를 봐야 한다.
   **해로운 이유는 되쓰기와 클램프의 조합이다.** 그 핸들러는 실측 폭을 비율로 되쓰는데
   열에 `MinWidth="320"` 이 걸려 있어서, 좁은 창에서는 실측 폭이 **사용자가 고른 비율이
   아니라 벽에 막힌 폭**이다. 목록을 한 번 스크롤하면 그것이 정본이 되고 저장 파일까지 간다.
   > **일반화**: 값이 양방향으로 흐르는 자리에서는 **왕복이 고정점인가**가 불변식이다.
   > 여기서는 레이아웃 클램프(320px)와 VM 클램프(0.15~0.85)가 서로를 모르고, 되쓰기가
   > 그 차이를 사용자의 선택으로 둔갑시켰다. **두 클램프가 다른 단위로 살면 그 사이는
   > 언제나 새는 자리다.**
   이 기계는 **2560 과 1080(세로)** 을 오간다 — 1080 폭에서 도달 가능한 비율은 0.30~0.70
   뿐이라 레이아웃이 자르는 구간이 넓다.

16. **창 메시지 훅에서 `lParam` 을 조건 없이 마샬링하면 프로세스가 죽는다** (2026-08-12).
   `HwndSource.AddHook` 은 **모든** 메시지를 받고, 대상 메시지가 아닌 것의 `lParam` 은
   문자열 포인터가 아니다(좌표·핸들·구조체 포인터). `Marshal.PtrToStringUni` 로 읽으면
   보호된 메모리를 훑어 `AccessViolationException` 이 난다.
   **그리고 그것은 §14 의 `DispatcherUnhandledException` 이 못 잡는다** — corrupted state
   exception 이라 `error.log` 에도 안 남고 창이 아예 뜨지 않는다.
   → **메시지 종류를 먼저 가르고 그 다음에 읽는다.** 그 순서가 테스트에 드러나게 하려면
   이름을 문자열이 아니라 **지연 함수**로 받는다 (`SystemThemeWatcher.IsImmersiveColorChange`).
   진단은 **이벤트 로그의 `.NET Runtime` 항목**이다 (`error.log` 가 아니다).

17. **XAML/BAML 로 로드된 `Freezable` 은 frozen 이다** (2026-08-12). BAML 로더가 모든 속성이
   정적 값인 것을 freeze 하므로 `<SolidColorBrush x:Key="..."/>` 의 `Color` 에 쓰면
   `InvalidOperationException` 이다. **코드로 만든 브러시는 frozen 이 아니라서, 그것으로
   seed 한 테스트는 실물과 다른 조건을 채점한다** — ADR-020 이 이 전제 위에 결정을 세웠고
   게이트 4종을 통과한 뒤 실물에서 뒤집혔다. 바꾸려면 **리소스 항목을 교체하고 참조를
   `DynamicResource` 로** 둔다.
   > **일반화**: **"프레임워크가 이렇게 동작할 것이다" 를 근거로 결정을 내렸으면, 실물에서
   > 그 전제를 확인하기 전까지 그 결정은 잠정이다.**

18. **`AppSettings` 에 항목을 늘리면 `JsonSettingsStore` 의 세 곳을 함께 고친다** (2026-08-12) —
   `Document` record · `SaveAsync` · `Parse`. 저장 형식이 Core 타입과 **의도적으로** 분리돼
   있어서(깨진 값 하나가 설정 전체를 막지 않게) `AppSettings` 만 늘리면 **화면에서는 골라지고
   저장도 복원도 되지 않는다.** `FakeSettingsStore` 는 `AppSettings` 를 객체째로 들고 있어
   Core 의 계약 테스트가 그대로 통과한다 — **새 설정의 라운드트립은
   `JsonSettingsStoreTests` 에서 본다** (§규칙 2 와 같은 계열).

19. **선택 인자(`= null`)로 주입하는 포트는 조립을 빠뜨려도 아무도 안 잡는다** (2026-08-24).
   `PaneViewModel`·`SettingsViewModel` 이 `IExternalToolCatalog`·`IExternalToolLauncher` 를
   기본값 `null` 로 받는다 — 기존 테스트를 한 줄도 안 고치려고 그렇게 했고 그 값은 실제로
   받았다. 대신 **`AppComposition` 이 넘기는 것을 잊어도 컴파일 오류가 아니고**, 결과는
   예외가 아니라 **버튼이 회색인 채로 남는 것**이다. 게이트 넷이 전부 초록인 채로
   실제로 그렇게 나갔다: ViewModel 테스트는 포트를 fake 로 직접 넣어 만들고,
   그때의 `FlexDir.Host.Tests` 94개는 조립의 *정리 순서*만 봤다 —
   **"실제로 주입됐는가" 를 보는 테스트가 없었다.**
   **잡은 것은 게이트가 아니다** — step 8 이 툴바를 실물 캡처했을 때 버튼 둘이 회색으로
   떴고, step 9 가 배포 직전에 `blocked` 로 세웠다. **이제 막는 것은**
   `AppCompositionTests.Create_GivesThePaneWorkingExternalTools` 다 (커밋 `ecbb175`) —
   프리셋을 명령 프롬프트로 두고 `CanOpenInTerminal` 을 본다. `cmd.exe` 는 모든 Windows 에
   있어 기계마다 답이 갈리지 않는다.
   > **규칙**: 포트를 선택 인자로 열면 **조립 쪽에 그것을 보는 테스트를 함께 만든다.**
   > 그러지 않을 거면 **필수 인자로 받아 컴파일러에게 맡긴다** — 둘 중 하나여야 하고,
   > "기존 테스트를 안 고치려고" 는 앞의 것을 건너뛸 사유가 되지 못한다.
   > **이 규칙은 닫힌 결함의 기록이 아니라 다음 포트에 거는 조건이다** — 이번 것은
   > 실물 캡처가 우연히 잡았고, 캡처가 없는 자리였으면 배포까지 나갔다.

20. **WPF 기본 템플릿이 색을 상수로 박아 둔 컨트롤이 있다** (2026-08-24). `ComboBox` 는
   기본 템플릿이 배경을 `ComboBox.Static.Background`(`#FFF0F0F0`)로 그린다 — 시스템 색이
   아니라 **템플릿 안의 상수**라, `Background` 를 줘도 **설정만 되고 그려지지는 않는다.**
   다크에서 흰 상자로 떴고 실물 캡처로만 잡혔다. 이 앱은 테마를 브러시의 `Color` 로 푸는데
   (ADR-020) 그 방식은 **브러시를 참조하는 템플릿에만 닿는다.**
   > **규칙**: 새 컨트롤을 다크에서 한 번은 실물로 본다. `Background` 가 안 먹으면
   > 색을 더 주지 말고 **템플릿째 간다** — `x:Key` 를 달아 그 자리에만 붙이고
   > 암시적 스타일로 만들지 않는다 (`SettingsCombo`·`SettingsComboItem` 이 그 본이다).
   > 곁의 함정 하나: **템플릿을 갈면 `DisplayMemberPath` 가 닫힌 상자에서 끊긴다** —
   > `ContentPresenter` 에 `ContentTemplateSelector={TemplateBinding ItemTemplateSelector}`
   > 가 있어야 닿는다. 목록은 멀쩡한데 닫힌 상자만 `ToString()` 이 뜬다.


## 구현체가 다음 phase 에 넘긴 사실

**phase B(View)가 틀리기 쉬운 것**

- **썸네일·아이콘은 premultiplied BGRA 다.** `PixelFormats.Bgra32` 가 아니라 **`Pbgra32`** 로
  `WriteableBitmap` 을 만든다. 틀리면 반투명 가장자리가 어둡게 번진다.
  (B-3 에서 `ThumbnailImageConverter` 가 그렇게 만들었고 실물로 확인했다.)
- 96 요청이 `SHIL_JUMBO` 의 **256×256(256KB)** 을 낸다. 축소는 View 가 하고 확장자 캐시는
  비우지 않으므로 상주 프로세스에서 쌓인다.
- **`SHGetFileInfo` 는 동시에 부르면 조용히 실패한다** (B-3 에서 실물로 잡았다). 예외도
  오류 코드도 없이 0 을 내고, 그것을 막으면 뒤의 시스템 이미지 리스트가 던진다.
  `Interop/ShellInfoGate` 가 형식 아이콘 경로 전체를 프로세스 단위로 직렬화한다 —
  `ShellTypeNameProvider` 도 같은 API 라 함께 지난다. **`StaWorkQueue` 로는 안 풀린다**:
  아파트먼트를 보장할 뿐 워커가 넷이라 동시성이 그대로 남는다.
  실패가 호출자 캐시에 남아 재시도되지 않으므로 (PRD §4) 한 번 지면 끝까지 빈칸이었다.
- **View 의 중복 제거 캐시와 ViewModel 의 `Reset()` 은 서로를 모른다** (B-3 에서 실물로
  잡았다). `VisibleRangeSync` 는 "같은 목록이면 안 민다" 이고 `thumbnails.Reset()` 은
  "지금 보이는 것을 잊는다" 인데, 항목 인스턴스가 그대로면 (`MergeItems`) 다시 밀 신호가
  없어 그림이 영영 오지 않는다. 그래서 `Reset()` 은 **폴더가 실제로 바뀔 때만** 부른다.
- **`SetOwnerWindow` 는 이제 부른다** (B-4). Shell 계층은 여전히 창을 모른다 — Host 가
  공급자를 물려 준다 (위 §포트 현황). shell 대화상자가 flex-dir 창을 부모로 한다.
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

- [x] **`IContextMenuProvider` — 끝났다** (B-4). 포트에 창 핸들을 두지 않고 Host 가 쥔다.
      메뉴 루프는 STA 워커 + 자체 숨은 창 (`SHELL_NOTES.md` §컨텍스트 메뉴). 오너드로
      항목이 그려지는 것과 메뉴 밖 클릭으로 닫히는 것까지 실물 확인됐다.
- [x] **완전 종료 = 트레이 아이콘 — 끝났고 실물 확인됐다** (사용자 결정 2026-08-06).
      알림 영역 아이콘 우클릭 → "완전 종료" 가 유일한 `Application.Shutdown()` 경로다
      (`Startup/TrayMenu` + Program 의 WinForms `NotifyIcon` — WPF 에는 알림 영역 API 가
      없다). 주의: `ResidentWindow` 가 `Closing` 을 취소하므로 종료는 반드시 `Shutdown()`
      경로여야 한다 — **`Shutdown()` 이 그 취소를 무시하는 것까지 실물로 봤다**
      (manual-plan §B-2, 2026-08-06). 아이콘도 제품 아이콘으로 바뀌었다
      (`Startup/ProductIcon`) — 마감의 smoke 테스트에서 오버플로를 펼쳐 확인했다.
- [ ] **클라우드 자리표시자 확인 불가** — 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·
      `RECALL_ON_OPEN` 속성을 가진 항목이 0개다. `SHELL_NOTES.md` §열거 함정 3 의 핵심이고
      틀리면 스크롤만으로 수 GB 를 내려받는다. 동기 중인 OneDrive 가 있는 기계가 필요하다.
- [ ] **대화상자가 뜨는 실패 경로 — 조작 쪽은 밟았다** (2026-08-07). UNC 에서 이름 충돌
      ("파일 바꾸기 또는 건너뛰기")과 영구 삭제 확인이 **flex-dir 을 부모로** 뜨는 것을
      실물로 봤다. 남은 것은 **활성화 실패**(연결 프로그램 없음·취소)다.
      주의: 이런 대화상자는 별도 최상위 창이라 프로세스의 `MainWindowHandle` 이 그쪽으로
      옮겨간다. 캡처·입력은 `EnumWindows` 로 찾아 핸들을 직접 잡는다.
- [ ] **스플리터 비율·창 크기가 저절로 바뀐다 — 원인 하나를 고쳤지만 닫지 않았다**
      (2026-08-07). 조작 중에 좌 페인이 330px→595px→890px 로 여러 번 변한 관찰이다.

      **고친 것**: 스크롤바 드래그가 비율을 되쓰던 것 (§규칙 15 · 커밋 `6617eb8`).
      좁은 창에서 `MinWidth` 에 잘린 폭이 사용자의 선택을 덮었고, 이 기계는 2560 과
      1080 을 오간다. 관찰된 330px 이 `MinWidth` 320 + 페인 안쪽 여백과 맞는다.

      **그래도 열어 둔 이유**: 원래 관찰을 재현하지 못했다. 아닌 것으로 밝혀진 둘 —
      닫기(=숨기기)→다시 열기 왕복은 **일반 창에서도 최대화에서도 고정점이었다**
      (각 6회, `rect`·`normalPosition`·DPI 불변). `ResidentWindow` 의
      `Closing`→`WindowPlacement`→`Apply` 되먹임은 그 두 경로에서 표류하지 않는다.
      → **다음에 또 보이면** 그때의 창 폭과 모니터를 함께 적는다. 재는 법은
      `manual-plan.md` §확인 도구 에 있다 (UIA 로 페인 폭을 직접 읽는다).
- [x] **Host 테스트의 간헐 실패 — 원인이 잡혔고 고쳤다** (2026-08-07).
      이름은 `FirstItemMeterTests.EveryFolderChange_IsMeasured`,
      증상은 `perf.log` 읽기의 `IOException`(공유 위반)이었다.
      **테스트 결함이 아니라 제품 결함이다.**

      `FirstItemMeter` 가 측정할 때마다 `recording` 을 **덮어썼다** — 이어 붙이지 않았다.
      그래서 `Recording` 은 마지막 하나만 가리키고, 앞선 기록은 아직 파일을 쥔 채로 남는다.
      `PerformanceLog.RecordAsync` 는 실패를 삼키므로(계측 때문에 앱이 죽으면 안 된다)
      **두 기록이 겹치면 줄이 조용히 사라진다** — §규칙 9 가 경고한 그 패턴이고, 나중에
      수치를 보는 사람은 "그때 안 쟀나 보다" 로 읽는다. 종료 경로도 `Recording` 을
      기다리므로 앞선 기록이 잘린다.

      고친 것 둘: **파일의 주인이 자기 쓰기를 직렬화한다**
      (`PerformanceLog.writeGate` — `JsonViewStateStore` 와 같은 수), 그리고
      **`FirstItemMeter` 가 기록을 이어 붙인다**(`RecordAfterAsync` — 걸린 시간과 시각은
      이벤트 시점의 값을 넘긴다, 앞선 기록을 기다린 뒤 재면 대기 시간이 수치에 섞인다).
      Host 테스트 **12회 연속 초록**으로 확인했다.

      > 앞선 세션이 이 자리를 `DisposeAsync_…StaThread` 로 의심했던 것은 **틀린 짐작이었다.**
      > 그때 "재현되지 않았으므로 원인이라 적지 않는다" 고 유보해 둔 것이 맞았다.

## 순서

```
A. Shell interop   포트 14/14 ✅  (v1 열하나 + 트리·즐겨찾기가 요구한 셋)
C. Host 뼈대        ✅  진입점 + DI + single instance + IUsageLog + 계측, 화면 없이 조립까지
B. View            ✅  B-1·B-2·B-3·B-4 완료 — v1 기능이 전부 들어왔다
마감               ✅  제품 아이콘 · 커스텀 타이틀바 · breadcrumb · 여유 용량 · 게이트 스크립트
→ 매일 쓰기 → 걸리는 것을 GitHub 이슈로       도그푸딩 게이트는 폐기 (ADR-007 §폐기)
v1.1               ✅  Details 그룹화 (ADR-017)
v2 네트워크         ✅  N-1 UNC · N-2 열거/감시/조작 · N-3 공유 목록 · N-4 자격증명 안내
                      **진입 조건을 게이트 PASS 로 잡았던 것은 폐기됐다** (docs/PRD-v2.md §5)
                      사람 손이 필요한 확인 둘만 남았다 (manual-plan §v2 네트워크)
배포                ✅  인스톨러 + 자동 업데이트 (Velopack · GitHub Releases · docs/PRD-v2.md §9)
                      저장소를 public 으로 전환했다. 릴리스는 HISTORY.md §배포 기록
도그푸딩이 낸 것    ✅  2026-08-10 — 전부 쓰다가 걸린 것에서 나왔다
                      폴더 트리 (§10) · 네트워크 위치 (§10-1) · 즐겨찾기 (§10-2) ·
                      Details 컬럼 폭 (§11) · type-ahead 연타 · 창 닫을 때 상태 저장
설정 창             ✅  정보 · 시작 폴더 · 숨김 파일 (§12 · v0.4.0)
도그푸딩이 낸 결함  ✅  §13 감시 폭주 · §14 UI 스레드 예외 · §15 마우스 보조 버튼 ·
                      §16 활성화 채널 — 전부 배포까지 닫혔다
탭                 ✅  §17 — 소유 구조 · 저장 포맷 · 탭 줄 화면 · 드래그.
                      사람 확인 아홉 닫힘, v0.5.0 (2026-08-12)
분할               ✅  §18 — 1~4 프리셋 · 기본 1분할 · 접기 · MRU 대상 (ADR-019).
                      게이트 4종 ✅ · 기계 확인 다섯 ✅ · 사람 확인 일곱 ✅ ·
                      **v0.6.0 배포 ✅** (2026-08-12). 열린 항목 없다
창 결함 둘         ✅  최대화 8px 잘림 · 최대화 복원이 주 모니터로 가던 것 ·
                      트리 가로 스크롤. **v0.6.1 배포 ✅** (2026-08-12).
                      사용자가 실물로 확인했다. 열린 항목 없다
다크모드           ✅  §19 · ADR-020 — 테마 3택·OS 추종·기본 크롬 재도색. 포트 16/16.
                      게이트 4종 ✅ · 실물 확인 여덟 ✅ ·
                      **v0.7.0 배포 ✅** (2026-08-13 · 첫 서명본 · 알림 경로로 갔다).
                      게이트 통과 뒤 실물에서 결함 셋이 나왔고 전부 고쳤다 (§규칙 16·17·18).
                      남은 둘은 매일 쓰며 판정한다 — 위 §다음 작업 0-3
→ 다음 축           ⬜  도그푸딩이 고른다. 후보 셋은 위 §다음 작업 0-2
```

