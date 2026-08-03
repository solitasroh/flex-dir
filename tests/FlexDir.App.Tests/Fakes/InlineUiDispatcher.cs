using FlexDir.App.Threading;

namespace FlexDir.App.Tests.Fakes;

/// <summary>
/// <see cref="IUiDispatcher"/> 의 fake. 넘어온 동작을 그 자리에서 실행한다.
/// <para>
/// 테스트에는 WPF 애플리케이션이 없으므로 <c>Dispatcher</c> 를 만들 수 없다. 실제
/// <c>Dispatcher</c> 기반 구현은 수동 UI phase 의 일이다 (ADR-009).
/// </para>
/// <para>
/// <see cref="IsInvoking"/> 를 둔 이유: "목록 변경은 전부 UI 스레드에서" 를 검증할 수단이
/// 필요하다. 컬렉션 알림을 받는 쪽에서 이 값을 보면 그 변경이 <see cref="InvokeAsync"/> 안에서
/// 일어났는지 알 수 있다.
/// </para>
/// </summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    /// <summary>지금 <see cref="InvokeAsync"/> 안인가.</summary>
    public bool IsInvoking { get; private set; }

    /// <summary>실행한 동작의 수.</summary>
    public int Invocations { get; private set; }

    /// <summary>인라인 실행이므로 항상 참이다.</summary>
    public bool IsOnUiThread => true;

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Invocations++;
        IsInvoking = true;

        try
        {
            action();
        }
        catch (Exception error)
        {
            // 예외를 삼키지 않는다. 실제 Dispatcher.InvokeAsync 도 반환한 Task 를 오류로 만든다 —
            // 목록 갱신에서 터진 예외가 조용히 사라지면 화면과 파일시스템이 어긋난 채로 남는다.
            return Task.FromException(error);
        }
        finally
        {
            IsInvoking = false;
        }

        return Task.CompletedTask;
    }
}
