# HANDOFF — 다음 세션에서 이어갈 것

> **이 파일은 `docs/` 아래 두지 않는다.** `docs/*.md` 중 셋은 harness `guardrails` 로 매 step
> 프롬프트에 주입되므로, 세션 인수인계가 거기 들어가면 모든 구현 세션을 오염시킨다.
>
> 갱신: 2026-08-11. 함께 볼 것: [`manual-plan.md`](./manual-plan.md) — 수동 phase 계획과
> 사람 확인 항목(프로브·실물 실측 결과가 거기 있다). 세션 커밋 이력은 `git log` 를 본다.

## 한 줄

**도그푸딩이 기능을 끌어내기 시작했다** (2026-08-10). 쓰다가 걸린 것이 그대로 작업이 됐고
(ADR-007 §폐기가 노린 자리다), 그렇게 **폴더 트리 · 네트워크 위치 · 즐겨찾기 · 컬럼 폭**이
들어와 **v0.3.0** 까지 나갔다.

**그리고 도그푸딩이 결함도 끌어냈다** (2026-08-10 저녁). 사용자가 `\\wsl.localhost` 를
열었더니 UI 가 먹통이 됐고, 파 보니 **감시 오버플로의 처방이 증상을 다시 만드는 고리**였다
(`docs/PRD-v2.md` §13). WSL 전용이 아니라 감시가 즉시 실패하는 모든 경로의 문제다 —
`manual-plan.md` 가 "느린/끊긴 서버에서의 조작감" 을 미확인으로 남겨 둔 바로 그 자리다.

**이 세션에 들어온 것**: v0.3.1·v0.4.0 릴리스 · **설정 창**(정보·시작 폴더·숨김 파일) ·
**감시 백오프** · **트리가 활성 페인을 따라간다** · **같은 폴더 재열거의 깜박임 제거**.

**마지막 것은 도그푸딩이 두 번째로 되돌린 판정이다.** 0.4.0 을 쓰던 사용자가 "WSL 이 여전히
깜박인다" 고 신고했고, 백오프가 **초당 열 번을 30초에 한 번으로 줄였을 뿐 그 한 번이 하는
일은 그대로**였다는 것이 드러났다 (`docs/PRD-v2.md` §13 §백오프만으로는 부족했다).
**그 수정은 v0.4.1 로 나갔고 사용자가 확인했다** (2026-08-11 아침) — WSL 에서 **깜박임이
완전히 멎었고 선택도 그대로**다. 신고 → 백오프 → 그 한 번이 하던 일까지 고침의 세 단계가
전부 실물에서 닫혔다. 아래 §0.4.1 배포.

**그리고 도그푸딩이 세 번째로 작업을 끌어냈다** (2026-08-11). 2026-08-10 의 XAML 크래시가
남긴 자국 — **UI 스레드 예외 하나가 상주 프로세스와 트레이 아이콘을 통째로 날리던 것** —
을 닫았다 (`docs/PRD-v2.md` §14). 1분에 5회까지 삼키고 넘으면 오늘처럼 죽으며, 삼킨 것은
`error.log` 에 스택째 남고 알림 바가 예외 이름과 함께 한 번 말한다.
**v0.4.2 로 나갔다** (아래 §0.4.2 배포).

**그리고 네 번째다 — 이번엔 결함이 아니라 손동작이었다** (2026-08-11 오후). 사용자가
**마우스 보조 버튼(4·5번)으로 뒤로·앞으로**를 요청했다 (`docs/PRD-v2.md` §15).
뒤로·앞으로는 v1 부터 있었고 없던 것은 **그것을 부르는 손동작**이다. 대상은 **커서 아래
페인**이고 그 페인이 활성이 된다 — 키보드(`Alt+←`)와 다른 유일한 자리이며, 이유는 마우스에
자리가 있다는 것이다. **v0.4.3 으로 나갔다** (아래 §0.4.3 배포).
**지금 저장소의 끝은 0.4.3 이고, 4·5번이 실물에서 먹는지는 아직 사람이 안 봤다.**

**진단 도구가 하나 늘었다**: `dotnet-stack report -p <pid>` (전역 도구, 이번에 설치).
UI 스레드가 무엇을 하는지 관리 스택으로 보여준다 — §13 의 범인을 이것이 좁혔다.
`Get-Process` 의 스레드별 `TotalProcessorTime` 으로 "누가 태우나" 를 먼저 좁히면 빠르다.

> **"이번 세션" 이라고 적힌 아래 절들은 그 날짜의 기록이다.** 최신은 여기와 §현재 상태 ·
> §다음 작업 셋뿐이고, 나머지는 그때의 사실로 남긴다.

이번 세션에 시안(`docs/mockups/v1-two-pane.html`)과 실물을 픽셀로 대조했다. 색 토큰 ·
비활성 페인 강등 · 툴바 · 페인 테두리/라운드는 **이미 맞았고**, 달랐던 넷(타이틀바 ·
breadcrumb · 여유 용량 · 페인 테두리)에서 **앞의 셋을 채웠다.** 네 번째는 오해였다 —
아래 §시안 대조 참조.

## 이번 세션 (2026-08-07 오후)

**게이트가 폐기됐다.** 판정은 스크립트가 하고 처방은 사람이 해야 하는 물건이었다 —
"며칠 썼나" 는 셀 수 있어도 "새 기능인가 버그 수정인가" 는 셀 수 없어서 강제가 늘 사람
손에 있었다. 게다가 첫 설치가 `usage.log` 를 지운 뒤로는 **숫자가 사실도 아니었다.**
대체물은 GitHub 이슈다. `IUsageLog` 와 `usage.log` 는 남긴다 (사용자 결정) — **읽는
스크립트가 없는 상태**이므로 다음 사람은 그 파일에 소비자가 없다는 것을 알고 있어야 한다.
전문은 ADR-007 §폐기.

**스플리터 결함 하나를 고쳤다** (`6617eb8`). 아래 §규칙 15 가 그것이다. **원래 관찰된
현상(330→595→890)을 재현한 것은 아니다** — 재현 시도와 아닌 것으로 밝혀진 둘은
`manual-plan.md` §v2 네트워크 끝에 있다.

**배포 확인 둘이 끝났다** (`나중에` · 제거→재설치). 하나는 문서가 틀렸다는 것을 밝혔다:
**`나중에` 가 미루는 것은 재시작이지 설치가 아니다.** 남은 둘(SmartScreen · 피드 불통)은
`manual-plan.md` §배포 에 사유와 함께 있다.

**README 가 생겼다.** 소개 + 설치 안내(릴리스 링크 · SmartScreen 경고 · 상태 폴더).

## 현재 상태

