namespace FlexDir.App.Threading;

/// <summary>
/// UI 스레드로 동작을 옮기는 포트. 구현체는 WPF <c>Dispatcher</c> 를 감싸며
/// <c>FlexDir.Host</c> 가 주입한다.
/// <para>
/// ViewModel 이 <c>Dispatcher.CurrentDispatcher</c>·<c>Application.Current</c> 를 직접
/// 부르지 않게 하는 것이 목적이다. 테스트에는 WPF 애플리케이션이 없고, 열거는 UI 스레드
/// 밖에서 돌기 때문에 (CLAUDE.md §3) 반영 지점만 명시적으로 옮긴다.
/// </para>
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// UI 스레드에서 <paramref name="action"/> 을 실행하고 완료를 기다릴 수 있는 Task 를 낸다.
    /// 동작이 예외로 끝나면 그 Task 가 오류가 된다 — 목록 갱신에서 터진 예외를 삼키면
    /// 화면과 파일시스템이 어긋난 채로 남는다.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>지금 UI 스레드인가.</summary>
    bool IsOnUiThread { get; }
}
