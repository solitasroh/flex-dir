using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 탭 줄의 배치와 가로 스크롤 (docs/DESIGN.md §1-1).
/// <para>
/// 재는 것은 순수 함수 둘이다. <see cref="TabStripPanel.Widths"/> 는 "몇 px 씩 나눠 갖나",
/// <see cref="TabStripPanel.Reveal"/> 는 "활성 탭을 보이게 하려면 얼마나 밀어야 하나".
/// 실제 배치는 그 둘을 부르는 것뿐이라 <c>OnSta</c> 로 한 번만 확인한다 — 픽셀의 판정은
/// 사람이 한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TabStripPanelTests
{
    // ── 폭 나누기 (docs/DESIGN.md §1-1: 최대 180 · 최소 90 · 고정 28 · 간격 2) ──

    [Fact]
    public void Widths_WithRoomToSpare_StopsAtTheMaximum()
    {
        // 넓다고 끝없이 늘어나면 탭 둘이 페인을 반씩 먹는다. 브라우저도 최대가 있다.
        Assert.Equal([180, 180], TabStripPanel.Widths([false, false], 1000));
    }

    [Fact]
    public void Widths_WhenTight_SharesTheRoomEvenly()
    {
        // 간격 2 가 둘(탭 셋 사이) 이므로 나눌 자리는 296 이다.
        var widths = TabStripPanel.Widths([false, false, false], 300);

        Assert.All(widths, width => Assert.Equal(296d / 3, width, 3));
    }

    [Fact]
    public void Widths_WhenTooTight_StopsAtTheMinimumAndLetsItOverflow()
    {
        // 최소 90 아래로는 줄이지 않는다 — 제목에 남는 자리가 36 이고 그 아래는 읽히지
        // 않는다. 넘치는 것은 가로 스크롤이 받는다 (Reveal).
        var widths = TabStripPanel.Widths([false, false, false], 200);

        Assert.All(widths, width => Assert.Equal(90, width));
    }

    [Fact]
    public void Widths_PinnedTabs_TakeTwentyEightWhateverTheRoom()
    {
        Assert.Equal([28, 180, 180], TabStripPanel.Widths([true, false, false], 1000));
        Assert.Equal([28, 28, 28], TabStripPanel.Widths([true, true, true], 1000));
        Assert.Equal([28, 28, 28], TabStripPanel.Widths([true, true, true], 60));
    }

    [Fact]
    public void Widths_PinnedTabs_EatTheirRoomBeforeTheRestIsShared()
    {
        // 고정이 먼저 28 을 가져가고 남은 것을 비고정이 나눈다 — 300 - 간격 4 - 고정 28 = 268.
        var widths = TabStripPanel.Widths([true, false, false], 300);

        Assert.Equal(28, widths[0]);
        Assert.Equal(134, widths[1], 3);
        Assert.Equal(134, widths[2], 3);
    }

    [Fact]
    public void Widths_WithNoTabs_IsEmpty()
    {
        Assert.Empty(TabStripPanel.Widths([], 300));
    }

    [Fact]
    public void Widths_WithoutAConstraint_UsesTheMaximum()
    {
        // 측정이 무한으로 오는 자리에 넣으면 NaN 폭이 배치를 터뜨린다. 제약이 없다는 것은
        // "얼마든지 써도 된다" 이므로 답은 최대다.
        Assert.Equal([180, 180], TabStripPanel.Widths([false, false], double.PositiveInfinity));
    }

    // ── 활성 탭을 보이는 자리로 (docs/DESIGN.md §1-1) ──────────────

    [Fact]
    public void Reveal_WhenTheTabIsAlreadyVisible_DoesNotMove()
    {
        // 갱신마다 끌어당기면 사용자가 휠로 옮겨 놓은 화면이 제자리로 돌아간다
        // (FocusScroll 이 같은 이유로 못 찾은 이름만 다시 본다).
        Assert.Equal(50, TabStripPanel.Reveal(50, start: 60, width: 100, viewport: 300));
    }

    [Fact]
    public void Reveal_WhenTheTabIsOffToTheLeft_PullsItToTheLeftEdge()
    {
        Assert.Equal(20, TabStripPanel.Reveal(100, start: 20, width: 90, viewport: 300));
    }

    [Fact]
    public void Reveal_WhenTheTabIsOffToTheRight_PullsItToTheRightEdge()
    {
        // 오른쪽 끝에 딱 붙인다 — 더 밀면 그 오른쪽의 빈 자리가 보인다.
        Assert.Equal(190, TabStripPanel.Reveal(0, start: 400, width: 90, viewport: 300));
    }

    [Fact]
    public void Reveal_ATabWiderThanTheViewport_ShowsItsLeftEdge()
    {
        // 페인 최소 320 에서는 일어나지 않지만, 일어나면 제목이 보이는 쪽이 왼쪽이다.
        Assert.Equal(400, TabStripPanel.Reveal(0, start: 400, width: 500, viewport: 300));
    }

    // ── 실제 배치 ─────────────────────────────────────────────────

    [Fact]
    public void Arrange_PlacesTabsLeftToRightWithTheGapBetweenThem()
    {
        OnSta(() =>
        {
            var panel = new TabStripPanel();
            var first = new Border();
            var second = new Border();

            panel.Children.Add(first);
            panel.Children.Add(second);

            panel.Measure(new Size(300, 28));
            panel.Arrange(new Rect(0, 0, 300, 28));

            // 300 - 간격 2 = 298 을 둘이 나눈다.
            Assert.Equal(149, first.ActualWidth, 3);
            Assert.Equal(24, first.ActualHeight, 3);
            Assert.Equal(151, second.TranslatePoint(default, panel).X, 3);
        });
    }

    [Fact]
    public void Arrange_KeepsTheActiveTabInsideTheViewport()
    {
        OnSta(() =>
        {
            var panel = new TabStripPanel();

            // 넷을 90 까지 줄여도 366 이라 300 을 넘는다.
            for (var index = 0; index < 4; index++)
            {
                panel.Children.Add(new Border { DataContext = index });
            }

            panel.ActiveTab = 3;

            panel.Measure(new Size(300, 28));
            panel.Arrange(new Rect(0, 0, 300, 28));

            var last = (Border)panel.Children[3];
            var right = last.TranslatePoint(new Point(last.ActualWidth, 0), panel).X;

            Assert.True(right <= 300.5, $"마지막 탭의 오른쪽 끝이 화면 밖이다: {right}");
        });
    }

    /// <summary>
    /// WPF 요소는 STA 에서만 만들어지고 xunit 은 스레드풀(MTA)에서 돈다
    /// (<c>ListInputTests</c> 와 같은 수).
    /// </summary>
    private static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }
        })
        {
            IsBackground = true,
            Name = "flex-dir test sta",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA 스레드가 끝나지 않았다.");

        failure?.Throw();
    }
}