```
브랜치   main  ·  819968c 까지 푸시됨. 미푸시 커밋은 없다
버전     0.4.3   릴리스 v0.1.0 ~ v0.4.3. **v0.4.3 이 §15(마우스 보조 버튼)를 실어 나갔다**
                 (2026-08-11 · 아래 §0.4.3 배포). **이 기계 설치본은 0.4.2 이고**
                 pack.ps1 이 죽인 뒤 다시 띄웠다 — 0.4.3 은 그 실행이 받는다
테스트   1706 통과   Core 534 · App 788 · Shell 293 · Host 91
                 (+13: MouseNavigationInput 9 · Workspace 4)
게이트   fast (build -warnaserror · test --blame-hang · check-structure) ✅
         full (Release build -warnaserror) ✅ **0.4.3 배포 뒤에 둘 다 다시 돌렸다** —
         상주 프로세스가 죽어 있는 동안이라 잠금이 없었다 (아래 §0.4.1 배포)
         ⛔ 도그푸딩 게이트는 없다 — 스크립트를 지웠다 (ADR-007 §폐기)
phases/  0-core-model · 1-core-pipeline · 2-viewmodel 모두 completed
```

### 0.4.3 배포 — 끝났다 (2026-08-11 오후 · §15 를 실어 나갔다)

`gh release list` 에 **v0.4.3 이 Latest**, 자산 여섯. `vpk pack` 은 9초.
**순서가 지난번과 하나 달랐다**: 테스트 전체(1706)는 커밋 전에 돌렸고, 빌드 게이트 둘
(`-warnaserror` · Release)은 **`pack.ps1` 이 상주 프로세스를 죽인 뒤**에 돌렸다 — 그 동안이
파일 잠금이 없는 유일한 창이다. 둘 다 오류 0.

