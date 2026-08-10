using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 되풀이되는 감시 오버플로를 늦춘다 (docs/PRD-v2.md §13 · <see cref="WatchBackoff"/>).
/// <para>
/// <b>왜 이 테스트가 있어야 하나</b>: 오버플로의 처방(전체 새로고침)이 감시를 다시 걸고,
/// 다시 건 감시가 즉시 같은 오류를 내는 경로가 실물에 있다 — <c>\\wsl.localhost</c> 에서
/// 12초에 재열거 123회를 실측했고 그동안 UI 선택이 먹히지 않았다. 처방과 증상이 한 고리에
/// 있으므로 <b>대기가 없으면 반드시 폭주한다</b>.
/// </para>
/// <para>
/// <see cref="ManualTimeProvider"/> 를 감지 않으면 오버플로 사이 간격이 0 이라 전부
/// '이어진 것' 으로 센다 — 실측한 폭주(100~200ms 간격)를 그대로 재현한 것이다.
/// </para>
/// </summary>
public class PaneWatchBackoffTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly ManualTimeProvider clock = new();

    private PaneViewModel CreatePane() => new(
        source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
        dispatcher, Culture, TimeZoneInfo.Utc, contextMenus, clock);

    private LocationId Folder()
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out _));

        source.Folders[folder] =
        [
            new FileItem("a.txt", folder.Combine("a.txt"), 0, DateTimeOffset.UnixEpoch, FileItemFlags.None),
        ];

        return folder;
    }

    private static async Task UntilAsync(Func<bool> reached, string expectation)
    {
        var deadline = DateTime.UtcNow + Limit;

        while (DateTime.UtcNow < deadline)
        {
            if (reached())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"{expectation} — {Limit.TotalSeconds}초 안에 일어나지 않았다.");
    }

    [Fact]
    public async Task OneOverflow_RefreshesRightAway()
    {
        // 한 번의 오버플로는 정상이다 (파일 100개를 한 번에 복사하면 그것만으로 넘친다).
        // 늦추면 목록이 그만큼 오래 파일시스템과 어긋난 채 남는다.
        var folder = Folder();
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        var before = source.EnumerateCalls.Count;

        watcher.PushOverflow();

        await UntilAsync(() => source.EnumerateCalls.Count > before, "오버플로 한 번이 곧바로 재열거를 부른다");
        Assert.False(pane.IsWatchThrottled);
    }

    /// <summary>실물의 폭주 간격(100~200ms)과 같은 리듬으로 오버플로를 민다.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// 폭주하는 감시를 흉내낸다 — 멎을 때까지 계속 오버플로를 낸다.
    ///
    /// <para>
    /// <b>재열거를 기다렸다가 다음 것을 밀면 안 된다.</b> 재열거는 감시를 새로 걸고, 그
    /// 교체 사이에 민 알림은 유실될 수 있다 — 기다리는 테스트는 거기서 간헐적으로 멈춘다
    /// (게이트에서 실제로 한 번 깨졌다). 실물의 감시도 우리 사정을 기다려 주지 않는다.
    /// </para>
    /// <para>
    /// <b>한꺼번에 밀어도 안 된다.</b> 감시 루프가 그것들을 한 배치로 묶어
    /// <c>ApplyAsync</c> 를 한 번만 지나므로 폭주가 재현되지 않는다.
    /// </para>
    /// </summary>
    private async Task<int> PushOverflowsAsync(TimeSpan howLong, Func<bool>? until = null)
    {
        var pushed = 0;
        var deadline = DateTime.UtcNow + howLong;

        while (DateTime.UtcNow < deadline)
        {
            if (until?.Invoke() == true)
            {
                break;
            }

            watcher.PushOverflow();
            pushed++;

            await Task.Delay(Beat);
        }

        return pushed;
    }

    [Fact]
    public async Task RepeatedOverflows_DoNotRefreshOncePerOverflow()
    {
        // 여기가 이 결함의 본체다. 대기가 없으면 민 수만큼 재열거가 나가고, 실물에서는
        // 감시가 스스로 되풀이하므로 초당 열 번이 된다.
        var folder = Folder();
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        var before = source.EnumerateCalls.Count;

        var pushed = await PushOverflowsAsync(TimeSpan.FromSeconds(2));
        var refreshes = source.EnumerateCalls.Count - before;

        // 예정된 대기는 0 + 0.5 + 1 + 2s 이므로 2초 안에는 네 번 남짓이다. 절반을
        // 기준으로 잡는다 — 정확한 수를 못박으면 무관한 변경이 이 테스트를 깨뜨린다.
        Assert.True(
            refreshes * 2 < pushed,
            $"재열거가 늦춰지지 않았다 — 오버플로 {pushed}회에 재열거 {refreshes}회.");
    }

    [Fact]
    public async Task RepeatedOverflows_TellTheUser()
    {
        // 목록이 파일시스템과 어긋날 수 있다는 것은 사용자가 손쓸 수 있는(새로 고침)
        // 사실이라 조용히 두지 않는다 (사용자 결정 2026-08-10).
        var folder = Folder();
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        await PushOverflowsAsync(Limit, until: () => pane.IsWatchThrottled);

        Assert.True(pane.IsWatchThrottled, "자동 갱신이 느려졌음을 알려야 한다.");
        Assert.Contains("자동 갱신", pane.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovingToAnotherFolder_ClearsTheWarning()
    {
        // 세는 것은 폴더마다 따로다 — 앞선 폴더의 폭주가 다음 폴더의 대기와 문구로 새면
        // 멀쩡한 폴더가 고장난 것처럼 보인다.
        var folder = Folder();
        Assert.True(LocationId.TryParse(@"C:\Other", out var other, out _));
        source.Folders[other] = [];

        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        await PushOverflowsAsync(Limit, until: () => pane.IsWatchThrottled);

        Assert.True(pane.IsWatchThrottled, "먼저 경고가 켜져 있어야 이 테스트가 의미를 갖는다.");

        await pane.NavigateAsync(other);

        Assert.False(pane.IsWatchThrottled);
        Assert.DoesNotContain("자동 갱신", pane.StatusText, StringComparison.Ordinal);
    }
}
