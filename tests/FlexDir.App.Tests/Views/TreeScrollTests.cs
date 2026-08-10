using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 고른 트리 노드를 화면으로 가져오는 <see cref="TreeScroll"/> (docs/PRD-v2.md §10-3).
///
/// <para>
/// <b>왜 필요한가</b>: 트리가 페인을 따라가 경로를 펴도 <c>TreeView</c> 는 프로그램이 정한
/// 선택으로 <b>스크롤하지 않는다</b>. 실물에서 <c>C:\Users\SOOJANG\orca\projects\flex-dir\docs</c>
/// 로 갔을 때 노드는 펴졌지만 화면은 <c>SOOJANG</c> 근처에 남아 있었다 — 사용자에게는
/// "안 되는 것" 으로 보인다. <c>ARCHITECTURE.md</c> §5 가 목록에서 배운 것과 같은 자리다
/// (<see cref="FocusScroll"/>).
/// </para>
///
/// <para>
/// <b>단순한 <c>BringIntoView</c> 로는 안 된다.</b> 트리는 가상화돼 있어 화면 밖 노드는
/// 컨테이너가 아예 없고, 컨테이너를 얻으려면 <b>부모부터 차례로</b> 실현해야 한다 —
/// 그래서 이 배선은 노드 하나가 아니라 <c>FolderTreeViewModel.RevealedPath</c> 를 받는다.
/// </para>
///
/// <para>
/// <see cref="TreeSync"/> 와 같은 구도로 <b>판정만</b> 채점한다 — 실제 스크롤과 컨테이너
/// 실현은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TreeScrollTests
{
    [Fact]
    public void APathWithSteps_IsWorthWalking()
    {
        Assert.True(TreeScroll.ShouldWalk(2));
        Assert.True(TreeScroll.ShouldWalk(1));
    }

    [Fact]
    public void AnEmptyPath_IsNotWalked()
    {
        // 트리 밖 폴더를 열었거나 아직 아무 곳도 따라가지 않았다. 걷어내면 View 가 이전
        // 선택을 화면에서 놓친다 — 아무 일도 하지 않는 편이 낫다.
        Assert.False(TreeScroll.ShouldWalk(0));
    }

    [Fact]
    public void ANegativeCount_IsNotWalked()
    {
        // 방어가 아니라 계산의 전체성이다. 여기서 던지면 바인딩 콜백이 UI 스레드에서
        // 죽고, 그것을 잡을 사람이 없다 (2026-08-10 의 XAML 크래시와 같은 자리).
        Assert.False(TreeScroll.ShouldWalk(-1));
    }

    [Fact]
    public void EveryStepButTheLast_IsExpanded()
    {
        // 마지막은 고른 폴더다 — 그것까지 펴면 사용자가 접어 둔 하위가 멋대로 열린다.
        Assert.True(TreeScroll.ShouldExpand(step: 0, count: 3));
        Assert.True(TreeScroll.ShouldExpand(step: 1, count: 3));
        Assert.False(TreeScroll.ShouldExpand(step: 2, count: 3));
    }

    [Fact]
    public void ASingleStepPath_ExpandsNothing()
    {
        // 루트 자신을 고른 경우다 (C:\ 로 이동).
        Assert.False(TreeScroll.ShouldExpand(step: 0, count: 1));
    }
}
