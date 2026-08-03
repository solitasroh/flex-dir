# Step 1: file-item

목록의 한 줄을 표현하는 모델이다. 열거·정렬·표시·썸네일이 모두 이 타입을 본다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — 이전 step 산출물. 경로는 이 타입으로 다룬다
- `docs/ARCHITECTURE.md` — §1 프로젝트 구성
- `docs/SHELL_NOTES.md` — **§열거**. 함정 3(클라우드 자리표시자)과 함정 4(확장자 판정)가 이 step 의 요구사항이다
- `docs/PRD.md` — §2 목록 컬럼 (이름·크기·유형·수정시각)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.

1. 먼저 `tests/FlexDir.Core.Tests/Model/FileItemTests.cs` 와
   `tests/FlexDir.Core.Tests/Model/FileAttributeMappingTests.cs` 를 쓴다 (red).
2. 그 다음 `src/FlexDir.Core/Model/FileItem.cs` 와
   `src/FlexDir.Core/Model/FileAttributeMapping.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Model/FileItem.cs`

```csharp
namespace FlexDir.Core.Model;

using FlexDir.Core.Locations;

[Flags]
public enum FileItemFlags
{
    None            = 0,
    Directory       = 1 << 0,
    Hidden          = 1 << 1,
    System          = 1 << 2,
    ReparsePoint    = 1 << 3,
    Offline         = 1 << 4,
    CloudPlaceholder = 1 << 5,
}

public sealed record FileItem(
    string Name,
    LocationId Location,
    long Size,
    DateTimeOffset ModifiedUtc,
    FileItemFlags Flags)
{
    public bool IsDirectory { get; }

    /// 선행 `.` 은 확장자 구분자로 보지 않는다. `.bashrc` → "" (확장자 없음).
    /// 점을 포함하지 않는다. `report.tar.gz` → "gz".
    public string Extension { get; }

    public string NameWithoutExtension { get; }

    /// 내용을 건드리면 클라우드 다운로드가 트리거되는 항목.
    /// 썸네일·미리보기가 이 항목을 건너뛰는 근거다.
    public bool IsContentAccessRisky { get; }
}
```

- `Extension` 은 **소문자로 정규화**한다. 유형 컬럼과 아이콘 캐시가 확장자를 키로 쓰므로
  `.JPG` 와 `.jpg` 가 갈라지면 안 된다.
- 디렉터리는 `Extension` 이 항상 `""` 다. `My.Folder` 를 확장자 `Folder` 로 보면 안 된다.
- `IsContentAccessRisky` 는 `Offline` 또는 `CloudPlaceholder` 중 하나라도 서면 `true` 다.

### 2. `src/FlexDir.Core/Model/FileAttributeMapping.cs`

Win32 파일 속성 비트를 `FileItemFlags` 로 옮기는 순수 함수다.

```csharp
namespace FlexDir.Core.Model;

public static class FileAttributeMapping
{
    public static FileItemFlags FromWin32Attributes(uint attributes);
}
```

`docs/SHELL_NOTES.md` §열거 함정 3 의 값을 그대로 쓴다.

| 비트 | 값 | → 플래그 |
|---|---|---|
| `FILE_ATTRIBUTE_DIRECTORY` | `0x00000010` | `Directory` |
| `FILE_ATTRIBUTE_HIDDEN` | `0x00000002` | `Hidden` |
| `FILE_ATTRIBUTE_SYSTEM` | `0x00000004` | `System` |
| `FILE_ATTRIBUTE_REPARSE_POINT` | `0x00000400` | `ReparsePoint` |
| `FILE_ATTRIBUTE_OFFLINE` | `0x00001000` | `Offline` |
| `RECALL_ON_DATA_ACCESS` | `0x00400000` | `CloudPlaceholder` |
| `RECALL_ON_OPEN` | `0x00040000` | `CloudPlaceholder` |

상수는 이 파일 안에 `private const uint` 로 이름을 붙여 둔다. 매직 넘버로 흘리지 마라.

> **`FlexDir.Core` 가 Windows API 를 참조하는 것이 아니다.** 상수 값만 알고 있을 뿐이며
> P/Invoke·COM 은 없다. 구조 게이트(`scripts/check-structure.ps1`)가 검사하는 것은
> 어셈블리 참조와 `UseWPF`·Windows 타겟이다.

### 테스트가 반드시 덮어야 할 것

- `.bashrc` → `Extension == ""`, `NameWithoutExtension == ".bashrc"`
- `report.tar.gz` → `Extension == "gz"`, `NameWithoutExtension == "report.tar"`
- `README` → `Extension == ""`
- `PHOTO.JPG` → `Extension == "jpg"` (소문자 정규화)
- `My.Folder` + `Directory` → `Extension == ""`
- `0x00400000` 과 `0x00040000` 각각이 단독으로 `CloudPlaceholder` 를 세운다
- `Offline` 만 서도 `IsContentAccessRisky == true`
- 플래그가 없는 평범한 파일은 `IsContentAccessRisky == false`

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `FlexDir.Core` 에 P/Invoke·COM·`System.Runtime.InteropServices` 호출이 들어가지 않았는가?
   - 클라우드 자리표시자 비트 두 개가 모두 반영됐는가?
   - 파일 위치가 `src/FlexDir.Core/Model/` 인가?
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **파일시스템에서 속성을 읽지 마라.** 이유: `FileItem` 은 값 타입이고 열거는 다음 phase 다.
  `FileInfo`·`Directory` 를 쓰면 테스트가 실제 디스크에 묶인다.
- **`System.IO.Path.GetExtension` 에 위임하지 마라.** 이유: `.bashrc` 를 확장자 `bashrc` 로 본다.
  이 step 이 요구하는 규칙과 다르다 (`docs/SHELL_NOTES.md` §열거 함정 4).
- **표시용 문자열(크기 `1.2 MB`, 날짜 포맷)을 이 타입에 넣지 마라.** 이유: 다음 step 의 책임이고,
  모델이 로케일에 묶이면 정렬이 표시 형식에 영향받는다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
