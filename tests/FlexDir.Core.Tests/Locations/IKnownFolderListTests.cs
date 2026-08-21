using FlexDir.Core.Locations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Locations;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>KnownFolderList</c>)는
/// <c>FlexDir.Shell</c> 의 몫이다 — <see cref="Storage.IDriveList"/> 와 같은 구도다.
/// <para>
/// 포트에는 동작이 없으므로 채점하는 것은 <b>레코드와 열거형의 계약</b>이다 —
/// 특히 <see cref="KnownFolderKind"/> 의 순서는 곧 메뉴에 뜨는 순서라서
/// 뒤바뀌면 화면이 바뀐다.
/// </para>
/// </summary>
public class FakeKnownFolderListTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public void KnownFolderKind_IsExactlyFive_InMenuOrder()
    {
        // 이 순서가 메뉴 순서다. 값을 더하거나 순서를 바꾸는 것은 화면을 바꾸는 일이다.
        Assert.Equal(
            [
                KnownFolderKind.Home,
                KnownFolderKind.Desktop,
                KnownFolderKind.Documents,
                KnownFolderKind.Downloads,
                KnownFolderKind.Pictures,
            ],
            Enum.GetValues<KnownFolderKind>());
    }

    [Fact]
    public void KnownFolder_HasValueEquality()
    {
        var a = new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", Path(@"C:\Users\me\Desktop"));
        var b = new KnownFolder(KnownFolderKind.Desktop, "바탕 화면", Path(@"C:\Users\me\Desktop"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void KnownFolder_WithoutLocation_MeansTheFolderIsMissing()
    {
        // 없는 폴더는 목록에서 빠지는 게 아니라 Location = null 로 온다 —
        // 거르는 것은 ViewModel 의 일이다 (step 1).
        var missing = new KnownFolder(KnownFolderKind.Pictures, "사진", null);

        Assert.Null(missing.Location);
        Assert.Equal(KnownFolderKind.Pictures, missing.Kind);
    }

    [Fact]
    public async Task ListAsync_GivesWhatWasPutIn()
    {
        var folders = new FakeKnownFolderList();
        folders.Folders.Add(new KnownFolder(KnownFolderKind.Home, "me", Path(@"C:\Users\me")));

        var listed = await folders.ListAsync(CancellationToken.None);

        Assert.Equal([new KnownFolder(KnownFolderKind.Home, "me", Path(@"C:\Users\me"))], listed);
    }

    [Fact]
    public async Task ListAsync_WithNoFolders_IsEmptyNotNull()
    {
        var listed = await new FakeKnownFolderList().ListAsync(CancellationToken.None);

        Assert.Empty(listed);
    }

    [Fact]
    public async Task ListAsync_CountsHowManyTimesItWasAsked()
    {
        // step 1 이 "탭마다 다시 묻지 않는가" 를 이 카운터로 채점한다.
        var folders = new FakeKnownFolderList();

        await folders.ListAsync(CancellationToken.None);
        await folders.ListAsync(CancellationToken.None);

        Assert.Equal(2, folders.Asked);
    }

    [Fact]
    public async Task ListAsync_Cancelled_Throws()
    {
        // 실패는 던지지 않지만 취소는 예외로 나온다 — IDriveList 와 같은 계약이다.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeKnownFolderList().ListAsync(cts.Token));
    }
}
