using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace FlexDir.Host.SingleInstance;

/// <summary>
/// 실행이 하나뿐임을 정하고, 두 번째 실행의 인자를 첫 프로세스로 넘기는 자리
/// (ADR-003 · docs/ARCHITECTURE.md §6).
/// <para>
/// <b>이름 있는 뮤텍스가 판정하고 이름 있는 파이프가 나른다.</b> 파이프 서버 생성만으로
/// 판정하지 않는 이유: 서버는 요청을 하나 받을 때마다 닫고 다시 열어야 하는데, 그 사이의
/// 틈에 들어온 두 번째 실행이 자기를 상주 프로세스로 착각한다. 뮤텍스는 프로세스가 살아
/// 있는 동안 계속 잡혀 있어 그 틈이 없다.
/// </para>
/// <para>
/// 뮤텍스를 <b>기다리거나 놓지 않는다.</b> 생성 시 <c>createdNew</c> 하나로 판정이 끝나므로
/// 잠금의 스레드 친화성 문제가 생기지 않고, 프로세스가 죽으면 핸들이 닫히며 이름이 사라져
/// 다음 실행이 새 상주 프로세스가 된다.
/// </para>
/// </summary>
public sealed class SingleInstanceGate : IDisposable
{
    /// <summary>상주 프로세스를 가리키는 이름. 사용자마다 다른 세션에서 각자 하나씩 뜬다.</summary>
    public const string DefaultName = "flex-dir-single-instance";

    /// <summary>
    /// 두 번째 실행이 상주 프로세스를 기다려 주는 시간. 무한이면 상주 프로세스가 정리 중일 때
    /// 아이콘을 눌러도 아무 일이 없는 상태로 매달린다.
    /// </summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly string name;
    private readonly Mutex mutex;

    private SingleInstanceGate(string name, Mutex mutex, bool isPrimary)
    {
        this.name = name;
        this.mutex = mutex;
        IsPrimary = isPrimary;
    }

    /// <summary>이 프로세스가 상주 프로세스인가. 아니면 인자를 넘기고 끝나야 한다.</summary>
    public bool IsPrimary { get; }

    public static SingleInstanceGate Acquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);

        return new SingleInstanceGate(name, mutex, createdNew);
    }

    /// <summary>
    /// 상주 프로세스가 받는 활성화 요청. 취소될 때까지 계속 낸다.
    /// <para>
    /// 한 번에 하나만 받고 곧바로 서버를 다시 연다. 한 번만 받으면 두 번째 실행부터
    /// 조용히 사라진다 — 상주 프로세스는 수십 번 활성화된다.
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<string>> ActivationsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsPrimary)
        {
            throw new InvalidOperationException(
                "상주 프로세스만 활성화 요청을 받는다. 두 번째 실행은 SendAsync 로 넘기고 끝난다.");
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            string payload;

            await using (var server = new NamedPipeServerStream(
                name,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                payload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }

            yield return Parse(payload);
        }
    }

    /// <summary>
    /// 상주 프로세스로 인자를 넘긴다. 붙을 상대가 없으면 <c>false</c> 다 — 그때 호출자가
    /// 무엇을 할지는 호출자의 판단이고, 여기서 매달리는 것만은 안 된다.
    /// </summary>
    public async Task<bool> SendAsync(
        IReadOnlyList<string> args,
        TimeSpan connectTimeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (IsPrimary)
        {
            throw new InvalidOperationException("상주 프로세스는 자기에게 인자를 넘기지 않는다.");
        }

        await using var client = new NamedPipeClientStream(
            ".", name, PipeDirection.Out, PipeOptions.Asynchronous);

        try
        {
            await client
                .ConnectAsync((int)connectTimeout.TotalMilliseconds, ct)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is TimeoutException or IOException)
        {
            // 상주 프로세스가 아직 듣지 않거나 정리 중이다.
            return false;
        }

        // 인자는 한 줄에 하나다. 경로에는 줄바꿈이 들어갈 수 없으므로 구분자로 충분하고,
        // 직렬화 형식을 하나 더 들이지 않는다.
        await using (var writer = new StreamWriter(client, new UTF8Encoding(false)))
        {
            foreach (var arg in args)
            {
                await writer.WriteLineAsync(arg.AsMemory(), ct).ConfigureAwait(false);
            }
        }

        return true;
    }

    public void Dispose() => mutex.Dispose();

    private static IReadOnlyList<string> Parse(string payload)
        => [.. payload
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)];
}