**받는 시간은 또 30초 안쪽이었다.** 0.4.2 설치본을 다시 띄우니 `packages\` 에
`flex-dir-0.4.3-full.nupkg` 가 들어왔고 알림 바가 `새 버전 0.4.3 이 준비됐습니다.` 로 떴다.
**`지금 설치` 를 UIA `InvokePattern` 으로 눌렀고** 앱이 0.4.3 으로 다시 떴다 —
창이 서고 목록이 `항목 182개` 를 세는 것까지 UIA 로 읽었다.

> **XAML 루트를 건드린 변경은 여기까지 봐야 끝난다.** §10 의 `StaticResource` 크래시가
> 컴파일과 테스트를 다 통과하고 **처음 우클릭할 때** 터졌던 자리다. 이번 변경은 창 루트에
> attached property 하나를 더한 것이므로 실패한다면 **창이 안 뜨는 모양**이고, 그것은
> 0.4.3 이 실제로 시작되는 것을 보는 것으로만 배제된다.
>
> (곁: UIA 로 버튼을 훑을 때 `ControlType=Button` 조건의 `FindAll` 이 이번에 빈 목록을
> 냈고, `Name='지금 설치'` 조건은 곧장 찾았다 — 같은 창에서 `ControlType=Text` 는 잘 왔다.
> 원인은 안 팠다. **버튼은 이름으로 찾는 것이 확실하다.**)

**아직 사람이 봐야 하는 것은 하나다** — 마우스 4·5번이 실물에서 먹는가
(`docs/PRD-v2.md` §15 §남은 것). 보조 버튼은 UIA 로 만들 수 없다.

### 0.4.2 배포 — 끝났다 (2026-08-11 · §14 를 실어 나갔다)

아래 §0.4.1 배포 의 순서를 그대로 밟았고 그대로 돌았다. `gh release list` 에
**v0.4.2 가 Latest**, 자산 여섯(`flex-dir-0.4.2-full.nupkg` 74MB ·
`flex-dir-win-Setup.exe` 78MB · Portable · `RELEASES` · json 둘). `vpk pack` 은 8초.

**받는 시간이 또 달랐다 — 30초도 안 걸렸다.** 0.4.1 설치본을 띄우자 그 안에 알림 바가
`새 버전 0.4.2 이 준비됐습니다.` 로 떴다. 사내망에서 15분이던 적(2026-08-07)과 같은
74MB 다. **그러므로 "74MB 라서 오래 걸린다" 는 상수가 아니다** — 변하지 않는 것은
받는 동안 화면에 표시가 없다는 쪽뿐이다.

**델타는 이번에도 켜지 않았다.** 다만 "지금 아니면 놓친다" 는 틀린 이해다 —
`pack.ps1` 은 **매 실행 시작 시점에 직전 릴리스의 `.nupkg` 가 그 폴더에 있는 상태**로
시작하고 그것을 지운다(`pack.ps1:61`). 그러므로 델타 켜기는 **어느 릴리스에서든 똑같이
쉽다.** 급할 것이 없다.

### 0.4.1 배포 — 끝났다 (2026-08-11 아침)

깜박임 수정(`59c427d`)과 그 테스트(`0378619`)가 릴리스에 없던 것을 내보냈다.
순서는 **① 확인용 Release 프로세스를 죽인다 → ② full 게이트 → ③ 버전 올림(`d46d1d8`) →
④ 푸시·`scripts\pack.ps1`** 였고 그대로 돌았다. ①을 빼먹으면 ②가 파일 잠금으로 깨진다 —
**다음 배포도 이 순서다.**

`gh release list` 에 **v0.4.1 이 Latest**, 자산 여섯(`assets.win.json` ·
`flex-dir-0.4.1-full.nupkg` 74MB · `flex-dir-win-Portable.zip` · `flex-dir-win-Setup.exe`
78MB · `RELEASES` · `releases.win.json`). `vpk pack` 은 12초, 업로드까지 합쳐 몇 분이다 —
**굽는 시간과 받는 시간을 헷갈리지 않는다** (받는 쪽은 §4 참조).

푸시는 `CLAUDE.md` §7 대로 별도 지시가 있을 때만. 원격은 `origin`
(`git@github.com:solitasroh/flex-dir.git`).

```
Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue   # 먼저 (아래)
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
```

> **게이트 전에 상주 프로세스를 반드시 죽인다.** 도그푸딩 중인 `FlexDir.Host` 는 자기가
> 띄워진 산출물을 잠그고, 그 빌드는 오류 수십 개로 깨진다. **Debug 든 Release 든 마찬가지다**
> — 한때 이 문서는 "Release 실행 파일로 띄우면 된다" 고 적었지만 그러면 게이트 4번째
> (Release 빌드)가 대신 깨진다. 어느 쪽으로 띄우든 게이트 전에는 죽여야 한다.
>
> **이것은 저장소 산출물로 띄웠을 때의 이야기다. 인스톨러가 그 교착을 풀었다**
> (2026-08-07). 설치본은 `%LOCALAPPDATA%\flex-dir\current\` 에서 돌아 저장소를 잠그지
> 않는다 — **설치본을 켜 둔 채 게이트 4종이 전부 통과하는 것을 확인했다.**
> 그러므로 도그푸딩은 설치본으로 한다. `dotnet run`·`bin\Debug` 실행은 확인용으로만 쓰고
> 게이트 전에 죽인다.

## 포트 현황 — 15/15

2026-08-10 에 넷이 늘었다. 셋은 트리와 즐겨찾기가, 하나는 설정 창이 요구한 것이다
(docs/PRD-v2.md §10·§10-2·§12).

| 포트 | 구현 | 왜 따로인가 |
|---|---|---|
| `ISettingsStore` | `Shell/Settings/JsonSettingsStore.cs` | 설정은 캐시가 아니라 **사용자의 의도**다 — 뷰 상태가 깨져 기본값으로 접히는 사건이 "숨김 파일을 보겠다" 를 함께 뒤집으면 안 된다. 즐겨찾기와 같은 판단이고 파일도 따로 쓴다 (`settings.json`) |
| `IDriveList` | `Shell/Storage/SystemDriveList.cs` | 드라이브 목록. COM 이 아니라 `DriveInfo`+`mpr.dll` 이라 STA 도 정리도 없다 |
| `INetworkPlaceList` | `Shell/Storage/ShellNetworkPlaceList.cs` | '네트워크 위치 추가' 는 드라이브 문자를 만들지 않아 `WNetGetConnection` 에 안 잡힌다. **COM 이라 STA 를 들고 정리 목록에 들어간다** |
| `IFavoriteStore` | `Shell/Favorites/JsonFavoriteStore.cs` | 즐겨찾기는 캐시가 아니라 사용자 데이터다 — 뷰 상태와 **다른 파일**이어야 한다 |

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
| `IThumbnailSource` | `Shell/Presentation/ShellThumbnailSource.cs` | 23 |
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
페인 관찰자 좌우 각 1개). **상주 중 WindowShown 3~9ms**(예산 100) ·
**FirstItem 0~18ms**(예산 150) — 첫 표시만 367ms(창 생성 포함, 상주가 한 번만 내는 비용).
닫기 = 숨기기 · 트레이 완전 종료 · 시작 상태 복원도 실물 확인됐다 (manual-plan §B-2).

## 다음 작업 (2026-08-10 저녁 갱신)

### 1. 0.4.1 배포 — 끝났다 (위 §0.4.1 배포)

**'업데이트 확인' 버튼이 처음으로 판정됐다** (docs/PRD-v2.md §12 §실물 확인). 0.4.0 까지는
늘 그것이 최신이라 "최신 버전입니다" 밖에 볼 수 없었다 — 새 릴리스가 있어야 밟히는
경로였고, 0.4.1 이 그것을 만들었다.

### 2. 사람이 손으로 볼 것 — 이번 세션이 만든 것

자동으로 닿는 데까지는 확인했다. 남은 것은 **조작감**이고, 매일 쓰면서 판정한다.

| 확인 | 왜 사람이 봐야 하나 |
|---|---|
| ~~**WSL 에서 깜박임이 멎었는가**~~ | **끝났다** (2026-08-11 · 설치본 0.4.1). 사용자가 눈으로 확인했다 — **완전히 멎었고 선택도 그대로**였다. 기계로도 같은 자리에서 140초(갱신주기 5회) 스크롤 변동 0 · 컨테이너 교체 0/30. 전문은 docs/PRD-v2.md §13 |
| 설정 패널의 생김새·간격 | 픽셀은 자동 채점되지 않는다 (CLAUDE.md §5) |
| 트리 따라가기의 **체감** | `C:\Users\SOOJANG` 처럼 형제가 90개인 폴더를 지날 때 화면이 튀지 않는가 |
| 느린 경로에서의 따라가기 | NAS·WSL 에서 단계마다 열거가 나간다. 지금은 로컬만 확인했다 |
| 감시 백오프 문구 | `자동 갱신이 느려졌습니다 — 새로 고침(F5)` 이 거슬리는 자리인가 |

### 3. 사람 손이 필요한 확인 (그대로 남아 있다)

| 확인 | 왜 아직 못 봤나 |
|---|---|
| 감시가 연결 끊김을 견디는가 | 어댑터 제어에 관리자 권한이 필요하다 |
| 피드에 못 닿을 때 조용한가 | 〃 (2026-08-07 세션에서 건너뛰기로 했다) |
| 느린/끊긴 서버에서의 조작감 | 이 NAS 는 185ms 로 빠르다. 없는 서버는 한 번에 42초다 |
| SmartScreen 경고 | **MOTW 를 붙여 재현을 시도했지만 안 떴다** — 다른 기계가 필요하다 |

### 4. 곁에서 드러났고 손대지 않은 것 — 하나가 닫혔다

**~~`DispatcherUnhandledException` 핸들러가 없다~~ — 2026-08-11 에 들어왔다.**
정본은 `docs/PRD-v2.md` §14. 1분에 5회까지 삼키고 넘으면 오늘처럼 죽으며, 삼킨 것은
`%APPDATA%\flex-dir\error.log` 에 스택째 남고, 알림 바가 예외 이름과 함께 한 번 말한다.
실물 확인 여섯 항목이 전부 닫혔다 (§14 §실물 확인). **v0.4.2 로 나갔다** (위 §0.4.2 배포) —
이 기계 설치본은 받아 둔 상태이고 '지금 설치' 를 누르면 그 위에서 돈다.

남은 둘은 그대로다.

- **9P(WSL) 경로에서 shell 아이콘/썸네일 조회가 항목당 250ms 다** (로컬 78ms) — 그리고
  결과는 `null` 이다. §13 폭주의 원인은 **아니었지만**(그것은 감시였다) 큰 폴더에서 아이콘이
  늦게 붙는다. 프로브로 실측한 값이고 손대지 않았다.
- **델타 패키지를 만들지 않는다.** 갱신마다 74MB 를 받는다. **걸리는 시간은 때에 따라
  크게 다르다** — 한 번은 사내망에서 약 15분이었고(2026-08-07), 0.4.0→0.4.1 은 몇 분
  안에 끝났다(2026-08-11). 그러므로 "15분" 을 상수로 쓰지 않는다. 변하지 않는 것은
  **받는 동안 화면에 아무 표시가 없어 "알림이 안 뜬다" 로 보인다**는 쪽이다.
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

### 인스톨러와 자동 업데이트 — 들어왔다 (2026-08-07)

정본은 `docs/PRD-v2.md` §9. 여기서는 다음 세션이 걸릴 것만 옮긴다.

**Velopack + GitHub Releases + "알리고 사용자가 고른다"** (전부 사용자 결정).
저장소를 **public 으로 전환했다** — private 이면 배포물에 PAT 를 실어야 했다.
전환 전에 실측한 것: 비밀값은 없고(`.env` 는 이력에 없다) 사내 NAS 주소 41곳과 공유 이름
4곳이 이력에 남아 있다. **사용자가 그대로 공개하기로 했다.**

```
scripts\pack.ps1                 # publish → vpk pack → gh release
Directory.Build.props <Version>  # 배포 버전의 정본. 올리지 않으면 아무도 갱신되지 않는다
```

**밟은 것 셋** (전부 실물에서 드러났다):

1. **`UpdateManager` 를 생성자에서 만들면 앱 전체가 안 뜬다.** 그 생성자는
   `VelopackApp.Build().Run()` 이 세우는 locator 를 요구하고, 테스트 실행기와 `dotnet run`
   이 정확히 그 상태다. 늦게 만들고 `VelopackLocator.IsCurrentSet` 을 먼저 본다.
2. **설치 폴더와 상태 폴더가 같았다** — 둘 다 `%LOCALAPPDATA%\flex-dir`.
   **첫 설치가 기존 `usage.log` 를 지웠다** (도그푸딩 게이트의 입력이다). 상태를
   `%APPDATA%\flex-dir` 로 옮기고 남은 기록을 손으로 옮겼다. `check-dogfooding.ps1` 도
   같이 고쳤다 — **경로를 옮기면 그것을 읽는 스크립트가 조용히 다른 곳을 본다.**
3. **인스톨러가 게이트와 도그푸딩의 교착을 풀었다** (아래 §현재 상태 의 경고 참조).

**실물 확인**: 0.1.0 설치 → 0.1.1 릴리스 → 알림 바(`새 버전 0.1.1 이 준비됐습니다.`) →
`지금 설치` → 패키지 교체 후 재시작까지 봤다. **0.1.2 에서 `나중에` 와 제거→재설치도
봤다** (2026-08-07 오후 · manual-plan §배포).

**틀리기 쉬운 것 — `나중에` 가 미루는 것은 재시작이지 설치가 아니다.** 알림은 접히지만
**다음 실행에 다시 뜨지 않는다**: 받아 둔 패키지를 다음 실행의 `VelopackApp.Build().Run()`
이 시작 지점에서 적용하므로 그 실행이 이미 새 버전이다. 문서와 주석이 한동안 "다음 실행의
확인이 같은 버전을 다시 찾는다" 라고 적고 있었는데 그럴 수 없는 구조다.
**받기를 미리 끝내는 이상 이 모양은 강제된다** — 받아 둔 것을 적용하지 않으려면 매 실행마다
되돌려야 하고 그러면 미리 받는 의미가 없다. 결정이 지키려던 것("상주 앱을 마음대로
재시작하지 않는다")은 지켜진다.

**제거는 깨끗하다** (상주 중에 돌려도 된다): 프로세스 종료 → 설치 폴더·바로가기 둘·제어판
등록이 사라지고 **`%APPDATA%\flex-dir` 셋은 해시까지 남는다.** 재설치하면 창 배치까지
이어진다.

**남은 것**: 코드 서명이 없어 SmartScreen 경고가 뜬다 (혼자 쓰는 동안은 감수) — 다만
**이 기계에서는 MOTW 를 붙여도 안 떴다** (manual-plan §배포).
델타 패키지를 만들지 않아 갱신마다 74MB 를 받는다 (`pack.ps1` 주석에 근거).

### v2 네트워크 마무리 (2026-08-07) — N-4 와 실물 확인이 결함 둘을 냈다

`docs/PRD-v2.md` §5 가 정본이다. 여기서는 **다음 세션이 걸릴 것**만 옮긴다.

**N-4 — 1219 를 `AccessDenied` 에서 떼어냈다.** `LocationErrorKind.CredentialConflict` 가
늘었고 그 분류만 경로 대신 명령을 낸다:
`다른 자격 증명으로 이미 연결돼 있습니다 — net use \\10.10.10.23 /delete 후 다시 여세요`.
끊을 대상이 공유가 아니라 서버라 `LocationId.Server` 가 생겼다.
`Win32ErrorMapping` 에는 **실측한 셋만**(53·67·1219) 넣었다 — v1 이 걸어 둔 유보를 그대로
이어받았다. **1219 는 앱 안에서 재현하지 못했다** (열거는 기존 세션을 재사용한다) —
문구는 테스트로만 서 있다.

**결함 1 — SMB 는 삭제 중인 폴더를 `ACCESS_DENIED` 로 낸다.** 실측: 삭제 후 367ms 에 5,
414ms 에 3. 감시 알림에 곧장 재열거하는 우리가 그 50ms 창에 들어가 **사라진 폴더에
"액세스가 거부되었습니다" 를 띄우고 상위로 올라가지도 않았다.** 로컬은 같은 상황에서
곧장 3 을 받아 정상 동작한다 — **네트워크에서만 달랐다.**
고친 것: `FileSystemFolderSource.ShouldRetryOpen` — **네트워크 경로의 권한 실패만** 250ms
뒤 한 번 더 연다. 다른 실패는 다시 묻지 않는다(없는 서버는 한 번이 42초다).

> **일반화**: 로컬과 네트워크가 같은 코드를 지나도 오류 코드의 **시간적 모양**이 다르다.
> "무엇이 오는가" 뿐 아니라 **"언제 오는가"** 가 분류를 바꾼다.

**결함 2 — 이름에 `:` 를 쓰면 앱이 죽었다** (네트워크와 무관한 v1 결함).
`LocationId.Combine` 의 `ArgumentException` 이 `AsyncRelayCommand` 밖으로 새어
**프로세스가 종료됐다**. `회의록 10:30.txt` 로 이름을 바꾸는 것만으로 난다.
고친 것 둘:

1. `LocationId.TryCombine(name, out child, out ChildNameError)` — `TryParse` 와 같은 짝이다.
   **사용자가 친 이름에는 `Combine` 을 쓰지 않는다.** `PaneViewModel` 이 사유를 문구로 낸다
   (`이름에 쓸 수 없는 문자가 있습니다: \ / : * ? " < > |`).
