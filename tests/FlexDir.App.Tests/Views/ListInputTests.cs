using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

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

    // ── 편집기 안의 클릭 (B-4 실물이 잡은 자리) ───────────────────

    [Fact]
    public void IsEditing_AClickInsideTheRenameEditor_IsNotTheListsBusiness()
    {
        // 편집기는 행 템플릿 안에 있으므로 그 클릭이 목록의 터널링 핸들러를 먼저 지난다.
        // 목록이 포커스를 가져가면 편집기가 포커스를 잃고 — 그것이 취소다 (§9-1).
        // 캐럿을 옮기려고 누른 것뿐인데 편집이 닫힌다.
        OnSta(() =>
        {
            var editor = new TextBox();
            var list = new Border { Child = new Border { Child = editor, DataContext = Item("a.txt") } };

            Assert.True(ListInput.IsEditing(list, editor));
        });
    }

    [Fact]
    public void IsEditing_AClickOnTheRowItself_IsTheListsBusiness()
    {
        OnSta(() =>
        {
            var text = new TextBlock();
            var list = new Border { Child = new Border { Child = text, DataContext = Item("a.txt") } };

            Assert.False(ListInput.IsEditing(list, text));
        });
    }

    [Fact]
    public void IsEditing_WithoutAnOrigin_IsFalse()
    {
        OnSta(() => Assert.False(ListInput.IsEditing(new Border(), null)));
    }

    // ── 선택 확정을 미루는가 (phase B-4) ──────────────────────────

    [Theory]
    // 이미 선택된 항목을 평 클릭했다 — 여기서 선택을 접으면 여러 개를 끌 수 없다.
    // 무엇을 할지는 마우스를 뗄 때 정해진다 (탐색기와 같다)
    [InlineData(ModifierKeys.None, 1, true, true)]
    // 선택 밖을 눌렀다 — 지금 선택해야 그것이 끌린다
    [InlineData(ModifierKeys.None, 1, false, false)]
    // 수정키는 선택 자체가 목적이다. 미루면 Ctrl 클릭이 눈에 반응하지 않는다
    [InlineData(ModifierKeys.Control, 1, true, false)]
    [InlineData(ModifierKeys.Shift, 1, true, false)]
    // 두 번째 클릭은 열기다
    [InlineData(ModifierKeys.None, 2, true, false)]
    public void DefersSelection_OnlyWhenTheClickMightBecomeADrag(
        ModifierKeys modifiers,
        int clicks,
        bool isSelected,
        bool expected)
    {
        Assert.Equal(expected, ListInput.DefersSelection(modifiers, clicks, isSelected));
    }

    // ── 재클릭 이름변경 (phase B-4 · docs/DESIGN.md §9-1) ─────────

    [Theory]
    // 평 클릭 · 유일한 선택 · 이미 포커스가 있던 목록 — 셋이 다 맞을 때만이다
    [InlineData(ModifierKeys.None, 1, true, true, true)]
    // 두 번째 클릭은 열기다. 편집을 열어 두면 그 위로 더블클릭이 떨어진다
    [InlineData(ModifierKeys.None, 2, true, true, false)]
    // 선택을 바꾸는 클릭은 이름변경이 아니다
    [InlineData(ModifierKeys.Control, 1, true, true, false)]
    [InlineData(ModifierKeys.Shift, 1, true, true, false)]
    // 방금 선택된 항목·다중 선택은 무엇의 이름인지 정해지지 않는다
    [InlineData(ModifierKeys.None, 1, false, true, false)]
    // 반대편 페인을 누른 것이다 — 2분할에서 페인 전환 클릭은 일상이라 여기서 걸러야 한다
    [InlineData(ModifierKeys.None, 1, true, false, false)]
    public void IsRenameGesture_OnlyWhenTheClickChangesNothing(
        ModifierKeys modifiers,
        int clicks,
        bool wasSoleSelection,
        bool listHadFocus,
        bool expected)
    {
        Assert.Equal(expected, ListInput.IsRenameGesture(modifiers, clicks, wasSoleSelection, listHadFocus));
    }

    // ── 눌린 자리 → 항목 (phase B-3 · ADR-016) ────────────────────

    [Fact]
    public void ItemAt_FindsTheItemUnderThePointer()
    {
        OnSta(() =>
        {
            var item = Item("a.txt");
            var text = new TextBlock();
            var list = new Border { Child = new Border { Child = text, DataContext = item } };

            Assert.Same(item, ListInput.ItemAt(list, text));
        });
    }

    [Fact]
    public void ItemAt_InsideACompositeRow_FindsTheItemNotTheRow()
    {
        // wrap 뷰 3종에서 목록의 컨테이너는 행이다 (ADR-016) — 거기서 멈추면 클릭이
        // 항목에 닿지 않고, 커맨드가 받는 타입도 아니다.
        OnSta(() =>
        {
            var item = Item("a.txt");
            var text = new TextBlock();
            var cell = new Border { Child = text, DataContext = item };
            var row = new Border { Child = cell, DataContext = new RowViewModel([item], 3) };
            var list = new Border { Child = row };

            Assert.Same(item, ListInput.ItemAt(list, text));
        });
    }

    [Fact]
    public void ItemAt_OnTheEmptyPartOfARow_IsNothing()
    {
        // 마지막 줄의 남은 칸은 빈 곳이다 — 선택이 풀려야 한다 (docs/DESIGN.md §9-1).
        OnSta(() =>
        {
            var row = new Border { DataContext = new RowViewModel([Item("a.txt")], 3) };
            var list = new Border { Child = row };

            Assert.Null(ListInput.ItemAt(list, row));
        });
    }

    [Fact]
    public void ItemAt_OnTheListItself_IsNothing()
    {
        OnSta(() =>
        {
            var list = new Border { DataContext = "페인" };

            Assert.Null(ListInput.ItemAt(list, list));
        });
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
        ListInput.SetRenameCommand(element, command);

        Assert.Same(command, ListInput.GetRenameCommand(element));
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

    /// <summary>
    /// WPF 요소는 STA 에서만 만들어지고 xunit 은 스레드풀(MTA)에서 돈다. 시각 트리를 세우는
    /// 테스트만 여기 안에서 돈다. 시한을 주는 이유는 게이트의 <c>--blame-hang</c> 과 같다 —
    /// 매달림이 아니라 실패여야 진단이 된다 (.harness/HANDOFF.md §규칙 4).
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

    private static FileItemViewModel Item(string name)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out _));

        return new FileItemViewModel(
            new FileItem(name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None),
            string.Empty,
            string.Empty,
            string.Empty);
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
