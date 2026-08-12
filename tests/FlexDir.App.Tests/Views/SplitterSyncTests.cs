using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// GridSplitter 와 <c>SplitterRatio</c> 를 잇는 attached behavior (phase B-2).
/// <para>
/// GridSplitter 는 열 정의를 직접 바꾸므로 ViewModel 이 모른다. 드래그가 끝나면 실측
/// 너비를 비율로 접어 VM 으로 보내고, VM 의 비율(복원·클램프)이 바뀌면 열 너비로 편다 —
/// 비율 계산과 너비 변환만 여기서 채점하고, 이벤트 훅과 실제 드래그는 사람이 확인한다
/// (CLAUDE.md §5).
/// </para>
/// </summary>
public class SplitterSyncTests
{
    // ── 실측 너비 → 비율 판정 ─────────────────────────────────────

    [Theory]
    [InlineData(700, 300, 0.7)]
    [InlineData(320, 320, 0.5)]
    [InlineData(1, 3, 0.25)]
    public void RatioOf_FoldsTheMeasuredWidthsIntoARatio(double left, double right, double expected)
    {
        Assert.Equal(expected, SplitterSync.RatioOf(left, right), precision: 10);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 300)]
    [InlineData(double.PositiveInfinity, 300)]
    public void RatioOf_WhenTheWidthsAreNotUsable_FallsBackToHalf(double left, double right)
    {
        // 레이아웃 전(0)이나 깨진 값에서 비율을 만들면 그 값이 VM 을 거쳐 저장 파일까지 간다.
        Assert.Equal(0.5, SplitterSync.RatioOf(left, right));
    }

    // ── 어떤 드래그가 스플리터의 것인가 (2026-08-07 재현) ─────────
    //
    // Thumb.DragCompleted 는 버블링이라 페인 안의 스크롤바 썸이 올린 것도 Grid 의 핸들러에
    // 닿는다 (ScrollBar 는 그 이벤트를 삼키지 않는다 — 실물로 확인). 그것을 스플리터 드래그로
    // 읽으면 열의 MinWidth 에 잘린 폭이 사용자가 고른 비율을 덮어쓴다. 좁은 창에서 목록을
    // 한 번 스크롤하는 것으로 충분하고, 그 값은 저장 파일까지 간다.

    [Fact]
    public void DragCompleted_FromAnotherThumb_LeavesTheRatioAlone()
    {
        OnSta(() =>
        {
            var grid = MeasuredGrid();
            SplitterSync.SetRatio(grid, 0.3);
            SplitterSync.SetEnabled(grid, true);

            var scrollThumb = new Thumb();
            grid.Children.Add(scrollThumb);

            RaiseDragCompleted(scrollThumb);

            // 실측 폭은 반반이지만 (MeasuredGrid) 사용자가 고른 것은 0.3 이다.
            Assert.Equal(0.3, SplitterSync.GetRatio(grid));
        });
    }

    [Fact]
    public void DragCompleted_FromTheColumnSplitter_FoldsTheMeasuredWidthsIn()
    {
        OnSta(() =>
        {
            var grid = MeasuredGrid();
            SplitterSync.SetRatio(grid, 0.3);
            SplitterSync.SetEnabled(grid, true);

            RaiseDragCompleted(Splitter(grid, GridResizeDirection.Columns));

            Assert.Equal(0.5, SplitterSync.GetRatio(grid), precision: 2);
        });
    }

    // ── 가로 스플리터 (docs/PRD-v2.md §18) ────────────────────────
    // 3·4분할이 들어오며 축이 둘이 됐다. 어느 축인지는 GridSplitter 가 스스로 말한다
    // (ResizeDirection) — 한 핸들러가 둘을 받으므로 그것을 안 보면 세로로 끈 것이 열 비율을
    // 덮어쓴다.

    [Fact]
    public void DragCompleted_FromTheRowSplitter_FoldsTheMeasuredHeightsIn()
    {
        OnSta(() =>
        {
            var grid = MeasuredGrid();
            SplitterSync.SetRowRatio(grid, 0.3);
            SplitterSync.SetEnabled(grid, true);

            RaiseDragCompleted(Splitter(grid, GridResizeDirection.Rows));

            Assert.Equal(0.5, SplitterSync.GetRowRatio(grid), precision: 2);
        });
    }

    [Fact]
    public void DragCompleted_FromTheRowSplitter_LeavesTheColumnRatioAlone()
    {
        OnSta(() =>
        {
            var grid = MeasuredGrid();
            SplitterSync.SetRatio(grid, 0.3);
            SplitterSync.SetEnabled(grid, true);

            RaiseDragCompleted(Splitter(grid, GridResizeDirection.Rows));

            Assert.Equal(0.3, SplitterSync.GetRatio(grid));
        });
    }

    [Fact]
    public void DragCompleted_FromTheColumnSplitter_LeavesTheRowRatioAlone()
    {
        OnSta(() =>
        {
            var grid = MeasuredGrid();
            SplitterSync.SetRowRatio(grid, 0.3);
            SplitterSync.SetEnabled(grid, true);

            RaiseDragCompleted(Splitter(grid, GridResizeDirection.Columns));

            Assert.Equal(0.3, SplitterSync.GetRowRatio(grid));
        });
    }

    /// <summary>
    /// 반반으로 <b>실측된</b> 페인 그리드. 열 셋·행 셋(각각 <c>*</c>·스플리터 6·<c>*</c>)은
    /// MainWindow.xaml 과 같다 — 실측 폭이 있어야 되쓰기가 무엇을 쓰는지 보인다.
    /// </summary>
    private static Grid MeasuredGrid()
    {
        var grid = new Grid();

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Measure(new Size(1000, 1000));
        grid.Arrange(new Rect(0, 0, 1000, 1000));

        return grid;
    }

    /// <summary>격자에 스플리터 하나를 끼운다. 축은 <c>ResizeDirection</c> 이 말한다.</summary>
    private static GridSplitter Splitter(Grid grid, GridResizeDirection direction)
    {
        var splitter = new GridSplitter { ResizeDirection = direction };

        if (direction == GridResizeDirection.Columns)
        {
            Grid.SetColumn(splitter, 1);
        }
        else
        {
            Grid.SetRow(splitter, 1);
        }

        grid.Children.Add(splitter);

        return splitter;
    }

    private static void RaiseDragCompleted(UIElement source)
        => source.RaiseEvent(
            new DragCompletedEventArgs(0, 0, canceled: false) { RoutedEvent = Thumb.DragCompletedEvent });

    /// <summary>WPF 요소는 STA 에서만 만들어진다 — xunit 은 스레드풀(MTA)에서 돈다.</summary>
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

    // ── 비율 → 열 너비 변환 ───────────────────────────────────────

    [Fact]
    public void WidthsFor_SplitsTheStarsByTheRatio()
    {
        var (left, right) = SplitterSync.WidthsFor(0.3);

        Assert.Equal(new GridLength(0.3, GridUnitType.Star), left);
        Assert.Equal(new GridLength(0.7, GridUnitType.Star), right);
    }

    [Fact]
    public void RowRatio_RoundTripsAndDefaultsToHalf()
    {
        var element = new DependencyObject();

        Assert.Equal(0.5, SplitterSync.GetRowRatio(element));

        SplitterSync.SetRowRatio(element, 0.3);

        Assert.Equal(0.3, SplitterSync.GetRowRatio(element));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Ratio_RoundTrips()
    {
        var element = new DependencyObject();

        SplitterSync.SetRatio(element, 0.3);

        Assert.Equal(0.3, SplitterSync.GetRatio(element));
    }

    [Fact]
    public void Ratio_DefaultsToHalf()
    {
        Assert.Equal(0.5, SplitterSync.GetRatio(new DependencyObject()));
    }

    [Fact]
    public void Enabled_RoundTripsAndDefaultsToOff()
    {
        var element = new DependencyObject();

        Assert.False(SplitterSync.GetEnabled(element));

        SplitterSync.SetEnabled(element, true);

        Assert.True(SplitterSync.GetEnabled(element));
    }
}
