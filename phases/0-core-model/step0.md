# Step 0: location-id

`FlexDir.Core` 의 첫 타입이다. 이후 모든 포트가 경로를 이 타입으로 주고받는다.

## 읽어야 할 파일

- `docs/ARCHITECTURE.md` — §1 프로젝트 구성, §2 포트 (경로를 문자열이 아니라 `LocationId` 로 주고받는다)
- `docs/ADR.md` — ADR-010 (네트워크는 v2, 경로 추상화는 v1 에서 준비)
- `docs/SHELL_NOTES.md` — **§경로 정규화** 와 **§위치 표현**. 함정 1~4 가 이 step 의 요구사항이다

## TDD 순서 — 어기면 훅이 편집을 거부한다

**0. 먼저 소스 디렉터리를 만든다** — 예: `mkdir -p src/FlexDir.Core/Sorting`. 존재하지 않는
디렉터리에 `Write` 하면 훅이 테스트 프로젝트를 찾지 못해 **무조건 거부**한다 (`CLAUDE.md` §6).

이 저장소에는 TDD Guard 훅이 걸려 있다. 대응 테스트가 없는 새 소스는 `Write` 가 **거부**된다.

1. 먼저 `tests/FlexDir.Core.Tests/Locations/LocationIdTests.cs` 를 쓴다 (이 시점엔 컴파일 실패 = red).
2. 그 다음 `src/FlexDir.Core/Locations/LocationId.cs` 를 쓴다.
3. AC 가 통과할 때까지 구현을 고친다.

## 작업

`src/FlexDir.Core/Locations/LocationId.cs` 에 아래를 만든다.

```csharp
namespace FlexDir.Core.Locations;

public enum LocationKind
{
    FileSystem,        // v1 이 다루는 유일한 종류
    ShellKnownFolder,  // v2 예약 — 이 step 에서 구현하지 않는다
    ShellNamespace,    // v2 예약 — 이 step 에서 구현하지 않는다
}

public enum LocationParseError
{
    None,
    Empty,
    RelativePath,
    AlternateDataStream,
    InvalidCharacter,
    NetworkPathNotSupported,   // v1 범위 밖. '잘못된 경로' 와 구분해야 한다
}

public sealed class LocationId : IEquatable<LocationId>
{
    public LocationKind Kind { get; }

    /// 정규화된 내부 표현. 로컬은 항상 `\\?\` 확장 접두사로 시작한다.
    public string Value { get; }

    /// 사용자에게 보여줄 형태. `\\?\` 접두사를 제거한 것.
    public string DisplayPath { get; }

    /// 마지막 구성요소. 루트(`\\?\C:\`)는 `C:\` 를 낸다.
    public string Name { get; }

    public static bool TryParse(string? input, out LocationId? location, out LocationParseError error);

    public bool TryGetParent(out LocationId? parent);

    /// 하위 이름을 붙인다. 구분자·상대경로 요소가 섞이면 ArgumentException.
    public LocationId Combine(string childName);
}
```

### 정규화 규칙

`docs/SHELL_NOTES.md` §경로 정규화 의 함정을 그대로 구현한다.

1. **UNC 판정을 구분자 정규화보다 먼저 한다** (함정 1). `//server/share` 를 먼저 `\` 로 바꾸면
   드라이브 문자가 없어 상대 경로로 오분류된다. 판정이 먼저이므로 이 입력은
   `RelativePath` 가 아니라 `NetworkPathNotSupported` 로 분류돼야 한다.
2. **경로 본문의 `:` 는 NTFS 대체 데이터 스트림 구분자다 → `AlternateDataStream` 으로 거부**
   (함정 2). 단 드라이브 문자 뒤의 `C:` 는 합법이므로 스캔에서 제외한다.
3. **`\\?\C:\` 는 UNC 가 아니다** (함정 3). `\\` 로 시작한다고 UNC 로 판정하면 틀린다.
   이미 확장 접두사가 붙은 입력은 그대로 받는다.
4. **`\\server` (서버만, 공유 없음)도 유효한 탐색 대상이다** (함정 4). 탐색기는 이것을
   공유 목록 폴더로 취급한다. v1 은 `NetworkPathNotSupported` 로 거부하지만,
   `InvalidCharacter` 나 `RelativePath` 로 뭉개서는 안 된다 — v2 에서 이 분기를 살려 쓴다.
5. `.` 과 `..` 요소는 해소한다. 루트를 넘어가는 `..` 는 루트에서 멈춘다.
6. 뒤에 붙은 구분자는 제거한다. 단 드라이브 루트는 `\\?\C:\` 로 구분자를 유지한다.
7. 동등성·해시는 **`OrdinalIgnoreCase`** 다. Windows 파일시스템은 대소문자를 구분하지 않으므로
   `\\?\C:\Temp` 와 `\\?\c:\temp` 는 같은 위치다.

### 테스트가 반드시 덮어야 할 것

- 함정 1: `//server/share` → `NetworkPathNotSupported` (`RelativePath` 아님)
- 함정 2: `C:\a\b:stream` → `AlternateDataStream`, `C:\a` → 성공
- 함정 3: `\\?\C:\Users` → 성공하고 `Kind == FileSystem`
- 함정 4: `\\server` → `NetworkPathNotSupported`
- `docs` (상대 경로) → `RelativePath`, `""`/`null` → `Empty`
- `C:\a\.\b\..\c` → `\\?\C:\a\c`
- `C:\` 의 `TryGetParent` → `false`
- `\\?\C:\Temp` 와 `\\?\c:\temp` 의 동등성과 해시 일치
- `Combine("x")` 성공, `Combine("a\b")` 와 `Combine("..")` 는 `ArgumentException`

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror && dotnet test --nologo && pwsh -File scripts/check-structure.ps1
```

## 검증 절차

1. 위 AC 커맨드를 실행한다.
2. 체크리스트를 확인한다:
   - `FlexDir.Core` 에 Windows·COM·WPF 참조가 들어가지 않았는가? (`check-structure.ps1` 이 검사한다)
   - 파일 위치가 `src/FlexDir.Core/Locations/` 인가?
   - v2 예약 종류(`ShellKnownFolder`·`ShellNamespace`)에 구현을 넣지 않았는가?
3. 결과에 따라 `phases/0-core-model/index.json` 의 해당 step 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 는 다음 step 프롬프트에 그대로 전달된다. 만든 파일 경로와 핵심 결정을 담아라.

## 금지사항

- **UNC·네트워크 경로를 실제로 지원하지 마라.** 이유: v1 범위 밖이고(`docs/PRD.md` §3),
  검증되지 않은 분기가 남는다. 판정해서 `NetworkPathNotSupported` 로 거부하는 것까지가 이 step 이다.
- **`System.IO` 의 `Path.GetFullPath` 에 정규화를 위임하지 마라.** 이유: 프로세스의 현재 디렉터리를
  읽어 상대 경로를 조용히 절대 경로로 바꾼다. 상대 경로는 거부해야 하는 입력이다.
- **파일시스템에 접근하지 마라.** 이유: `LocationId` 는 순수 값 타입이다. 존재 여부 확인은
  열거 계층의 일이고, I/O 가 섞이면 테스트가 실제 디스크에 의존한다.
- `Kind` 에 v1 이 만들지 않는 값을 세우지 마라. 이유: 소비자가 존재하지 않는 상태를 분기하게 된다.
- 기존 테스트를 깨뜨리지 마라. AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라.
- 이 step 에 명시되지 않은 파일을 추가하지 마라.
