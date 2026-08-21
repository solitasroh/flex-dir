# Step 3: shell-known-folders

## 읽어야 할 파일

- `CLAUDE.md` — CRITICAL 규칙. 특히 §1(참조 방향) · §3(UI 스레드) · §6(TDD 순서)
- `docs/ARCHITECTURE.md` — 포트/어댑터
- `docs/SHELL_NOTES.md` — **반드시 읽어라.** 이 step 은 경로와 shell API 를 다룬다
- `src/FlexDir.Core/Locations/IKnownFolderList.cs` — **step 0 이 만든 포트.**
  `KnownFolderKind`(Home · Desktop · Documents · Downloads · Pictures 다섯, 이 순서) ·
  `KnownFolder(Kind, Label, LocationId?)` · `ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken)`
- `src/FlexDir.Shell/Settings/RegistrySystemThemeSource.cs` — **이 step 이 그대로 따라 쓰는
  본보기다.** COM 이 아니라 STA 도 `IDisposable` 도 필요 없고, 그래도 `Task.Run` 을 거치며,
  실패는 던지지 않고 기본값으로 접는다
- `src/FlexDir.Shell/Storage/SystemDriveList.cs` — 목록을 한 번에 내는 포트의 구현 본보기.
  `LocationId` 를 어떻게 만드는지 확인하라
- `src/FlexDir.Core/Locations/LocationId.cs` — `TryParse` 의 계약(드라이브 루트로 시작해야
  한다 · `\\?\` 확장 접두사 · 드라이브 루트만 후행 구분자를 남긴다)
- `tests/FlexDir.Shell.Tests/Settings/` 와 `tests/FlexDir.Shell.Tests/Storage/` —
  **이 기계의 실물을 건드리는 테스트를 어떻게 쓰는지.** 특정 기계의 구성에 의존하는
  단정을 어떻게 피했는지 보라

## 배경

step 0 이 만든 `IKnownFolderList` 의 구현체를 `FlexDir.Shell` 에 만든다. 툴바의 알려진
폴더 메뉴(글리프 `E707` 핀)가 이것을 통해 홈 · 바탕화면 · 문서 · 다운로드 · 사진 다섯의
경로를 얻는다.

## 작업

### 0. 디렉터리를 먼저 만든다

`src/FlexDir.Shell/Locations/` 와 `tests/FlexDir.Shell.Tests/Locations/` 는 **둘 다 없다.**
파일을 쓰기 전에 만들어라 — 디렉터리가 없으면 TDD 훅이 테스트 프로젝트를 찾지 못해
**편집을 무조건 거부한다** (CLAUDE.md §6-1).

```bash
mkdir -p src/FlexDir.Shell/Locations tests/FlexDir.Shell.Tests/Locations
```

### 1. 구현체

`src/FlexDir.Shell/Locations/KnownFolderList.cs`

```csharp
namespace FlexDir.Shell.Locations;

public sealed class KnownFolderList : IKnownFolderList
```

**`IDisposable` 을 구현하지 마라.** COM 이 아니다 — `RegistrySystemThemeSource` ·
`SystemDriveList` · `FileSystemDriveSpace` 와 같은 자리이고, 그래서 `AppComposition` 의
정리 목록에도 들어가지 않는다 (step 4 가 그렇게 조립한다).

**`Task.Run` 을 거쳐라.** 리디렉션된 알려진 폴더(OneDrive · 도메인 로밍 프로필)는
네트워크로 내려가 초 단위로 블로킹한다 (CLAUDE.md §3). `RegistrySystemThemeSource.ReadAsync`
가 하는 그대로다.

### 2. 경로를 어디서 얻는가 — 실측 매핑

**넷은 `Environment.GetFolderPath` 로 얻는다:**

| `KnownFolderKind` | `Environment.SpecialFolder` |
|---|---|
| `Home` | `UserProfile` |
| `Desktop` | `DesktopDirectory` |
| `Documents` | `MyDocuments` |
| `Pictures` | `MyPictures` |

> `DesktopDirectory` 다. `Desktop` 이 아니다 — `Desktop` 은 가상 shell 폴더를 가리켜
> 파일시스템 경로가 아닐 수 있다. 우리가 원하는 것은 열 수 있는 디렉터리다.

**다운로드만 `Environment.SpecialFolder` 에 없다.** `SHGetKnownFolderPath` 로 얻어라:

- `FOLDERID_Downloads` = `{374DE290-123F-4565-9164-39C4925E467B}`
- `shell32.dll` 의 `SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath)`
- `dwFlags = 0` · `hToken = 0`(현재 사용자)
- **반환된 포인터는 `CoTaskMemFree` 로 반드시 해제한다.** `Marshal.FreeCoTaskMem` 이 그것이다
- `HRESULT` 가 0 이 아니면 **던지지 말고 경로 없음으로 접는다**

`[LibraryImport]` 를 써라 (`[DllImport]` 이 아니라) — 이 저장소의 다른 interop 이 그것을
쓰는지 `src/FlexDir.Shell/Interop/` 을 보고 맞춰라. 맞추는 쪽이 우선이다.

### 3. 라벨

**라벨은 이 구현체가 정한다** (그것이 포트가 `Label` 을 들고 있는 이유다 — step 0 의 주석).
아래 다섯을 그대로 쓴다:

| `KnownFolderKind` | 라벨 |
|---|---|
| `Home` | `홈` |
| `Desktop` | `바탕화면` |
| `Documents` | `문서` |
| `Downloads` | `다운로드` |
| `Pictures` | `사진` |

`Environment.GetFolderPath` 가 낸 경로의 마지막 조각을 라벨로 쓰지 마라 — OS 언어에 따라
`Desktop`/`바탕 화면` 으로 갈리고, 툴바 메뉴에 영문과 한글이 섞인다.

### 4. 없는 폴더의 표현

- `GetFolderPath` 가 **빈 문자열**을 내면 (그 폴더가 이 기계에 없다는 뜻이다)
  `Location = null` 로 낸다
- `LocationId.TryParse` 가 거부해도 `Location = null` 로 낸다
- **목록에서 빼지 마라.** 다섯은 항상 다섯으로 온다 — 거르는 것은 ViewModel 의 일이다
  (step 1 의 `ToOptions`). step 0 의 포트 주석에 이 계약이 적혀 있다.
- **순서는 `KnownFolderKind` 선언 순서 그대로다.**

### 5. 실패는 던지지 않는다

취소(`OperationCanceledException`)만 밖으로 나간다. 그 외에는 전부 삼키고 해당 항목을
`Location = null` 로 접어라 — 알려진 폴더를 못 읽는 것이 폴더를 못 여는 사건이 되면 안 된다.
`RegistrySystemThemeSource` 가 라이트로 접는 것과 같은 판단이다.

### 6. 테스트

`tests/FlexDir.Shell.Tests/Locations/KnownFolderListTests.cs` —
**파일명은 정확히 이것이어야 한다** (CLAUDE.md §6-2).

이 기계의 실물을 부르는 테스트다. **특정 기계의 구성에 기대는 단정을 쓰지 마라** —
`C:\Users\SOOJANG\Downloads` 같은 값을 박으면 다른 기계에서 게이트가 깨진다.

채점할 것 (최소):

1. **항상 다섯 개가 온다** — 이 기계에 없는 폴더가 있어도 다섯이다
2. **순서가 `Home · Desktop · Documents · Downloads · Pictures`** 다
3. 라벨이 위 표와 같다 (다섯 전부)
4. `Home` 의 `Location` 이 `null` 이 아니다 — 사용자 프로필 없이는 프로세스가 돌지 않으므로
   이것 하나는 기계와 무관하게 참이다
5. `Location` 이 있는 항목은 전부 **절대 경로**이고 `LocationId` 로 파싱된 값이다
6. 이미 취소된 토큰을 주면 `OperationCanceledException` 이 나온다
7. 두 번 불러도 같은 결과가 온다 (멱등)
8. **UI 스레드를 잡지 않는다** — `Task.Run` 을 거치는지 확인할 방법이 마땅치 않으면
   이 항목은 생략하고, 대신 `ListAsync` 가 동기 완료가 아님을 확인하라

## Acceptance Criteria

```bash
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

