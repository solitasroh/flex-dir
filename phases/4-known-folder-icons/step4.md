# Step 4: release-0.8.1

## 읽어야 할 파일

- `CLAUDE.md` §7(커밋)
- `.harness/HANDOFF.md` — **§배포 절차** 와 **§현재 상태** (버전 0.8.0 · 테스트 총계 · 포트 17/17)
- `.harness/HISTORY.md` §배포 기록 — **v0.8.0 항목**의 형식. 그 형식을 따른다
- `docs/PRD-v2.md` **§20 툴바 오버플로와 알려진 폴더** — 이 step 이 절 안에 소절을 더한다.
  §알려진 폴더 다섯 · §값을 치르고 배운 것 이 어디인지 확인하라
- `docs/DESIGN.md` §7(아이콘) — *"파일·폴더 아이콘은 디자인 대상이 아니다"* 원칙과
  **탭 아이콘 예외**가 적힌 인용 블록
- `docs/ARCHITECTURE.md` — 포트 목록 (`IThumbnailSource` 행이 있다)
- `scripts/pack.ps1` — 머리 주석에 버전 정본과 자동 업데이트 규칙이 있다
- `Directory.Build.props` — `<Version>` 이 버전의 정본이다. 지금 `0.8.0` 이다
- 이 phase 의 step 0~3 지시서와 `phases/4-known-folder-icons/index.json` 의 각 `summary`

## 배경

v0.8.1 의 마감이다. **코드는 더 쓰지 않는다** — 문서·버전·패키지만 한다.

v0.8.1 이 싣는 것: **알려진 폴더 메뉴 항목의 실제 shell 아이콘** (사용자 요청 2026-08-21).
v0.8.0 이 나간 직후의 후속이라 새 절이 아니라 **§20 안의 소절**로 들어간다.

## 작업

### 1. Release 빌드부터 통과시킨다

```bash
dotnet build -c Release --nologo -warnaserror
```

여기서 깨지면 **문서를 쓰기 전에** 고쳐라.

> 저장소 산출물로 앱을 띄워 두었다면 먼저 죽여라 — 자기가 띄워진 산출물을 잠근다
> (`.harness/HANDOFF.md` §배포 절차 ①).
> `Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue`
> **설치본(`%LOCALAPPDATA%\flex-dir\current`)은 저장소를 잠그지 않으므로 죽이지 않아도 된다.**

### 2. `docs/PRD-v2.md` §20 에 소절을 더한다

§알려진 폴더 다섯 **바로 뒤**에:

**`### 항목 아이콘은 shell 에서 온다 (사용자 요청 2026-08-21 · v0.8.1)`**

담을 것:

- **무엇이 보이나** — 다섯 항목에 Windows 가 그 폴더에 붙여 둔 아이콘이 뜬다. 다운로드의
  화살표, 사진의 그림처럼 알려진 폴더는 저마다 다르다.
- **왜 새 포트가 아니라 `IThumbnailSource` 인가** — 기존 메서드 둘로는 안 됐다:
  `GetThumbnailAsync` 는 `SIIGBF_THUMBNAILONLY` 라 폴더에서 언제나 `null` 이고,
  `GetTypeIconAsync` 는 확장자 + `SHGFI_USEFILEATTRIBUTES` 라 **모든 폴더가 같은 아이콘**
  이다. 그래서 `GetItemIconAsync`(경로를 보는 조회)가 늘었다.
  **`IKnownFolderList` 에 싣지 않은 이유**도 적어라 — 그러면 그쪽이 STA 워커와
  `IDisposable` 을 들여야 하고 이미지 리스트·BGRA 변환이 두 벌이 된다.
  *"어디에 있나"* 와 *"어떻게 생겼나"* 는 다른 포트다.
- **비용** — 다섯 번, 프로세스당 한 번, 배경 스레드. **경로를 보는 조회라 목록 항목마다
  부르면 안 된다** (대용량 폴더에서 확장자마다 한 번이라는 승리가 무너진다).
- **아이콘 없이 먼저 뜬다** — 다 읽을 때까지 메뉴를 비워 두지 않는다.
  아이콘을 못 읽은 항목도 **목록에 남는다** (거르는 기준은 경로 하나다).
- **32 를 받아 16 에 그린다** — 150% 배율에서 16 원본은 뭉갠다.

### 3. `docs/DESIGN.md` §7

**탭 아이콘 예외를 적어 둔 인용 블록에 한 문단을 덧붙여라.** 지금 그 블록은 *"규칙대로
shell 아이콘을 쓰려면 `PaneViewModel` 이 자기 폴더의 아이콘을 내야 하는데 그것이 저장소
호출이다"* 라고 적고 탭에서만 글리프로 도망친 이유를 든다.

**알려진 폴더 메뉴는 그 계산이 반대라 규칙대로 shell 아이콘을 쓴다**는 것을 적어라 —
탭은 8개까지 열리고 배경 탭에서도 비용이 나가지만, 알려진 폴더는 **다섯 고정 · 프로세스당
한 번 · 열려 있는 메뉴 안**이다. **예외의 기준은 "shell 아이콘이냐 글리프냐" 가 아니라
"그 비용이 몇 번 곱해지느냐" 라는 것**이 이 두 자리를 나란히 놓아야 보인다.

### 4. `docs/ARCHITECTURE.md`

`IThumbnailSource` 행에 `GetItemIconAsync` 가 늘었다는 것을 한 줄 적어라 — 셋이 각각
무엇을 답하는지(내용 미리보기 / 확장자별 형식 아이콘 / 경로별 항목 아이콘)가 갈려야 한다.

### 5. `.harness/HANDOFF.md`

