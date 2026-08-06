using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 보이는 항목을 <c>PaneViewModel.SetVisibleRange</c> 로 미는 attached behavior (phase B-3).
/// <para>
/// "무엇이 보이는가" 를 아는 쪽은 View 뿐이다 (.harness/manual-plan.md §B). 실현된 컨테이너의
/// <c>DataContext</c> 를 항목으로 펴는 판정과, 같은 목록을 다시 밀지 않는 판정만 여기서
/// 채점한다 — 시각 트리 순회와 스크롤 체감은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class VisibleRangeSyncTests
{
    // ── 컨테이너의 DataContext → 항목 (ADR-016 의 소스 둘) ────────

    [Fact]
    public void Flatten_TakesDetailsRowsAsThemselves()
    {
        var items = Items(3);

        Assert.Equal(items, VisibleRangeSync.Flatten([items[0], items[1], items[2]]));
    }

    [Fact]
    public void Flatten_UnwrapsCompositeRowsInDisplayOrder()
    {
        // wrap 뷰 3종의 컨테이너는 행이다 — 그 안의 항목이 실제로 보이는 것이다.
        var items = Items(5);

        var visible = VisibleRangeSync.Flatten(
        [
            new RowViewModel([items[0], items[1], items[2]], 3),
            new RowViewModel([items[3], items[4]], 3),
        ]);

        Assert.Equal(items, visible);
    }

    [Fact]
    public void Flatten_SkipsWhatIsNotARow()
    {
        // 재활용 중인 빈 컨테이너와 바인딩이 붙기 전의 자리가 섞여 들어온다.
        var items = Items(1);

        Assert.Equal(items, VisibleRangeSync.Flatten([null, "머리글", items[0]]));
    }

    // ── 같은 목록은 다시 밀지 않는다 ─────────────────────────────

    [Fact]
    public void Unchanged_IsTrueForTheSameInstancesInTheSameOrder()
    {
        // 밀 때마다 스케줄러가 진행 중 요청을 전부 끊고 새 세대를 연다
        // (ThumbnailRequestScheduler.SetVisibleRange) — 레이아웃마다 밀면 썸네일이 영영
        // 완성되지 않는다.
        var items = Items(3);

        Assert.True(VisibleRangeSync.Unchanged([items[0], items[1]], [items[0], items[1]]));
    }

    [Fact]
    public void Unchanged_IsFalseWhenTheOrderChanges()
    {
        var items = Items(2);

        Assert.False(VisibleRangeSync.Unchanged([items[0], items[1]], [items[1], items[0]]));
    }

    [Fact]
    public void Unchanged_IsFalseWhenTheCountChanges()
    {
        var items = Items(2);

        Assert.False(VisibleRangeSync.Unchanged([items[0]], [items[0], items[1]]));
    }

    [Fact]
    public void Unchanged_ComparesByInstanceNotByName()
    {
        // 감시 갱신은 같은 이름의 행 인스턴스를 갈아끼운다 — 새 인스턴스는 그림이 없다.
        var first = Items(1);
        var second = Items(1);

        Assert.Equal(first[0].Name, second[0].Name);
        Assert.False(VisibleRangeSync.Unchanged(first, second));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Enabled_RoundTripsAndDefaultsToOff()
    {
        var element = new System.Windows.DependencyObject();

        Assert.False(VisibleRangeSync.GetEnabled(element));

        VisibleRangeSync.SetEnabled(element, true);

        Assert.True(VisibleRangeSync.GetEnabled(element));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private static FileItemViewModel[] Items(int count)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out _));

        return [.. Enumerable.Range(0, count).Select(index => new FileItemViewModel(
            new FileItem(
                $"a{index:00}.txt",
                folder.Combine($"a{index:00}.txt"),
                1024,
                DateTimeOffset.UnixEpoch,
                FileItemFlags.None),
            string.Empty,
            string.Empty,
            string.Empty))];
    }
}
