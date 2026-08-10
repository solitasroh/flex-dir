using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 트리 열과 <c>FolderTreeViewModel</c> 을 잇는 <see cref="TreeSync"/>.
/// <para>
/// <see cref="SplitterSync"/> 와 같은 구도다 — 판정(순수 함수)만 채점하고 이벤트 훅은
/// 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TreeSyncTests
{
    [Fact]
    public void WidthsFor_WhenShown_GivesPixelWidths()
    {
        // 트리는 픽셀 폭이다 — 페인처럼 비율로 두면 창을 넓힐 때마다 트리가 같이 넓어진다.
        var (tree, splitter) = TreeSync.WidthsFor(220, visible: true);

        Assert.Equal(220, tree.Value);
        Assert.Equal(GridUnitType.Pixel, tree.GridUnitType);
        Assert.True(splitter.Value > 0, "보이는 동안에는 끌 자리가 있어야 한다.");
    }

    [Fact]
    public void WidthsFor_WhenHidden_TakesNoSpaceAtAll()
    {
        // Visibility 만으로는 열이 남는다 — 접었는데 왼쪽에 빈 띠가 남으면 가로 공간을
        // 되찾지 못한 것이고, 그것이 접기를 넣은 이유 전부다.
        var (tree, splitter) = TreeSync.WidthsFor(220, visible: false);

        Assert.Equal(0, tree.Value);
        Assert.Equal(0, splitter.Value);
    }

    [Fact]
    public void WidthsFor_WithAnUnusableWidth_FallsBackToTheDefault()
    {
        // 레이아웃 전의 0 과 NaN 이 여기로 온다. 그대로 펴면 트리가 사라진 채로 뜬다.
        Assert.Equal(TreeSync.DefaultWidth, TreeSync.WidthsFor(double.NaN, visible: true).Tree.Value);
        Assert.Equal(TreeSync.DefaultWidth, TreeSync.WidthsFor(0, visible: true).Tree.Value);
    }
}
