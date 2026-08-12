using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Usage;

namespace FlexDir.Host.Startup;

/// <summary>
/// 실행과 활성화 요청이 앱에 닿는 자리. 상주 프로세스(ADR-003)에서 <b>사용자가 창을
/// 요구한 순간</b>이 여기다 — 첫 실행과, 그 뒤 두 번째 실행이 넘긴 요청 전부.
/// <para>
/// 그래서 사용 기록(ADR-007)을 남기는 곳도 여기다. 프로세스 수명이나 창 표시 시간이 아니라
/// 이 순간을 세는 이유는 <see cref="IUsageLog"/> 에 있다.
/// </para>
/// <para>
/// <c>Program.Main</c> 이 아니라 별도 클래스인 이유: <c>Program.cs</c> 는 TDD 가드의 검사
/// 대상이 아니다. 판단이 있는 코드가 그 안에 들어가면 채점되지 않는 자리에 로직이 자란다.
/// </para>
/// </summary>
public sealed class ActivationRouter
{
    private readonly WorkspaceViewModel workspace;
    private readonly IUsageLog usageLog;
    private readonly TimeProvider clock;
    private readonly Func<CancellationToken, Task>? presentWindow;

    /// <param name="presentWindow">
    /// 창을 보이고 앞으로 가져오는 손. null 이면 창 없는 조립이다 (테스트·프로브).
    /// 활성화가 곧 "창을 요구한 순간" 이므로 (ADR-003) 매 활성화마다 부른다.
    /// </param>
    public ActivationRouter(
        WorkspaceViewModel workspace,
        IUsageLog usageLog,
        TimeProvider clock,
        Func<CancellationToken, Task>? presentWindow = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(usageLog);
        ArgumentNullException.ThrowIfNull(clock);

        this.workspace = workspace;
        this.usageLog = usageLog;
        this.clock = clock;
        this.presentWindow = presentWindow;
    }

    /// <summary>
    /// 첫 실행. 마지막 폴더를 복원한 뒤 첫 활성화를 처리한다 — 이 순서가 존재 이유다:
    /// 복원이 활성화 뒤에 오면 인자로 요구한 폴더를 마지막 폴더가 덮는다.
    /// </summary>
    /// <param name="fallbackFolderPath">
    /// 기억이 없을 때 여는 폴더 (사용자 프로필). 환경을 읽는 곳은 조립뿐이므로 경로
    /// 문자열로 받고, 못 읽는 값이면 페인을 비워 둔다.
    /// </param>
    public async Task StartAsync(
        IReadOnlyList<string> args,
        string? fallbackFolderPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        var fallback =
            fallbackFolderPath is not null && LocationId.TryParse(fallbackFolderPath, out var parsed, out _)
                ? parsed
                : null;

        await workspace.RestoreAsync(fallback, ct).ConfigureAwait(false);
        await ActivateAsync(args, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 한 번의 활성화. 사용을 남기고, 인자에 폴더가 있으면 <b>활성 페인</b>에서 연다.
    /// <para>
    /// 왼쪽에 못박지 않는다 — 오른쪽에서 일하는 중에 두 번째 실행이 왼쪽을 갈아치우면
    /// 보고 있던 폴더가 사라진다.
    /// </para>
    /// </summary>
    public async Task ActivateAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        // 기록이 먼저다. 폴더를 여는 데 실패해도 사용은 사용이다 — 게이트가 세는 것은
        // 성공한 조작이 아니라 앱을 쓴 날이다.
        await usageLog.RecordAsync(clock.GetLocalNow(), ct).ConfigureAwait(false);

        // 창이 폴더보다 먼저다 — 사용자가 요구한 것은 창이고 폴더는 그 안의 내용이다.
        if (presentWindow is { } present)
        {
            await present(ct).ConfigureAwait(false);
        }

        if (FirstLocation(args) is { } location)
        {
            await workspace.ActiveTab.NavigateAsync(location, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 상주 중 들어오는 활성화 요청을 계속 처리한다. 취소로만 끝난다.
    /// </summary>
    public async Task RunAsync(
        IAsyncEnumerable<IReadOnlyList<string>> activations,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(activations);

        await foreach (var args in activations.WithCancellation(ct).ConfigureAwait(false))
        {
            try
            {
                await ActivateAsync(args, ct).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // 활성화 하나 때문에 귀를 닫으면 그 뒤의 두 번째 실행이 전부 조용히
                // 사라지고, 사용자에게는 앱이 죽은 것처럼 보인다. 이 자리에서 사용자에게
                // 알릴 방법도 없다 — 창이 있다는 보장이 없다.
            }
        }
    }

    /// <summary>
    /// 인자 중 경로로 읽히는 첫 번째 것. 없으면 <c>null</c> 이다.
    /// <para>
    /// 첫 인자만 보지 않는다 — 실제 실행은 스위치와 경로가 섞여 들어오고, 스위치 하나에
    /// 폴더를 잃으면 두 번째 실행이 아무 일도 하지 않는 것처럼 보인다.
    /// </para>
    /// </summary>
    private static LocationId? FirstLocation(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (LocationId.TryParse(arg, out var location, out _))
            {
                return location;
            }
        }

        return null;
    }
}
