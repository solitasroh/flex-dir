# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> **여기에는 살아 있는 것만 둔다** (2026-08-12 에 갈랐다). 얼어붙은 기록 — 배포 기록 ·
> 지난 구간이 만든 표면 · 시안 대조 · 최대화 여백 — 은 [`HISTORY.md`](./HISTORY.md) 에 있다.
> 이 파일이 1,094줄까지 자라 **살아 있는 것이 역사에 묻혔던 것**이 이유다.
>
> 갱신: 2026-09-22. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) — 수동 phase 계획과
> 사람 확인 항목(프로브·실물 실측 결과가 거기 있다). 세션 커밋 이력은 `git log` 를 본다.

## 한 줄

**v1 이 나갔고, 그 뒤로는 도그푸딩이 무엇을 만들지 정하고 있다** (ADR-007 §폐기가 노린
자리다). 쓰다가 걸린 것이 그대로 작업이 됐다 — 폴더 트리 · 네트워크 위치 · 즐겨찾기 ·
컬럼 폭 · 설정 창 · 마우스 보조 버튼 · 탭 · 분할 · 툴바 오버플로 · 외부 도구가 전부
그렇게 들어왔다. **지금은 v0.9.2 이고 열린 구간은 없다.**

**버전별 서술은 이 파일에 두지 않는다** — `HISTORY.md` §배포 기록(v0.1.0~v0.9.2)이
정본이다. 여기는 *지금 무엇이 참인가* 와 *다음에 무엇을 하는가* 만 적는다.

> **값을 치른 자국 셋** (전문은 `docs/PRD-v2.md` §17 §값을 치르고 배운 것):
> WPF 바인딩은 런타임 조회라 **타입을 바꾸면 템플릿 안 바인딩이 컴파일 에러 없이 조용히
> 죽는다** · `Command` 가 `null` 인 `MenuItem` 은 **정상으로 뜨고 눌리기까지 한다**(그래서
> 메뉴는 실제로 눌러 봐야 한다) · **근거가 무너진 스펙 값은 조용히 남는다**
> (활성 탭 배경색이 그랬고, 대비가 6/255 였다).

**"안 켜진다" 는 먼저 `perf.log` 를 본다** — `WindowShown` 이 찍히면 Show 는 됐고 좌표가
문제다; `ColdStart` 와 `WindowShown` 의 시각 차가 복원 대기다 (v0.9.2 가 그 자리였다).

**진단 도구**: `dotnet-stack report -p <pid>` 가 UI 스레드의 관리 스택을 준다. `Get-Process`
의 스레드별 `TotalProcessorTime` 으로 "누가 태우나" 를 먼저 좁히면 빠르다. 받기·네트워크가
진행 중인지는 `Get-NetTCPConnection -OwningProcess <pid>` 로 본다 — 파일 크기는 버퍼 때문에
점프한다. COM interop 이 죽으면 **이벤트 로그의 `.NET Runtime` 항목**이 관리 스택을 준다
(`error.log` 에 안 남는 corrupted state exception 이 있다 — §규칙 16).

## 현재 상태

