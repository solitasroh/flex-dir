using System.Windows;

using FlexDir.App.Views;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 활성 페인 표시가 타는 attached property (docs/DESIGN.md §6).
/// <para>
/// 코드비하인드가 금지라 (CLAUDE.md §2) 페인 루트에 "내가 활성인가" 를 실어 나르는 자리가
/// 필요하다. 상속되는 속성이라 행 템플릿까지 내려간다 — 비활성 페인의 선택색 강등과
/// 포커스 테두리 숨김이 이 값 하나로 갈린다.
/// </para>
/// </summary>
public class PaneChromeTests
{
    [Fact]
    public void IsActive_DefaultsToFalse()
    {
        var element = new DependencyObject();

        Assert.False(PaneChrome.GetIsActive(element));
    }

    [Fact]
    public void IsActive_RoundTrips()
    {
        var element = new DependencyObject();

        PaneChrome.SetIsActive(element, true);

        Assert.True(PaneChrome.GetIsActive(element));
    }

    [Fact]
    public void IsActive_InheritsDownTheTree()
    {
        // 페인 루트에 한 번만 걸면 행 템플릿의 트리거가 그대로 읽는다. 상속이 꺼지면
        // 행마다 페인 루트를 찾아 바인딩해야 한다.
        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            PaneChrome.IsActiveProperty.GetMetadata(typeof(DependencyObject)));

        Assert.True(metadata.Inherits);
    }
}