2. `PaneViewModel.RunAsync` 가 **취소 외 모든 예외를 잡는다.** 그 메서드의 주석은 원래부터
   *"예외를 밖으로 던지지 않는다"* 라고 적고 있었는데 실제로는 `LocationAccessException`
   만 잡았다 — **열거 경로(`FillAsync`)가 이미 배운 교훈이 조작 경로에는 안 와 있었다.**

**실물로 확인한 것** (`\\10.10.10.23\home\` · 사용자가 지정한 자리):
감시(생성 반영 · robocopy 300개 10.3초 부어도 목록 일치) · 조작 넷(이름변경·복사·이동·삭제) ·
대화상자 둘(이름 충돌 · **영구 삭제 확인** — 네트워크엔 휴지통이 없다)이 flex-dir 을 부모로
뜬다. 전문은 `manual-plan.md` §v2 네트워크.

### v2 네트워크 — N-1·N-2·N-3 이 들어왔다 (2026-08-07)

**UNC 가 된다.** `\\10.10.10.23\공유` 로 열거·아이콘·breadcrumb·여유 용량이 돌고,
`\\10.10.10.23` 은 **공유 목록 30개**를 낸다. 정본은 `docs/PRD-v2.md` §5.

| 표면 | 하는 일 |
|---|---|
| `Core/Locations/LocationId.cs` | UNC 를 담는다. 내부 `\\?\UNC\server\share`, 표시 `\\server\share`. `IsNetwork`·`IsNetworkServer` |
| `Shell/Enumeration/NetworkShareSource.cs` | `WNetOpenEnum`+`WNetEnumResource`. **`NetShareEnum` 을 쓰면 이 NAS 가 막는다** |
| `Shell/Enumeration/RoutingFolderSource.cs` | `IsNetworkServer` 로 공유/폴더를 가른다. 페인은 여전히 포트 하나만 안다 |
| `Shell/Storage/FileSystemDriveSpace.cs` | UNC 는 `GetDiskFreeSpaceEx` 로. `DriveInfo` 는 드라이브 문자만 받는다 |
| `Core/Formatting/TimestampFormatter.cs` | 모르는 시각(`default`)은 **빈 칸**이다 — 공유에는 수정 시각이 없다 |

**값을 치르고 배운 것**

1. **진입 조건이 교착이었다.** v2 를 "게이트 PASS 후" 로 잡았는데 **게이트가 막는 그것이
   게이트를 통과하는 데 필요했다.** 사용자가 "매일 쓰려면 네트워크가 필요하다" 고 말해
   드러났다. → 게이트의 입력을 막는 기능은 게이트 뒤에 둘 수 없다.
2. **거부하던 것을 받아들이면 그 거부에 기대던 자리가 전부 조용히 틀린다.** UNC 를 담자
   `FileSystemDriveSpace`(여유 용량이 사라짐)·`ShellClipboardBridge`(UNC 경로를 버림)가
   함께 틀렸다. **테스트 셋이 UNC 를 "나쁜 입력" 의 예로 쓰고 있어서** 그것이 깨져 알았다.
3. **매핑 드라이브는 v1 때부터 됐다.** 파서가 막은 것은 `\\` 표기뿐이고 `Z:` 는 그냥
   지났다 — 사용자에게는 둘 다 "네트워크가 안 된다" 로 보였다.

**남은 것은 없다** — N-4 와 감시 확인은 위 §v2 네트워크 마무리 에서 끝났다.

### v2 계획은 승인됐다 — `docs/PRD-v2.md`

**사용자 승인 2026-08-06.** 요점 넷만 옮긴다. 정본은 그 문서다.

- **후보 8개 중 6개가 전작이 갖추고도 실패한 목록이다** (ADR-004 대조). 그래서 계획이
  "무엇을 만드나" 가 아니라 **"무엇이 들어올 자격을 얻나"** 로 서 있다. 승격 조건 셋:
  탈출구인가 · 축에 복무하는가 · v1 이 성공한 축(성능)을 깨지 않는가.
  "그 목록이면 금지" 는 규칙으로 틀렸다 — **세션복원이 반례**다 (v1 이 냈다).
- **v2 축은 네트워크 하나.** `PRD.md` §3 이 유일하게 `v2` 로 못박은 것이고, ADR-007 이
  실패한 셋에 네트워크를 넣어 뒀다 — **갖추고도 안 쓴 기능이 아니라 갖추지 못한 축**이다.
  `SHELL_NOTES.md` §네트워크 에 3주치 결론이 이미 있고 `LocationId` 가 `NetworkPath` 를
  '잘못된 경로' 와 구분해 거부한다 (ADR-010 이 노린 확장 경로가 살아 있다).
- **진입 조건은 게이트 PASS(5/7)** 다. 5일을 쓰고도 네트워크 때문에 탐색기를 연 적이
  없으면 **축이 틀린 것이고 다시 판정한다.** 그전까지는 v1.1 트랙.
- **그룹화는 v1.1 로 들어왔다** (아래 §Details 그룹화). 제외 사유가 무효였던 근거는
  ADR-017 이 정본이다.

### Details 그룹화 — 들어왔다 (2026-08-06 · ADR-017)

**게이트가 BLOCK(2/7)인 상태에서 사람 재량으로 진행했다** (사용자 결정). ADR-007 의
게이트는 보고만 하고 강제는 사람이 한다 — 그 재량을 쓴 첫 사례다.

| 표면 | 하는 일 |
|---|---|
| `Core/Grouping/FileItemGroups.cs` | 라벨 규칙 + `WithGroupKey`(그룹 키를 정렬 1차 키로) |
| `Core/ViewState/FolderViewState.cs` | `GroupBy`·`Collapsed` 가 늘었다. **둘 다 선택적이라 v1 이 쓴 파일이 그대로 읽힌다** |
| `App/ViewModels/PaneViewModel.cs` | `GroupBy`·`IsGrouped`·`DetailRows`·`ChangeGroupCommand`·`ToggleGroupCommand` |
| 〃 `DetailRowViewModel` | 헤더이거나 항목인 한 줄. `RowViewModel` 과 같은 자리에 선언했다 |
| `App/Views/ListInput.cs` | `IsGroupHeader` — 헤더 클릭을 빈 자리로 보지 않는다 |
| 〃 `GroupOption` | 메뉴 항목 다섯. **두 진입점이 같은 목록을 쓴다** — XAML 에 두 벌 쓰지 않는다 |
| `App/Views/MainWindow.xaml` | `DetailsGroupedRow` 템플릿 · 툴바 분류 드롭다운(`F168`) · 헤더 우클릭 메뉴 |

**목록의 원소 종류가 늘면 View 배선 셋을 함께 고친다 — 둘을 실제로 빠뜨렸다.**

같은 목록 컨트롤에 소스가 셋이다(평평한 항목 · 합성 행 · 그룹 행). 원소 종류로 분기하는
자리가 셋인데 **`ViewSourceConverter` 만 고치고 나머지 둘을 빠뜨린 채 실물 확인까지
통과했다.** 항목 10개짜리 데모 폴더라 스크롤이 없었기 때문이다.

| 자리 | 빠뜨렸을 때의 증상 |
|---|---|
| `Views/ViewConverters.cs` `ViewSourceConverter` | 목록이 비어 보인다 — **바로 보인다** |
| `Views/FocusScroll.cs` `Target` | 포커스는 움직이는데 **화면이 따라오지 않는다** |
| `Views/VisibleRangeSync.cs` `Flatten` | 보이는 범위가 늘 비어 **아이콘이 끝까지 빈칸**이다 (실패가 캐시된다 — §규칙 9) |

**뒤의 둘은 화면을 봐도 원인이 안 보인다.** 앞의 하나만 맞으면 목록은 정상으로 보이기
때문이다. 규칙으로 `ARCHITECTURE.md` §5 에 올렸다. 둘 다 재현 테스트를 먼저 쓰고 고쳤다
(`FocusScrollTests.Target_InGroupedDetails_*` · `VisibleRangeSyncTests.Flatten_UnwrapsGroupedDetailRows`).

**틀리기 쉬운 것 넷** (전부 값을 치르고 정한 것이다):

1. **그룹 경계는 정렬된 목록의 인접 비교로만 잡는다.** 그룹마다 순서 번호를 두면 그것이
   비교기와 어긋나는 순간 **같은 라벨의 헤더가 두 자리에** 생긴다. 그래서 그룹 키를 정렬
   1차 키로 앞세우는 것이 뼈대다.
2. **기호를 "기타" 로 묶으면 안 된다.** `NaturalStringComparer` 는 텍스트 런을
   `OrdinalIgnoreCase` 로 비교해 `(` 는 숫자보다 앞, `_` 는 `Z` 보다 뒤다. 묶는 순간 1번이
   깨진다. `FileItemGroupsTests.Labels_OfASortedList_FormContiguousRuns` 가 이 불변식을
   7가지 조합으로 고정한다 — 라벨 규칙을 고치면 그것이 먼저 깨져야 한다.
3. **폴더는 기준과 무관하게 한 그룹이다.** `directoriesFirst` 때문에 기준별로 나누면
   같은 라벨이 폴더 구간과 파일 구간에 두 번 나온다.
4. **헤더 클릭은 목록의 터널링 핸들러를 먼저 지난다.** 막지 않으면 접을 때마다 선택이
   통째로 풀린다 (B-4 함정 2 와 같은 자리). `ListInput.IsGroupHeader` 가 막는다 —
   **실물에서 확인했다**: 헤더를 눌러도 상태표시줄의 `1개 선택` 이 그대로 남는다.

**실물 확인됨** (2026-08-06 · `PrintWindow` 캡처 + 합성 클릭): 그룹마다 헤더 하나 ·
접으면 항목이 사라지고 개수는 남고 · 글리프가 `E70D`→`E70E` 로 뒤집히고 · 선택이 유지된다.
**툴바 드롭다운은 사용자가 확인했다** (2026-08-07 · 다섯 항목 + 체크 표시).
**남은 확인은 대용량 폴더에서의 체감**이다 (manual-plan §v1.1).

### 진입점을 하나만 둔 것이 틀렸다 (2026-08-07)

처음에는 **컬럼 헤더 우클릭 하나**만 두었다. 탐색기와 같은 자리라는 이유였는데 둘이 틀렸다.

1. **사용자가 못 찾았다.** 만든 사람이 설명해야 찾을 수 있으면 잘못 고른 자리다.
   탐색기도 리본에 같은 항목을 둔다. → **툴바 드롭다운을 주 진입점으로** 넣었다.
2. **애초에 뜨지도 않았다.** 컬럼마다 `HeaderButton` 이 헤더를 꽉 채워 **우클릭의 원본이
   늘 버튼인데 메뉴를 부모 `Grid` 에만 달아 두었다.** 스타일에도 같은 메뉴를 단다.
   `ListInput` 의 터널링 함정과 같은 종류다 — **부모에 단 것이 자식 위에서 동작하리라고
   가정하지 않는다.**

**툴바를 붙이면서 둘을 더 밟았다.**

3. **아이콘 폰트를 스타일에 걸면 하위 메뉴 글자가 두부(□)가 된다.** `FontFamily` 는 상속
   속성이다 — `ToolMenu` 스타일에 걸었더니 드롭다운의 한글 다섯이 전부 □ 였다.
   **글리프를 그리는 `TextBlock` 에만 건다.**
4. **글리프는 툴바 맥락에서 실제 크기(16px)로 고른다.** 처음 쓴 `E8FD` 는 목록 뷰 버튼과
   같은 코드포인트였고, 고쳐 놓은 `F168` 은 **30px 로 그려 놓고 골라서** 또 틀렸다 —
   16px 에서는 선 텍스처라 `E8A4`·`E8FD` 와 같은 계열로 보인다. 이 툴바에는 계열이 둘
   (선 · 사각) 있고 새 아이콘은 **어느 쪽도 아닌 실루엣**이어야 한다. `E8EC`(태그)다.
   근거는 `DESIGN.md` §7 에 규칙으로 올렸다.

**전부 사용자가 실물로 확인했다** (2026-08-07): 툴바 드롭다운 · 한글 · 체크 표시 ·
유형 기준 그룹 다섯 · 접기 · **헤더 우클릭 메뉴**(처음에 안 뜨던 그 자리).

### 도그푸딩 게이트 — 폐기했다 (2026-08-07 · 사용자 결정)

`scripts/check-dogfooding.ps1` 은 **지웠다.** 전문은 ADR-007 §폐기 에 있고, 여기서는
다음 사람이 걸릴 것만 옮긴다.

- **`usage.log` 에는 이제 소비자가 없다.** `IUsageLog`·`FileUsageLog`·`ActivationRouter`
  의 기록은 그대로 돈다 (사용자 결정: 남긴다). 파일 모양의 계약("한 줄에 하루, 앞 10자가
  `yyyy-MM-dd`")도 그대로 두었다 — 읽는 것이 이제 사람이다.
- **"BLOCK 이니 새 기능 금지" 같은 판정은 더 이상 없다.** 무엇을 할지는 사람이 정한다.
- **대체물은 GitHub 이슈다.** 쓰다가 걸린 것을 거기 남긴다. 저장소는 public 이다.

`DESIGN.md` §10 의 미결 둘(#4 밀도 옵션 · #5 비활성 페인 크롬 강등)은 실물을 매일 보며
판단한다 — 그것은 그대로다.

### 사람 확인 — 끝났다 (2026-08-06 smoke 테스트)

마감이 만든 다섯 항목(트레이 아이콘 · 타이틀바 조작감 · 다른 배율/모니터 최대화 ·
breadcrumb 상호작용 · 여유 용량)을 **사용자가 손으로 확인했고 전부 통과했다.**
항목별 결과는 `manual-plan.md` §마감 에 있다 — 여기서 되풀이하지 않는다.

**확인 잔여는 이 기계에서 못 보는 것뿐이다**: 클라우드 자리표시자 · 네트워크 경로 ·
연결 프로그램 없는 확장자와 파일 조작 실패 대화상자. 전부 `manual-plan.md` §A 에 있다.

### 시안 대조 (2026-08-06) — 실물 캡처 vs `docs/mockups/v1-two-pane.html`

**이미 맞았던 것** (픽셀로 확인): 색 토큰 전부(창 `#F3F3F3` · 활성 크롬 `#F9F9F9` ·
페인 `#FFFFFF`) · 비활성 페인 크롬 강등 · 툴바 구성과 구분선 · 활성 뷰 버튼 accent ·
주소줄 활성 accent 테두리 · 상태표시줄 24px.

**이번에 채운 것**: 32px 커스텀 타이틀바 · 주소줄 breadcrumb · 상태표시줄 여유 용량.

**차이인 줄 알았으나 아니었던 것 — 페인 테두리·라운드는 처음부터 있었다.**
`PaneRoot` 에 `BorderThickness="1" CornerRadius="6,6,0,0"` 이 있고 페인 그리드에
좌우 4px 여백이 있다. 캡처에서 **창의 보이지 않는 리사이즈 테두리 8px**(PrintWindow 가
검게 그린다)를 페인 가장자리로 읽어 "없다" 고 판단했던 것이다.
실측: `x=8~11` 창 배경 · `x=12` `#E5E5E5` 테두리 · `x=13` 페인 흰색, 좌상단은
`y=35→x=15` · `y=38→x=12` 로 라운드가 돈다.
**교훈: PrintWindow 캡처의 바깥 8px 은 창이 아니다.** 클라이언트 원점은 `(8, 31)` 이다.

### 최대화 여백 — 손대지 않기로 했다 (실측 근거)

`WindowChrome` 창을 최대화하면 창 rect 가 `(-8,-8)-(2568,1400)` 으로 작업영역
`(0,0)-(2560,1392)` 밖으로 사방 8px 나간다. **그런데 보이는 내용은 창 기준 `(8,8)` 에서
시작해 화면 좌표로 정확히 작업영역과 맞는다 — 잘리지 않는다.**

`WM_GETMINMAXINFO` 훅을 붙여 봤고 값도 맞게 왔지만(로그로 확인) **결과가 한 픽셀도
달라지지 않아 걷어냈다.** 그 훅의 `MaxTrackSize` 클램프는 오히려 모니터를 옮길 때 새
회귀를 만든다. 근거는 `MainWindow.xaml` 의 `WindowChrome` 주석에 남겼다.

**사용자가 다른 배율·다른 모니터에서도 확인했고 이상 없었다** (2026-08-06 smoke 테스트).
그래도 이 판단은 본 구성에 한한다 — 작업표시줄을 옆에 두거나 훨씬 큰 배율에서 **실제로
잘리는 것을 보면** 그때 훅을 다시 꺼낸다.

> 아래 §시안 · §B-1 표면 · §B-3 View 배선은 phase B 의 배경이다. **B-1~B-4 가 전부
> 끝났으므로** 지금은 "왜 그렇게 돼 있는가" 를 읽는 자료다.

### 시안은 이미 있다 — `docs/mockups/v1-two-pane.html`

**248줄짜리 인터랙티브 목업**이다(`docs/DESIGN.md:5` 에서 링크). 뷰 4종 · 상태 5종을
버튼으로 전환할 수 있고 스플리터도 실제로 끌린다. **브라우저로 열어 보고 시작한다.**

`DESIGN.md` §1~§8 + 목업이 이미 확정한 것 — XAML 은 받아쓰면 된다:
뷰 4종 레이아웃·치수 · Details 컬럼 폭 · 색 토큰 전부 · 활성/비활성 페인 표현 ·
상태 5종(일반 · 빈 폴더 · 권한 없음 · 열거 중 진행바 · 썸네일 로딩) · 상태표시줄 · 툴바.

### 선행조건은 없다 — `docs/DESIGN.md` §9 를 채웠다

키보드 맵 · 이름변경 인라인 편집 · 스플리터 드래그 중 · 페인 간 드래그앤드롭이 전부
`DESIGN.md` §9 · §9-1 에 확정돼 있다. **바로 착수할 수 있다.**

착수 전에 읽을 것: `DESIGN.md` §9(키보드·상호작용) · `ARCHITECTURE.md` §5(합성 행) ·
**ADR-016**(왜 합성 행인가, 기각한 셋).

세 가지가 이 phase 의 모양을 정한다.

- **뷰 3종은 합성 행 위에 선다** (ADR-016). `PaneViewModel` 이 `Rows` 를 소유하고 View 는
  `SetViewportSize(width, height)` 만 민다. **Details 는 평평한 `Items` 그대로**다.
- **이동·선택을 `ListView` 에 맡기지 않는다.** `FocusedName` + `MoveFocus(...)` 를 새로
  만든다. 합성 행 위에서 내장 이동은 행 단위로만 움직이기 때문이다.
- **드래그앤드롭에 `FlexDir.Shell` 이 필요 없다.** WPF `System.Windows.DataObject` 가
  CF_HDROP 마샬링을 대신한다(컴파일로 확인). 포트를 늘리지 말고 `PaneViewModel` 에
  `DropAsync(paths, targetFolder, isMove)` 만 더한다.

### B-1 이 만든 표면 — 전부 들어왔고 전부 자동 채점된다

| 표면 | 비고 |
|---|---|
| `SetViewportSize(double w, double h)` | **열 수가 바뀔 때만** `Rows` 를 다시 묶는다. 디바운스 없음 |
| `Rows : IReadOnlyList<RowViewModel>` | wrap 3종 전용. Details 는 평평한 `Items` 그대로 (ADR-016) |
| `FocusedName` · `MoveFocus(...)` | 2D 이동 · Shift 확장 · Ctrl 포커스만. 폴더 전환이 포커스를 접는다 |
| `TypeAhead(char)` | 리셋 시한 1초. `TimeProvider` 는 ctor **선택 주입**(기본 시스템 시계) |
| `DropAsync(paths, targetFolder, isMove, ct)` | 경로 파싱 포함. 제자리 **이동**만 걸러낸다 (복사는 허용) |
| `GoBack`·`GoForward`·`GoUp`·`RefreshCommand` | 네비게이션 넷이 커맨드로도 노출된다. 메서드 표면은 그대로다 |

**View 가 알아야 하는 것 (B-2·B-3 에서 실제로 그렇게 배선했다)**

- 뷰 3종의 `ItemsSource` 는 `Rows`, Details 는 `Items` 다. View 가 미는 것은
  `SetViewportSize` 와 `SetVisibleRange` 둘뿐이다.
- PageUp/Down 의 '한 화면' 은 `DESIGN.md` §2 치수에서 유도했다 — Details 24 ·
  목록 열 폭 212(200+12) · 타일 60(56+4) · 큰 아이콘 148(140+8). §2 가 바뀌면
  `PaneViewModel` 의 `*Pitch` 상수도 같이 바꿔야 한다.
- 포커스가 없을 때 첫 방향키는 첫 항목에서 시작한다 (`End` 만 마지막). `Ctrl+Space` 는
  View 가 `Selection.Toggle(FocusedName)` 로 잇는다 (§9 키보드 맵 그대로).

### B-3 이 만든 View 배선 — 전부 `ItemsPanel` 에 붙는다

| 파일 | 하는 일 |
|---|---|
| `Views/ViewportSync.cs` | 패널 크기 → `SetViewportSize`. **목록 컨트롤이 아니라 `ItemsPanel` 에 건다** |
| `Views/VisibleRangeSync.cs` | 실현된 컨테이너 → `SetVisibleRange`. 신호는 `LayoutUpdated` 하나 |
| `Views/TypeAheadInput.cs` | 문자 키 → `TypeAhead(char)`. 목록 내장 `TextSearch` 는 껐다 |
| `Views/AddressFocus.cs` | `Ctrl+L`·`Alt+D` → 그 페인 주소줄. **창이 아니라 페인 루트에 건다** |
| `Views/ViewConverters.cs` | `ViewSourceConverter`(뷰 모드 → `Items`/`Rows`) · `ThumbnailImageConverter`(BGRA → `Pbgra32`) |

**왜 `ItemsPanel` 인가** → `ARCHITECTURE.md` §5 에 규칙으로 올렸다. 목록 컨트롤에 걸면
마지막 칸이 잘리고 PageUp/Down 이 조용히 틀린다.

**왜 `LayoutUpdated` 하나인가**: 스크롤·뷰 전환·목록 갱신이 전부 레이아웃을 지난다. 셋을
따로 훅하면 컨테이너가 아직 실현되지 않은 시점에 물어보는 자리가 생긴다. 대신 **같은
목록이면 밀지 않는다** — 스케줄러는 부를 때마다 진행 중 요청을 전부 끊고 새 세대를 연다.

### B-4 가 만든 View 배선 — 클릭은 전부 `ListInput` 하나를 지난다

| 파일 | 하는 일 |
|---|---|
| `Views/FocusScroll.cs` | `FocusedName`·`RenamingName` → `ScrollIntoView`. **바인딩으로 받는다** (스레드 마샬링) |
| `Views/RenameEditor.cs` | 편집기의 포커스·초기 선택 범위·키 판정. **여는 것은 행 템플릿이다** |
| `Views/DragDropInput.cs` | 수정키 → 효과 · 놓인 자리 → 폴더 · 임계값. `FlexDir.Shell` 을 쓰지 않는다 |
| `Views/ListInput.cs` | 좌·우·더블클릭 전부. 미룬 선택 확정 · 재클릭 타이머 · `IsEditing` |

**B-4 가 값을 치르고 배운 것 넷** (전부 자동 채점이 닿지 않던 자리다):

1. **`KeyDown` 을 전부 `Handled` 로 표시하면 타이핑이 죽는다.** WPF 가 그 키에서
   `TextInput` 을 만들지 않는다 — 캐럿·선택은 멀쩡한데 글자만 안 들어간다. 삼킬 키를
   명시한다 (`RenameEditor.Swallows`). `Delete` 는 창이 휴지통에 걸어 두었으므로 삼킨다.
2. **목록의 터널링 핸들러가 편집기 클릭을 먼저 가져간다.** `PreviewMouseLeftButtonDown` 은
   목록 → 편집기 순서라 목록이 `Focus()` 하면 편집이 취소된다. 같은 함정이 더블클릭(파일이
   열린다)·우클릭(shell 메뉴가 뜬다)·드래그(파일이 딸려 나간다)에도 있다. `ListInput.IsEditing`.
3. **마우스 다운이 선택을 즉시 접으면 여러 개를 끌 수 없다.** 탐색기가 선택 확정을 마우스
   업으로 미루는 이유가 이것이다. `ListInput.DefersSelection`.
4. **COM 인터페이스에서 배열 파라미터의 기본 마샬링은 `SafeArray` 다** (P/Invoke 는
   `LPArray`). `GetUIObjectOf` 가 PIDL 배열을 SAFEARRAY 로 받아 **프로세스가 죽었다**.
   `SHELL_NOTES.md` §컨텍스트 메뉴 함정 9. **선언이 틀려도 컴파일되고, 실행 지점을 바꿔
   끼운 테스트는 진짜 vtable 을 지나지 않는다** — 이 종류는 실물 말고 잡을 방법이 없다.

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
                      저장소를 public 으로 전환했다. 릴리스 v0.1.0 · v0.1.1
도그푸딩이 낸 것    ✅  2026-08-10 — 전부 쓰다가 걸린 것에서 나왔다
                      폴더 트리 (§10) · 네트워크 위치 (§10-1) · 즐겨찾기 (§10-2) ·
                      Details 컬럼 폭 (§11) · type-ahead 연타 · 창 닫을 때 상태 저장
→ 설정 창           ⬜  범위는 정해졌다 (위 §다음 작업 1)
```

### 마감이 만든 표면

| 파일 | 하는 일 |
|---|---|
| `assets/flex-dir.ico` | 16·32·48·256. **16·32 는 축소가 아니라 직접 그린 것**이다 |
| `scripts/new-icon.ps1` | 원본 PNG 생성 (OpenAI 이미지 API · `.env` 의 키). 빌드와 무관 |
| `scripts/make-ico.ps1` | 원본 PNG → `.ico`. 굽고 나서 **다시 읽어 확인한다** |
| `Host/Startup/ProductIcon.cs` | 박아 둔 `.ico` 에서 크기별 프레임을 꺼낸다 (트레이용) |
| `App/Views/WindowCaption.cs` | 캡션 커맨드 배선. `CommandBinding` 이 `*.xaml.cs` 로 가는 것을 막는다 |
| `Core/Locations/PathSegments.cs` | 경로 → breadcrumb 칸. `IsFirst` 로 구분자 위치를 정한다 |
| `Core/Storage/IDriveSpace.cs` | 여유 용량 포트. 모르면 `null` — `0 B` 와 다르다 |

**아이콘을 다시 만들 때**: `.ico` 와 원본 PNG 가 저장소에 있으므로 API 키 없이
`make-ico.ps1` 만 돌리면 된다. 도안을 바꿀 때만 `new-icon.ps1` 이 필요하다.
그 스크립트는 **한 장씩 따로 보낸다** — `n=3` 을 `quality=high` 로 한 요청에 담으면
게이트웨이가 먼저 끊고 Cloudflare 520 이 온다 (장당 45초 이상 걸린다).

**phase B 를 쪼갠다면 이 순서를 권한다** (자율 실행 대상이 아니므로 `phases/` 에 넣지 않는다):

```
B-1  ViewModel 확장   ✅ Rows · FocusedName · MoveFocus · TypeAhead · DropAsync
                     + 네비게이션 커맨드 4개.  화면 없이 전부 채점됐다 (App 258→321)
B-2  창 + Details     ✅ 골격 + 시작 폴더 복원 + 계측 둘(WindowShown·FirstItem) +
                     스플리터 비율·창 배치 복원/저장(SplitterSync·ResidentWindow) +
                     닫기 = 숨기기(상주) + 트레이 완전 종료(TrayMenu·NotifyIcon).
                     핵심 경로 전부 실물 확인 (2026-08-06 · manual-plan §B-2) — 이때
                     복원 PropertyChanged 의 스레드 친화성 잠복 버그도 잡았다.
                     확인 잔여(가벼움): 클릭/더블클릭 조합 · 주소줄 오타 사유 ·
                     Shift/Ctrl 키 조합 · 대용량 폴더 스크롤 · 두 번째 실행의 인자 폴더.
                     모니터 구성이 바뀌면 복원 위치가 화면 밖일 수 있다(의도적으로 안
                     막았다 — 필요해지면 VirtualScreen 클램프).
                     주의: 도그푸딩 상주 프로세스가 산출물을 잠근다 — 위 §현재 상태 참조.
                     → 매일 쓸 수 있다. 도그푸딩하며 B-3 로 간다
B-3  나머지 뷰 3종     ✅ 합성 행 템플릿 3종 + 뷰 전환(툴바 4버튼·Ctrl+Shift+1~4) +
                     behavior 넷(ViewportSync·VisibleRangeSync·TypeAheadInput·AddressFocus) +
                     Pbgra32 변환. 실물 확인 (2026-08-06 · manual-plan §B-3) — 이때
                     SHGetFileInfo 동시 호출 실패와 Reset/중복제거 어긋남을 잡았다.
                     확인 잔여: type-ahead · Ctrl+L · 뷰 3종 키보드 이동 · wrap 뷰 클릭 ·
                     대용량 폴더 스크롤 체감.
B-4  상호작용        ✅ 이름변경 인라인 편집 + 드래그앤드롭 + IContextMenuProvider
                     (포트·Shell 구현·Host 소유 창) + 클릭 경로 정리.
                     실물 확인 (2026-08-06 · manual-plan §B-4) — 이때 KeyDown 을 전부
                     삼켜 타이핑이 죽던 것, 편집기 클릭을 목록이 가로채던 것,
                     다중 드래그가 접히던 것, GetUIObjectOf 의 SafeArray AV 를 잡았다.
                     이 확인이 B-3 이 남긴 스크롤 결함도 함께 드러냈다.
```

B-1 을 먼저 두는 이유: 그 덩이가 **화면 없이 전부 자동 채점된다.** 화면부터 붙이면
2D 이동 규칙의 버그와 XAML 버그가 섞여서 나온다 (`CLAUDE.md` §5).
