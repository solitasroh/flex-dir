using FlexDir.App.Navigation;

using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.App.Tests.Navigation;

public class PaneHistoryTests
{
    // ── 초기 상태 ─────────────────────────────────────────────────

    [Fact]
    public void New_HasNothingToShowOrGoTo()
    {
        var history = new PaneHistory();

        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void New_GoBackAndGoForward_ReturnNullWithoutThrowing()
    {
        var history = new PaneHistory();

        Assert.Null(history.GoBack());
        Assert.Null(history.GoForward());
        Assert.Null(history.Current);
    }

    // ── 뒤로 / 앞으로 ─────────────────────────────────────────────

    [Fact]
    public void Navigate_FirstLocation_BecomesCurrentWithNowhereToGo()
    {
        var history = new PaneHistory();

        history.Navigate(Loc(@"C:\a"));

        Assert.Equal(Loc(@"C:\a"), history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void GoBack_AfterTwoNavigations_ReturnsPreviousAndEnablesForward()
    {
        var history = History(@"C:\a", @"C:\b");

        var moved = history.GoBack();

        Assert.Equal(Loc(@"C:\a"), moved);
        Assert.Equal(Loc(@"C:\a"), history.Current);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void GoForward_AfterGoBack_ReturnsToNewerLocation()
    {
        var history = History(@"C:\a", @"C:\b");
        history.GoBack();

        var moved = history.GoForward();

        Assert.Equal(Loc(@"C:\b"), moved);
        Assert.Equal(Loc(@"C:\b"), history.Current);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    // ── 규칙 1: Navigate 는 앞으로 기록을 버린다 ──────────────────

    [Fact]
    public void Navigate_AfterGoBack_DiscardsForwardHistory()
    {
        var history = History(@"C:\a", @"C:\b");
        history.GoBack();

        history.Navigate(Loc(@"C:\c"));

        Assert.Equal(Loc(@"C:\c"), history.Current);
        Assert.False(history.CanGoForward);
        Assert.Equal(Loc(@"C:\a"), history.GoBack());
    }

    // ── 규칙 2: 현재와 같은 곳으로 Navigate 하면 아무 일도 없다 ──

    [Fact]
    public void Navigate_SameLocationTwice_DoesNotStackDuplicate()
    {
        var history = History(@"C:\a", @"C:\b", @"C:\b");

        Assert.Equal(Loc(@"C:\a"), history.GoBack());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Navigate_ToCurrentAfterGoBack_KeepsForwardHistory()
    {
        var history = History(@"C:\a", @"C:\b");
        history.GoBack();

        history.Navigate(Loc(@"C:\a"));

        Assert.Equal(Loc(@"C:\a"), history.Current);
        Assert.True(history.CanGoForward);
        Assert.Equal(Loc(@"C:\b"), history.GoForward());
    }

    // 규칙 2: 비교는 LocationId 동등성(OrdinalIgnoreCase)을 쓴다.
    [Fact]
    public void Navigate_SameLocationDifferingOnlyInCase_IsTreatedAsSame()
    {
        var history = History(@"C:\a", @"C:\Temp");
        history.GoBack();

        history.Navigate(Loc(@"c:\a"));

        Assert.True(history.CanGoForward);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Navigate_CurrentDifferingOnlyInCase_DoesNotStackDuplicate()
    {
        var history = History(@"C:\a", @"C:\Temp", @"c:\temp");

        Assert.Equal(Loc(@"C:\a"), history.GoBack());
        Assert.False(history.CanGoBack);
    }

    // ── 규칙 3: MaxEntries 를 넘으면 가장 오래된 것부터 버린다 ────

    [Fact]
    public void Navigate_BeyondMaxEntries_DropsOldestSoBackStopsEarlier()
    {
        var history = new PaneHistory();
        const int overflow = 10;

        for (var i = 0; i < PaneHistory.MaxEntries + overflow; i++)
        {
            history.Navigate(Loc($@"C:\f{i}"));
        }

        var steps = 0;
        while (history.GoBack() is not null)
        {
            steps++;
            Assert.True(steps <= PaneHistory.MaxEntries, "뒤로가 MaxEntries 를 넘어 이어졌다.");
        }

        // 100개만 남았으므로 뒤로는 99번만 가능하고, 가장 오래된 것은 f10 이다.
        Assert.Equal(PaneHistory.MaxEntries - 1, steps);
        Assert.Equal(Loc($@"C:\f{overflow}"), history.Current);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Navigate_BeyondMaxEntries_KeepsForwardIntactToNewest()
    {
        var history = new PaneHistory();
        const int overflow = 10;
        const int last = PaneHistory.MaxEntries + overflow - 1;

        for (var i = 0; i <= last; i++)
        {
            history.Navigate(Loc($@"C:\f{i}"));
        }

        while (history.GoBack() is not null)
        {
        }

        while (history.GoForward() is not null)
        {
        }

        Assert.Equal(Loc($@"C:\f{last}"), history.Current);
        Assert.False(history.CanGoForward);
    }

    // ── 규칙 4: 갈 수 없으면 예외가 아니라 null ──────────────────

    [Fact]
    public void GoBack_AtOldestEntry_ReturnsNullAndKeepsCurrent()
    {
        var history = History(@"C:\a");

        Assert.Null(history.GoBack());
        Assert.Equal(Loc(@"C:\a"), history.Current);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void GoForward_AtNewestEntry_ReturnsNullAndKeepsCurrent()
    {
        var history = History(@"C:\a", @"C:\b");

        Assert.Null(history.GoForward());
        Assert.Equal(Loc(@"C:\b"), history.Current);
        Assert.False(history.CanGoForward);
    }

    // ── 규칙 5: "상위로" 는 부모로 Navigate 하는 것이며 기록에 남는다 ──

    [Fact]
    public void Navigate_ToParent_IsRecordedLikeAnyOtherMove()
    {
        var history = History(@"C:\a\b");
        Assert.True(Loc(@"C:\a\b").TryGetParent(out var parent));

        history.Navigate(parent);

        Assert.Equal(Loc(@"C:\a"), history.Current);
        Assert.True(history.CanGoBack);
        Assert.Equal(Loc(@"C:\a\b"), history.GoBack());
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private static PaneHistory History(params string[] paths)
    {
        var history = new PaneHistory();
        foreach (var path in paths)
        {
            history.Navigate(Loc(path));
        }

        return history;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
