namespace FlexDir.Core.Locations;

/// <summary>
/// 주소줄 breadcrumb 의 한 칸. <see cref="Name"/> 은 보이는 글자이고
/// <see cref="Path"/> 는 눌렀을 때 갈 곳이다.
/// <para>
/// 둘이 다른 이유는 드라이브 루트뿐이다 — 보여줄 때는 <c>C:</c>, 갈 때는 <c>C:\</c> 다.
/// <c>C:</c> 는 드라이브 상대 경로라 <see cref="LocationId.TryParse"/> 가 거부한다.
/// </para>
/// </summary>
/// <param name="IsFirst">
/// 목록의 첫 칸인가. 주소줄은 칸 <b>사이</b>에만 구분자를 넣으므로 (목업 <c>.chev</c>)
/// View 가 이것을 보고 앞 구분자를 그릴지 정한다. 순서는 목록의 사실이지 그리기 방식이
/// 아니다 — 무엇을 그릴지는 View 가 정한다.
/// </param>
public readonly record struct PathSegment(string Name, string Path, bool IsFirst);

/// <summary>
/// 위치를 주소줄에 놓을 칸들로 나눈다 (docs/DESIGN.md §1 · 목업 <c>.addr</c>).
/// <para>
/// <b>View 에서 문자열을 자르지 않는 이유</b>: 자르는 규칙이 <see cref="LocationId"/> 의
/// 정규화 규칙과 같아야 한다. 내부 표현에는 <c>\\?\</c> 확장 접두사가 붙어 있고 드라이브
/// 루트만 후행 구분자를 남긴다 — 그 둘을 모르는 <c>Split('\\')</c> 는 빈 칸을 만들거나
/// 루트를 잃는다. 그래서 부모를 따라 올라가며 만든다.
/// </para>
/// </summary>
public static class PathSegments
{
    public static IReadOnlyList<PathSegment> Of(LocationId location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var segments = new List<PathSegment>();
        var current = location;

        while (true)
        {
            // 드라이브 루트의 Name 은 "C:\" 다. 보이는 글자에서만 구분자를 떼고 경로는 그대로 둔다.
            segments.Add(new PathSegment(current.Name.TrimEnd('\\'), current.DisplayPath, IsFirst: false));

            if (!current.TryGetParent(out var parent))
            {
                break;
            }

            current = parent;
        }

        // 만든 순서는 아래에서 위다. 주소줄은 반대로 읽는다.
        segments.Reverse();

        // 마지막에 표시한다 — 뒤집기 전에는 "첫 칸" 이 목록의 끝이다.
        segments[0] = segments[0] with { IsFirst = true };

        return segments;
    }
}
