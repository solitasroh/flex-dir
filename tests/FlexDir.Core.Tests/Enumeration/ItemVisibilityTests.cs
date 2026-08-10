using FlexDir.Core.Enumeration;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// 숨김·시스템 필터 (docs/PRD-v2.md §12).
/// <para>
/// <b>규칙이 Core 에 사는 이유</b>: 목록(<c>PaneViewModel</c>)과 트리
/// (<c>FolderTreeViewModel</c>) 둘이 같은 답을 내야 한다. 두 ViewModel 에 각각 조건을 쓰면
/// 한쪽만 고치는 순간 트리에는 있는데 목록에는 없는 폴더가 생긴다 — 트리가 열거 정책을
/// 그대로 따르기로 한 것이 애초에 그 이유였다.
/// </para>
/// <para>
/// <b>열거는 여전히 다 내준다.</b> <c>FileSystemFolderSource</c> 는 <c>AttributesToSkip = 0</c>
/// 이고 "보여줄지 말지는 UI 정책" 이라고 적어 놓았다 — 이 파일이 그 정책이다.
/// </para>
/// </summary>
public class ItemVisibilityTests
{
    private static FileItem Item(FileItemFlags flags)
    {
        LocationId.TryParse(@"C:\folder\item", out var location, out _);

        return new FileItem("item", location!, 0, default, flags);
    }

    [Fact]
    public void OrdinaryItems_AreVisibleEitherWay()
    {
        Assert.True(ItemVisibility.IsVisible(Item(FileItemFlags.None), showHidden: false));
        Assert.True(ItemVisibility.IsVisible(Item(FileItemFlags.None), showHidden: true));
        Assert.True(ItemVisibility.IsVisible(Item(FileItemFlags.Directory), showHidden: false));
    }

    [Theory]
    [InlineData(FileItemFlags.Hidden)]
    [InlineData(FileItemFlags.System)]
    [InlineData(FileItemFlags.Hidden | FileItemFlags.System)]
    [InlineData(FileItemFlags.Directory | FileItemFlags.Hidden | FileItemFlags.System)]
    public void HiddenAndSystemItems_AreHiddenUnlessAsked(FileItemFlags flags)
    {
        // 토글 하나다 (사용자 결정 2026-08-10) — 숨김만 켜서는 $Recycle.Bin 이 보이지 않는
        // 탐색기의 두 단계를 따라가지 않는다.
        Assert.False(ItemVisibility.IsVisible(Item(flags), showHidden: false));
        Assert.True(ItemVisibility.IsVisible(Item(flags), showHidden: true));
    }

    [Theory]
    [InlineData(FileItemFlags.ReparsePoint)]
    [InlineData(FileItemFlags.Offline)]
    [InlineData(FileItemFlags.CloudPlaceholder)]
    public void OtherFlags_DoNotHideAnything(FileItemFlags flags)
    {
        // 클라우드 자리표시자·심볼릭 링크는 숨김이 아니다. 썸네일이 그것을 건너뛰는 것과
        // 목록에서 지우는 것은 다른 판단이다 (FileItem.IsContentAccessRisky).
        Assert.True(ItemVisibility.IsVisible(Item(flags), showHidden: false));
    }
}
