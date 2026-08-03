# Step 2: natural-sort

`file2` 가 `file10` 보다 앞에 오게 만든다. 폴더는 항상 파일보다 먼저다.

## 읽어야 할 파일

- `src/FlexDir.Core/Model/FileItem.cs` — 이전 step 산출물. 정렬 대상 타입
- `src/FlexDir.Core/Locations/LocationId.cs` — 이전 step 산출물
- `docs/PRD.md` — §2 정렬 (자연 정렬 · 폴더 먼저 · 다중 키)
- `docs/DESIGN.md` — §3 Details 컬럼 (정렬 가능한 컬럼이 무엇인지), §6 정렬 방향 표시

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.

1. 먼저 `tests/FlexDir.Core.Tests/Sorting/NaturalStringComparerTests.cs` 와
   `tests/FlexDir.Core.Tests/Sorting/FileItemComparerTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.Core/Sorting/NaturalStringComparer.cs` 와
   `src/FlexDir.Core/Sorting/FileItemComparer.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Sorting/NaturalStringComparer.cs`

```csharp
namespace FlexDir.Core.Sorting;

/// 숫자 런을 수치로 비교하는 문자열 비교기. 대소문자를 구분하지 않는다.
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; }

    public int Compare(string? x, string? y);
}
```

규칙:

1. 문자열을 **문자 런과 숫자 런으로 쪼개** 앞에서부터 비교한다.
2. 숫자 런끼리는 **수치로** 비교한다. 자릿수가 매우 클 수 있으므로 `long` 파싱에 의존하지 말고
   선행 0 을 제거한 뒤 길이 → 자릿값 순으로 비교한다. 이유: 파일명에 20자리 숫자가 오면
   `long.Parse` 는 예외를 던진다.
3. 수치가 같은데 표기가 다르면(`img1` vs `img001`) **선행 0 이 적은 쪽이 먼저**다.
   완전 동률로 두면 정렬이 불안정해진다.
4. 문자 런끼리는 `OrdinalIgnoreCase` 로 비교한다.
5. 전체가 대소문자만 다르면(`readme` vs `README`) `Ordinal` 로 최종 판정해 안정성을 준다.

### 2. `src/FlexDir.Core/Sorting/FileItemComparer.cs`

```csharp
namespace FlexDir.Core.Sorting;

using FlexDir.Core.Model;

public enum SortKey { Name, Size, Type, Modified }

public sealed record SortOrder(SortKey Key, bool Descending = false);

public sealed class FileItemComparer : IComparer<FileItem>
{
    public FileItemComparer(IReadOnlyList<SortOrder> keys, bool directoriesFirst = true);

    public static FileItemComparer Default { get; }   // Name 오름차순, 폴더 먼저

    public int Compare(FileItem? x, FileItem? y);
}
```

규칙:

1. **`directoriesFirst` 는 정렬 방향과 무관하다.** 내림차순에서도 폴더가 위에 남는다.
   이유: 탐색기가 그렇게 동작하고, 내림차순마다 폴더가 바닥으로 내려가면 상위 이동이 어려워진다.
2. 키를 앞에서부터 적용한다. 동률이면 다음 키로 넘어간다.
3. 모든 키가 동률이면 **최종 tie-break 는 이름 오름차순**이다. 안정 정렬을 보장해야
   갱신·뷰 전환 후 순서가 흔들리지 않는다.
4. `SortKey.Name` 은 `NaturalStringComparer` 를 쓴다.
5. `SortKey.Size` 는 **디렉터리끼리 비교할 때 크기를 0 으로 본다.** 폴더 크기는 계산하지 않는다
   (`docs/PRD.md` §3 — 폴더 용량 계산은 v1 범위 밖).
6. `SortKey.Type` 은 `Extension` 을 `NaturalStringComparer` 로 비교하고, 같으면 이름으로 넘어간다.
7. `keys` 가 비어 있으면 `ArgumentException`. 이유: 빈 정렬은 호출자의 버그이며,
   조용히 기본값으로 바꾸면 폴더별 뷰상태가 어긋난 것을 놓친다.

### 테스트가 반드시 덮어야 할 것

- `file2` < `file10`, `a1b` < `a10b`
- `img1` < `img001` (수치 동률, 선행 0 규칙)
- 20자리 숫자 이름이 예외 없이 비교된다
- `readme` 와 `README` 가 동률로 뭉치지 않는다
- 폴더가 파일보다 먼저 — **오름차순과 내림차순 모두에서**
- 다중 키: `[Size desc, Name asc]` 에서 크기 동률이면 이름 오름차순
- 디렉터리는 `SortKey.Size` 비교에서 크기 0
- `SortKey.Type` 동률이면 이름으로 넘어간다
- 빈 `keys` → `ArgumentException`

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `FlexDir.Core` 가 여전히 순수 .NET 인가? (`check-structure.ps1`)
   - 내림차순에서도 폴더가 먼저인가?
   - 정렬이 안정적인가 (모든 키 동률 시 이름 tie-break)?
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **`StrCmpLogicalW`(shlwapi)를 P/Invoke 하지 마라.** 이유: `FlexDir.Core` 는 Windows API 를
  참조하지 않는다(`CLAUDE.md` §1). 탐색기와 미세한 순서 차이가 남는 것은 알려진 트레이드오프이며,
  이 step 의 테스트가 우리 순서를 고정한다.
- **`CompareInfo`·`CultureInfo` 기반 언어별 정렬을 쓰지 마라.** 이유: 로케일이 바뀌면 순서가
  바뀌고, 폴더별로 기억한 정렬 상태가 다른 결과를 낸다.
- **`long.Parse`·`int.Parse` 로 숫자 런을 비교하지 마라.** 이유: 긴 숫자 이름에서 예외가 난다.
- **정렬 안에서 파일시스템을 읽지 마라** (폴더 크기 계산 등). 이유: 비교 함수는 O(1) 이어야 하고,
  10만 항목 정렬에서 I/O 가 섞이면 UI 가 멈춘다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
