using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 목록에서 트리로 끌어다 놓아 고정하는 <see cref="FavoriteDropInput"/>.
/// <para>
/// 판정 둘만 채점한다 — 무엇을 받을 것인가와 어떤 효과로 보일 것인가. 실제 드롭은
/// 모달 루프라 자동 테스트가 밟을 수 없다 (<see cref="DragDropInput"/> 과 같은 자리).
/// </para>
/// </summary>
public class FavoriteDropInputTests
{
    [Fact]
    public void Effect_ForFiles_IsLink()
    {
        // 복사도 이동도 아니다 — 원본은 그대로 있고 트리에 가리키는 것만 생긴다.
        // 커서가 그 뜻을 보여줘야 사용자가 파일이 옮겨진다고 오해하지 않는다.
        Assert.Equal(DragDropEffects.Link, FavoriteDropInput.Effect(hasFiles: true));
    }

    [Fact]
    public void Effect_ForAnythingElse_IsNone()
    {
        // 글자를 끌어 온 것 같은 경우다. 받을 수 없으면 받을 수 없다고 보여야 한다.
        Assert.Equal(DragDropEffects.None, FavoriteDropInput.Effect(hasFiles: false));
    }

    [Fact]
    public void Paths_TakesTheFileDropList()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\work", @"C:\build" });

        Assert.Equal([@"C:\work", @"C:\build"], FavoriteDropInput.Paths(data));
    }

    [Fact]
    public void Paths_WithoutFiles_IsEmpty()
    {
        Assert.Empty(FavoriteDropInput.Paths(new DataObject(DataFormats.Text, "글자")));
    }

    [Fact]
    public void Paths_OfNothing_IsEmpty()
    {
        Assert.Empty(FavoriteDropInput.Paths(null));
    }
}
