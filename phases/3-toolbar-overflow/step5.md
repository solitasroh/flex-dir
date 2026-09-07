# Step 5: release-0.8.0

## 읽어야 할 파일

- `CLAUDE.md` §7(커밋)
- `.harness/HANDOFF.md` — **§배포 절차** 와 **§현재 상태**. 이 step 이 그 절차를 그대로 밟는다
- `.harness/HISTORY.md` §배포 기록 — v0.7.0 항목이 어떤 형식인지. 그 형식을 따른다
- `docs/PRD-v2.md` — 마지막 절이 `§19 다크모드` 다 (1745줄). 이 step 이 **§20** 을 붙인다
- `docs/ADR.md` — 마지막이 `ADR-020` 이다 (572줄). 이 step 이 **ADR-021** 을 붙인다
- `docs/DESIGN.md` §1(레이아웃) · §7(아이콘 표)
- `docs/ARCHITECTURE.md` — 포트 목록이 있는 절
- `scripts/pack.ps1` — 머리 주석에 버전 정본과 자동 업데이트 규칙이 있다
- `Directory.Build.props` — `<Version>` 이 버전의 정본이다
- 이 phase 의 step 0~4 지시서 (`phases/3-toolbar-overflow/step0.md` ~ `step4.md`) 와
  `phases/3-toolbar-overflow/index.json` 의 각 `summary` — 무엇이 만들어졌는지의 정본

## 배경

1구간(v0.8.0)의 마감이다. **코드는 더 쓰지 않는다** — 문서·버전·패키지만 한다.

## 작업

### 1. Release 빌드부터 통과시킨다

```bash
dotnet build -c Release --nologo -warnaserror
```

여기서 깨지면 **문서를 쓰기 전에** 고쳐라. Debug 만 통과하고 Release 가 깨지는 일이 있다.

> 저장소 산출물로 앱을 띄워 두었다면 **먼저 죽여라** — 자기가 띄워진 산출물을 잠그고
> 빌드가 오류 수십 개를 낸다 (`.harness/HANDOFF.md` §배포 절차 ①).
> `Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue`

### 2. `docs/ADR.md` — ADR-021 을 붙인다

제목: **`## ADR-021 — 툴바 오버플로는 무리 단위로 접고, » 메뉴는 따로 선언한다`**

기존 ADR 들과 같은 형식(맥락 · 결정 · 근거 · 대안과 기각 사유 · 대가)으로 쓴다.
반드시 담을 것 셋:

1. **살아 있는 `UIElement` 를 `»` 로 옮기지 않는다.** 논리 트리 부모가 하나뿐이라
   스타일·`DynamicResource`·바인딩이 재평가되며 `ToolButton` 크롬이 `MenuItem` 안에서
   깨진다. **대가를 명시하라**: 툴바와 `»` 에 같은 항목이 두 벌 선언된다. 그것을 감수하는
   이유는 `Command` 가 같은 ViewModel 을 보므로 진실원천은 하나이기 때문이다.
2. **접기는 버튼 하나가 아니라 `FoldOrder` 무리 단위다.** 뷰 모드 4개가 하나씩 사라지면
   남은 것이 무슨 뜻인지 알 수 없고 구분자가 고아로 남는다.
3. **자연 폭을 캐시한다.** `Collapsed` 인 자식은 `DesiredSize` 가 0 이라, 그 0 을 다시
   읽으면 펼쳤다 접었다 **진동한다.**

기각한 대안도 적어라: `ScrollViewer`(무한 폭으로 측정해 판정이 발동하지 않는다) ·
`DockPanel` 오른쪽 정렬(`»` 의 가시성이 한 프레임 늦어 진동한다).

### 3. `docs/PRD-v2.md` — §20 을 붙인다

제목: **`## §20 툴바 오버플로와 알려진 폴더 (2026-08-21 · 사용자 결정)`**

§19(다크모드) 뒤에 붙인다. §17·§18·§19 의 형식을 따른다. 담을 것:

- **왜 들어왔나** — 페인이 1~4분할이라(ADR-019) 좁은 페인에서 툴바가 잘린다
- **접힘 규칙** — 안 접히는 것(이동 `E72B`/`E72A`/`E74A` · 새로 고침 `E72C` ·
  알려진 폴더 `E707`)과 접힘 순서(외부 도구 → 분류 → 뷰 모드). `FoldOrder 1` 은 2구간의
  외부 도구를 위해 비워 두었다는 것도 적어라
