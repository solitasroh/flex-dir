# Step 0: known-folder-port

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(참조 방향) · §6(코드를 쓰는 순서)
- `docs/ARCHITECTURE.md` — 포트/어댑터 구조
- `src/FlexDir.Core/Storage/IDriveList.cs` — **이 step 이 그대로 따라 쓸 본보기다.**
  값 레코드 + 포트 인터페이스가 한 파일에 있고, `ValueTask<IReadOnlyList<T>> ListAsync(CancellationToken)`
  하나만 낸다
- `src/FlexDir.Core/Storage/IDriveSpace.cs` — "실패는 던지지 않는다" 를 XML 주석으로 못 박은 본보기
- `src/FlexDir.Core/Locations/LocationId.cs` — `LocationId` 의 생성/파싱 방법
- `tests/FlexDir.Core.Tests/Fakes/FakeDriveList.cs` — fake 의 형태
- `tests/FlexDir.Core.Tests/Storage/IDriveListTests.cs` (있으면) — 포트만 있는 파일의 테스트가
  무엇을 채점하는지

## 배경 — 이 포트가 왜 필요한가

페인 툴바 오른쪽 끝에 **알려진 폴더 메뉴**(글리프 `E707` 핀)가 들어간다. 홈 · 바탕화면 ·
문서 · 다운로드 · 사진 다섯 곳으로 현재 탭을 이동시킨다. 설정은 없다 — 다섯 개 고정이다.

경로를 구하는 것은 `Environment.GetFolderPath` 와 `SHGetKnownFolderPath` 이고 **둘 다
Windows API 다.** `FlexDir.Core` 는 Windows API 를 참조하지 않으므로(CLAUDE.md §1) 포트로
가른다. 구현체 `KnownFolderList` 는 **step 3 에서 `FlexDir.Shell` 에 만든다 — 이 step 에서는
만들지 않는다.**

## 작업

### 1. 포트 파일

`src/FlexDir.Core/Locations/IKnownFolderList.cs` 하나에 아래 셋을 전부 담는다.
디렉터리 `src/FlexDir.Core/Locations/` 는 **이미 존재한다** (`LocationId.cs` · `PathSegments.cs`).

```csharp
namespace FlexDir.Core.Locations;

/// 다섯 고정. 순서가 곧 메뉴에 뜨는 순서다.
public enum KnownFolderKind
{
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
}

/// 알려진 폴더 하나. Location 이 null 이면 이 기계에 그 폴더가 없다는 뜻이다.
public readonly record struct KnownFolder(KnownFolderKind Kind, string Label, LocationId? Location);

public interface IKnownFolderList
{
    ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken ct);
}
```

**`Label` 을 구현체가 채우는 이유**를 XML 주석에 적어라: 표시 문자열이 Core 에 박히면
`KnownFolderKind` 하나를 늘릴 때 두 곳을 고쳐야 하고, OS 가 실제로 쓰는 이름
(사용자가 바탕화면을 이름변경했을 수도 있다)과 갈릴 수 있다. 라벨은 경로와 **같은 곳에서
같이 온다.**

**동작이 없는 선언을 별도 파일로 두지 마라** (CLAUDE.md §6 마지막 문단). `KnownFolderKind` 와
`KnownFolder` 를 각각 다른 파일로 가르면 억지 테스트가 생긴다. 한 파일이다.

### 2. 계약을 XML 주석으로 못 박는다

`IDriveSpace.cs` 와 같은 밀도로 적어라. 반드시 담을 것 넷:

1. **왜 포트인가** — 알려진 폴더 조회는 저장소·레지스트리에 닿는다. 리디렉션된 폴더
   (OneDrive · 도메인 로밍 프로필)에서는 네트워크로 내려가 초 단위로 블로킹하므로 UI
   스레드에서 부를 수 없다 (CLAUDE.md §3).
2. **목록은 한 번에 온다** — 스트림이 아니다. 다섯 개고 점진적으로 그려서 얻을 것이 없다.
   (`IDriveList` 와 같은 판단이고 같은 문장이 거기 있다.)
