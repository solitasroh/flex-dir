using System.Globalization;
using System.Windows;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 창에 상주 규약(ADR-003)을 거는 behavior (phase B-2): 배치 복원, 닫기 = 숨기기 + 배치
/// 저장. 배치의 접기/펴기 판정만 여기서 채점한다 — 실제 창은 STA 가 필요해 이벤트 훅과
/// 함께 사람이 확인한다 (CLAUDE.md §5).
/// </summary>
public class ResidentWindowTests
{
    // ── 창 → 배치 접기 ────────────────────────────────────────────

    [Fact]
    public void Capture_RemembersTheBounds()
    {
        var placement = ResidentWindow.Capture(new Rect(10, 20, 900, 600), WindowState.Normal);

        Assert.Equal(new WindowPlacement(10, 20, 900, 600, Maximized: false), placement);
    }

    [Fact]
    public void Capture_AMaximizedWindow_RemembersTheRestoreBounds()
    {
        // 최대화 상태의 실측(모니터 전체)을 저장하면 다음 실행에서 "최대화를 푼 창" 이
        // 모니터만 하게 뜬다. 저장할 것은 풀었을 때의 자리다.
        var placement = ResidentWindow.Capture(new Rect(10, 20, 900, 600), WindowState.Maximized);

        Assert.Equal(new WindowPlacement(10, 20, 900, 600, Maximized: true), placement);
    }

    [Fact]
    public void Capture_AMinimizedWindow_IsRememberedAsNormal()
    {
        // 최소화된 채 닫힌 창을 최소화로 복원하면 다음 실행에서 창이 보이지 않는다.
        var placement = ResidentWindow.Capture(new Rect(10, 20, 900, 600), WindowState.Minimized);

        Assert.Equal(new WindowPlacement(10, 20, 900, 600, Maximized: false), placement);
    }

    [Fact]
    public void Capture_AWindowThatWasNeverLaidOut_RemembersNothing()
    {
        // 보인 적 없는 창의 RestoreBounds 는 Empty 다. 그것을 저장하면 다음 실행에서
        // 크기 0 짜리 창이 뜬다.
        Assert.Null(ResidentWindow.Capture(Rect.Empty, WindowState.Normal));
        Assert.Null(ResidentWindow.Capture(new Rect(0, 0, 0, 0), WindowState.Normal));
    }

    // ── 배치 → 창 펴기 ────────────────────────────────────────────

    [Fact]
    public void Expand_RoundTripsWithCapture()
    {
        var placement = new WindowPlacement(64, 32, 1280, 800, Maximized: true);

        var (bounds, state) = ResidentWindow.Expand(placement);

        Assert.Equal(placement, ResidentWindow.Capture(bounds, state));
    }

    [Fact]
    public void Expand_ANormalPlacement_IsANormalWindow()
    {
        var (bounds, state) = ResidentWindow.Expand(new WindowPlacement(64, 32, 1280, 800, Maximized: false));

        Assert.Equal(new Rect(64, 32, 1280, 800), bounds);
        Assert.Equal(WindowState.Normal, state);
    }

    // ── 인수 검사 ─────────────────────────────────────────────────

    [Fact]
    public void Attach_WithoutAWindow_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ResidentWindow.Attach(null!, CreateWorkspace()));
    }

    private static WorkspaceViewModel CreateWorkspace()
    {
        var viewStates = new InMemoryViewStateStore();

        PaneViewModel Pane() => new(
            new FakeFolderSource(), new FakeFolderWatcher(), new FakeTypeNameProvider(),
            new FakeThumbnailSource(), viewStates, new FakeFileOperations(), new FakeClipboardBridge(),
            new FakeItemActivator(), new InlineUiDispatcher(), CultureInfo.InvariantCulture, TimeZoneInfo.Utc,
            new FakeContextMenuProvider());

        return new WorkspaceViewModel(Pane(), Pane(), viewStates);
    }
}
