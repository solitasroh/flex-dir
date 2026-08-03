using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.Core.Tests.Model;

/// <summary>
/// 비트 값은 docs/SHELL_NOTES.md §열거 함정 3 을 가리킨다.
/// 자리표시자 비트는 <c>FILE_ATTRIBUTE_OFFLINE</c> 하나가 아니라 세 개다.
/// </summary>
public class FileAttributeMappingTests
{
    [Theory]
    [InlineData(0x00000010u, FileItemFlags.Directory)]
    [InlineData(0x00000002u, FileItemFlags.Hidden)]
    [InlineData(0x00000004u, FileItemFlags.System)]
    [InlineData(0x00000400u, FileItemFlags.ReparsePoint)]
    [InlineData(0x00001000u, FileItemFlags.Offline)]
    [InlineData(0x00400000u, FileItemFlags.CloudPlaceholder)]  // RECALL_ON_DATA_ACCESS
    [InlineData(0x00040000u, FileItemFlags.CloudPlaceholder)]  // RECALL_ON_OPEN
    public void SingleBit_MapsToSingleFlag(uint attributes, FileItemFlags expected)
    {
        Assert.Equal(expected, FileAttributeMapping.FromWin32Attributes(attributes));
    }

    [Fact]
    public void NoBits_MapToNone()
    {
        Assert.Equal(FileItemFlags.None, FileAttributeMapping.FromWin32Attributes(0u));
    }

    [Fact]
    public void UnmappedBits_AreIgnored()
    {
        // ARCHIVE(0x20) · NORMAL(0x80) · NOT_CONTENT_INDEXED(0x2000) 은 목록에 쓰지 않는다.
        Assert.Equal(FileItemFlags.None, FileAttributeMapping.FromWin32Attributes(0x20u | 0x80u | 0x2000u));
    }

    [Fact]
    public void BothRecallBits_SetCloudPlaceholderOnce()
    {
        Assert.Equal(
            FileItemFlags.CloudPlaceholder,
            FileAttributeMapping.FromWin32Attributes(0x00400000u | 0x00040000u));
    }

    [Fact]
    public void CombinedBits_MapEveryFlag()
    {
        var flags = FileAttributeMapping.FromWin32Attributes(
            0x00000010u | 0x00000002u | 0x00000004u | 0x00000400u | 0x00001000u | 0x00040000u);

        Assert.Equal(
            FileItemFlags.Directory | FileItemFlags.Hidden | FileItemFlags.System
                | FileItemFlags.ReparsePoint | FileItemFlags.Offline | FileItemFlags.CloudPlaceholder,
            flags);
    }
}
