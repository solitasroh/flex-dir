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

## 3. UI 스레드에서 shell·I/O 를 부르지 않는다

`SHGetFileInfo`·`IShellItemImageFactory`·디렉터리 열거·네트워크 경로 접근은
전부 UI 스레드 밖에서 한다.

이유: 파일 탐색기가 멈추는 진짜 원인은 렌더링이 아니라 동기 shell 호출이다.
네트워크 경로에서는 초 단위로 블로킹된다.

## 4. 진실원천은 파일시스템이다

메모리 목록·썸네일·폴더별 뷰 설정은 전부 캐시다. 어긋나면 파일시스템을 믿는다.
외부 변경은 `IFolderWatcher` 로 감지해 갱신하고, **갱신 중에도 선택은 유지한다.**

## 5. 자동 채점할 수 없는 것은 자율 실행에 맡기지 않는다

Shell interop 과 UI 조작감은 사람이 확인한다. 자율 실행 대상은 `Core` 와
ViewModel 로직뿐이다.

이유: "손에 붙는가" 를 판정하는 셸 커맨드는 존재하지 않는다. 전작은 25 step 전부
자가 채점을 통과하고도 쓰이지 않았다.

## 6. 커밋

- Conventional Commits (`feat:` `fix:` `docs:` `refactor:` `chore:` `test:`).
- 메시지에 AI·에이전트·생성 도구 이름을 넣지 않는다. `Co-Authored-By` 도 넣지 않는다.
- 본문은 한국어 허용. 타입 prefix 는 영문.
- **커밋은 사용자가 요청할 때만.** 푸시는 별도 지시가 있을 때만.
  (`/harness:run` 자율 실행 구간은 예외 — 실행기가 step 단위로 커밋한다.)
