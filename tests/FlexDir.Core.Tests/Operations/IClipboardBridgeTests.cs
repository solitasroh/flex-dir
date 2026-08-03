using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Operations;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// 탐색기와의 상호 호환(<c>CFSTR_SHELLIDLIST</c>)은 <c>FlexDir.Shell</c> 구현체가
/// 수동으로 검증받는다 (ADR-009).
/// <para>
/// 여기서 못 박는 것은 <b>왕복</b>과 <b>빈 클립보드</b>다. 붙여넣기가 복사인지 이동인지를
/// 이 왕복 하나가 정하고, 비었을 때 <c>false</c> 를 내지 않으면 붙여넣기가 빈 목록으로
/// shell 을 부른다.
/// </para>
/// </summary>
public class FakeClipboardBridgeTests
{
    [Fact]
    public void TryGetPaste_OnAnEmptyClipboard_IsFalse()
    {
        var clipboard = new FakeClipboardBridge();

        Assert.False(clipboard.TryGetPaste(out var items, out var isMove));

        // false 를 무시하고 열거해도 터지지 않아야 한다.
        Assert.Empty(items);
        Assert.False(isMove);
        Assert.Equal(1, clipboard.PasteQueries);
    }

    [Fact]
    public void SetCopy_RoundTripsAsACopy()
    {
        var clipboard = new FakeClipboardBridge();
        var copied = new[] { Loc(@"C:\Temp\a.txt"), Loc(@"C:\Temp\b.txt") };

        clipboard.SetCopy(copied);

        Assert.True(clipboard.TryGetPaste(out var items, out var isMove));
        Assert.Equal(copied, items);
        Assert.False(isMove);
        Assert.Equal(copied, Assert.Single(clipboard.CopySets));
        Assert.Empty(clipboard.CutSets);
    }

    [Fact]
    public void SetCut_RoundTripsAsAMove()
    {
        var clipboard = new FakeClipboardBridge();
        var cut = new[] { Loc(@"C:\Temp\a.txt") };

        clipboard.SetCut(cut);

        Assert.True(clipboard.TryGetPaste(out var items, out var isMove));
        Assert.Equal(cut, items);
        Assert.True(isMove);
        Assert.Equal(cut, Assert.Single(clipboard.CutSets));
        Assert.Empty(clipboard.CopySets);
    }

    [Fact]
    public void SetCopy_AfterSetCut_ReplacesTheContentAndTheEffect()
    {
        var clipboard = new FakeClipboardBridge();
        var cut = new[] { Loc(@"C:\Temp\a.txt") };
        var copied = new[] { Loc(@"C:\Temp\b.txt") };

        clipboard.SetCut(cut);
        clipboard.SetCopy(copied);

        Assert.True(clipboard.TryGetPaste(out var items, out var isMove));
        Assert.Equal(copied, items);
        Assert.False(isMove);
    }

    [Fact]
    public void TryGetPaste_Twice_KeepsTheContent()
    {
        // 붙여넣은 뒤 클립보드를 비우는지는 shell 의 정책이고 포트가 규정하지 않는다.
        var clipboard = new FakeClipboardBridge();
        clipboard.SetCopy([Loc(@"C:\Temp\a.txt")]);

        Assert.True(clipboard.TryGetPaste(out _, out _));
        Assert.True(clipboard.TryGetPaste(out var items, out _));

        Assert.Single(items);
        Assert.Equal(2, clipboard.PasteQueries);
    }

    [Fact]
    public void Clipboard_IsUsableThroughThePortAlone()
    {
        // 호출부는 fake 를 모른다. 포트만으로 쓸 수 있어야 한다.
        IClipboardBridge port = new FakeClipboardBridge();

        port.SetCut([Loc(@"C:\Temp\a.txt")]);

        Assert.True(port.TryGetPaste(out _, out var isMove));
        Assert.True(isMove);
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
