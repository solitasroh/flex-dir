using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.Core.Tests.Watching;

/// <summary>
/// <see cref="FolderChange"/> 는 값이다 — 감시 구현체가 낸 알림을 그대로 나른다.
/// <para>
/// 여기서 못을 박는 것은 두 가지다. (1) <see cref="FolderChangeKind.Overflow"/> 가
/// 별도 종류로 존재하고 이름을 담지 않는다 — 유실된 이벤트를 개별 변경으로 흉내내면
/// 목록이 파일시스템과 어긋난 채 남는다 (docs/SHELL_NOTES.md §폴더 감시).
/// (2) 값 동등성 — 소비자(ViewModel)가 같은 변경을 접을 때 쓴다. 디바운스·병합은
/// 소비자의 정책이므로 포트가 아니라 이 동등성 위에서 이뤄진다.
/// </para>
/// </summary>
public class FolderChangeTests
{
    [Fact]
    public void Overflowed_IsAnOverflowWithoutNames()
    {
        var change = FolderChange.Overflowed;

        Assert.Equal(FolderChangeKind.Overflow, change.Kind);

        // 이름이 없다. 무엇이 바뀌었는지 모르는 것이 오버플로의 정의다 —
        // 소비자는 전체 새로고침으로 폴백한다.
        Assert.Equal(string.Empty, change.Name);
        Assert.Equal(string.Empty, change.OldName);
    }

    [Fact]
    public void Overflow_IsNotAnOrdinaryChange()
    {
        // Changed 로 뭉개면 소비자가 한 항목만 다시 읽고 끝낸다.
        Assert.NotEqual(FolderChangeKind.Changed, FolderChange.Overflowed.Kind);
    }

    [Fact]
    public void OldName_IsAbsentUnlessGiven()
    {
        Assert.Null(new FolderChange(FolderChangeKind.Added, "a.txt").OldName);
    }

    [Fact]
    public void Renamed_CarriesBothNames()
    {
        var change = new FolderChange(FolderChangeKind.Renamed, "new.txt", "old.txt");

        Assert.Equal("new.txt", change.Name);
        Assert.Equal("old.txt", change.OldName);
    }

    [Fact]
    public void SameKindAndName_AreEqual()
    {
        Assert.Equal(
            new FolderChange(FolderChangeKind.Added, "a.txt"),
            new FolderChange(FolderChangeKind.Added, "a.txt"));

        Assert.NotEqual(
            new FolderChange(FolderChangeKind.Added, "a.txt"),
            new FolderChange(FolderChangeKind.Changed, "a.txt"));

        Assert.NotEqual(
            new FolderChange(FolderChangeKind.Added, "a.txt"),
            new FolderChange(FolderChangeKind.Added, "b.txt"));
    }
}
