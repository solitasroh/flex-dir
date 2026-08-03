using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.Core.Tests.Model;

/// <summary>
/// 확장자 규칙은 docs/SHELL_NOTES.md §열거 함정 4,
/// 클라우드 자리표시자는 같은 절 함정 3 을 가리킨다.
/// </summary>
public class FileItemTests
{
    private static readonly DateTimeOffset Modified = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    // ── 함정 4: 선행 '.' 은 확장자 구분자가 아니다 ────────────────────

    [Fact]
    public void DotFile_HasNoExtension()
    {
        var item = MakeFile(".bashrc");

        Assert.Equal("", item.Extension);
        Assert.Equal(".bashrc", item.NameWithoutExtension);
    }

    [Fact]
    public void MultipleDots_TakeLastSegmentOnly()
    {
        var item = MakeFile("report.tar.gz");

        Assert.Equal("gz", item.Extension);
        Assert.Equal("report.tar", item.NameWithoutExtension);
    }

    [Fact]
    public void NoDot_HasNoExtension()
    {
        var item = MakeFile("README");

        Assert.Equal("", item.Extension);
        Assert.Equal("README", item.NameWithoutExtension);
    }

    // ── 확장자는 소문자로 정규화한다 ─────────────────────────────────
    // 유형 컬럼과 아이콘 캐시가 확장자를 키로 쓰므로 .JPG 와 .jpg 가 갈라지면 안 된다.

    [Fact]
    public void Extension_IsNormalizedToLowerCase()
    {
        Assert.Equal("jpg", MakeFile("PHOTO.JPG").Extension);
    }

    [Fact]
    public void NameWithoutExtension_KeepsOriginalCase()
    {
        Assert.Equal("PHOTO", MakeFile("PHOTO.JPG").NameWithoutExtension);
    }

    // ── 디렉터리는 확장자가 없다 ─────────────────────────────────────

    [Fact]
    public void Directory_HasNoExtension_EvenWhenNameContainsDot()
    {
        var item = MakeItem("My.Folder", FileItemFlags.Directory);

        Assert.True(item.IsDirectory);
        Assert.Equal("", item.Extension);
        Assert.Equal("My.Folder", item.NameWithoutExtension);
    }

    [Fact]
    public void File_IsNotDirectory()
    {
        Assert.False(MakeFile("a.txt").IsDirectory);
    }

    // ── 함정 3: 내용을 건드리면 다운로드가 트리거되는 항목 ──────────────

    [Theory]
    [InlineData(FileItemFlags.Offline)]
    [InlineData(FileItemFlags.CloudPlaceholder)]
    [InlineData(FileItemFlags.Offline | FileItemFlags.CloudPlaceholder)]
    public void CloudFlags_MakeContentAccessRisky(FileItemFlags flags)
    {
        Assert.True(MakeItem("photo.jpg", flags).IsContentAccessRisky);
    }

    [Theory]
    [InlineData(FileItemFlags.None)]
    [InlineData(FileItemFlags.Hidden | FileItemFlags.System | FileItemFlags.ReparsePoint | FileItemFlags.Directory)]
    public void OtherFlags_AreNotContentAccessRisky(FileItemFlags flags)
    {
        Assert.False(MakeItem("photo.jpg", flags).IsContentAccessRisky);
    }

    // ── 넘겨받은 값은 그대로 노출한다 ────────────────────────────────

    [Fact]
    public void Item_ExposesGivenValues()
    {
        var location = Parse(@"C:\Temp\photo.jpg");
        var item = new FileItem("photo.jpg", location, 1234L, Modified, FileItemFlags.Hidden);

        Assert.Equal("photo.jpg", item.Name);
        Assert.Equal(location, item.Location);
        Assert.Equal(1234L, item.Size);
        Assert.Equal(Modified, item.ModifiedUtc);
        Assert.Equal(FileItemFlags.Hidden, item.Flags);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    private static FileItem MakeFile(string name) => MakeItem(name, FileItemFlags.None);

    private static FileItem MakeItem(string name, FileItemFlags flags)
        => new(name, Parse(@"C:\Temp").Combine(name), 0L, Modified, flags);

    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out var error), $"파싱 실패: {error}");
        return location!;
    }
}
