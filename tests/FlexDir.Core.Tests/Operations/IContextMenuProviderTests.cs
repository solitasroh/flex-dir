using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Operations;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// 실제 구현체(<c>IContextMenu</c> + <c>IContextMenu2/3</c> 메시지 펌핑)는
/// <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009 · docs/SHELL_NOTES.md §컨텍스트 메뉴).
/// <para>
/// <b>포트에 창 핸들이 없는 것이 이 인터페이스의 결정이다</b> (사용자 결정 2026-08-06).
/// <c>FlexDir.Core</c> 는 <c>HWND</c> 를 모른다 — 소유 창은 <c>FlexDir.Host</c> 가 쥐고
/// 구현체에 물려 준다. 대가는 "어느 창 위에" 를 호출자가 못 정한다는 것이고, 창이
/// 하나라는 전제(<c>ResidentWindow</c>)에 기댄다.
/// </para>
/// </summary>
public class FakeContextMenuProviderTests
{
    [Fact]
    public async Task ShowAsync_RecordsTheItemsAndWhereTheMenuGoes()
    {
        var provider = new FakeContextMenuProvider();
        var folder = Loc(@"C:\Temp");
        var item = Loc(@"C:\Temp\a.txt");

        await provider.ShowAsync([item], folder, new ScreenPoint(120, 340), CancellationToken.None);

        var request = Assert.Single(provider.Requests);

        Assert.Equal(item, Assert.Single(request.Items));
        Assert.Equal(folder, request.Folder);
        Assert.Equal(new ScreenPoint(120, 340), request.At);
    }

    [Fact]
    public async Task ShowAsync_WithNoItems_IsTheBackgroundMenu()
    {
        // 항목 메뉴와 배경 메뉴는 shell 에서 다른 API 다 (GetUIObjectOf 대 CreateViewObject).
        // 포트는 그 구분을 "항목이 있는가" 하나로 나른다.
        var provider = new FakeContextMenuProvider();
        var folder = Loc(@"C:\Temp");

        await provider.ShowAsync([], folder, new ScreenPoint(0, 0), CancellationToken.None);

        Assert.Empty(Assert.Single(provider.Requests).Items);
    }

    [Fact]
    public async Task ShowAsync_RecordsEveryCallInOrder()
    {
        var provider = new FakeContextMenuProvider();
        var folder = Loc(@"C:\Temp");

        await provider.ShowAsync([Loc(@"C:\Temp\a.txt")], folder, new ScreenPoint(1, 1), CancellationToken.None);
        await provider.ShowAsync([Loc(@"C:\Temp\b.txt")], folder, new ScreenPoint(2, 2), CancellationToken.None);

        Assert.Equal(["a.txt", "b.txt"], [.. provider.Requests.Select(request => request.Items[0].Name)]);
    }

    [Fact]
    public async Task ShowAsync_TakesACopyOfTheItems()
    {
        // 호출자가 선택 목록을 그대로 넘기고 곧바로 고칠 수 있다.
        var provider = new FakeContextMenuProvider();
        var items = new List<LocationId> { Loc(@"C:\Temp\a.txt") };

        await provider.ShowAsync(items, Loc(@"C:\Temp"), new ScreenPoint(0, 0), CancellationToken.None);

        items.Clear();

        Assert.Single(Assert.Single(provider.Requests).Items);
    }

    [Fact]
    public async Task ShowAsync_WhenCancelled_Throws()
    {
        var provider = new FakeContextMenuProvider();

        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ShowAsync([], Loc(@"C:\Temp"), new ScreenPoint(0, 0), cancelled.Token));
    }

    [Fact]
    public void ScreenPoint_IsAValue()
    {
        // 좌표는 값이다 — 같은 자리를 두 번 물으면 같은 것이어야 기록을 단정할 수 있다.
        Assert.Equal(new ScreenPoint(3, 4), new ScreenPoint(3, 4));
        Assert.NotEqual(new ScreenPoint(3, 4), new ScreenPoint(4, 3));
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _));

        return location;
    }
}