**살아 있는 것만 둔다** (그 파일 머리의 경고 — 배포 기록은 `HISTORY.md` 다).

- §현재 상태 — 버전 **0.8.1** · **테스트 총계를 실제 실행 결과로** 갱신
- §다음 작업 — **2구간(v0.9.0) 외부 도구**는 그대로 두고, **1구간 사람 확인 중 아직 안
  끝난 것**을 적어라: 라이트 테마 대비 · 창 900 언저리 4분할에서 `»` 접힘 · 페인 우클릭
  크래시 없음(§10 전례) · 📍 실루엣 판정
- §포트 현황 — 포트 **개수는 그대로 17/17** 이다 (메서드가 는 것이지 포트가 는 것이 아니다).
  헷갈리지 않게 `IThumbnailSource` 행에 메서드 셋을 적어라

### 6. `Directory.Build.props`

`<Version>` 을 **`0.8.1`** 로 올린다. 자동 업데이트는 *"설치된 버전 < 피드의 최신 버전"*
하나로 돈다 — 올리지 않으면 아무도 갱신되지 않는다.

### 7. `.harness/HISTORY.md`

§배포 기록에 **v0.8.1** 항목을 v0.8.0 과 같은 형식으로 추가한다:
날짜(2026-08-21) · 실은 것(알려진 폴더 항목 아이콘) · 서명 지문 · 게이트 결과 ·
**v0.8.0 의 사람 확인이 아직 안 끝난 채로 그 위에 얹혔다는 것.**

### 8. 패키지를 굽는다

```bash
pwsh -File scripts/pack.ps1 -NoUpload -SignThumbprint 81DB2944731E8C0ADA660C7F49E44EDA042B712C
```

- **`-NoUpload` 는 필수다.** 빼면 `gh release create` 가 실제로 GitHub 에 릴리스를 만든다 —
  되돌리기 어렵고 바깥으로 나가는 일이라 사용자 승인 없이 하지 않는다.
- 지문 `81DB2944731E8C0ADA660C7F49E44EDA042B712C` 은 이 기계 `Cert:\CurrentUser\My` 의
  코드서명 인증서다. **주체가 `CN=Rootech` 인 인증서가 둘 있으므로 지문으로 골라야 한다.**
- 서명 뒤 `Get-AuthenticodeSignature` 가 `UnknownError` 를 내는 것은 **정상이다** —
  서명은 붙어 있고 이 기계의 신뢰 루트에 자체 서명 인증서가 없어서다.

### 9. `git push` 를 하지 마라

푸시는 사용자 지시가 있을 때만 한다.

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
dotnet build -c Release --nologo -warnaserror
```

넷 다 exit 0, 빌드 둘 다 **경고 0**. 테스트 총계가 **2073 보다 커야 한다.**

그리고:

```bash
grep -c '<Version>0\.8\.1</Version>' Directory.Build.props   # 1 이어야 한다
grep -c 'GetItemIconAsync' docs/PRD-v2.md docs/ARCHITECTURE.md
ls artifacts/packages/flex-dir-win-Setup.exe
```

## 검증 절차

1. 위 AC 커맨드를 전부 실행한다.
2. 체크리스트:
   - `git status` 에 `.harness/HANDOFF.md` · `.harness/HISTORY.md` · `docs/PRD-v2.md` ·
     `docs/DESIGN.md` · `docs/ARCHITECTURE.md` · `Directory.Build.props` 여섯이 올라와 있는가?
   - HANDOFF 의 **테스트 총계가 실제로 실행한 숫자**인가? (2073 을 그대로 두면 실패다)
   - HANDOFF 의 포트 개수를 **17/17 그대로** 두었는가? (메서드가 는 것이지 포트가 는 것이 아니다)
   - `git log` 에 `Co-Authored-By` 나 `Claude-Session` 트레일러가 없는가?
   - 릴리스가 GitHub 에 **올라가지 않았는가**? (`-NoUpload` 를 썼는가)
3. `phases/4-known-folder-icons/index.json` 의 step 4 를 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 **최종 테스트 총계**와 **패키지 산출물 경로**를 적어라 — 사람 확인이 그
설치본으로 v0.8.0 의 남은 항목까지 함께 밟는다.

## 금지사항

- **코드를 고치지 마라** (`src/` 아래). 이유: 기능은 step 3 에서 끝났다. Release 빌드가
  깨졌다면 그것만 최소로 고치고 **무엇을 왜 고쳤는지 `summary` 에 적어라.**
- **`git push` 를 하지 마라.** 이유: 푸시는 사용자 지시가 있을 때만이다.
- **`pack.ps1` 을 `-NoUpload` 없이 돌리지 마라.** 이유: `gh release create` 가 실제
  GitHub 릴리스를 만든다.
- **설치본을 설치하거나 실행하지 마라.** 이유: 그것은 사람 확인이고, 상주 앱이 뜨면
  저장소 산출물을 잠가 다음 빌드가 깨진다.
- **`docs/` 의 기존 문장을 지우지 마라.** 이유: 늘리기만 한다. 틀린 진술을 발견하면
  지우지 말고 고친 이유를 함께 적어라.
- **`.harness/HANDOFF.md` 에 얼어붙은 기록을 넣지 마라.** 이유: 그 파일이 1,094줄까지
  자라 살아 있는 것이 역사에 묻혔다. 배포 기록은 `HISTORY.md` 다.
- **`docs/PRD-v2.md` 에 §21 을 만들지 마라.** 이유: 이것은 §20 이 실은 기능의 후속이다.
  절을 가르면 알려진 폴더 이야기가 두 곳으로 나뉜다.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
