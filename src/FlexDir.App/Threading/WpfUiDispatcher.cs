using System.Windows.Threading;

namespace FlexDir.App.Threading;

/// <summary>
/// <see cref="IUiDispatcher"/> 의 실물. WPF <see cref="Dispatcher"/> 하나를 감싼다.
/// <para>
/// <c>Application.Current.Dispatcher</c> 를 여기서 읽지 않는다. 그것을 읽는 순간 이 어댑터가
/// WPF 애플리케이션의 존재를 요구하게 되고, 테스트와 Host 조립이 창 없이 돌아갈 수 없다 —
/// 어느 Dispatcher 인지는 <c>FlexDir.Host</c> 가 정한다 (docs/ARCHITECTURE.md §1).
/// </para>
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        this.dispatcher = dispatcher;
    }

    public bool IsOnUiThread => dispatcher.CheckAccess();

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // 종료 중인 Dispatcher 는 더 이상 동작을 받지 않고 BeginInvoke 가 던진다. 완전
        // 종료 경로(ADR-003)가 ViewModel 정리를 지나며 이 자리를 밟으므로, 예외로 만들면
        // 정리가 거기서 멈추고 shell STA 워커가 남는다. 조용히 물러난다 — 창이 사라진
        // 뒤의 목록 갱신은 아무도 보지 않는다.
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return Task.CompletedTask;
        }

        // 이미 UI 스레드면 그 자리에서 실행한다. 큐에 넣으면 폴더 하나를 여는 데 왕복이
        // 수백 번 생기고, UI 스레드에서 그 완료를 기다리는 호출자는 자기 큐를 기다린다.
        if (dispatcher.CheckAccess())
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                // Dispatcher.InvokeAsync 와 같은 모양으로 낸다 — 호출자가 인라인 여부에 따라
                // 다른 실패 형태를 보면 안 된다.
                return Task.FromException(error);
            }

            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
