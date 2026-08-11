using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 마우스 보조 버튼(4·5번) → 뒤로·앞으로 (docs/PRD-v2.md §15).
/// <para>
/// 채점하는 것은 판정 둘이다 — <b>어느 버튼이 어느 방향인가</b>와 <b>눌린 자리가 어느
/// 페인인가</b>. 실제 버튼은 사람이 확인한다: 마우스 보조 버튼은 UIA 로 만들 수 없고
/// (CLAUDE.md §5), 드라이버에 따라 <c>WM_XBUTTONDOWN</c> 이 아니라 <c>WM_APPCOMMAND</c> 로
/// 오는 기계도 있다.
/// </para>
/// </summary>
public class MouseNavigationInputTests
{
    // ── 버튼 → 방향 ───────────────────────────────────────────────

    [Theory]
    [InlineData(MouseButton.XButton1, true, false)]
    [InlineData(MouseButton.XButton2, false, true)]
    [InlineData(MouseButton.Left, false, false)]
    [InlineData(MouseButton.Right, false, false)]
    [InlineData(MouseButton.Middle, false, false)]
    public void TheOnlyButtonsWeTakeAreTheTwoSideButtons(MouseButton button, bool back, bool forward)
    {
        // 4번이 뒤로다 — 브라우저·탐색기가 그 매핑이라 반대로 붙이면 손이 매번 틀린다.
        // 가운데 버튼은 우리 것이 아니다 (사용자 결정 2026-08-11: 상위 폴더로 쓰지 않는다).
        Assert.Equal(back, MouseNavigationInput.IsBack(button));
        Assert.Equal(forward, MouseNavigationInput.IsForward(button));
    }

    // ── 눌린 자리 → 페인 ─────────────────────────────────────────

    [Fact]
    public void PaneAt_FindsThePaneUnderThePointer()
    {
        // DataContext 는 트리를 따라 상속된다 — 페인 안의 요소는 자기 DataContext 없이도
        // 페인을 들고 있다. 그래서 걸음이 하는 일은 "위로 찾아 올라가기" 가 아니라 아래
        // 테스트가 재는 것, 즉 행·칸이 덮어쓴 구간을 지나 상속값으로 되돌아가는 것이다.
        OnSta(() =>
        {
            var pane = Pane();
            var text = new TextBlock();
            var list = new Border { Child = text, DataContext = pane };
            var window = new Border { Child = list };

            Assert.Same(pane, MouseNavigationInput.PaneAt(window, text));
        });
    }

    [Fact]
    public void PaneAt_InsideARow_FindsThePaneNotTheRow()
    {
        // 행과 칸은 자기 DataContext(항목·합성 행)를 갖는다 — 여기서 멈추면 목록 위에서
        // 누른 버튼이 페인을 못 찾아 활성 페인으로 새어 나간다. 그 자리는 아무 값이면 된다.
        OnSta(() =>
        {
            var pane = Pane();
            var text = new TextBlock();
            var row = new Border { Child = text, DataContext = new object() };
            var list = new Border { Child = row, DataContext = pane };
            var window = new Border { Child = list };

            Assert.Same(pane, MouseNavigationInput.PaneAt(window, text));
        });
    }

    [Fact]
    public void PaneAt_OutsideBothPanes_IsNull()
    {
        // 트리·툴바·상태표시줄 위에서 누른 것이 이것이다. 호출자가 활성 페인으로 보낸다.
        // 실제 창의 모양으로 세운다 — 루트가 워크스페이스를 얹고 있고, 그 값이 트리까지
        // 상속된다. 워크스페이스를 페인으로 오인하면 여기서 걸린다.
        OnSta(() =>
        {
            var text = new TextBlock();
            var tree = new Border { Child = text };
            var window = new Border { Child = tree, DataContext = Workspace() };

            Assert.Null(MouseNavigationInput.PaneAt(window, text));
        });
    }

    [Fact]
    public void PaneAt_WithoutAnOrigin_IsNull()
    {
        OnSta(() => Assert.Null(MouseNavigationInput.PaneAt(new Border(), null)));
    }

    /// <summary>창의 DataContext. 페인 밖 판정이 이것을 페인으로 오인하지 않아야 한다.</summary>
    private static WorkspaceViewModel Workspace()
        => new(Pane(), Pane(), new InMemoryViewStateStore());

    private static PaneViewModel Pane()
        => new(
            new FakeFolderSource(), new FakeFolderWatcher(), new FakeTypeNameProvider(),
            new FakeThumbnailSource(), new InMemoryViewStateStore(), new FakeFileOperations(),
            new FakeClipboardBridge(), new FakeItemActivator(), new InlineUiDispatcher(),
            CultureInfo.InvariantCulture, TimeZoneInfo.Utc, new FakeContextMenuProvider());

    /// <summary>
    /// WPF 요소는 STA 에서만 만들어지고 xunit 은 스레드풀(MTA)에서 돈다. 시각 트리를 세우는
    /// 테스트만 여기 안에서 돈다 (<c>ListInputTests</c> 와 같은 자리).
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
