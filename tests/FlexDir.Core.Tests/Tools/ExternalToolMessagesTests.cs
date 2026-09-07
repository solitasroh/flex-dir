using FlexDir.Core.Errors;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.Core.Tests.Tools;

/// <summary>
/// 외부 도구 실행 실패의 문구. <see cref="LocationErrorMessages"/> 를 재사용하지 않는다 —
/// 그쪽은 <b>폴더</b>를 못 여는 사건을 말하고, 여기서 그것을 내면 사용자는 자기 폴더가
/// 사라진 줄 안다.
/// </summary>
public class ExternalToolMessagesTests
{
    [Fact]
    public void Describe_NotFound_NamesTheTool()
    {
        var message = ExternalToolMessages.Describe(LocationErrorKind.NotFound, "VS Code");

        Assert.Contains("VS Code", message);
        Assert.Contains("찾을 수 없", message);
    }

    /// <summary>없는 것과 막힌 것은 사용자가 손쓸 것이 다르다.</summary>
    [Fact]
    public void Describe_AccessDenied_DiffersFromNotFound()
    {
        Assert.NotEqual(
            ExternalToolMessages.Describe(LocationErrorKind.NotFound, "VS Code"),
            ExternalToolMessages.Describe(LocationErrorKind.AccessDenied, "VS Code"));
    }

    /// <summary>성공을 실패 문구로 만드는 호출은 버그다. 조용히 넘기면 헛말이 뜬다.</summary>
    [Fact]
    public void Describe_None_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalToolMessages.Describe(LocationErrorKind.None, "VS Code"));
    }

    [Fact]
    public void Describe_EveryKind_IsStatusBarWording()
    {
        foreach (var kind in Enum.GetValues<LocationErrorKind>())
        {
            if (kind == LocationErrorKind.None)
            {
                continue;
            }

            var message = ExternalToolMessages.Describe(kind, "Windows Terminal");

            Assert.False(string.IsNullOrWhiteSpace(message));

            // docs/UI_GUIDE.md §상태 표현 · LocationErrorMessages 와 같은 규칙이다.
            Assert.False(message.EndsWith('.'), $"{kind} 문구가 마침표로 끝난다: {message}");
        }
    }
}
