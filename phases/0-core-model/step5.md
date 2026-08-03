# Step 5: error-classification

열거·탐색 실패를 사용자가 손쓸 수 있는 분류로 바꾼다. phase 0 의 마지막 step 이다.

## 읽어야 할 파일

- `src/FlexDir.Core/Locations/LocationId.cs` — 이전 step 산출물
- `docs/SHELL_NOTES.md` — **§네트워크 → 오류 코드 매핑**. 표의 분류 사고방식을 따르되 v1 코드만 쓴다
- `docs/PRD.md` — §4 엣지케이스 (접근 권한 없음 · 경로가 사라짐)
- `docs/UI_GUIDE.md` — §상태 표현 (권한 없음: 상태 표시줄에 사유, 경로는 그대로 둔다)

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.

> **테스트 파일 이름 규칙 — 훅이 파일명으로 찾는다.** 소스가 `Foo.cs` 면 테스트는 반드시
> `FooTests.cs` 여야 한다. 열거형만 담은 파일은 의미 있는 테스트를 쓸 수 없으므로,
> 아래처럼 **동작이 있는 파일에 함께 선언**한다.

1. 먼저 아래를 쓴다 (red):
   - `tests/FlexDir.Core.Tests/Errors/LocationErrorTests.cs`
   - `tests/FlexDir.Core.Tests/Errors/Win32ErrorMappingTests.cs`
   - `tests/FlexDir.Core.Tests/Errors/LocationAccessExceptionTests.cs`
2. 그 다음 `src/FlexDir.Core/Errors/` 아래 소스 **세 개**를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

### 1. `src/FlexDir.Core/Errors/LocationError.cs`

열거형과 사용자 문구를 한 파일에 둔다 — "오류가 무엇인가"와 "그것을 어떻게 말하는가"는
같은 관심사이며, 열거형만 있는 파일은 테스트할 것이 없다.

```csharp
namespace FlexDir.Core.Errors;

public enum LocationErrorKind
{
    None,
    AccessDenied,     // 권한 문제 — 경로는 맞다
    NotFound,         // 경로·파일이 없다
    DeviceNotReady,   // 드라이브가 준비되지 않았다 (빈 카드 리더 등)
    Sharing,          // 다른 프로세스가 잠갔다
    Unknown,
}
```

**경로 문제와 권한 문제를 구분하는 것이 이 분류의 목적이다.** 뭉뚱그리면 사용자가 손쓸
방법을 알 수 없다 (`docs/SHELL_NOTES.md` §오류 코드 매핑).

같은 파일에 사용자 문구를 둔다. 상태표시줄에 그대로 들어갈 한국어이며, 기술 용어와 경로는
원문을 유지한다.

```csharp
public static class LocationErrorMessages
{
    public static string Describe(LocationErrorKind kind, LocationId location);
}
```

| 분류 | 문구 |
|---|---|
| `AccessDenied` | `액세스가 거부되었습니다 — {DisplayPath}` |
| `NotFound` | `경로를 찾을 수 없습니다 — {DisplayPath}` |
| `DeviceNotReady` | `드라이브가 준비되지 않았습니다 — {DisplayPath}` |
| `Sharing` | `다른 프로그램이 사용 중입니다 — {DisplayPath}` |
| `Unknown` | `열 수 없습니다 — {DisplayPath}` |
| `None` | `ArgumentException` — 오류가 아닌 것을 설명하라는 호출은 버그다 |

경로는 `Value`(`\\?\` 접두사가 붙은 내부 표현)가 아니라 **`DisplayPath`** 를 쓴다.
이유: 사용자가 `\\?\C:\Users` 를 보면 무슨 일인지 알 수 없다.

### 2. `src/FlexDir.Core/Errors/Win32ErrorMapping.cs`

```csharp
namespace FlexDir.Core.Errors;

public static class Win32ErrorMapping
{
    public static LocationErrorKind Classify(int win32Error);
}
```

v1 이 다루는 코드만 넣는다. 상수는 `private const int` 로 이름을 붙인다.

| 코드 | 이름 | → 분류 |
|---|---|---|
| `0` | `ERROR_SUCCESS` | `None` |
| `2` | `ERROR_FILE_NOT_FOUND` | `NotFound` |
| `3` | `ERROR_PATH_NOT_FOUND` | `NotFound` |
| `5` | `ERROR_ACCESS_DENIED` | `AccessDenied` |
| `15` | `ERROR_INVALID_DRIVE` | `NotFound` |
| `19` | `ERROR_WRITE_PROTECT` | `AccessDenied` |
| `21` | `ERROR_NOT_READY` | `DeviceNotReady` |
| `32` | `ERROR_SHARING_VIOLATION` | `Sharing` |
| `1314` | `ERROR_PRIVILEGE_NOT_HELD` | `AccessDenied` |
| 그 외 | | `Unknown` |

### 3. `src/FlexDir.Core/Errors/LocationAccessException.cs`

열거·탐색 계층이 던지는 예외다. 다음 phase 의 `IFolderSource` 가 이것을 쓴다.

```csharp
namespace FlexDir.Core.Errors;

using FlexDir.Core.Locations;

public sealed class LocationAccessException : Exception
{
    public LocationAccessException(LocationErrorKind kind, LocationId location, int win32Error = 0);

    public LocationErrorKind Kind { get; }
    public LocationId Location { get; }
    public int Win32Error { get; }
}
```

### 테스트가 반드시 덮어야 할 것

- 위 표의 아홉 코드가 각각 정확한 분류로 간다
- 표에 없는 코드(예: `1223`)는 `Unknown`
- 네트워크 코드(`53`·`67`·`1326`·`1219`)는 v1 에서 **`Unknown`** 이다 — 특별 취급이 없음을 고정한다
- `Describe` 가 `\\?\` 접두사를 노출하지 않는다
- `Describe(LocationErrorKind.None, ...)` → `ArgumentException`
- `LocationAccessException` 이 `Kind`·`Location`·`Win32Error` 를 보존한다

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `FlexDir.Core` 에 `Marshal.GetLastWin32Error`·`Win32Exception` 사용이 없는가? 코드는 `int` 로 받는다
   - 메시지에 `\\?\` 가 새어 나오지 않는가?
   - v1 범위 밖 코드에 매핑을 넣지 않았는가?
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

## 금지사항

- **네트워크 오류 코드(`53`·`67`·`64`·`52`·`1231`·`1232`·`1326`·`1311`·`1219`)를 매핑하지 마라.**
  이유: v1 은 로컬 파일시스템만 다룬다(`docs/PRD.md` §3). 지금 넣으면 실행되지 않는 분기가 남고,
  v2 에서 실물 검증 없이 옳다고 믿게 된다. 표는 `docs/SHELL_NOTES.md` 에 이미 있으니 그때 쓴다.
- **`System.ComponentModel.Win32Exception` 이나 `Marshal` 을 쓰지 마라.** 이유: `FlexDir.Core` 는
  Windows API 를 참조하지 않는다(`CLAUDE.md` §1). 코드는 호출자가 `int` 로 넘긴다.
- **예외 메시지에 영어와 한국어를 섞지 마라.** 이유: 상태표시줄 한 줄에 들어가야 하고
  문구가 갈라지면 사용자가 같은 오류를 다른 것으로 읽는다.
- **오류를 만나면 이전 경로로 되돌리는 로직을 여기에 넣지 마라.** 이유: `docs/PRD.md` §4 는
  경로를 그대로 두라고 정했고, 탐색 정책은 ViewModel 의 일이다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
