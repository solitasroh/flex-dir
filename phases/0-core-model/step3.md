# Step 3: display-format

크기·수정시각·상태표시줄 문자열을 만든다. 모델과 표시를 분리하는 층이다.

## 읽어야 할 파일

- `src/FlexDir.Core/Model/FileItem.cs` — 이전 step 산출물
- `docs/DESIGN.md` — §1 상태표시줄 높이 24, §4 상태표시줄 폰트 11 (이 문자열이 들어갈 자리)
- `docs/UI_GUIDE.md` — §상태 표현 (열거 중 진행 표시는 상태표시줄에)
- `docs/PRD.md` — §4 엣지케이스 (빈 폴더 · 권한 없음)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.

1. 먼저 `tests/FlexDir.Core.Tests/Formatting/SizeFormatterTests.cs`,
   `tests/FlexDir.Core.Tests/Formatting/TimestampFormatterTests.cs`,
   `tests/FlexDir.Core.Tests/Formatting/StatusSummaryTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.Core/Formatting/` 아래 대응 소스를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Formatting/SizeFormatter.cs`

```csharp
namespace FlexDir.Core.Formatting;

using FlexDir.Core.Model;

public static class SizeFormatter
{
    public static string Format(long bytes, IFormatProvider culture);

    /// 디렉터리는 빈 문자열을 낸다.
    public static string ForItem(FileItem item, IFormatProvider culture);
}
```

규칙 (1024 기반):

| 입력 | 출력 |
|---|---|
| 디렉터리 | `""` |
| `0` | `0 KB` |
| `1` ~ `1023` | `1 KB` (올림, 0 KB 로 표시하지 않는다) |
| `1024` ~ `1048575` | `{n} KB` — 올림, 천단위 구분 기호 (`1,024 KB`) |
| `1048576` ~ `1073741823` | `{n.n} MB` — 소수 1자리 |
| `1073741824` 이상 | `{n.n} GB` — 소수 1자리 |
| 음수 | `ArgumentOutOfRangeException` |

> 이 단위 규칙은 Windows 11 탐색기 관례를 따른 것이다. 실물에서 탐색기와 나란히 비교하는 것은
> 수동 UI phase 의 일이며, 여기서는 위 표를 테스트로 고정하는 것까지가 범위다.

### 2. `src/FlexDir.Core/Formatting/TimestampFormatter.cs`

```csharp
namespace FlexDir.Core.Formatting;

public static class TimestampFormatter
{
    /// 짧은 날짜 + 짧은 시간. 로케일의 ShortDatePattern 과 ShortTimePattern 을 공백으로 잇는다.
    public static string Format(DateTimeOffset utc, TimeZoneInfo zone, IFormatProvider culture);
}
```

- **표준 시간대를 인자로 받는다.** `ToLocalTime()` 을 쓰지 마라 — 테스트가 실행 기계의
  시간대에 묶이면 CI 와 개발 기계에서 다른 결과가 난다.
- 로케일 패턴을 직접 조립한다. `"g"` 서식 지정자는 로케일에 따라 초를 포함하기도 하므로 쓰지 않는다.

### 3. `src/FlexDir.Core/Formatting/StatusSummary.cs`

상태표시줄 문자열이다. UI 텍스트는 한국어다(`.harness/ui-design-request.md` 의 UI 텍스트 규칙과 동일).

```csharp
namespace FlexDir.Core.Formatting;

public static class StatusSummary
{
    /// "항목 232개"
    public static string ForItems(int itemCount, IFormatProvider culture);

    /// "232개 중 3개 선택 · 1.2 MB"  — 선택 크기 합계는 SizeFormatter 를 쓴다
    public static string ForSelection(int itemCount, int selectedCount, long selectedBytes, IFormatProvider culture);

    /// "항목 1,204개 읽는 중…"  — 열거가 끝나지 않았음을 알린다
    public static string ForEnumerating(int itemsSoFar, IFormatProvider culture);

    /// "빈 폴더"
    public static string Empty { get; }
}
```

- 숫자는 천단위 구분 기호를 붙인다 (`culture` 사용).
- `selectedCount == 0` 이면 `ForSelection` 은 `ForItems` 와 같은 문자열을 낸다.
  이유: 호출자가 분기하지 않아도 되게 한다.
- 구분자는 가운뎃점 `·` 하나로 통일한다. 상태표시줄 폭이 좁으므로(`docs/DESIGN.md` §1) 짧게 유지한다.

### 테스트가 반드시 덮어야 할 것

- `0` → `0 KB`, `1` → `1 KB`, `1023` → `1 KB`, `1024` → `1 KB`, `1025` → `2 KB` (올림)
- `1048576` → `1.0 MB`, `1073741824` → `1.0 GB`
- 디렉터리 `FileItem` → `""`
- 음수 → `ArgumentOutOfRangeException`
- 천단위 구분 기호가 `InvariantCulture` 와 `ko-KR` 에서 모두 나온다
- `TimestampFormatter` 가 서로 다른 `TimeZoneInfo` 두 개에서 다른 시각을 낸다
- `ForSelection(232, 0, 0)` == `ForItems(232)`
- `ForEnumerating` 결과에 항목 수가 들어 있다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - 어떤 함수도 `DateTime.Now`·`TimeZoneInfo.Local`·`CultureInfo.CurrentCulture` 를 내부에서 읽지 않는가?
   - `FlexDir.Core` 가 여전히 순수 .NET 인가? (`check-structure.ps1`)
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **주변 환경(`DateTime.Now`·`TimeZoneInfo.Local`·`CultureInfo.CurrentCulture`)을 함수 안에서 읽지 마라.**
  이유: 테스트가 실행 기계에 묶이고, 같은 코드가 기계마다 다른 결과를 낸다. 전부 인자로 받는다.
- **1000 기반(SI) 단위를 쓰지 마라.** 이유: 탐색기는 1024 기반이고, 나란히 놓으면 값이 달라 보인다.
- **오류 메시지 문자열을 여기에 만들지 마라.** 이유: step 5 `error-classification` 의 책임이다.
  두 곳에서 만들면 문구가 갈라진다.
- **`ToString("g")`·`ToString()` 기본 서식에 의존하지 마라.** 이유: 로케일에 따라 초·오전/오후가
  들쭉날쭉해져 행 높이 24px 안에서 잘린다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
