using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Operations;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// <c>FlexDir.Shell</c> 의 구현체는 같은 계약을 <b>수동</b>으로 검증받는다 (ADR-009).
/// <para>
/// 여기서 못 박는 것은 fake 가 <b>믿을 만한 기록계</b>라는 것이다. ViewModel 테스트가
/// "복사가 아니라 이동이 불렸다" 를 단정하는 근거가 이 목록들이므로, 세는 쪽이 먼저
/// 믿을 수 있어야 한다.
/// </para>
/// <para>
/// 휴지통 강제·이름 충돌 대화상자·진행 표시는 shell 에 위임하는 규칙이라 셸 커맨드로
/// 판정할 수 없다 (docs/SHELL_NOTES.md §파일 조작). 그 규칙은 인터페이스 XML 주석이
/// 지고 있고, 수동 Shell phase 가 확인한다.
/// </para>
/// </summary>
public class FakeFileOperationsTests
{
    [Fact]
    public async Task CopyAsync_RecordsTheSourcesAndTheDestination()
    {
        var operations = new FakeFileOperations();
        var sources = new[] { Loc(@"C:\Temp\a.txt"), Loc(@"C:\Temp\b.txt") };
        var destination = Loc(@"C:\Backup");

        await operations.CopyAsync(sources, destination, CancellationToken.None);

        var call = Assert.Single(operations.Copies);
        Assert.Equal(sources, call.Sources);
        Assert.Equal(destination, call.Destination);
        Assert.Empty(operations.Moves);
    }

    [Fact]
    public async Task MoveAsync_IsRecordedSeparatelyFromCopy()
    {
        // 복사와 이동을 한 목록에 섞어 담으면 "이동이 불렸는가" 를 필터로 물어야 한다.
        var operations = new FakeFileOperations();
        var sources = new[] { Loc(@"C:\Temp\a.txt") };
        var destination = Loc(@"C:\Backup");

        await operations.MoveAsync(sources, destination, CancellationToken.None);

        var call = Assert.Single(operations.Moves);
        Assert.Equal(sources, call.Sources);
        Assert.Equal(destination, call.Destination);
        Assert.Empty(operations.Copies);
    }

    [Fact]
    public async Task DeleteAsync_RecordsTheItems()
    {
        var operations = new FakeFileOperations();
        var items = new[] { Loc(@"C:\Temp\a.txt"), Loc(@"C:\Temp\Docs") };

        await operations.DeleteAsync(items, CancellationToken.None);

        Assert.Equal(items, Assert.Single(operations.Deletes));
    }

    [Fact]
    public async Task RenameAsync_RecordsTheItemAndTheNewName()
    {
        var operations = new FakeFileOperations();
        var item = Loc(@"C:\Temp\a.txt");

        await operations.RenameAsync(item, "b.txt", CancellationToken.None);

        Assert.Equal((item, "b.txt"), Assert.Single(operations.Renames));
    }

    [Fact]
    public async Task CreateFolderAsync_ReturnsTheLocationUnderTheParent()
    {
        var operations = new FakeFileOperations();
        var parent = Loc(@"C:\Temp");

        var created = await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None);

        Assert.Equal((parent, "새 폴더"), Assert.Single(operations.CreatedFolders));
        Assert.Equal(Loc(@"C:\Temp\새 폴더"), created);
        Assert.Equal("새 폴더", created.Name);
    }

    [Fact]
    public async Task CreateFolderAsync_CanReportADifferentNameThanRequested()
    {
        // 이름이 겹치면 구현체가 유일한 이름을 만든다 — 호출자는 요청한 이름이 아니라
        // 만들어진 위치를 봐야 한다.
        var operations = new FakeFileOperations { CreatedFolderName = "새 폴더 (2)" };
        var parent = Loc(@"C:\Temp");

        var created = await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None);

        Assert.Equal("새 폴더 (2)", created.Name);
        Assert.Equal((parent, "새 폴더"), Assert.Single(operations.CreatedFolders));
    }

    [Fact]
    public async Task Failure_IsThrownByEveryOperationAndTheCallIsStillRecorded()
    {
        // "불렸는데 실패했다" 와 "아예 안 불렸다" 는 다른 사건이다.
        var item = Loc(@"C:\Temp\a.txt");
        var operations = new FakeFileOperations
        {
            Failure = new LocationAccessException(LocationErrorKind.AccessDenied, item),
        };

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            () => operations.RenameAsync(item, "b.txt", CancellationToken.None));

        Assert.Equal(LocationErrorKind.AccessDenied, error.Kind);
        Assert.Single(operations.Renames);

        await Assert.ThrowsAsync<LocationAccessException>(
            () => operations.DeleteAsync([item], CancellationToken.None));
        await Assert.ThrowsAsync<LocationAccessException>(
            () => operations.CreateFolderAsync(Loc(@"C:\Temp"), "새 폴더", CancellationToken.None));
    }

    [Fact]
    public async Task Operations_ObserveCancellation()
    {
        var operations = new FakeFileOperations();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.DeleteAsync([Loc(@"C:\Temp\a.txt")], cts.Token));

        Assert.Equal(1, operations.CancellationsObserved);
    }

    [Fact]
    public async Task Gate_HoldsTheOperationUntilItIsReleased()
    {
        // 조작이 진행 중인 상태를 만드는 knob 이다 — 반대편 페인이 그 사이에 움직이는지 본다.
        var released = new TaskCompletionSource();
        var operations = new FakeFileOperations { Gate = released.Task };

        var pending = operations.DeleteAsync([Loc(@"C:\Temp\a.txt")], CancellationToken.None);

        Assert.False(pending.IsCompleted);

        released.SetResult();
        await pending;
    }

    [Fact]
    public async Task Operations_AreUsableThroughThePortAlone()
    {
        // 호출부는 fake 를 모른다. 포트만으로 쓸 수 있어야 한다.
        IFileOperations port = new FakeFileOperations();

        await port.CopyAsync([Loc(@"C:\Temp\a.txt")], Loc(@"C:\Backup"), CancellationToken.None);
        var created = await port.CreateFolderAsync(Loc(@"C:\Temp"), "새 폴더", CancellationToken.None);

        Assert.Equal(Loc(@"C:\Temp\새 폴더"), created);
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