3. **없는 폴더는 목록에서 빠지지 않고 `Location = null` 로 온다** — 구현체가 걸러 내면
   호출자가 "다섯 개 중 몇 번째" 를 셀 수 없고, 무엇이 왜 없는지 진단할 길이 사라진다.
   **거르는 것은 ViewModel 의 일이다** (step 1).
4. **실패는 던지지 않는다** — 알려진 폴더는 곁다리다. 못 읽는다고 폴더를 못 여는 사건이
   되면 안 된다. 취소만 예외로 나온다. (`IDriveList` · `IDriveSpace` 와 같은 문장이다.)

### 3. 테스트

`tests/FlexDir.Core.Tests/Locations/IKnownFolderListTests.cs` — 디렉터리는 **이미 존재한다.**

**파일명은 정확히 `IKnownFolderListTests.cs` 여야 한다** (CLAUDE.md §6-2: 인터페이스도
예외가 아니다). 파일 안의 클래스 이름은 달라도 된다.

포트에는 동작이 없으므로 채점할 것은 **레코드와 열거형의 계약**이다:

- `KnownFolderKind` 가 정확히 다섯이고 순서가 `Home · Desktop · Documents · Downloads ·
  Pictures` 다 — 이 순서가 메뉴 순서이므로 뒤바뀌면 화면이 바뀐다
- `KnownFolder` 는 `readonly record struct` 이므로 값 동등성을 갖는다
- `Location` 이 `null` 인 `KnownFolder` 를 만들 수 있다 (없는 폴더의 표현)
- `FakeKnownFolderList` 가 계약대로 답한다 (아래 4번)

### 4. Fake

`tests/FlexDir.Core.Tests/Fakes/FakeKnownFolderList.cs` — 디렉터리는 이미 존재한다.

`FakeDriveList.cs` 와 **같은 모양**으로 만들어라. `FlexDir.App.Tests` 가 이 프로젝트를
참조해 재사용한다 (CLAUDE.md §6-5). 담을 것:

- 생성자나 속성으로 낼 목록을 주입할 수 있다
- 취소 토큰을 관측한다 (`ct.ThrowIfCancellationRequested()`)
- 호출 횟수를 셀 수 있다 — step 1 이 "탭마다 다시 묻지 않는가" 를 채점할 때 쓴다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0 이어야 하고, `dotnet build` 는 **경고가 0** 이어야 한다.
테스트 총계가 이 step 전(2028)보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `src/FlexDir.Core/` 안에 `using System.Runtime.InteropServices` 나 `[DllImport]` 가
     새로 생기지 않았는가? (CLAUDE.md §1 — Core 는 순수 .NET 이다)
   - 테스트 파일명이 정확히 `IKnownFolderListTests.cs` 인가?
   - `KnownFolderKind` 다섯의 **순서**가 지시서와 같은가?
3. `phases/3-toolbar-overflow/index.json` 의 step 0 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에는 만든 파일 경로 셋과 `KnownFolderKind` 의 순서를 적어라 — step 1 이
그대로 받는다.

## 금지사항

- **`FlexDir.Shell` 에 아무것도 만들지 마라.** 이유: 구현체는 step 3 이 만든다. 여기서
  만들면 step 3 이 red 상태를 만들 수 없어 TDD 훅이 편집을 거부한다 (CLAUDE.md §6-4).
- **`PaneViewModel` 을 건드리지 마라.** 이유: ViewModel 배선은 step 1 이다. 이 step 은
  포트 한 겹이다.
- **`Environment.GetFolderPath` 를 `FlexDir.Core` 에서 부르지 마라.** 이유: CLAUDE.md §1 —
  Core 는 Windows API 를 참조하지 않는다. 그것이 이 포트가 존재하는 이유다.
- **라벨 문자열("바탕화면" 등)을 Core 에 박지 마라.** 이유: 라벨은 경로와 같은 곳에서
  온다 (위 §1). Core 에 박으면 OS 가 쓰는 실제 이름과 갈린다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
