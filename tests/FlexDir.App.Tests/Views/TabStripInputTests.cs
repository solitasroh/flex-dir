using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 탭 줄의 마우스 입력을 커맨드로 넘기는 attached behavior (docs/PRD-v2.md §17 마우스).
/// <para>
/// <c>MouseBinding</c> 으로 둘 수 없어서 여기 있다 — 그것은 <c>Freezable</c> 이라
/// <c>DataContext</c> 를 상속받지 않고, 탭의 <c>DataContext</c> 는 탭 자신(<c>PaneViewModel</c>)
/// 인데 커맨드는 그 부모(<c>PaneTabsViewModel</c>)에 있다. <c>ListInput</c> 이 행 템플릿에서
/// 밟은 것과 같은 자리다.
/// </para>
/// <para>
/// 판정(<see cref="TabStripInput.TabAt"/>)만 채점한다. 이벤트 훅과 히트테스트는 사람이
/// 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TabStripInputTests
{
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    // ── 어느 탭 위인가 ────────────────────────────────────────────

    [Fact]
    public void TabAt_OnATab_FindsThatTab()
    {
        OnSta(() =>
        {
            var tab = CreatePane();
            var chrome = new Border { DataContext = tab };
            var strip = new TabStripPanel();
            strip.Children.Add(chrome);

            Assert.Same(tab, TabStripInput.TabAt(strip, chrome));
        });
    }

    [Fact]
    public void TabAt_OnSomethingDrawnInsideTheTab_StillFindsTheTab()
    {
        // 눌리는 것은 제목 글자이거나 아이콘이다. 탭 자체가 눌리는 일은 거의 없다.
        OnSta(() =>
        {
            var tab = CreatePane();
            var title = new TextBlock();
            var chrome = new Border { DataContext = tab, Child = title };
            var strip = new TabStripPanel();
            strip.Children.Add(chrome);

            Assert.Same(tab, TabStripInput.TabAt(strip, title));
        });
    }

    [Fact]
    public void TabAt_OnTheEmptyPartOfTheStrip_IsNothing()
    {
        // 그 자리의 더블클릭이 '새 탭' 이다 (docs/PRD-v2.md §17). 탭 위에서 나면 안 된다.
        OnSta(() =>
        {
            var strip = new TabStripPanel();
            strip.Children.Add(new Border { DataContext = CreatePane() });

            Assert.Null(TabStripInput.TabAt(strip, strip));
        });
    }

    [Fact]
    public void TabAt_WithoutAnOrigin_IsNothing()
    {
        OnSta(() => Assert.Null(TabStripInput.TabAt(new TabStripPanel(), null)));
    }

    private static PaneViewModel CreatePane()
        => new(
            new FakeFolderSource(),
            new FakeFolderWatcher(),
            new FakeTypeNameProvider(),
            new FakeThumbnailSource(),
            new InMemoryViewStateStore(),
            new FakeFileOperations(),
            new FakeClipboardBridge(),
            new FakeItemActivator(),
            new InlineUiDispatcher(),
            Culture,
            TimeZoneInfo.Utc,
            new FakeContextMenuProvider());

    /// <summary>WPF 요소는 STA 에서만 만들어진다 (<c>ListInputTests</c> 와 같은 수).</summary>
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
