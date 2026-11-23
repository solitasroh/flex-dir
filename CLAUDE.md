# flex-dir — CRITICAL 규칙

어기면 되돌리기 어려운 것만 적는다. 설명과 근거는 `docs/` 와 `DECISIONS.md` 에 있다.

## 1. 참조 방향 (컴파일로 강제)

```
FlexDir.Core   ← FlexDir.Shell
FlexDir.Core   ← FlexDir.App
FlexDir.Host   → Core · Shell · App   (DI 조립 전용)
```

- **`FlexDir.App` 은 `FlexDir.Shell` 을 참조하지 않는다.** App 은 `Core` 의 포트
  인터페이스만 안다. 구현체는 `Host` 가 주입한다.
- `FlexDir.Core` 는 WPF·COM·Windows API 를 참조하지 않는다. 순수 .NET 이다.
- 이유: 전작은 shell·UI·상태가 한 파일(2,836줄)에 뒤엉켜 고칠 수 없었다.

## 2. 코드비하인드 금지

`*.xaml.cs` 에는 `InitializeComponent()` 호출만 둔다. 상태·분기·이벤트 처리 로직을
넣지 않는다.

이유: WPF 에서 god object 가 자라는 자리가 여기다. 전작의 `main-window.cpp` 가
이름만 바꿔 부활한다.

## 3. UI 스레드에서 저장소에 닿는 호출을 하지 않는다

`SHGetFileInfo`·`IShellItemImageFactory`·`IFileOperation`·디렉터리 열거·네트워크 경로
접근은 전부 UI 스레드 밖에서 한다.

이유: 파일 탐색기가 멈추는 진짜 원인은 렌더링이 아니라 동기 shell 호출이다.
네트워크 경로에서는 초 단위로 블로킹된다. **누가 얼마나 걸릴지 모르는 것이 기준이다.**

사용자 입력을 기다리는 모달 루프는 여기 해당하지 않는다 — `DragDrop.DoDragDrop` 은 UI
스레드에서만 시작할 수 있고(마우스 캡처와 자체 메시지 루프) 싣는 것이 경로 문자열뿐이라
저장소에 닿지 않는다. 그것은 멈춰 있는 것이 아니라 사용자를 기다리는 것이다.

## 4. 진실원천은 파일시스템이다

메모리 목록·썸네일·폴더별 뷰 설정은 전부 캐시다. 어긋나면 파일시스템을 믿는다.
외부 변경은 `IFolderWatcher` 로 감지해 갱신하고, **갱신 중에도 선택은 유지한다.**

## 5. 자동 채점할 수 없는 것은 자율 실행에 맡기지 않는다

Shell interop 과 UI 조작감은 사람이 확인한다. 자율 실행 대상은 `Core` 와
ViewModel 로직뿐이다.

이유: "손에 붙는가" 를 판정하는 셸 커맨드는 존재하지 않는다. 전작은 25 step 전부
자가 채점을 통과하고도 쓰이지 않았다.

목록 위 컨텍스트 메뉴는 합성 입력(UIA·SendInput)으로 밟히지 않는다. 시도하지 말고
사람에게 눌러 달라고 한다.

## 6. 테스트를 먼저 쓰고, 끝내기 전에 게이트를 돌린다

이것을 강제하는 훅은 없다 (ADR-023). 기계가 막지 않으므로 스스로 지킨다.

1. **테스트를 먼저 쓰고 실패(red)를 본 뒤에 소스를 쓴다.**
2. **테스트 위치는 소스 구조를 미러링한다.** `src/FlexDir.Core/X/Foo.cs` →
   `tests/FlexDir.Core.Tests/X/FooTests.cs`.
3. Core 포트를 구현하는 fake 는 `tests/FlexDir.Core.Tests/Fakes/` 에 둔다.
   `App`·`Shell`·`Host` 의 테스트가 그 프로젝트를 참조해 재사용한다.

작업을 끝냈다고 말하기 전에 아래 셋이 전부 통과해야 한다.

```
dotnet build --nologo -warnaserror
dotnet test --nologo --blame-hang --blame-hang-timeout 120s
pwsh -File scripts/check-structure.ps1
```

`--blame-hang` 은 빼지 않는다. 취소 토큰을 넘기지 않은 fake 하나로 `dotnet test` 가
무한 대기한 적이 있다 — 매달림은 실패로 만들어야 진단이 된다.

Linux(클라우드 세션)에서는 `EnableWindowsTargeting=true` 로 빌드는 되지만 테스트는
`FlexDir.Core.Tests` 만 돈다. `App`·`Host` 테스트는 WPF 런타임이, `Shell` 테스트는
Windows shell 이 필요하다. 그 셋은 Windows 에서 확인했다고 말할 수 있을 때만 통과로 적는다.

## 7. 커밋

- Conventional Commits (`feat:` `fix:` `docs:` `refactor:` `chore:` `test:`).
- 메시지에 AI·에이전트·생성 도구 이름을 넣지 않는다. `Co-Authored-By` 도 넣지 않는다.
- 본문은 한국어 허용. 타입 prefix 는 영문.
- **커밋은 사용자가 요청할 때만.** 푸시는 별도 지시가 있을 때만.
