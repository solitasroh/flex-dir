using System.Globalization;

using FlexDir.App.ViewModels;

using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 목록 한 줄. 표시 문자열을 <b>여기서 만들지 않는다</b> — 포맷터는 culture·표준시간대를
/// 인자로 요구하고 유형 이름은 비동기다. 조립은 <c>PaneViewModel</c> 이 한다.
/// </summary>
public class FileItemViewModelTests
{
    [Fact]
    public void Ctor_TakesTheDisplayStringsAsGiven()
    {
        var row = new FileItemViewModel(File("report.txt", 2048), "2 KB", "2026-08-03 오전 9:00", "텍스트 문서");

        Assert.Equal("2 KB", row.SizeText);
        Assert.Equal("2026-08-03 오전 9:00", row.ModifiedText);
        Assert.Equal("텍스트 문서", row.TypeText);
    }

    [Fact]
    public void Name_And_IsDirectory_ComeFromTheItem()
    {
        var file = new FileItemViewModel(File("report.txt", 1), "1 KB", "-", "TXT");
        var folder = new FileItemViewModel(Directory("Docs"), string.Empty, "-", "DIR");

        Assert.Equal("report.txt", file.Name);
        Assert.False(file.IsDirectory);

        Assert.Equal("Docs", folder.Name);
        Assert.True(folder.IsDirectory);
    }

    [Fact]
    public void Item_IsTheSameInstance()
    {
        // 정렬·감시 갱신·파일 조작이 모두 이 원본을 본다. 복사해 두면 어긋난다.
        var item = File("report.txt", 1);

        Assert.Same(item, new FileItemViewModel(item, "1 KB", "-", "TXT").Item);
    }

    [Fact]
    public void Ctor_HoldsWhatTheFormattersProduced()
    {
        // 문자열의 출처가 Core 의 포맷터임을 못 박는다 — 두 곳에서 만들면 갈라진다.
        var item = File("report.txt", 1536);
        var culture = CultureInfo.InvariantCulture;

        var row = new FileItemViewModel(
            item,
            SizeFormatter.ForItem(item, culture),
            TimestampFormatter.Format(item.ModifiedUtc, TimeZoneInfo.Utc, culture),
            "TXT");

        Assert.Equal(SizeFormatter.ForItem(item, culture), row.SizeText);
        Assert.Equal(TimestampFormatter.Format(item.ModifiedUtc, TimeZoneInfo.Utc, culture), row.ModifiedText);
    }

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        var item = File("report.txt", 1);

        Assert.Throws<ArgumentNullException>(() => new FileItemViewModel(null!, "1 KB", "-", "TXT"));
        Assert.Throws<ArgumentNullException>(() => new FileItemViewModel(item, null!, "-", "TXT"));
        Assert.Throws<ArgumentNullException>(() => new FileItemViewModel(item, "1 KB", null!, "TXT"));
        Assert.Throws<ArgumentNullException>(() => new FileItemViewModel(item, "1 KB", "-", null!));
    }

    private static FileItem File(string name, long size)
        => new(name, Loc($@"C:\Temp\{name}"), size, DateTimeOffset.UnixEpoch, FileItemFlags.None);

    private static FileItem Directory(string name)
        => new(name, Loc($@"C:\Temp\{name}"), 0, DateTimeOffset.UnixEpoch, FileItemFlags.Directory);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
