using FlexDir.Core.Favorites;
using FlexDir.Core.Locations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Favorites;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 실제 구현체(<c>JsonFavoriteStore</c>)는
/// <c>FlexDir.Shell</c> 의 몫이다.
/// <para>
/// <b>뷰 상태와 다른 저장소인 이유</b>: 즐겨찾기는 캐시가 아니라 <b>사용자가 만든
/// 데이터</b>다. <c>IViewStateStore</c> 는 읽기 실패를 조용히 기본값으로 접는데
/// (CLAUDE.md §4 — 어긋나면 파일시스템을 믿는다), 즐겨찾기에 그렇게 하면 사용자가
/// 모아 둔 목록이 조용히 사라진다.
/// </para>
/// </summary>
public class FakeFavoriteStoreTests
{
    private static LocationId Path(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task LoadAsync_BeforeAnythingIsSaved_IsEmpty()
    {
        // 처음 켠 사람에게는 즐겨찾기가 없다. 오류가 아니다.
        Assert.Empty(await new FakeFavoriteStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SavedFavorites_AreLoadedBackInOrder()
    {
        // 순서가 곧 사용자가 정한 순서다 (§10-2 순서 바꾸기). 집합으로 다루면 그것을 잃는다.
        var store = new FakeFavoriteStore();
        Favorite[] favorites =
        [
            new(Path(@"C:\work"), "작업"),
            new(Path(@"\\10.10.10.23\home"), "NAS"),
        ];

        await store.SaveAsync(favorites, CancellationToken.None);

        Assert.Equal(favorites, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ReplacesTheWholeList()
    {
        // 포트는 목록 전체를 받는다 — 추가·제거·이름·순서를 각각 API 로 두면 같은 규칙이
        // 저장소와 ViewModel 두 곳에 생긴다.
        var store = new FakeFavoriteStore();

        await store.SaveAsync([new Favorite(Path(@"C:\a"), "A")], CancellationToken.None);
        await store.SaveAsync([new Favorite(Path(@"C:\b"), "B")], CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(["B"], loaded.Select(favorite => favorite.Label));
    }

    [Fact]
    public async Task SaveAsync_CopiesTheList()
    {
        // 호출자가 나중에 자기 목록을 바꿔도 저장된 것이 흔들리면 안 된다.
        var store = new FakeFavoriteStore();
        var mutable = new List<Favorite> { new(Path(@"C:\a"), "A") };

        await store.SaveAsync(mutable, CancellationToken.None);
        mutable.Clear();

        Assert.Single(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeFavoriteStore().LoadAsync(cts.Token));
    }
}
