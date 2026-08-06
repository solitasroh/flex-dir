using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.Core.Tests.Locations;

/// <summary>
/// 주소줄 breadcrumb 이 서는 자리 (docs/DESIGN.md §1 · 목업 <c>.addr .seg</c> ·
/// 사용자 결정 2026-08-06).
/// <para>
/// 경로 분해가 <c>Core</c> 에 있는 이유: 문자열을 자르는 규칙이 <c>LocationId</c> 의
/// 정규화 규칙과 같은 것을 알아야 한다. View 에서 <c>Split('\\')</c> 로 자르면 확장 접두사와
/// 드라이브 루트의 후행 구분자에서 갈린다.
/// </para>
/// </summary>
public class PathSegmentsTests
{
    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var id, out var error), $"파싱 실패: {error}");
        return id;
    }

    [Fact]
    public void Of_ADeepFolder_GoesFromTheDriveDown()
    {
        var segments = PathSegments.Of(Folder(@"C:\Projects\flex-dir"));

        Assert.Equal(["C:", "Projects", "flex-dir"], segments.Select(s => s.Name));
    }

    [Fact]
    public void Of_EachSegment_CarriesTheFullPathToItself()
    {
        // 세그먼트를 누르면 거기로 간다. 이름만 들고 있으면 View 가 경로를 다시 조립해야 한다.
        var segments = PathSegments.Of(Folder(@"C:\Projects\flex-dir"));

        Assert.Equal([@"C:\", @"C:\Projects", @"C:\Projects\flex-dir"], segments.Select(s => s.Path));
    }

    [Fact]
    public void Of_TheDriveRoot_IsOneSegmentWithoutTheTrailingSeparator()
    {
        // 이름은 목업대로 "C:" 다. 경로는 "C:\" 여야 한다 — "C:" 는 드라이브 상대 경로라
        // LocationId 가 거부한다.
        var segments = PathSegments.Of(Folder(@"C:\"));

        var only = Assert.Single(segments);
        Assert.Equal("C:", only.Name);
        Assert.Equal(@"C:\", only.Path);
        Assert.True(only.IsFirst);
    }

    [Fact]
    public void Of_OnlyTheFirstSegmentIsMarkedFirst()
    {
        // 구분자는 칸 사이에만 들어간다 (목업 .chev). 앞 구분자를 그릴지 View 가 이것으로 정한다.
        var segments = PathSegments.Of(Folder(@"C:\Projects\flex-dir"));

        Assert.Equal([true, false, false], segments.Select(s => s.IsFirst));
    }

    [Fact]
    public void Of_TheDriveRootAlone_IsAlsoTheFirst()
    {
        Assert.True(Assert.Single(PathSegments.Of(Folder(@"C:\"))).IsFirst);
    }

    [Fact]
    public void Of_ASegmentPath_ParsesBackIntoTheSameLocation()
    {
        // 왕복이 깨지면 세그먼트를 눌러도 이동하지 않는다 (OpenAddressCommand 가 다시 파싱한다).
        foreach (var segment in PathSegments.Of(Folder(@"C:\Projects\flex-dir")))
        {
            Assert.True(LocationId.TryParse(segment.Path, out _, out var error), $"{segment.Path}: {error}");
        }
    }
}