- **알려진 폴더 다섯** — 홈 · 바탕화면 · 문서 · 다운로드 · 사진. **설정 없음** ·
  경로가 빈 항목은 메뉴에서 뺀다 · 선택하면 **현재 탭에서 이동**하고 뒤로가기에 쌓인다 ·
  **단축키 없음**(ADR-017 이 분류에 내린 것과 같은 판단)
- **`E700` 이 아니라 `E707` 인 이유** — `E700` 은 타이틀바 폴더 트리 토글이 이미 쓰고 있고
  4분할이면 창에 다섯 개가 된다. 그리고 ☰ 는 DESIGN §7 이 금지한 **선 계열**이다.
  **사용자 확정 2026-08-21**
- **§값을 치르고 배운 것** 이 §17 에 있는 것처럼, 이번 구간이 배운 것도 한 문단 —
  `Collapsed` 자식의 `DesiredSize` 가 0 이라 자연 폭을 캐시하지 않으면 진동한다는 것
- **2구간(v0.9.0)에 남긴 것** — 외부 도구 `[VS]`·`[>_]` 가 `FoldOrder 1` 로 들어온다

### 4. `docs/DESIGN.md`

**§7 아이콘 표에 한 줄 추가:**

| 버튼 | 글리프 |
|---|---|
| 알려진 폴더 (드롭다운) | `E707` |

그리고 §7 의 인용 블록(`E8A6`→`E15B`, `E8FD`→`F168`→`E8EC` 를 두 번 틀린 기록)에
**이번 판단을 한 문단 덧붙여라**: 이 툴바에는 계열이 둘(선·사각)인데 ☰(`E700`)는 선
계열이고 **타이틀바에 이미 있었다**. `E707`(MapPin)은 물방울+원이라 어느 계열도 아니다.
**"툴바 아이콘은 툴바 맥락에서, 실제 크기로, 그리고 창 전체에 이미 있는 글리프와 함께
본다"** 로 규칙을 한 겹 늘려라 — 앞의 두 번은 툴바 안만 봤다.

**§1 레이아웃에** 툴바가 이제 폭에 따라 접힌다는 것을 한 문단 적어라. §8 밀도 검증의
크롬 높이 계산(툴바 36)은 **바뀌지 않는다** — 접히는 것은 가로이지 세로가 아니다.

### 5. `docs/ARCHITECTURE.md`

포트 목록에 `IKnownFolderList` 를 **한 줄 추가**한다 —
구현은 `Shell/Locations/KnownFolderList.cs`, "왜 따로인가" 는 *"알려진 폴더 조회는 저장소에
닿는다. 리디렉션된 폴더(OneDrive·도메인 로밍)에서는 네트워크로 내려간다"* 로 적는다.
COM 이 아니라 STA 도 정리도 필요 없다는 것(= `AppComposition` 정리 목록에 없다)도 적어라.

### 6. `.harness/HANDOFF.md`

**살아 있는 것만 둔다** (그 파일 머리의 경고). 갱신할 것:

- §현재 상태 — 브랜치 · 버전 **0.8.0** · **테스트 총계를 실제 실행 결과로** 갱신
  (2028 이 아니라 지금 값)
- §포트 현황 — **16/16 → 17/17** 로 올리고 `IKnownFolderList` 를 표에 넣는다
- §다음 작업 — **2구간(v0.9.0) 외부 도구**를 적는다:
  포트 둘(`IExternalToolCatalog` 탐지 · `IExternalToolLauncher` 실행) ·
  `[VS]`(App Paths 의 `code.exe`) · `[>_]`(프리셋 5 + 사용자 지정, `{path}` 치환) ·
  `\\server` 루트에서만 비활성 · 실패는 `StatusText` 한 줄 · `FoldOrder 1` 자리는 비어 있다
- §한 줄 — 이번 구간을 한 문단 추가

### 7. `Directory.Build.props`

`<Version>` 을 **`0.8.0`** 으로 올린다. 이것이 버전의 정본이고, 자동 업데이트는
*"설치된 버전 < 피드의 최신 버전"* 하나로 돈다 — 올리지 않으면 아무도 갱신되지 않는다.

### 8. `.harness/HISTORY.md`

§배포 기록에 **v0.8.0** 항목을 v0.7.0 과 같은 형식으로 추가한다. 담을 것:
날짜(2026-08-21) · 실은 것(툴바 오버플로 · 알려진 폴더 메뉴 `E707`) · 서명 지문 ·
게이트 결과 · **아직 사람 확인이 안 끝났다는 것**.

### 9. 패키지를 굽는다

