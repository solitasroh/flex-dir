using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Enumeration;
using FlexDir.Shell.Enumeration;

using Xunit;

namespace FlexDir.Shell.Tests.Enumeration;

/// <summary>
/// 실제 파일시스템을 읽는 <see cref="IFolderSource"/> 구현체.
/// <para>
/// <see cref="FolderSourceContract"/> 를 상속해 fake 가 받던 검증을 실물도 받는다.
/// 아래에 더 붙는 것은 <b>파일시스템이 실제로 무엇을 주는가</b>다 — 숨김 파일이 사라지지
/// 않는가, 크기·수정시각이 실물에서 오는가, 하위 폴더를 끌어오지 않는가.
/// </para>
/// <para>
/// 자동으로 재지 못하는 것 둘은 <c>.harness/manual-plan.md</c> 에 사람 확인 항목으로 있다 —
/// 권한 없는 폴더(관리자 권한 없이 만들 수 없다)와 클라우드 자리표시자(OneDrive 가 필요하다).
/// </para>
/// </summary>
public sealed class FileSystemFolderSourceTests : FolderSourceContract, IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "flex-dir-tests", Guid.NewGuid().ToString("N"));

    public FileSystemFolderSourceTests() => Directory.CreateDirectory(root);

    protected override LocationId SourceFolder => Folder(root);

    protected override IFolderSource CreateSource(LocationId folder, IReadOnlyList<FileItem> items)
    {
        foreach (var item in items)
        {
            File.WriteAllText(Path.Combine(root, item.Name), string.Empty);
        }

        return new FileSystemFolderSource();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 임시 폴더 청소 실패는 테스트 결과와 무관하다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── 파일시스템이 실제로 주는 것 ─────────────────────────────────

    // EnumerationOptions 의 기본 AttributesToSkip 은 Hidden|System 이다. 그대로 두면
    // 숨김 파일이 목록에서 조용히 사라진다 — 탐색기는 설정에 따라 보여주므로 "보여줄지"
    // 는 UI 정책이고, 열거는 있는 것을 다 내야 한다.
    [Fact]
    public async Task HiddenFile_IsYielded_WithTheHiddenFlag()
    {
        var path = Path.Combine(root, "secret.txt");
        File.WriteAllText(path, string.Empty);
        File.SetAttributes(path, FileAttributes.Hidden);

        var item = await SingleAsync("secret.txt");

        Assert.True(item.Flags.HasFlag(FileItemFlags.Hidden));
    }

    [Fact]
    public async Task Directory_IsYielded_WithTheDirectoryFlag()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var item = await SingleAsync("sub");

        Assert.True(item.IsDirectory);

        // 폴더 크기는 계산하지 않는다 (docs/PRD.md §3 — v1 범위 밖).
        Assert.Equal(0, item.Size);
        Assert.Equal(string.Empty, item.Extension);
    }

    [Fact]
    public async Task FileSizeAndModifiedTime_ComeFromTheFileSystem()
    {
        var path = Path.Combine(root, "data.bin");
        var written = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        await File.WriteAllBytesAsync(path, new byte[1234]);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);

        var item = await SingleAsync("data.bin");

        Assert.Equal(1234, item.Size);
        Assert.Equal(written, item.ModifiedUtc);
    }

    // 목록은 폴더 하나를 보여준다. 하위 트리를 끌어오면 10만 항목 폴더에서 첫 항목까지
    // 150ms (docs/PRD.md §5) 는 불가능해진다.
    [Fact]
    public async Task SubdirectoryContents_AreNotYielded()
    {
        var nested = Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(nested.FullName, "buried.txt"), string.Empty);

        var names = await NamesAsync();

        Assert.Equal(["sub"], names);
    }

    [Fact]
    public async Task ItemLocation_IsTheFolderCombinedWithTheName()
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), string.Empty);

        var item = await SingleAsync("a.txt");

        Assert.Equal(SourceFolder.Combine("a.txt"), item.Location);
    }

    // 대용량 폴더에서 항목을 잃지 않는지. 열거는 워커에서 돌고 채널로 넘어오므로
    // 경계(버퍼 크기)에서 항목이 새는 실수가 있으면 여기서 드러난다.
    [Fact]
    public async Task ManyItems_AreAllYielded()
    {
        for (var i = 0; i < 2000; i++)
        {
            File.WriteAllText(Path.Combine(root, $"f{i:D4}.txt"), string.Empty);
        }

        Assert.Equal(2000, (await NamesAsync()).Count);
    }

    // 열거자 생성자가 실패하는 경로에서는 잡아둔 Win32 코드에 닿을 수 없어 HResult 에서
    // 꺼낸다. 그 추출이 조용히 0 을 내면 분류는 타입 폴백으로 맞아도 진단이 사라진다 —
    // 실제로 부호 확장 때문에 그런 적이 있고, 테스트는 분류만 봐서 놓쳤다.
    // 파일을 폴더로 열거하면 ERROR_DIRECTORY(267) 가 결정적으로 나온다.
    [Fact]
    public async Task EnumeratingAFile_KeepsTheWin32Code()
    {
        var path = Path.Combine(root, "not-a-folder.txt");
        File.WriteAllText(path, string.Empty);

        var error = await Assert.ThrowsAsync<LocationAccessException>(async () =>
        {
            await foreach (var _ in new FileSystemFolderSource().EnumerateAsync(Folder(path), CancellationToken.None))
            {
            }
        });

        Assert.NotEqual(0, error.Win32Error);
        Assert.NotEqual(LocationErrorKind.None, error.Kind);
    }

    // ── 단건 조회 ───────────────────────────────────────────────────

    [Fact]
    public async Task TryGetItem_Directory_CarriesTheDirectoryFlag()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var item = await new FileSystemFolderSource()
            .TryGetItemAsync(SourceFolder.Combine("sub"), CancellationToken.None);

        Assert.NotNull(item);
        Assert.True(item.IsDirectory);
        Assert.Equal("sub", item.Name);
    }

    [Fact]
    public async Task TryGetItem_File_CarriesSizeAndTime()
    {
        var path = Path.Combine(root, "data.bin");
        await File.WriteAllBytesAsync(path, new byte[77]);

        var item = await new FileSystemFolderSource()
            .TryGetItemAsync(SourceFolder.Combine("data.bin"), CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal(77, item.Size);
        Assert.NotEqual(default, item.ModifiedUtc);
    }

    // 감시 알림과 실제 상태가 어긋나는 것은 정상이다 (CLAUDE.md §4). 폴더가 통째로
    // 사라진 뒤에 오는 알림도 마찬가지이며, 그때 예외가 나면 갱신 경로가 오류가 된다.
    [Fact]
    public async Task TryGetItem_InsideAMissingFolder_ReturnsNull()
    {
        var item = await new FileSystemFolderSource()
            .TryGetItemAsync(SourceFolder.Combine("gone").Combine("deeper.txt"), CancellationToken.None);

        Assert.Null(item);
    }

    private async Task<FileItem> SingleAsync(string name)
    {
        await foreach (var item in new FileSystemFolderSource().EnumerateAsync(SourceFolder, CancellationToken.None))
        {
            if (item.Name.Equals(name, StringComparison.Ordinal))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"'{name}' 이 열거되지 않았다.");
    }

    private async Task<List<string>> NamesAsync()
    {
        var names = new List<string>();

        await foreach (var item in new FileSystemFolderSource().EnumerateAsync(SourceFolder, CancellationToken.None))
        {
            names.Add(item.Name);
        }

        return names;
    }
}
