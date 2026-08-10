using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Shell.Settings;

using Xunit;

namespace FlexDir.Shell.Tests.Settings;

/// <summary>
/// 설정을 파일 하나에 담는 <see cref="JsonSettingsStore"/>.
/// <para>
/// 뷰 상태와 <b>다른 파일</b>이다 (docs/PRD-v2.md §12) — 뷰 상태가 깨져서 기본값으로 접히는
/// 사건이 "숨김 파일을 보겠다"·"여기서 시작하겠다" 를 함께 뒤집으면 안 된다.
/// 즐겨찾기(<c>JsonFavoriteStore</c>)와 같은 자리이고 실패를 다루는 방식도 같다.
/// </para>
/// </summary>
public class JsonSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flexdir-set-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private JsonSettingsStore CreateStore() => new(directory);

    private string FilePath => Path.Combine(directory, JsonSettingsStore.FileName);

    private void WriteRawFile(string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, text);
    }

    private static LocationId Loc(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    [Fact]
    public async Task LoadAsync_WithNoFile_IsDefault()
    {
        // 처음 켠 사람에게는 파일이 없다. 오류가 아니다.
        Assert.Equal(AppSettings.Default, await CreateStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SavedSettings_AreLoadedBack()
    {
        var store = CreateStore();
        var settings = new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Loc(@"C:\work"),
            ShowHiddenItems = true,
        };

        await store.SaveAsync(settings, CancellationToken.None);

        Assert.Equal(settings, await CreateStore().LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_CreatesTheDirectory()
    {
        await CreateStore().SaveAsync(AppSettings.Default, CancellationToken.None);

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTemporaryFileBehind()
    {
        // 제자리에서 덮어쓰지 않고 임시 파일에 쓴 뒤 옮긴다 — 쓰다 죽었을 때 반쪽 파일이
        // 정본이 되면 안 된다. 옮기고 나면 임시 파일은 남지 않는다.
        await CreateStore().SaveAsync(AppSettings.Default, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SavedFile_IsReadableByAPerson()
    {
        // 되찾는 마지막 수단이 사람이 열어 고치는 것이다. 경로도 표시형으로 남긴다.
        await CreateStore().SaveAsync(
            new AppSettings { StartMode = StartFolderMode.Fixed, StartFolder = Loc(@"C:\work") },
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(FilePath);

        Assert.Contains(@"C:\\work", text, StringComparison.Ordinal);
        Assert.Contains("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrokenFile_IsQuarantinedInsteadOfOverwritten()
    {
        // 빈 설정으로 읽고 다음 저장이 덮어쓰면 정해 둔 것이 영영 사라진다. 즐겨찾기와 같은
        // 판단이다 — 설정도 파일시스템 어디에도 없는 사용자 데이터다.
        WriteRawFile("{ 이건 JSON 이 아니다");

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.Default, loaded);
        Assert.True(File.Exists(FilePath + ".bak"));
    }

    [Fact]
    public async Task BrokenStartFolder_DoesNotThrowAwayTheRest()
    {
        // 손으로 고치다 경로 하나가 깨졌다고 숨김 설정까지 잃지 않는다. 파싱되지 않는
        // 경로는 '정하지 않음' 으로 읽고, 그러면 시작 폴더 규칙이 마지막 폴더로 되돌아간다.
        WriteRawFile("""
            {
              "startMode": "Fixed",
              "startFolder": "||이건 경로가 아니다||",
              "showHiddenItems": true
            }
            """);

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(StartFolderMode.Fixed, loaded.StartMode);
        Assert.Null(loaded.StartFolder);
        Assert.True(loaded.ShowHiddenItems);
        Assert.False(File.Exists(FilePath + ".bak"));   // 깨진 파일이 아니다. 격리 대상이 아니다
    }

    [Fact]
    public async Task UnknownStartMode_FallsBackToTheLastFolder()
    {
        // 손으로 고치다 오타가 났거나 옛 버전이 남긴 값이다. 던지면 설정 전체를 잃는다.
        WriteRawFile("""{ "startMode": "Blah", "showHiddenItems": true }""");

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(StartFolderMode.LastFolder, loaded.StartMode);
        Assert.True(loaded.ShowHiddenItems);
    }

    [Fact]
    public async Task MissingFields_TakeTheDefaults()
    {
        // 설정이 늘어나도 옛 파일이 그대로 읽혀야 한다 (FolderViewState 의 GroupBy·Collapsed 와
        // 같은 자리다).
        WriteRawFile("{}");

        Assert.Equal(AppSettings.Default, await CreateStore().LoadAsync(CancellationToken.None));
    }
}