```bash
pwsh -File scripts/pack.ps1 -NoUpload -SignThumbprint 81DB2944731E8C0ADA660C7F49E44EDA042B712C
```

- **`-NoUpload` 는 필수다.** 빼면 `gh release create` 가 실제로 GitHub 에 릴리스를 만든다 —
  되돌리기 어렵고 바깥으로 나가는 일이라 사용자 승인 없이 하지 않는다.
- 지문 `81DB2944731E8C0ADA660C7F49E44EDA042B712C` 은 이 기계 `Cert:\CurrentUser\My` 의
  코드서명 인증서다. **주체가 `CN=Rootech` 인 인증서가 둘 있으므로 지문으로 골라야 한다.**
- 서명 뒤 `Get-AuthenticodeSignature` 가 `UnknownError` 를 내는 것은 **정상이다** —
  서명은 붙어 있고 이 기계의 신뢰 루트에 자체 서명 인증서가 없어서다
  (`.harness/HISTORY.md` §v0.7.0).
- 지문이 없는 기계라면 `-SignThumbprint` 없이 굽는다. `vpk` 가
  `N file(s) will not be signed` 로 경고하고 그대로 진행한다 — **그것은 실패가 아니다.**

### 10. `git push` 를 하지 마라

푸시는 사용자 지시가 있을 때만 한다 (`.harness/HANDOFF.md` §현재 상태 · CLAUDE.md §7).

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
```

넷 다 exit 0, 빌드 둘 다 **경고 0**. 테스트 총계가 **2028 보다 커야 한다.**

그리고:

```bash
grep -c '<Version>0\.8\.0</Version>' Directory.Build.props   # 1 이어야 한다
grep -c 'ADR-021' docs/ADR.md                                 # 1 이상
grep -c '§20' docs/PRD-v2.md                                  # 1 이상
grep -c 'E707' docs/DESIGN.md                                 # 1 이상
```

## 검증 절차

1. 위 AC 커맨드를 전부 실행한다.
2. 체크리스트:
   - `git status` 에 `.harness/HANDOFF.md` · `.harness/HISTORY.md` · `docs/ADR.md` ·
     `docs/PRD-v2.md` · `docs/DESIGN.md` · `docs/ARCHITECTURE.md` ·
     `Directory.Build.props` 일곱이 전부 올라와 있는가?
   - HANDOFF 의 **테스트 총계가 실제로 실행한 숫자**인가? (2028 을 그대로 두면 실패다)
   - HANDOFF 의 포트 현황이 **17/17** 인가?
   - `git log` 에 `Co-Authored-By` 나 `Claude-Session` 트레일러가 없는가?
   - 릴리스가 GitHub 에 **올라가지 않았는가**? (`-NoUpload` 를 썼는가)
3. `phases/3-toolbar-overflow/index.json` 의 step 5 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 **최종 테스트 총계**와 **패키지 산출물 경로**를 적어라 — 사람 확인이 그
설치본으로 12번(서명된 설치본이 실제로 뜨는가)을 밟는다.

## 금지사항

- **코드를 고치지 마라** (`src/` 아래). 이유: 1구간의 기능은 step 4 에서 끝났다. 여기서
  고치면 그 변경은 아무 테스트도 통과하지 않은 채 배포본에 실린다. Release 빌드가
  깨졌다면 그것만 최소로 고치고 **무엇을 왜 고쳤는지 `summary` 에 적어라.**
- **`git push` 를 하지 마라.** 이유: 푸시는 사용자 지시가 있을 때만이다.
- **`pack.ps1` 을 `-NoUpload` 없이 돌리지 마라.** 이유: `gh release create` 가 실제
  GitHub 릴리스를 만든다. 되돌리기 어렵고 바깥으로 나가는 일이다.
- **설치본을 설치하거나 실행하지 마라.** 이유: 그것은 사람 확인 항목(12번)이고, 상주 앱이
  뜨면 저장소 산출물을 잠가 다음 빌드가 깨진다.
- **`docs/` 의 기존 문장을 지우지 마라.** 이유: `.harness/HISTORY.md` 와 `docs/` 는 얼어붙은
  기록이다. 늘리기만 한다. 틀린 진술을 발견하면 지우지 말고 **고친 이유를 함께 적어라.**
- **`.harness/HANDOFF.md` 에 얼어붙은 기록을 넣지 마라.** 이유: 그 파일이 1,094줄까지
  자라 살아 있는 것이 역사에 묻혔다 (파일 머리 경고). 배포 기록은 `HISTORY.md` 다.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
