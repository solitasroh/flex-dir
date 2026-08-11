using System.Runtime.ExceptionServices;
using System.Windows.Controls;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 탭을 전환하면 키보드 포커스가 그 탭의 목록으로 간다 (docs/PRD-v2.md §17 사용자 렌즈 ·
/// docs/DESIGN.md §1-1).
/// <para>
/// 탭이 바뀌면 <c>ContentControl</c> 이 무는 것이 바뀌어 페인 트리가 통째로 다시 선다 —
/// 그래서 신호는 "새 목록이 붙었다"(<c>Loaded</c>)이고, 판정은 <b>그 페인이 활성인가</b>
/// 하나다. 시작할 때 두 페인이 함께 서므로 그 판정이 없으면 나중에 선 쪽(오른쪽)이 포커스를
/// 가져간다.
/// </para>
/// <para>
/// <c>Loaded</c> 는 <c>PresentationSource</c> 없이 뜨지 않아 자동으로 밟을 수 없다 —
/// 판정만 채점하고 실제 포커스 이동은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TabFocusTests
{
    [Fact]
    public void ShouldTake_UnderAnActivePane_IsTrue()
    {
        OnSta(() =>
        {
            var list = new ListBox();
            var pane = new ContentControl { Content = new Border { Child = list } };

            PaneChrome.SetIsActive(pane, true);

            Assert.True(TabFocus.ShouldTake(list));
        });
    }

    [Fact]
    public void ShouldTake_UnderAnInactivePane_IsFalse()
    {
        // 시작할 때 좌·우가 함께 서는 자리다. 여기가 참이면 왼쪽이 활성인데 오른쪽 목록이
        // 포커스를 들고 있어, 방향키가 보고 있지 않은 페인을 움직인다.
        OnSta(() =>
        {
            var list = new ListBox();
            var pane = new ContentControl { Content = new Border { Child = list } };

            PaneChrome.SetIsActive(pane, false);

            Assert.False(TabFocus.ShouldTake(list));
        });
    }

    [Fact]
    public void ShouldTake_WithoutAnyPaneAbove_IsFalse()
    {
        // 상속이 끊긴 자리는 기본값(false)이다 — 붙지 않은 목록이 포커스를 가져가지 않는다.
        OnSta(() => Assert.False(TabFocus.ShouldTake(new ListBox())));
    }

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