셋 다 exit 0, `dotnet build` 는 **경고 0**. 테스트 총계가 step 2 직후보다 커야 한다.

## 검증 절차

1. 위 AC 커맨드 셋을 순서대로 실행한다.
2. 체크리스트:
   - `src/FlexDir.Shell/Locations/` 와 `tests/FlexDir.Shell.Tests/Locations/` 가 실제로
     만들어졌는가?
   - `FlexDir.Core` 에 `[LibraryImport]`·`[DllImport]` 가 새로 생기지 않았는가?
     (`check-structure.ps1` 의 C 항목)
   - `SHGetKnownFolderPath` 가 낸 포인터를 **모든 경로에서** 해제하는가? 예외가 나도?
   - 다섯이 **항상 다섯**으로 오는가? (`Location == null` 인 것을 구현체가 거르면 안 된다)
3. `phases/3-toolbar-overflow/index.json` 의 step 3 을 갱신한다:
   - 성공 → `"status": "completed"`, `"summary": "산출물 한 줄 요약"`
   - 3회 시도 후에도 실패 → `"status": "error"`, `"error_message": "구체적 원인"`
   - 사람만 할 수 있는 일에 막힘 → `"status": "blocked"`, `"blocked_reason": "사유"` 후 즉시 중단

`summary` 에 클래스의 전체 이름(`FlexDir.Shell.Locations.KnownFolderList`)과
`IDisposable` 을 구현하지 않았다는 사실을 적어라 — step 4 가 `AppComposition` 의 정리
목록에 넣을지 말지를 그것으로 정한다.

## 금지사항

- **`AppComposition` 을 건드리지 마라.** 이유: 조립은 step 4 다. 여기서 주입하면 패널과
  XAML 이 없는 상태로 배선만 앞서 나간다.
- **`PaneViewModel` 이나 XAML 을 건드리지 마라.** 이유: 각각 step 1 과 step 4 가 끝냈거나 한다.
- **`IDisposable` 을 구현하지 마라.** 이유: COM 이 아니다. 정리 목록에 들어가면
  `AppComposition.DisposeAsync` 가 없는 자원을 닫는 코드를 들게 된다.
- **`Environment.SpecialFolder.Desktop` 을 쓰지 마라.** 이유: 가상 shell 폴더를 가리켜
  파일시스템 경로가 아닐 수 있다. `DesktopDirectory` 다.
- **`Location == null` 인 항목을 구현체에서 거르지 마라.** 이유: step 0 의 포트 계약이다 —
  거르면 호출자가 무엇이 왜 없는지 진단할 길이 사라진다. 거르는 것은 step 1 의 `ToOptions` 다.
- **경로를 하드코딩한 단정을 테스트에 쓰지 마라.** 이유: 다른 기계에서 게이트가 깨진다.
- 기존 테스트를 깨뜨리지 마라.
- AC 를 통과시키려고 테스트를 삭제·스킵·약화시키지 마라. 실패는 구현을 고쳐 통과시켜라.
- 이 step 에 명시되지 않은 기능이나 파일을 추가하지 마라.
