using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// 폴더를 빠르게 넘겨도 이전 폴더의 결과가 새 폴더에 섞이지 않는지 본다
/// (docs/PRD.md §4 열거 중 폴더 이탈 — 전작에서 잔버그가 살던 자리다).
/// <para>
/// 경합은 <see cref="FakeFolderSource.YieldDelayMilliseconds"/> 로 만들되, "이전 열거가
/// 아직 진행 중" 이라는 지점은 시간이 아니라 <b>소비 시점</b>으로 잡는다 — 열거자를 직접
/// 돌려 첫 배치를 받은 뒤에 다음 <c>Start</c> 를 넣는다. 시간에 기대면 실행 속도에 따라
/// 간헐적으로 실패하고, 자율 실행에서 그 원인을 찾는 비용이 가장 크다.
/// </para>
/// </summary>
public class EnumerationSessionTests
{
    // ── 배치 경계 ───────────────────────────────────────────────────
    // 첫 배치 1개 · 이후 256개 · 남은 것은 마지막 배치. 시간 기반 플러시는 없다.

    [Fact]
    public async Task ThreeItems_AreBatchedAsOneThenTwo()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var source = new FakeFolderSource();
        source.Folders[folder] = Items(folder, 3);

        await using var session = new EnumerationSession(source);
        var batches = await CollectAsync(session.Start(folder));

        Assert.Equal([1, 2], batches.Select(batch => batch.Count));

