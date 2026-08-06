using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 문자 키를 <c>PaneViewModel.TypeAhead</c> 로 넘기는 attached behavior (phase B-3).
/// <para>
/// v1 에 필터·검색이 없으므로 큰 폴더에서 항목을 찾는 유일한 수단이다 (docs/DESIGN.md §9).
/// 무엇이 문자인가의 판정만 여기서 채점하고, 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class TypeAheadInputTests
{
    [Theory]
    [InlineData("a", 'a')]
    [InlineData("한", '한')]
    [InlineData(" ", ' ')]      // 이름에 공백이 있다 — 탐색기도 센다
    [InlineData("ab", 'a')]     // IME 가 여러 자를 한 번에 실어 오면 첫 자로 시작한다
    public void CharacterOf_TakesThePrintableCharacter(string text, char expected)
    {
        Assert.Equal(expected, TypeAheadInput.CharacterOf(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r")]      // Enter — 열기다
    [InlineData("\t")]      // Tab — 페인 전환이다
    [InlineData("\b")]      // Backspace
    [InlineData("")]  // Esc
    public void CharacterOf_IgnoresWhatIsNotACharacter(string text)
    {
        // 제어 문자를 접두어에 쌓으면 그 뒤의 진짜 문자까지 아무것도 찾지 못한다.
        Assert.Null(TypeAheadInput.CharacterOf(text));
    }

    [Fact]
    public void Enabled_RoundTripsAndDefaultsToOff()
    {
        var element = new DependencyObject();

        Assert.False(TypeAheadInput.GetEnabled(element));

        TypeAheadInput.SetEnabled(element, true);

        Assert.True(TypeAheadInput.GetEnabled(element));
    }
}
