using FlexDir.Core.Locations;
using FlexDir.Core.Tests.Watching;
using FlexDir.Core.Watching;
using FlexDir.Shell.Watching;

using Xunit;

namespace FlexDir.Shell.Tests.Watching;

/// <summary>
/// <see cref="FileSystemWatcher"/> 기반 구현체.
/// <para>
/// <see cref="FolderWatcherContract"/> 를 상속해 fake 가 받던 검증을 실물도 받는다.
/// 아래에 더 붙는 것은 <b>실제 파일시스템이라서 확인할 수 있는 것</b>이다 — 파일을 만들고
/// 지우고 고쳤을 때 실제로 관측되는가, 하위 폴더를 끌어오지 않는가, 없는 폴더는 어디서 터지는가.
/// </para>
/// <para>
/// <b>오버플로만은 실물로 재지 못한다.</b> 버퍼를 넘치게 하려면 OS 가 이벤트를 쏟아내는
/// 속도에 기대야 하고, 그런 테스트는 통과와 실패를 번갈아 낸다. 매 턴 도는 게이트에
/// 간헐적 실패를 넣는 것은 이 저장소가 가장 피하려는 것이라(harness.config.json 의 verify 주석)
/// <c>FileSystemWatcher</c> 를 만드는 자리를 바꿔 끼워 <c>Error</c> 를 직접 올린다. 즉
/// "Error 를 Overflow 로 올리는가" 는 자동으로 재고, "실제로 넘치는가" 는 사람이 확인한다
/// (ADR-009 의 경계와 같다).
/// </para>
/// </summary>
public sealed class FileSystemFolderWatcherTests : FolderWatcherContract, IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private const string Existing = "old.txt";

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "flex-dir-tests", Guid.NewGuid().ToString("N"));

    private readonly TaskCompletionSource<OverflowRaisingWatcher> created =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FileSystemFolderWatcherTests()
    {
        Directory.CreateDirectory(root);

        // 이름변경 대상은 감시가 걸리기 전에 있어야 한다. 감시를 건 뒤에 만들면 첫 변경이
        // Added 가 되어 계약의 "첫 변경" 검사가 이름변경을 보지 못한다.
        File.WriteAllText(Path.Combine(root, Existing), string.Empty);
    }

    protected override LocationId WatchedFolder => Folder(root);

    protected override IFolderWatcher CreateWatcher(LocationId folder) =>
        new FileSystemFolderWatcher(path =>
        {
            var watcher = new OverflowRaisingWatcher(path);
            created.TrySetResult(watcher);
            return watcher;
        });

    protected override async Task CauseAsync(IFolderWatcher watcher, FolderChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (change.Kind)
        {
            case FolderChangeKind.Overflow:
                // 감시가 실제로 걸린 뒤에 올려야 한다. 반복자 본문은 첫 MoveNextAsync 에서 돈다.
                (await created.Task.WaitAsync(Limit)).RaiseOverflow();
                break;

            case FolderChangeKind.Renamed:
                File.Move(Path.Combine(root, change.OldName!), Path.Combine(root, change.Name));
                break;

            case FolderChangeKind.Added:
                await File.WriteAllTextAsync(Path.Combine(root, change.Name), string.Empty);
                break;

            case FolderChangeKind.Removed:
                File.Delete(Path.Combine(root, change.Name));
                break;

            case FolderChangeKind.Changed:
                await File.AppendAllTextAsync(Path.Combine(root, change.Name), "x");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 임시 폴더 청소 실패는 테스트 결과와 무관하다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── 실제 파일시스템에서 관측되는가 ──────────────────────────────

    [Fact]
    public async Task CreatingAFile_IsReportedAsAdded()
    {
        var change = await FirstMatchAsync(
            c => c.Kind == FolderChangeKind.Added,
            () => File.WriteAllText(Path.Combine(root, "fresh.txt"), string.Empty));

        Assert.Equal("fresh.txt", change.Name);
    }

    [Fact]
    public async Task DeletingAFile_IsReportedAsRemoved()
    {
        var change = await FirstMatchAsync(
            c => c.Kind == FolderChangeKind.Removed,
            () => File.Delete(Path.Combine(root, Existing)));

        Assert.Equal(Existing, change.Name);
    }

    // NotifyFilter 에 LastWrite·Size 가 빠지면 내용만 바뀐 파일이 조용히 stale 로 남는다.
    [Fact]
    public async Task WritingToAFile_IsReportedAsChanged()
    {
        var change = await FirstMatchAsync(
            c => c.Kind == FolderChangeKind.Changed,
            () => File.AppendAllText(Path.Combine(root, Existing), "x"));

        Assert.Equal(Existing, change.Name);
    }

    // 목록은 폴더 하나를 보여준다. 하위 폴더 변경까지 끌어오면 보이지도 않는 항목 때문에
    // 갱신이 돌고, 큰 트리에서는 그것만으로 UI 가 바쁘다.
    [Fact]
    public async Task ChangesInASubdirectory_AreNotReported()
    {
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));

        var change = await FirstMatchAsync(
            c => c.Name.Equals("marker.txt", StringComparison.Ordinal),
            () =>
            {
                File.WriteAllText(Path.Combine(nested.FullName, "buried.txt"), string.Empty);
                File.WriteAllText(Path.Combine(root, "marker.txt"), string.Empty);
            });

        // 하위 폴더의 파일이 먼저 만들어졌는데도 먼저 관측된 것은 감시 폴더의 파일이다.
        Assert.Equal("marker.txt", change.Name);
    }

    // 없는 폴더는 WatchAsync 호출이 아니라 스트림에서 터져야 한다. 호출 지점에서 터지면
    // 소비자(PaneViewModel)가 열거와 같은 자리에서 잡지 못한다.
    [Fact]
    public async Task MissingFolder_FailsOnTheStreamNotOnTheCall()
    {
        var watcher = new FileSystemFolderWatcher();
        var missing = Folder(Path.Combine(root, "gone"));

        var changes = watcher.WatchAsync(missing, CancellationToken.None);   // 여기서는 터지지 않는다

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in changes)
            {
            }
        });
    }

    /// <summary>
    /// 감시를 먼저 걸고 <paramref name="cause"/> 를 일으켜 <paramref name="match"/> 에 맞는
    /// 첫 변경을 낸다. 실제 파일시스템은 같은 조작에도 여분의 이벤트를 내므로
    /// (파일 생성이 Created 와 Changed 를 함께 내는 등) 첫 변경이 아니라 조건으로 고른다.
    /// </summary>
    private async Task<FolderChange> FirstMatchAsync(Func<FolderChange, bool> match, Action cause)
    {
        var watcher = new FileSystemFolderWatcher();

        using var cts = new CancellationTokenSource(Limit);

        await using var changes = watcher.WatchAsync(WatchedFolder, cts.Token).GetAsyncEnumerator();

        var next = changes.MoveNextAsync().AsTask();
        cause();

        while (await next.WaitAsync(Limit))
        {
            if (match(changes.Current))
            {
                return changes.Current;
            }

            next = changes.MoveNextAsync().AsTask();
        }

        throw new InvalidOperationException("조건에 맞는 변경이 오지 않고 스트림이 끝났다.");
    }

    /// <summary>
    /// <c>Error</c> 를 직접 올릴 수 있는 <see cref="FileSystemWatcher"/>.
    /// <c>OnError</c> 가 protected 라 파생 클래스만 할 수 있다.
    /// </summary>
    private sealed class OverflowRaisingWatcher(string path) : FileSystemWatcher(path)
    {
        public void RaiseOverflow() => OnError(new ErrorEventArgs(new InternalBufferOverflowException()));
    }
}
