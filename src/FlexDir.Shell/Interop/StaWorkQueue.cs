using System.Collections.Concurrent;

namespace FlexDir.Shell.Interop;

/// <summary>
/// shell 호출을 STA 스레드에서 돌리는 작업 큐.
/// <para>
/// <b>존재 이유는 하나다.</b> <c>SHGetFileInfo</c>·<c>IFileOperation</c>·<c>IContextMenu</c> 는
/// STA 를 요구하는데 <c>Task.Run</c> 과 스레드풀은 MTA 다 — 그대로 쓰면 안 된다
/// (docs/SHELL_NOTES.md §COM 아파트먼트). 그래서 전용 스레드를 만들어
/// <see cref="ApartmentState.STA"/> 로 초기화한다.
/// </para>
/// <para>
/// 스레드가 여러 개인 이유: shell 조회는 동기 블로킹이고 네트워크·클라우드 항목에서 초 단위로
/// 멈춘다 (docs/SHELL_NOTES.md §아이콘 함정 1). 하나면 그 하나가 막는 동안 나머지가 전부 멈춘다.
/// </para>
/// <para>
/// <b>네이티브 핸들을 이 큐 밖으로 내보내지 마라.</b> 작업 안에서 만들고 작업 안에서 해제한다.
/// 전작은 워커가 만든 <c>HICON</c> 을 UI 로 넘기다 종료 시점에 흘렸다
/// (docs/SHELL_NOTES.md §아이콘 함정 3) — 핸들이 큐를 넘지 않으면 그 함정 자체가 없다.
/// </para>
/// </summary>
internal sealed class StaWorkQueue : IDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread[] workers;

    private bool disposed;

    /// <param name="threads">동시에 돌릴 shell 호출 수.</param>
    /// <param name="name">스레드 이름에 들어갈 꼬리표. 디버거에서 무엇이 막혔는지 보려면 필요하다.</param>
    public StaWorkQueue(int threads, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threads);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        workers = new Thread[threads];

        for (var index = 0; index < threads; index++)
        {
            var worker = new Thread(Drain)
            {
                Name = $"flex-dir shell {name} {index}",

                // 프로세스 종료를 붙잡지 않는다. 상주 프로세스(ADR-003)라 창을 닫아도
                // 살아 있어야 하지만, 완전 종료를 이 스레드가 막아서는 안 된다.
                IsBackground = true,
            };

            worker.SetApartmentState(ApartmentState.STA);
            workers[index] = worker;
            worker.Start();
        }
    }

    /// <summary>
    /// STA 워커에서 <paramref name="job"/> 을 돌린다. 취소는 큐에서 기다리는 동안에도 통한다.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        ObjectDisposedException.ThrowIf(disposed, this);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 앞선 작업이 초 단위로 막혀 있는 동안 큐에서 기다리던 요청도 풀려야 한다.
        // 그러지 않으면 폴더를 떠난 뒤에도 그 요청이 살아서 워커를 붙잡는다.
        var registration = ct.Register(() => completion.TrySetCanceled(ct));

        try
        {
            work.Add(() => Execute(job, completion, registration, ct));
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // CompleteAdding 이후다.
            registration.Dispose();

            throw new ObjectDisposedException(nameof(StaWorkQueue));
        }

        return completion.Task;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        work.CompleteAdding();

        // 명시적으로 join 한다. 전작은 워커를 정리하지 않아 마지막 결과를 흘렸다
        // (docs/SHELL_NOTES.md §아이콘 함정 3).
        foreach (var worker in workers)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        // BlockingCollection 은 Dispose 하지 않는다. join 이 시간 안에 끝나지 않은 워커가
        // 남아 있으면 GetConsumingEnumerable 안에서 ObjectDisposedException 이 터지고,
        // 그것은 워커 스레드에서 잡히지 않아 프로세스를 죽인다.
    }

    private static void Execute<T>(
        Func<T> job,
        TaskCompletionSource<T> completion,
        CancellationTokenRegistration registration,
        CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                completion.TrySetCanceled(ct);

                return;
            }

            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                // SHELL_NOTES §COM 아파트먼트 의 함정: 아파트먼트를 잡지 못했을 때 큐를 막지
                // 말고 계속 비우되 각 명령을 실패로 처리한다. 막으면 큐가 영원히 멈춘다.
                completion.TrySetException(new InvalidOperationException(
                    "shell 워커가 STA 로 초기화되지 않았다. SHGetFileInfo·IFileOperation·IContextMenu 는 STA 를 요구한다."));

                return;
            }

            completion.TrySetResult(job());
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally
        {
            registration.Dispose();
        }
    }

    private void Drain()
    {
        try
        {
            foreach (var job in work.GetConsumingEnumerable())
            {
                // job 은 자기 예외를 스스로 처리한다. 여기서 새는 것이 있으면 워커가 죽고
                // 큐가 조용히 멈추므로 방어한다.
                job();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // 큐가 닫혔다. 정상 종료다.
        }
    }
}
