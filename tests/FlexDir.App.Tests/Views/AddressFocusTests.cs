using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// <c>Ctrl+L</c>·<c>Alt+D</c> 로 주소줄에 포커스를 주는 attached behavior
/// (phase B-3 · docs/DESIGN.md §9).
/// <para>
/// 창이 아니라 <b>페인 루트</b>에 건다 — 키가 온 쪽이 곧 그 페인이므로 활성 페인을 따로
/// 물어볼 필요가 없다. 판정과 주소줄 찾기만 채점하고, 이벤트 훅과 실제 포커스 이동은
/// 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class AddressFocusTests
{
    // ── 어떤 키가 주소줄로 가는가 (탐색기와 같다) ─────────────────

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control)]
    [InlineData(Key.D, ModifierKeys.Alt)]
    public void IsAddressFocusKey_AcceptsWhatExplorerAccepts(Key key, ModifierKeys modifiers)
    {
        Assert.True(AddressFocus.IsAddressFocusKey(key, modifiers));
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.None)]                        // 문자 L 은 type-ahead 다
    [InlineData(Key.D, ModifierKeys.None)]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.A, ModifierKeys.Control)]                     // 전체 선택이다
    public void IsAddressFocusKey_RejectsTheRest(Key key, ModifierKeys modifiers)
    {
        Assert.False(AddressFocus.IsAddressFocusKey(key, modifiers));
    }

    // ── 주소줄 찾기 ───────────────────────────────────────────────

    [Fact]
    public void AddressBoxIn_FindsTheMarkedBox()
    {
        OnSta(() =>
        {
            var address = new TextBox();
            AddressFocus.SetIsAddressBox(address, true);

            var pane = new Border { Child = new Border { Child = address } };

            Assert.Same(address, AddressFocus.AddressBoxIn(pane));
        });
    }

    [Fact]
    public void AddressBoxIn_SkipsUnmarkedBoxes()
    {
        // B-4 의 이름변경 편집기가 같은 서브트리에 TextBox 를 하나 더 만든다 — 표시가
        // 없으면 그것이 먼저 잡혀 Ctrl+L 이 엉뚱한 곳으로 간다.
        OnSta(() =>
        {
            var panel = new StackPanel();
            var rename = new TextBox();
            var address = new TextBox();

            AddressFocus.SetIsAddressBox(address, true);
            panel.Children.Add(rename);
            panel.Children.Add(address);

            Assert.Same(address, AddressFocus.AddressBoxIn(new Border { Child = panel }));
        });
    }

    [Fact]
    public void AddressBoxIn_WithoutOne_IsNothing()
    {
        OnSta(() => Assert.Null(AddressFocus.AddressBoxIn(new Border { Child = new TextBox() })));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Properties_RoundTripAndDefaultToOff()
    {
        var element = new DependencyObject();

        Assert.False(AddressFocus.GetEnabled(element));
        Assert.False(AddressFocus.GetIsAddressBox(element));

        AddressFocus.SetEnabled(element, true);
        AddressFocus.SetIsAddressBox(element, true);

        Assert.True(AddressFocus.GetEnabled(element));
        Assert.True(AddressFocus.GetIsAddressBox(element));
    }

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
}
