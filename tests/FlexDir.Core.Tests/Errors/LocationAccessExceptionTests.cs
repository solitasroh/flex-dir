using FlexDir.Core.Errors;
using FlexDir.Core.Locations;

using Xunit;

namespace FlexDir.Core.Tests.Errors;

/// <summary>
/// 열거·탐색 계층이 던지는 예외. 다음 phase 의 <c>IFolderSource</c> 가 이것을 쓴다.
/// 잡는 쪽이 분류·위치·원본 코드를 전부 얻어야 상태표시줄 문구와 진단이 갈라지지 않는다.
/// </summary>
public class LocationAccessExceptionTests
{
    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out _));
        return location;
    }

    [Fact]
    public void PreservesKindLocationAndWin32Error()
    {
        var location = Parse(@"C:\Windows\System32\config");

        var exception = new LocationAccessException(LocationErrorKind.AccessDenied, location, 5);

        Assert.Equal(LocationErrorKind.AccessDenied, exception.Kind);
        Assert.Same(location, exception.Location);
        Assert.Equal(5, exception.Win32Error);
    }

    [Fact]
    public void Win32ErrorDefaultsToZero()
    {
        // 원본 코드가 없는 경로도 있다 (예: 상위 계층이 분류만 알고 던질 때).
        var exception = new LocationAccessException(LocationErrorKind.NotFound, Parse(@"C:\Temp\gone"));

        Assert.Equal(0, exception.Win32Error);
    }

    [Fact]
    public void MessageIsTheUserFacingWording()
    {
        var location = Parse(@"C:\Temp\gone");

        var exception = new LocationAccessException(LocationErrorKind.NotFound, location, 3);

        Assert.Equal(LocationErrorMessages.Describe(LocationErrorKind.NotFound, location), exception.Message);
    }

    [Fact]
    public void MessageDoesNotLeakTheExtendedPrefix()
    {
        var exception = new LocationAccessException(LocationErrorKind.Sharing, Parse(@"C:\Temp\a.xlsx"), 32);

        Assert.DoesNotContain(@"\\?\", exception.Message);
    }

    [Fact]
    public void IsAnException()
    {
        var exception = new LocationAccessException(LocationErrorKind.Unknown, Parse(@"C:\Temp"));

        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void NoneIsNotAThrowableKind()
    {
        // 오류가 아닌 것을 던지는 호출은 버그다.
        Assert.Throws<ArgumentException>(
            () => new LocationAccessException(LocationErrorKind.None, Parse(@"C:\Temp")));
    }

    [Fact]
    public void NullLocation_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new LocationAccessException(LocationErrorKind.NotFound, null!));
    }
}
