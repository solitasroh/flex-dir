# ADR — flex-dir

기술 선택과 그 근거. 번호를 매기고 누적한다.
실패 부검 전문은 [`../DECISIONS.md`](../DECISIONS.md).

---

## ADR-001 — 전작을 고치지 않고 백지에서 다시 만든다

**상태**: 확정

**맥락**: [fast-explorer](https://github.com/solitasroh/fast-explorer) 는 C++/Win32 로
3주 만에 v0.8.1 까지 갔고 기능은 Q-Dir 급이었으나 쓰이지 않았다.

**근거**

- `main-window.cpp` 2,836줄. 15-step 리팩터링(포트 8개·어댑터 7개 추출) 후에도
  살아남았다. 137K 를 밖으로 옮기고 431K 를 남겼다.
- 버그를 알아도 고치는 비용이 너무 컸다. 그래서 안 고쳐졌고, 안 고쳐지니 안 쓰게 됐다.
- 이미 마음이 떠난 코드베이스다. 혼자 만드는 앱에서 이건 실질 비용이다.

**대가**: PIDL·UNC·NetHood 삽질 3주가 코드와 함께 사라진다.
→ ADR-008 로 완화한다.

**기각한 대안**: UI 만 교체하고 `src/core`(172K, 실제로 잘 작동했다)를 이식.
사용자 판단으로 기각.

---

## ADR-002 — C# / WPF (.NET 9)

**상태**: 확정

**근거**

- **MVVM 이 god object 싱크홀을 제거한다.** Win32 에서는 WndProc 하나에 상태·shell·UI
  가 모이는 것이 자연스러웠다. WPF 에는 그 자리가 없다.
- **테스트가 버그가 있는 곳에 닿는다.** 전작은 테스트가 코드의 28% 였는데도 버그가
  이겼다. 버그가 사는 중심(WndProc)이 테스트 불가능한 물건이었기 때문이다.
  WPF 에서 그 중심은 평범한 C# 객체(ViewModel)다.
- 이미 커스텀 컨트롤(`Themes/Generic.xaml`)까지 쓰는 숙련 스택이다.
  전작에서는 스플리터·탭스트립·스크롤바를 손으로 다시 만들고 있었다.
- **뷰 모드 트레이드오프가 사라진다.** 전작은 속도를 위해 `LVS_OWNERDATA` 를 골랐고
  그 대가로 뷰 모드가 Details 하나에 갇혔다. WPF 는 `DataTemplate` 교체로 뷰가
  바뀌고 데이터 가상화는 유지된다.

**"WPF 는 무겁다" 에 대한 답**

| 우려 | 실제 |
|---|---|
| 렌더링이 느리다 | DirectX 리테인드 모드. GPU 가속된다 |
| 10만 항목 리스트 | `VirtualizingStackPanel` + Recycling 으로 처리. 단 깨뜨리는 패턴이 있다 (ARCHITECTURE §5) |
| 메모리 | 60~100MB 대. 논외 |
| **cold start** | **사실이다.** 유일한 실질 약점 → ADR-003 |

병목은 UI 가 아니라 I/O 와 shell 호출이며(아이콘 추출·네트워크 왕복·대용량 열거)
이는 스택과 무관하다. 전작에서 이미 풀어본 문제다 — 실제로 속도는 빨랐다.

**기각한 대안**

- WinUI 3 — Fluent 룩이 기본이지만 경험이 없고 패키징·도구체인이 까다롭다.
- Avalonia — 자체 렌더링이라 네이티브 느낌이 안 나고 Shell 연동 사례가 적다.
  조작감은 전작의 실패 원인 중 하나였다.
- Tauri/TypeScript — 네이티브 조작감·가상화·Shell COM 세 가지 모두 가장 불리.

---

## ADR-003 — cold start 는 상주 프로세스로 다룬다

**상태**: 확정

**근거**: 탐색기 대체 앱은 하루에 수십 번 띄운다. .NET + WPF 초기화 비용을 매번
낼 수 없다. single instance 로 한 번만 내고 이후 창은 재사용한다.
탐색기 자신이 같은 방식이다.

**대가**: 프로세스 수명 관리와 상태 누수 관리가 필요하다.

---

## ADR-004 — v1 축은 "여러 폴더 왕복" 하나

**상태**: 확정

**근거**: 기능 개수로는 탐색기를 이길 수 없다. 수십 년 쌓인 물건이다.
이길 수 있는 것은 **탐색기가 못 하는 한 가지**뿐이다.

전작은 탭·4분할·트리·필터·그룹핑·세션복원·자동업데이트를 전부 갖추고도
쓰이지 않았다. **기능 부족은 원인이 아니었다.**

---

## ADR-005 — v1 경계는 "탈출구 봉쇄 최소선"

**상태**: 확정

**근거**: 하나라도 못 하는 게 있으면 그때 탐색기를 열고, 한번 열면 그날은
계속 거기서 한다. 그래서 좁게 만들되 **하루치 루프에 구멍이 없어야** 한다.

이 때문에 범위가 좁은데도 shell 컨텍스트 메뉴·클립보드 호환·썸네일이
v1 에 포함된다. 이것들은 기능이 아니라 **탈출구 차단**이다.

---

## ADR-006 — 구조 게이트는 두 개만, 스크립트로 강제

**상태**: 확정 (강제 수단 정정)

1. 코드비하인드 금지 — `*.xaml.cs` 는 `InitializeComponent()` 만
2. 참조 방향 — `FlexDir.App` 은 `FlexDir.Shell` 을 참조하지 않는다 (`Host` 분리)

**강제 수단은 `scripts/check-structure.ps1` 이다.** `harness.config.json` 의
`verify.fast` 에 들어 있어 매 step 끝과 매 턴 끝에 돈다.

처음에는 2번을 "직접 참조 시 컴파일 에러" 라고 적었으나 **그것은 사실이 아니었다.**
컴파일이 막아주는 것은 *참조가 없는 상태* 뿐이고, `FlexDir.App.csproj` 에
`ProjectReference` 를 한 줄 추가하면 그냥 빌드된다. 게이트가 막으려던 **행위 자체는
아무것도 막고 있지 않았다.** 스크립트는 그 행위를 검사한다 — `App → Shell` 참조 추가,
`Core` 의 Windows 타겟·`UseWPF`, 코드비하인드에 들어온 로직.

**기각한 대안**: 파일 길이 상한, 테스트 커버리지 하한.
둘 다 **대리 지표**다. 숫자만 맞추고 본질을 비켜갈 수 있다.

---

## ADR-007 — 도그푸딩을 실행 가능한 게이트로 만든다

**상태**: 확정 (v1 완성 후 활성화)

**근거**: 전작 실패의 1원인은 "매일 안 씀" 이었다. 그리고 검증된 사실이 하나 더 있다 —
**실행 가능한 게이트가 있던 항목(성능)만 성공했고, 없던 셋(잔버그·조작감·네트워크)은
전부 실패했다.** "매일 쓰겠다" 는 의지 표명이지 게이트가 아니다.

앱이 사용 기록을 로컬에 남기면 스크립트로 읽히는 숫자가 된다.
지난 7일 중 5일 미만이면 새 기능 작업을 차단하고 버그 수정만 허용한다.

---

## ADR-008 — 전작의 shell 지식은 선행 추출 패스로 건진다

**상태**: 확정 (착수 0번)

**근거**: 전작의 shell 지식은 **문서가 아니라 코드에만** 있다. 문서는 전부 프로세스
기록(PDCA·마일스톤·handoff)이고, shell 엣지케이스는 `usage-guide.md` 에 한 줄이 전부다.

백지에서 시작하면 이 지식은 자동으로 따라오지 않는다. 그리고 **모르는 것은 찾을 수도
없다** — WPF 로 짜다 막혔을 때 "예전에 이거 풀었지" 를 떠올릴 수 있어야 한다.

산출물은 `docs/SHELL_NOTES.md` 이며 harness `guardrails` 에 등록해
모든 구현 세션이 참조하게 한다. 매 step 프롬프트에 복사되므로 **짧고 사실만** 담는다.

---

## ADR-009 — 자율 실행은 자동 채점 가능한 것에만 쓴다

**상태**: 확정

| 영역 | 방식 |
|---|---|
| Core · ViewModel 로직 | `/harness:run` 자율 |
| Shell interop | 수동 — 엣지케이스가 실물 검증을 요구 |
| UI 조작감 | 수동 + 매일 사용 |

**근거**: 조작감을 판정하는 셸 커맨드는 존재하지 않는다. 전작은 25 step 전부
자가 채점을 통과하고 리뷰에서 결함 11건이 나왔다. 자동 채점은 *지시한 것*만 보고
*지시하지 않은 것*은 보지 못한다.

---

## ADR-010 — 네트워크 경로는 v2, 단 경로 추상화는 v1 에서 준비

**상태**: 확정

**근거**: 네트워크는 백지 재작성에서 가장 위험한 항목이고(3주치 삽질을 다시 한다),
v1 축은 멀티페인이다. 다만 나중에 얹으려면 경로 타입이 처음부터 열려 있어야 한다.

v1 은 `LocationId` 로 경로를 주고받고 로컬 파일시스템만 담는다.
v2 에서 UNC·PIDL 을 같은 타입으로 확장한다. 이 추상화가 없으면 v2 는 전면 수정이 된다.

---

## ADR-011 — 외부 변경은 감시로 자동 갱신한다

**상태**: 확정

**근거**: 진실원천은 파일시스템이고 메모리 목록은 캐시다. stale 목록은 잔버그의
주요 원천이며, 잔버그는 전작 실패 원인 중 하나였다.

`IFolderWatcher` 로 감지해 갱신하되 **선택과 스크롤 위치를 유지한다.**
갱신 때마다 선택이 풀리면 감시가 없느니만 못하다.

---

## ADR-012 — TDD Guard 에 C# 지원을 추가한다

**상태**: **완료** (harness 0.2.0)

**맥락**: harness 플러그인의 `tdd-guard.mjs` 는 `ts·go·py·rs` 만 안다.
`.cs` 는 `if (!langKey) allow()` 로 무조건 통과한다. 즉 `tdd.enabled: true` 로
켜놔도 **이 프로젝트에서는 아무것도 막지 못한다.**

**근거**: 하네스 자신의 원칙 — *"게이트는 꺼져 있는 것보다 켜져 있는 것처럼
보이는 것이 위험하다."* 확정한 게이트가 거짓으로 시작해서는 안 된다.

**조치 (완료)**: `claude-harness-plugin` 에 `cs` 지원을 넣고 0.2.0 으로 올렸다.

- 테스트 위치: `<프로젝트>.Tests/<같은 경로>/FooTests.cs` (평평한 배치와 `tests/` 하위 모음도 인정)
- `dotnet test` 는 파일 경로를 받지 않으므로 러너가 인자를 직접 계산하는
  `target()` 계약을 추가했다. **테스트 프로젝트만** 지정해 솔루션 전체 빌드를 피한다.
- 검사 제외: `*.g.cs` · `*.Designer.cs` · `*.generated.cs` · **`*.xaml.cs`** ·
  `AssemblyInfo.cs` · `GlobalUsings.cs` · `Program.cs` · `bin`/`obj`.
  코드비하인드는 `InitializeComponent` 만 있어야 정상이므로 테스트 대상이 아니다.

`harness.config.json` 의 `tdd` 를 켰다.

**남은 한계**: 콜드 `dotnet test` 는 기본 `timeoutMs`(60초)를 넘길 수 있고, 넘기면
**차단이 아니라 조용한 통과**가 된다. 그 조짐이 보이면 `verifyRed: false` 로 내리고
Quality Gate 에 맡긴다. 방치하면 이 ADR 이 막으려던 상태로 되돌아간다.

---

## ADR-013 — MVVM 기반은 `CommunityToolkit.Mvvm` (8.4.2)

**상태**: 확정

**맥락**: `FlexDir.App` 의 **유일한 외부 런타임 의존**이며 ViewModel 전체가 그 위에 선다 —
`PaneViewModel`·`WorkspaceViewModel`·`PaneSelection`·`FileItemViewModel` 넷 모두
`ObservableObject` 를 상속하고, 커맨드 14개가 `[RelayCommand]` 소스 생성기에서 나온다.
버전은 `src/FlexDir.App/FlexDir.App.csproj` 에 고정한다.

**근거**

- **ADR-002 가 이것에 의존한다.** "MVVM 이 god object 싱크홀을 제거한다" 는 근거는 ViewModel 을
  쓰는 비용이 낮을 때만 성립한다. `INotifyPropertyChanged` 와 `ICommand` 를 손으로 쓰면 속성마다
  대여섯 줄이 붙고, 그러면 상태를 ViewModel 에 두는 것보다 코드비하인드에 두는 것이 싸 보인다 —
  전작이 무너진 방향이 정확히 그쪽이다.
- **소스 생성기다. 런타임 리플렉션이 없다.** cold start 가 WPF 의 유일한 실질 약점이므로
  (ADR-003) 시작 시각에 값을 치르는 물건을 넣을 수 없다.
- **UI 프레임워크 중립이다.** WPF 타입을 끌고 오지 않아 `App` 이 `Core` 의 포트만 아는 규칙을
  침범하지 않는다 (`CLAUDE.md` §1). `PaneSelection` 이 `ListView.SelectedItems` 를 모르는 것과
  같은 선이다.
- Microsoft 관리(.NET Foundation), MIT, 전이 의존 없음.

**대가**

- 외부 의존이 0 개에서 1 개가 된다. 이 ADR 이 그 값을 명시적으로 치른 기록이다.
- 생성된 멤버(`CopySelectionCommand` 등)가 소스에 보이지 않는다. 처음 읽는 사람은
  `[RelayCommand]` 를 모르면 그 이름을 찾을 수 없다.
- 모든 ViewModel 이 `partial` 이어야 한다.

**기각한 대안**

- **손으로 구현** — `SetProperty` 와 `RelayCommand` 를 직접 쓰면 결국 같은 물건을 다시 만든다.
  의존을 줄이는 대신 우리가 유지보수할 코드가 늘고, 그쪽이 더 비싸다.
- **Prism** — DI 컨테이너·모듈·네비게이션까지 함께 온다. v1 축은 멀티페인 하나이고(ADR-004)
  화면 구조는 창 하나에 페인 둘이다. 네비게이션 프레임워크를 쓸 자리가 없다.
- **ReactiveUI** — Rx 조합은 강력하지만 학습·디버깅 비용이 크고, 우리 상태 변화는
  "폴더를 열고 목록을 채운다" 로 대부분 선형이다. 스트림으로 표현해 얻는 것이 없다.

---

## ADR-014 — `IFolderSource` 는 `FileSystemEnumerator<T>` 로 구현한다

**상태**: 확정 (구현 완료 — `src/FlexDir.Shell/Enumeration/FileSystemFolderSource.cs`)

**맥락**: `docs/SHELL_NOTES.md` §열거 는 `FindFirstFileExW` 를 직접 P/Invoke 하라고
지시한다. 전작에서 속도의 핵심이었고, 근거는 둘이었다 — `FindExInfoBasic` 으로 8.3 단축
이름 조회를 건너뛰는 것, 그리고 `Directory.EnumerateFiles` 가 그 플래그를 못 주고 항목별
예외 기반이라는 것. **구현은 그 지시를 따르지 않고 `System.IO.Enumeration.FileSystemEnumerator<T>`
를 썼다.** guardrail 문서와 어긋난 선택이므로 여기 남긴다.

**근거**

- **`Directory.EnumerateFiles` 가 아니라 그 밑의 primitive 다.** SHELL_NOTES 가 기각한 것은
  `Directory.EnumerateFiles` 이고, 이 API 는 그것이 내부적으로 서는 저수준 층이다. Windows 에서
  `NtQueryDirectoryFile` 로 내려가며 **8.3 이름을 아예 묻지 않는다.** 즉 P/Invoke 를 지시한
  첫 근거는 이 API 에서 이미 충족된다.
- **항목별 예외가 없다.** `ContinueOnError` 로 열거 중 오류를 값으로 받는다. 두 번째 근거도
  사라진다.
- **unsafe 코드와 150줄 interop 을 치르지 않는다.** `WIN32_FIND_DATAW` 마샬링·핸들 수명·
  `SafeFindHandle` 을 우리가 유지보수하지 않는다. `FlexDir.Shell` 에서 손으로 쓴 P/Invoke 는
  대체 API 가 없는 것(`SHGetFileInfoW`·`IShellItemImageFactory`·`IFileOperation`)에만 남긴다.
- 실측이 뒷받침한다: `C:\Windows\WinSxS` 24,115개에서 첫 항목 **14.8ms** / 전체 73.6ms.
  `docs/PRD.md` §5 의 첫 항목 150ms 대비 10배 여유다.

**포기한 것**

- **shell 네임스페이스(내 PC · 네트워크 · 라이브러리)를 열거할 수 없다.** 이 API 는
  파일시스템 경로만 안다. v1 은 로컬만 다루므로(ADR-010) 지금은 손해가 아니다.
- v2 에서 PIDL 열거가 필요해지면 `IShellFolder::EnumObjects` 경로를 **따로** 세운다 —
  이 클래스를 확장하는 것이 아니라. 두 열거는 입력 타입부터 다르고, 섞으면
  `LocationId` 추상화가 구현 하나에 눌린다.

---

## ADR-015 — 실물 확인용 프로브는 `.harness/probe/` 에 두고 sln 밖에 둔다

**상태**: 확정

**맥락**: Shell 포트 8개는 자동 채점할 수 없다(ADR-009). 검증은 실물에서 손으로 하는데,
그 손이 쥘 도구가 필요하다 — 실제 폴더를 열거하고, 감시하고, 휴지통에 넣어 보는 콘솔 앱.
지난 세션에는 scratchpad 에 만들어 썼고 세션이 끝나며 사라졌다. 남은 포트 4개
(`IThumbnailSource`·`IItemActivator`·`IFileOperations`·`IClipboardBridge`)도 전부 같은
검증이 필요하다.

**`docs/ARCHITECTURE.md` §7 과의 관계**: §7 이 금지한 것은 **"실사용과 무관한 것을 재는
벤치마크 CLI"** 다. 전작은 그것을 만들고 실제 사용 경로가 아닌 숫자를 쟀다. 프로브는 재는
물건이 아니라 **포트 구현체를 실물 파일시스템에 물려 보는 손잡이**이고, 성능 계측은 §7 대로
앱 안에 남는다. 목적이 다르므로 §7 을 어기지 않는다 — 다만 겉모습이 같아 오해를 부르므로
이 ADR 로 선을 긋는다.

**결정**

- 위치는 `.harness/probe/`. `docs/` 도 `src/` 도 아니다 — 제품이 아니라 검증 도구다.
- **`FlexDir.sln` 에 넣지 않는다.** sln 이 프로젝트를 명시 열거하므로 게이트의
  `dotnet build` · `dotnet test` · `check-structure.ps1` 이 프로브를 보지 않는다.
  프로브가 깨져도 게이트는 초록이고, 그래야 프로브가 제품 코드의 제약을 끌어오지 않는다.
- `harness.config.json` 의 `tdd.exclude` 에 넣는다. 검증 도구에 TDD 를 요구하면
  "테스트를 검증하기 위한 테스트" 가 된다.
- 프로브는 `FlexDir.Shell` 과 `FlexDir.Core` 를 `ProjectReference` 로 직접 참조한다.
  검증 대상이 그 둘이다.
- **프로브에서 확인한 것은 `.harness/manual-plan.md` 의 사람 확인 항목에 결과를 적는다.**
  프로브가 있어도 기록이 없으면 다음 세션이 다시 확인해야 한다.

**대가**: 저장소에 빌드되지 않는 프로젝트가 하나 생긴다. sln 밖이라 IDE 가 열어 주지 않고,
`dotnet run --project .harness/probe` 로만 돈다. 리팩터링이 프로브를 깨뜨려도 게이트가
알려주지 않는다 — 다음에 쓸 때 알게 된다. 매 세션 다시 만드는 비용보다 싸다고 봤다.
