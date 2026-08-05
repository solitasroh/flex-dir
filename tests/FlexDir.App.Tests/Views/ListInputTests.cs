using System.Windows;
using System.Windows.Input;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 목록의 마우스 입력을 ViewModel 커맨드로 넘기는 attached behavior (phase B-2).
/// <para>
/// 행 템플릿의 <c>MouseBinding</c> 은 시각 트리 밖이라 페인 커맨드에 바인딩할 수 없다
/// (Freezable 은 자기 DataContext, 즉 행만 안다). 그래서 목록 컨트롤에 한 번 걸고 클릭된
/// 컨테이너를 찾아 커맨드로 넘긴다 — 어느 커맨드인가의 판정만 여기서 채점하고, 이벤트
/// 훅과 히트테스트는 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class ListInputTests
{
    // ── 수정키 → 커맨드 판정 (docs/DESIGN.md §9) ──────────────────

    [Theory]
    [InlineData(ModifierKeys.None, ListClick.Select)]
    [InlineData(ModifierKeys.Control, ListClick.Toggle)]
    [InlineData(ModifierKeys.Shift, ListClick.Range)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, ListClick.Range)]  // Shift 가 이긴다
    [InlineData(ModifierKeys.Alt, ListClick.Select)]                          // 모르는 수정키는 평 클릭
    public void Choose_MapsModifiersToTheClickKind(ModifierKeys modifiers, ListClick expected)
    {
        Assert.Equal(expected, ListInput.Choose(modifiers));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Commands_RoundTrip()
    {
        var element = new DependencyObject();
        var command = new NoopCommand();

        ListInput.SetSelectCommand(element, command);
        ListInput.SetToggleCommand(element, command);
        ListInput.SetRangeCommand(element, command);
        ListInput.SetOpenCommand(element, command);
        ListInput.SetEmptyCommand(element, command);

        Assert.Same(command, ListInput.GetSelectCommand(element));
        Assert.Same(command, ListInput.GetToggleCommand(element));
        Assert.Same(command, ListInput.GetRangeCommand(element));
        Assert.Same(command, ListInput.GetOpenCommand(element));
        Assert.Same(command, ListInput.GetEmptyCommand(element));
    }

    [Fact]
    public void Commands_DefaultToNull()
    {
        var element = new DependencyObject();

        Assert.Null(ListInput.GetSelectCommand(element));
        Assert.Null(ListInput.GetEmptyCommand(element));
    }

    private sealed class NoopCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