```
브랜치   **main 에 v0.9.2 커밋 셋이 직접 올라갔다** (2026-09-16).
                 태그 v0.9.2는 `4776f20`(0.9.2 로 올린다)을 가리킨다
작업트리 ✅ 화면 밖 창 복구 · 배치 복원 직후 창 표시가 들어갔다
버전     0.9.2   ✅ **v0.9.2 공개 릴리스 완료** (2026-09-16).
                 서명 자산 6개를 GitHub에 올렸고 이 기계 설치본은
                 `0.9.2+4776f20…`이다. 창 1.2초 표시와 새 오류 없음 확인.
                 그 앞은 0.9.1(마우스 선택·새 탭 설정) · 0.9.0(선택·트리 단순화) ·
                 0.8.5(외부 도구) · 0.7.0(다크모드) · 0.6.1(창 결함 둘) · 0.6.0(분할).
                 ⚠ `Get-AuthenticodeSignature` 는 `UnknownError` 를 내는데, **서명은 붙어
                 있고 이 기계의 신뢰 루트에 자체 서명 인증서가 없어서**다 — 결함이 아니다
                 (`HISTORY.md` §v0.7.0)
테스트   2278 통과   Core 654 · Shell 394 · App 1134 · Host 96
게이트   fast (build -warnaserror · test --blame-hang · check-structure) ✅
         full (Release build -warnaserror) ✅ **2026-09-16 · v0.9.2 배포 직전 HEAD에서 넷 다
         다시 돌렸다. 테스트 2278 · 실패 0 · 경고 0.**
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

**버전은 배포 직전에 한 번만 올린다** (사용자 결정 2026-09-07). 기능 작업 도중 중간 번호를
선점하지 않고, 배포할 변경이 모두 확정된 뒤 사용자가 정한 최종 patch 버전을
`Directory.Build.props`에 반영한다.

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

## 포트 현황 — 20/20

**전체 표는 `docs/ARCHITECTURE.md` §2 가 정본이다.** 여기에는 *왜 따로 두었는가* 만 남긴다.

v1 이 **열하나**로 나갔고(`IFolderSource` · `IThumbnailSource` · `IContextMenuProvider` ·
`IClipboardBridge` · `IFileOperations` · `IFolderWatcher` · `IViewStateStore` · `IUsageLog` ·
`IItemActivator` · `ITypeNameProvider` · `IDriveSpace`), 그 뒤로 아홉이 늘었다 —
2026-08-07 자동 업데이트 하나 · 2026-08-10 트리·즐겨찾기·설정이 넷 · 2026-08-12 다크모드 하나 ·
2026-08-21 알려진 폴더 하나 · 2026-08-24 외부 도구 둘.

**스물 전부 `AppComposition.Create` 가 조립한다.** 되돌리면
`AppCompositionTests.Create_GivesThePaneWorkingExternalTools` 가 실패한다.

| 포트 | 구현 | 왜 따로인가 |
|---|---|---|
| `IUpdateSource` | `Shell/Updates/VelopackUpdateSource.cs` | 새 버전 확인·내려받기·교체 후 재시작. **받는 것을 미리 끝내고 설치 시점만 사용자가 고른다** (docs/PRD-v2.md §9) |
| `ISettingsStore` | `Shell/Settings/JsonSettingsStore.cs` | 설정은 캐시가 아니라 **사용자의 의도**다 — 뷰 상태가 깨져 기본값으로 접히는 사건이 "숨김 파일을 보겠다" 를 함께 뒤집으면 안 된다. 파일도 따로 쓴다 (`settings.json`). ⚠ **`AppSettings` 에 항목을 늘리면 `Document` record · `SaveAsync` · `Parse` 셋을 함께 고친다** — `FakeSettingsStore` 는 객체째로 들고 있어 계약 테스트가 통과한다 (§규칙 2 계열) |
| `IDriveList` | `Shell/Storage/SystemDriveList.cs` | 드라이브 목록. COM 이 아니라 `DriveInfo`+`mpr.dll` 이라 STA 도 정리도 없다 |
| `INetworkPlaceList` | `Shell/Storage/ShellNetworkPlaceList.cs` | '네트워크 위치 추가' 는 드라이브 문자를 만들지 않아 `WNetGetConnection` 에 안 잡힌다. **COM 이라 STA 를 들고 정리 목록에 들어간다** |
| `IFavoriteStore` | `Shell/Favorites/JsonFavoriteStore.cs` | 즐겨찾기는 캐시가 아니라 사용자 데이터다 — 뷰 상태와 **다른 파일**이어야 한다 |
| `ISystemThemeSource` | `Shell/Settings/RegistrySystemThemeSource.cs` | OS 가 라이트/다크인가. 레지스트리 값 하나라 COM 도 STA 도 필요 없지만, 다른 시스템 조회 포트와 같은 이유로 비동기다 (ADR-020) |
| `IKnownFolderList` | `Shell/Locations/KnownFolderList.cs` | 알려진 폴더 다섯의 위치. **조회가 저장소에 닿는다** — 리디렉션된 폴더(OneDrive·도메인 로밍)에서는 네트워크로 내려간다. COM 이 아니라 정리 목록에 없다 |
| `IExternalToolCatalog` | `Shell/Tools/AppPathsToolCatalog.cs` | 설치된 외부 도구를 찾는다. **실패는 `null`, 취소만 예외.** **캐시하지 않는다** — 상주 앱이라 창이 다시 보일 때마다 다시 묻는다 (ADR-022) |
| `IExternalToolLauncher` | `Shell/Tools/ProcessToolLauncher.cs` | 외부 도구를 그 폴더를 작업 디렉터리로 띄운다. **실패는 `LocationErrorKind`, 던지지 않는다** — 실패가 정상 상황이라 호출자가 상태표시줄 한 줄로 만든다. 탐지와 가른 이유가 이 칸의 차이다 (ADR-022) |

`IDriveSpace` 는 v1 마감에서 늘었다 (여유 용량). 이름이 `Shell*` 이 아닌 이유는 COM 이
아니어서다 — `DriveInfo` 는 `GetDiskFreeSpaceEx` 로 내려가므로 STA 도 정리도 필요 없다.
**그래도 UI 스레드에서는 못 부른다** (네트워크·이동식 볼륨에서 초 단위 블로킹 —
CLAUDE.md §3). 아파트먼트와 블로킹은 다른 문제다 (§규칙 8).

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


## 다음 작업

**v0.9.2 까지 모든 구간이 닫혔다.** 구간별 경과·배포 기록·닫힌 사람 확인은
`HISTORY.md` §배포 기록 에 있다. 아래는 **지금 열려 있는 것만**이다.

### 다음 축 — 도그푸딩이 고른다 (ADR-007 §폐기의 자리)

| 후보 | 근거 |
|---|---|
| **9P(WSL) 감시** | `자동 갱신이 느려졌습니다 — 새로 고침(F5)` 을 사용자가 실제로 보고 거슬린다고 했다. **이론상의 미결이 아니라 매일 보이는 자국이다.** 판정 방법은 `docs/PRD-v2.md` §13 마지막 항목에 있고, **못 밝히면 "밝히지 못했다" 로 닫고 시도한 것을 적는 것까지가 결과다** |
| **고정 탭 vs 즐겨찾기 vs 분할** | 겹치는 자리에 하나가 더 늘었다 (§17 §겹치는 자리 · §18). 3분할에 탭 셋씩이면 무엇이 안 쓰이는지 며칠이면 보인다 |
| **델타 패키지** | 갱신마다 74MB 다. `vpk pack` 전에 이전 릴리스 `.nupkg` 를 `artifacts/packages` 에 두면 켜지는데, 지금은 같은 버전 재패킹을 위해 그 폴더를 매번 비운다. **내려받을 필요는 없다** — 비우는 시점에 직전 릴리스 `.nupkg` 가 이미 거기 있다. 그래서 **어느 릴리스에서든 난이도가 같다** |

**분할 단축키는 후보가 아니다** — 일부러 안 붙인 결정(사용자 결정 2026-08-12)이지
"막힌 작업" 이 아니다.

### 사람이 판정할 것 — 매일 쓰며 본다

- **고정 탭이 실제로 쓰이는가** (즐겨찾기·분할과 겹치는 자리 · §17 §겹치는 자리).
- **1080 세로에서의 4분할** · 분할 단축키 자리 (§18 §며칠 써야 보이는 것).
- **다크에서 shell 아이콘·썸네일이 밝은 배경째로 오는 것**이 거슬리는가 — OS 가 그리는
  것이라 우리 제어 밖이다 (탐색기도 같다).
- 설정 패널의 생김새·간격 — 픽셀은 자동 채점되지 않는다 (CLAUDE.md §5).
- 감시 백오프 문구가 거슬리는 자리인가.

### 사람 손이 필요한 확인 — 아직 못 밟았다

| 확인 | 왜 아직 못 봤나 |
|---|---|
| **OS 테마를 바꿔 시스템 추종이 도는가** | 사용자 OS 설정을 건드리는 확인이라 남겼다. **`WM_SETTINGCHANGE` 경로가 실물에서 한 번도 안 밟힌 유일한 자리다** — 그 훅이 §규칙 16 의 크래시를 냈던 곳이라 특히 그렇다 |
| **최대화에서 툴바가 접히는가** | 확인한 최대화는 넓은 모니터라 페인이 약 1044 였고 아무것도 안 접혔다. **§21 의 420 은 여전히 계산값이다** |
| 접근성 대비를 수치로 재기 | 눈으로는 읽힌다. 활성 탭 배경 대비가 6/255 였던 사건과 같은 함정이 남아 있을 수 있다 |
| 감시가 연결 끊김을 견디는가 | 어댑터 제어에 관리자 권한이 필요하다 |
| 피드에 못 닿을 때 조용한가 | 〃 |
| 느린/끊긴 서버에서의 조작감 | 이 NAS 는 185ms 로 빠르다. 없는 서버는 한 번에 42초다 |
| SmartScreen 경고 | **MOTW 를 붙여 재현을 시도했지만 안 떴다** — 다른 기계가 필요하다 |
| 클라우드 자리표시자 | 이 기계에 `OFFLINE`·`RECALL_ON_DATA_ACCESS`·`RECALL_ON_OPEN` 항목이 0개다. `SHELL_NOTES.md` §열거 함정 3 의 핵심이고 틀리면 스크롤만으로 수 GB 를 내려받는다 |
| 활성화 실패 대화상자 | 조작 쪽(이름 충돌·영구 삭제 확인)은 밟았다. 남은 것은 연결 프로그램 없음·취소다. **이런 대화상자는 별도 최상위 창이라 `MainWindowHandle` 이 그쪽으로 옮겨간다** — `EnumWindows` 로 핸들을 직접 잡는다 |

### 손대지 않고 남겨 둔 것

- **9P(WSL) 경로에서 shell 아이콘/썸네일 조회가 항목당 250ms 다** (로컬 78ms) — 그리고
  결과는 `null` 이다. §13 폭주의 원인은 **아니었다**(그것은 감시였다). 프로브 실측값이다.
- **`[실행해 보기]` 의 문구가 어색하다** — 라벨이 *"사용자 지정"* 이라 조사가 붙으면
  읽히지 않는다 (`사용자 지정 을(를) 열었습니다`). 프리셋 다섯은 자연스럽다. 관측만 했다.
- **스플리터 비율·창 크기가 저절로 바뀐다** — 원인 하나(스크롤바 드래그가 비율을 되쓰던
  것 · §규칙 15 · `6617eb8`)를 고쳤지만 닫지 않았다.
- **개인 키 백업(`.pfx`)이 없다.** 잃으면 배포한 신뢰가 새 인증서와 안 맞아 사내 기계를
  전부 다시 돌아야 한다. `signing/` 은 `.gitignore` 가 막는다. 이 기계의 인증서는
  `CN=Rootech, O=Rootech, C=KR` · 만료 2031-08-12 (지문은 저장소에 적지 않는다).
- ⚠ **자체 서명으로 SmartScreen 이 없어지지는 않는다.** 사라지는 것은 '알 수 없는 게시자'
  표기다. 없애려면 사내 공유·Intune 배포로 바꾸거나 공개 CA 로 간다.

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
| §다음 작업 0-0 (1구간 사람 확인 전문) | `HISTORY.md` §1구간 사람 확인 | 2026-08-24 에 옮겼다. 닫힌 항목 여덟과 그때의 경고들(`Setup.exe --silent` · 합성 클릭으로 못 밟는 우클릭 · `theme` 되돌리기). 열려 있던 라이트 테마 대비도 2026-08-24 에 닫혔다 |
| §다음 작업 **0 · 0-1 · 0-2 · 0-3** (구간별 번호) | 위 §다음 작업 | 2026-09-22 에 번호를 없앴다. 구간이 전부 닫혀 **열린 것만 남기니 번호를 붙일 구간이 없다.** 구간별 경과는 `HISTORY.md` §배포 기록, 아직 사람이 봐야 하는 것은 §다음 작업 §사람 손이 필요한 확인 |

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

**열린 항목은 위 §다음 작업 에 모았다.** 이 절에는 *닫히면서 제약을 남긴 것* 만 둔다 —
되돌리면 다시 깨지는 자리다.

- **종료는 반드시 `Application.Shutdown()` 경로여야 한다.** `ResidentWindow` 가 `Closing`
  을 취소하므로 다른 길은 없다. 알림 영역 아이콘 우클릭 → "완전 종료" 가 그 유일한
  경로다 (`Startup/TrayMenu` + Program 의 WinForms `NotifyIcon` — WPF 에는 알림 영역 API
  가 없다). **`Shutdown()` 이 그 취소를 무시하는 것까지 실물로 봤다.**
- **`IContextMenuProvider` 는 포트에 창 핸들을 두지 않는다** — Host 가 쥔다. 메뉴 루프는
  STA 워커 + 자체 숨은 창 (`SHELL_NOTES.md` §컨텍스트 메뉴).
- **`perf.log` 는 파일의 주인이 자기 쓰기를 직렬화한다** (`PerformanceLog.writeGate` —
  `JsonViewStateStore` 와 같은 수). `FirstItemMeter` 는 기록을 **덮어쓰지 않고 이어 붙인다**
  (`RecordAfterAsync`). 덮어쓰면 두 기록이 겹쳐 **줄이 조용히 사라지고**
  (`RecordAsync` 는 실패를 삼킨다 — §규칙 9), 나중에 수치를 보는 사람은 "그때 안 쟀나
  보다" 로 읽는다. 걸린 시간과 시각은 **이벤트 시점의 값**을 넘긴다 — 앞선 기록을 기다린
  뒤 재면 대기 시간이 수치에 섞인다.

## 순서

**v1 · v1.1 · v2 네트워크 · 배포 · 도그푸딩이 낸 것 · 설정 창 · 도그푸딩이 낸 결함 넷 ·
탭 · 분할 · 창 결함 · 다크모드 · 툴바 오버플로 · 외부 도구 · 선택/트리 단순화 ·
마우스 선택 · 창 표시 결함 — 전부 닫혔고 배포까지 갔다.** 구간별 경과와 릴리스는
`HISTORY.md` §배포 기록 (v0.1.0~v0.9.2) 이 정본이다.

```
→ 다음 축   ⬜  도그푸딩이 고른다. 후보 셋은 위 §다음 작업
```

**도그푸딩 게이트는 폐기됐다** (ADR-007 §폐기 · 사용자 결정 2026-08-07). 진입 조건을
게이트 PASS 로 잡았던 v2 네트워크의 방식도 함께 폐기됐다 (`docs/PRD-v2.md` §5).