        // 항목을 잃거나 뒤섞지 않는다 — 배치는 스트림을 끊는 것일 뿐이다.
        Assert.Equal(
            ["file0.txt", "file1.txt", "file2.txt"],
            batches.SelectMany(batch => batch).Select(item => item.Name));
    }

    [Fact]
    public async Task ThreeHundredItems_AreBatchedAsOneThen256ThenTheRest()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var source = new FakeFolderSource();
        source.Folders[folder] = Items(folder, 300);

        await using var session = new EnumerationSession(source);
        var batches = await CollectAsync(session.Start(folder));

        Assert.Equal([1, 256, 43], batches.Select(batch => batch.Count));
    }

    [Fact]
    public async Task EmptyFolder_YieldsNoBatch()
    {
        // 빈 배치를 내면 소비자가 "열거가 끝났다" 와 "낼 것이 없다" 를 구분하려고
        // 배치마다 개수를 검사하게 된다.
        var folder = Folder(@"C:\Temp\Empty");
        var source = new FakeFolderSource();
        source.Folders[folder] = [];

        await using var session = new EnumerationSession(source);
        var batches = await CollectAsync(session.Start(folder));

        Assert.Empty(batches);
    }

    // ── 세대 ────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_BumpsGeneration_AndMarksThePreviousRunStale()
    {
        var docs = Folder(@"C:\Temp\Docs");
        var pics = Folder(@"C:\Temp\Pics");
        var source = new FakeFolderSource();
        source.Folders[docs] = Items(docs, 3);
        source.Folders[pics] = Items(pics, 3);

        await using var session = new EnumerationSession(source);

        var first = session.Start(docs);
        Assert.Equal(docs, first.Folder);
        Assert.Equal(session.Generation, first.Generation);
        Assert.False(first.IsStale);

        var second = session.Start(pics);
        Assert.Equal(first.Generation + 1, second.Generation);
        Assert.True(first.IsStale);
        Assert.False(second.IsStale);
    }

    // ── 격리 ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartingAnotherFolder_StopsTheBatchesOfTheRunInFlight()
    {
        var docs = Folder(@"C:\Temp\Docs");
        var pics = Folder(@"C:\Temp\Pics");
        var source = new FakeFolderSource { YieldDelayMilliseconds = 5 };
        source.Folders[docs] = Items(docs, 300);
        source.Folders[pics] = Items(pics, 2);

        await using var session = new EnumerationSession(source);
        var stale = session.Start(docs);

        await using var batches = stale.BatchesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await batches.MoveNextAsync());
        Assert.Single(batches.Current);

        // 첫 배치까지만 받은 상태에서 폴더를 넘긴다. 남은 299개는 이미 떠난 폴더의 것이다.
        session.Start(pics);

        Assert.False(await batches.MoveNextAsync());
    }

    [Fact]
    public async Task StaleRun_EndsQuietly_WithoutAnyBatch()
    {
        // 폴더를 빠르게 넘기는 것은 정상 조작이다. 낡은 run 을 오류로 만들면
        // 상태 표시줄이 유령 오류로 덮인다 — OperationCanceledException 도 예외다.
        var docs = Folder(@"C:\Temp\Docs");
        var pics = Folder(@"C:\Temp\Pics");
        var source = new FakeFolderSource();
        source.Folders[docs] = Items(docs, 300);
        source.Folders[pics] = Items(pics, 2);

        await using var session = new EnumerationSession(source);
        var stale = session.Start(docs);
        session.Start(pics);

        var batches = await CollectAsync(stale);

        Assert.Empty(batches);
    }

    [Fact]
    public async Task Start_CancelsTheEnumerationInFlight()
    {
        var docs = Folder(@"C:\Temp\Docs");
        var pics = Folder(@"C:\Temp\Pics");
        var source = new FakeFolderSource();
        source.Folders[docs] = Items(docs, 300);
        source.Folders[pics] = Items(pics, 2);

        await using var session = new EnumerationSession(source);
        var stale = session.Start(docs);

        await using var batches = stale.BatchesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await batches.MoveNextAsync());

        session.Start(pics);
        Assert.False(await batches.MoveNextAsync());

        // fake 는 항목마다 취소를 관측한다. 0 이면 세션이 이전 열거를 놓아준 것이 아니라
        // 소비만 멈춘 것이다 — 네트워크 경로에서 그 열거는 계속 흘러 UI 를 붙잡는다.
        Assert.True(source.CancellationsObserved >= 1, "이전 열거가 취소되지 않았다.");
    }

    // ── 실패 전파 ───────────────────────────────────────────────────

    [Fact]
    public async Task CurrentRun_PropagatesTheInjectedFailure()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var source = new FakeFolderSource { FailureInjection = (1, LocationErrorKind.AccessDenied) };
        source.Folders[folder] = Items(folder, 5);

        await using var session = new EnumerationSession(source);
        var run = session.Start(folder);

        var error = await Assert.ThrowsAsync<LocationAccessException>(() => CollectAsync(run));

        Assert.Equal(LocationErrorKind.AccessDenied, error.Kind);
    }

    [Fact]
    public async Task StaleRun_SwallowsTheInjectedFailure()
    {
        var docs = Folder(@"C:\Temp\Docs");
        var pics = Folder(@"C:\Temp\Pics");

        // 항목 1개를 낸 직후가 주입 지점이다 — 첫 배치를 받은 뒤 폴더를 넘기면
        // 다음 MoveNext 가 그 실패를 만난다.
        var source = new FakeFolderSource { FailureInjection = (1, LocationErrorKind.AccessDenied) };
        source.Folders[docs] = Items(docs, 5);
        source.Folders[pics] = Items(pics, 2);

        await using var session = new EnumerationSession(source);
        var stale = session.Start(docs);

        await using var batches = stale.BatchesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await batches.MoveNextAsync());

        session.Start(pics);

        // 이미 떠난 폴더의 권한 오류다. 새 폴더의 오류로 표시하면 안 된다.
        Assert.False(await batches.MoveNextAsync());
    }

    // ── 정리 ────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CancelsAndAwaitsTheRunInFlight()
    {
        var folder = Folder(@"C:\Temp\Docs");
        var source = new FakeFolderSource { YieldDelayMilliseconds = 20 };
        source.Folders[folder] = Items(folder, 300);

        var session = new EnumerationSession(source);
        var run = session.Start(folder);

        // RunContinuationsAsynchronously 로 둔다. 기본값이면 아래 await 이 소비자
        // 스레드에서 이어져 DisposeAsync 가 열거를 미는 손을 붙잡는다.
        var firstBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumed = 0;
        var consuming = Task.Run(async () =>
        {
            await foreach (var batch in run.BatchesAsync(CancellationToken.None))
            {
                consumed += batch.Count;
                firstBatch.TrySetResult();
            }
        });

        await firstBatch.Task;

        await session.DisposeAsync();

        // Dispose 가 완료를 기다렸으므로 여기서 멈추지 않는다. 예외도 나오지 않는다.
        await consuming;

        Assert.True(consumed < 300, $"Dispose 가 열거를 끊지 못했다: {consumed}개");
    }

    private static async Task<List<IReadOnlyList<FileItem>>> CollectAsync(EnumerationRun run)
    {
        var batches = new List<IReadOnlyList<FileItem>>();

        await foreach (var batch in run.BatchesAsync(CancellationToken.None))
        {
            batches.Add(batch);
        }

        return batches;
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }

    private static List<FileItem> Items(LocationId folder, int count)
        => [.. Enumerable.Range(0, count).Select(index => Item(folder, $"file{index}.txt"))];

    private static FileItem Item(LocationId folder, string name)
        => new(name, folder.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.None);
}
