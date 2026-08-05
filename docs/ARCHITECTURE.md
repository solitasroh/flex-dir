# ARCHITECTURE — flex-dir

## 1. 프로젝트 구성

```
FlexDir.Core        순수 .NET. UI·COM·Win32 의존 없음
                    모델 · 정렬 · 뷰상태 · 경로 추상화 · 포트 인터페이스

FlexDir.Shell       → Core
                    COM interop 구현체. PIDL · 컨텍스트메뉴 · 썸네일 ·
                    클립보드 · 파일조작 · 폴더감시

FlexDir.App         → Core
                    WPF. View(XAML) + ViewModel. Shell 을 모른다

FlexDir.Host        → Core · Shell · App
                    exe. Application 진입점과 DI 조립만. 로직 없음

FlexDir.Core.Tests  → Core
                    포트의 fake 와 계약 기반 클래스(*Contract)를 둔다

FlexDir.App.Tests   → App, Core, Core.Tests
FlexDir.Shell.Tests → Shell, Core, Core.Tests

                    두 테스트 프로젝트가 Core.Tests 를 참조하는 이유는 하나다 —
                    fake 와 실물 구현체가 같은 계약 클래스를 상속해 같은 검증을 받는 것
```

**`App` 이 `Shell` 을 참조하면 컴파일이 깨진다.** 이것이 구조 게이트다.
`Host` 를 따로 둔 이유가 이것뿐이다 — DI 조립은 두 쪽을 모두 알아야 하므로,
그 지식을 로직 없는 프로젝트 하나에 가둔다.

## 2. 포트 (Core 가 정의, Shell 이 구현)

| 포트 | 책임 |
|---|---|
| `IFolderSource` | 경로를 받아 항목을 **비동기 스트림**으로 낸다. 취소 가능 |
| `IThumbnailSource` | 항목의 썸네일/아이콘. 비동기, 취소 가능 |
| `IContextMenuProvider` | shell 컨텍스트 메뉴 표시와 명령 실행 |
| `IClipboardBridge` | 탐색기 호환 복사/잘라내기/붙여넣기 |
| `IFileOperations` | 복사·이동·삭제(휴지통)·이름변경·새 폴더 |
| `IFolderWatcher` | 외부 변경 알림 |
| `IViewStateStore` | 폴더별 뷰 모드·정렬 저장/복원 |
| `IUsageLog` | 실행·사용 시간 기록 |
| `IItemActivator` | 더블클릭 시 연결 프로그램 실행 |

포트는 **경로를 문자열이 아니라 `LocationId` 로** 주고받는다. v1 은 로컬
파일시스템 경로만 담지만, v2 의 UNC·shell 네임스페이스(PIDL)를 같은 타입으로
표현할 수 있어야 한다. 이 추상화가 없으면 v2 에서 전면 수정이 된다.

## 3. 데이터 흐름

```
사용자 입력
   → ViewModel (App)
   → 포트 인터페이스 (Core)
   → 구현체 (Shell)          ← 여기부터 UI 스레드 밖
   → 결과를 비동기로 ViewModel 에 반영
   → 데이터 바인딩으로 View 갱신
```

**UI 스레드에서 shell 을 부르지 않는다.** 파일 탐색기가 멈추는 원인은 렌더링이
아니라 동기 shell 호출이다.

## 4. 상태

| 상태 | 소유자 | 수명 |
|---|---|---|
| 페인별 현재 경로·히스토리 | `PaneViewModel` | 프로세스 |
| 항목 목록 · 선택 | `PaneViewModel` | 폴더 전환까지 |
| 뷰 모드 · 정렬 | `IViewStateStore` | 영구 (폴더별) |
| 스플리터 비율 · 창 위치 | `IViewStateStore` | 영구 (전역) |
| 썸네일 | shell 캐시 | **우리가 소유하지 않는다** |
| 사용 로그 | `IUsageLog` | 영구 (append) |

**진실원천은 파일시스템이다.** 위 항목 중 목록·썸네일은 캐시이며, 어긋나면
파일시스템을 믿는다.

저장 위치: `%LOCALAPPDATA%\flex-dir\`

## 5. 리스트 가상화 — 깨뜨리면 안 되는 것

WPF 데이터 가상화는 쉽게 무력화된다. 다음을 금지한다:

- `ItemsControl` 을 `ScrollViewer` 로 감싸기 → 가상화 완전 해제
- `VirtualizingPanel.IsVirtualizing="False"`
- 가변 행 높이 (`VirtualizationMode=Recycling` 과 상충)
- `ItemsSource` 에 `IEnumerable` 전체 열거를 강제하는 LINQ 체인

필수: `VirtualizingStackPanel` + `VirtualizationMode="Recycling"` +
`ScrollUnit="Item"`.

전작은 속도를 위해 `LVS_OWNERDATA` 를 골랐고, 그 대가로 뷰 모드가 Details
하나로 고정됐다. WPF 에서는 `DataTemplate` 교체로 뷰 모드가 바뀌고 가상화는
유지된다 — **그 이점을 잃지 않는 것이 위 규칙의 목적이다.**

## 6. 상주 프로세스

- single instance. 두 번째 실행은 기존 프로세스에 인자를 넘기고 종료한다.
- 창을 닫아도 프로세스는 유지한다 (트레이 또는 무창 상태).
- 완전 종료는 명시적 메뉴로만.
- 이유: WPF 의 유일한 실질 약점이 cold start 다. 한 번만 낸다.

## 7. 계측

별도 벤치마크 CLI 를 만들지 않는다. 전작은 그걸 만들고 **실사용과 무관한 것을**
쟀다. 계측은 앱 안에 두고 실제 사용 경로에서 측정한다.

측정 대상: cold start · 상주 중 창 표시 · 폴더 전환 후 첫 항목 표시.

금지 대상은 **재는 도구**다. Shell 포트를 실물에 물려 보는 수동 확인용 프로브는 여기
해당하지 않으며 `.harness/probe/` 에 sln 밖으로 둔다 (ADR-015).
