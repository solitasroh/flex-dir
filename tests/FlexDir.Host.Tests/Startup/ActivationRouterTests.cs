using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Usage;

using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// 실행과 활성화 요청이 앱에 닿는 자리. 상주 프로세스(ADR-003)에서 <b>사용자가 창을
/// 요구한 순간</b>이 여기이므로, 사용 기록(ADR-007)을 남기는 곳도 여기다.
/// <para>
/// 두 번째 실행이 넘긴 폴더를 실제로 여는 것까지가 이 클래스의 일이다 — 인자를 받아
/// 아무것도 하지 않으면 single instance 는 "두 번째 창이 안 뜬다" 와 구별되지 않는다.
/// </para>
/// </summary>
public class ActivationRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly FakeUsageLog usage = new();

    [Fact]
    public async Task ActivateAsync_RecordsThatTheAppWasUsed()
    {
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync([], CancellationToken.None);

        Assert.Equal(Now, Assert.Single(usage.Records));
    }

    [Fact]
    public async Task ActivateAsync_WithAFolder_OpensItInTheActivePane()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync([@"C:\Temp"], CancellationToken.None);

        Assert.Equal(folder, workspace.Left.CurrentLocation);
        Assert.Single(workspace.Left.Items);
    }

    [Fact]
    public async Task ActivateAsync_WithAFolder_FollowsTheActivePane()
    {
        // 왼쪽에 못박으면 오른쪽에서 일하던 중에 두 번째 실행이 왼쪽을 갈아친다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var workspace = CreateWorkspace();
        workspace.ActivateCommand.Execute(PaneSide.Right);

        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync([@"C:\Temp"], CancellationToken.None);

        Assert.Equal(folder, workspace.Right.CurrentLocation);
        Assert.Null(workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task ActivateAsync_WithoutArguments_OnlyRecordsUsage()
    {
        // 인자 없는 두 번째 실행은 "창을 다오" 라는 뜻이다. 열려 있던 폴더를 건드리면 안 된다.
        Folder(@"C:\Temp", "a.txt");
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync([], CancellationToken.None);

        Assert.Null(workspace.Left.CurrentLocation);
        Assert.Single(usage.Records);
    }

    [Fact]
    public async Task ActivateAsync_WithAnUnparseableArgument_StillRecordsUsage()
    {
        // 쓰레기 인자로 사용 기록이 사라지면 게이트의 숫자가 조용히 낮아진다.
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync(["--이건경로가아니다"], CancellationToken.None);

        Assert.Null(workspace.Left.CurrentLocation);
        Assert.Single(usage.Records);
    }

    [Fact]
    public async Task ActivateAsync_TakesTheFirstArgumentThatIsAPath()
    {
        // 실제 실행은 스위치와 경로가 섞여 들어온다. 첫 인자만 보면 스위치 하나에 폴더를 잃는다.
        var folder = Folder(@"C:\Temp", "a.txt");
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.ActivateAsync(["--new-window", @"C:\Temp"], CancellationToken.None);

        Assert.Equal(folder, workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task RunAsync_HandlesEveryActivation()
    {
        var first = Folder(@"C:\A", "a.txt");
        var second = Folder(@"C:\B", "b.txt");
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await router.RunAsync(Activations(default, [@"C:\A"], [@"C:\B"]), CancellationToken.None);

        Assert.Equal(2, usage.Records.Count);
        Assert.Equal(second, workspace.Left.CurrentLocation);
        Assert.NotEqual(first, workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task RunAsync_WhenOneActivationFails_KeepsServingTheNext()
    {
        // 상주 프로세스가 활성화 하나 때문에 귀를 닫으면, 그 뒤로 두 번째 실행이 전부
        // 조용히 사라진다 — 사용자에게는 앱이 죽은 것처럼 보인다.
        var folder = Folder(@"C:\B", "b.txt");
        var workspace = CreateWorkspace();
        var failing = new FailingOnceUsageLog();
        var router = new ActivationRouter(workspace, failing, new FixedClock(Now));

        await router.RunAsync(Activations(default, [@"C:\A"], [@"C:\B"]), CancellationToken.None);

        Assert.Equal(2, failing.Attempts);
        Assert.Equal(folder, workspace.Left.CurrentLocation);
    }

    [Fact]
    public async Task RunAsync_Cancelled_Ends()
    {
        var workspace = CreateWorkspace();
        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.RunAsync(Activations(default, [@"C:\Temp"]), cts.Token));

        Assert.Empty(usage.Records);
    }

    [Fact]
    public async Task Arguments_AreInvalid_Throw()
    {
        var workspace = CreateWorkspace();

        Assert.Throws<ArgumentNullException>(() => new ActivationRouter(null!, usage, new FixedClock(Now)));
        Assert.Throws<ArgumentNullException>(() => new ActivationRouter(workspace, null!, new FixedClock(Now)));
        Assert.Throws<ArgumentNullException>(() => new ActivationRouter(workspace, usage, null!));

        var router = new ActivationRouter(workspace, usage, new FixedClock(Now));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => router.ActivateAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => router.RunAsync(null!, CancellationToken.None));
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private WorkspaceViewModel CreateWorkspace() => new(CreatePane(), CreatePane(), viewStates);

    private PaneViewModel CreatePane() => new(
        source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
        dispatcher, CultureInfo.InvariantCulture, TimeZoneInfo.Utc);

    /// <summary>
    /// 활성화 요청 흐름. 실물(<c>SingleInstanceGate.ActivationsAsync</c>)처럼 비동기이고
    /// 취소를 관측한다 — 동기 시퀀스로만 재면 취소가 통하는지 알 수 없다.
    /// </summary>
    private static async IAsyncEnumerable<IReadOnlyList<string>> Activations(
        [EnumeratorCancellation] CancellationToken ct,
        params string[][] requests)
    {
        foreach (var request in requests)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            yield return request;
        }
    }

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);
        source.Folders[folder] =
            [.. names.Select(name => new FileItem(
                name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None))];

        return folder;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }

    /// <summary>멈춰 있는 시계. 기록된 시각을 단정하려면 시각이 결정적이어야 한다.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>첫 기록만 실패한다. 계약을 어기는 구현체를 흉내 내는 자리다.</summary>
    private sealed class FailingOnceUsageLog : IUsageLog
    {
        public int Attempts { get; private set; }

        public ValueTask RecordAsync(DateTimeOffset at, CancellationToken ct)
        {
            Attempts++;

            return Attempts == 1
                ? ValueTask.FromException(new IOException("디스크"))
                : ValueTask.CompletedTask;
        }
    }
}
