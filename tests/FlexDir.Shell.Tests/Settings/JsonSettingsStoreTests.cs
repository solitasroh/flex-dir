using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Core.Tools;
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
            Theme = ThemeMode.Dark,
        };

        await store.SaveAsync(settings, CancellationToken.None);

        Assert.Equal(settings, await CreateStore().LoadAsync(CancellationToken.None));
    }

    // ── 테마 (docs/PRD-v2.md §19) ────────────────────────────────
    //
    // **이 저장소는 저장 형식을 Core 타입과 분리한다** (아래 Document record). 그래서
    // AppSettings 에 항목을 늘리는 것만으로는 저장되지 않는다 — 2026-08-12 에 실물에서
    // 그것을 밟았다: 다크를 골라도 settings.json 에 아무것도 안 남고, 파일에 손으로
    // theme 을 써도 안 읽혔다. FakeSettingsStore 는 AppSettings 를 객체째로 들고 있어
    // 무엇을 늘려도 통과하므로, **새 설정은 이 파일에서 라운드트립을 봐야 한다.**

    [Fact]
    public async Task SavedTheme_SurvivesARoundTrip()
    {
        await CreateStore().SaveAsync(
            new AppSettings { Theme = ThemeMode.Dark }, CancellationToken.None);

        Assert.Equal(ThemeMode.Dark, (await CreateStore().LoadAsync(CancellationToken.None)).Theme);
    }

    [Fact]
    public async Task SavedTheme_IsWrittenAsAReadableName()
    {
        // 사람이 열어 고칠 수 있어야 한다 — 숫자 2 로 남으면 그러지 못한다.
        await CreateStore().SaveAsync(
            new AppSettings { Theme = ThemeMode.Dark }, CancellationToken.None);

        Assert.Contains("\"theme\": \"Dark\"", await File.ReadAllTextAsync(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownTheme_FallsBackToTheSystemTheme()
    {
        // 손으로 고치다 오타가 났거나 옛/새 버전이 남긴 값이다. 던지면 설정 전체를 잃는다.
        WriteRawFile("""{ "theme": "Blah", "showHiddenItems": true }""");

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(ThemeMode.System, loaded.Theme);
        Assert.True(loaded.ShowHiddenItems);
    }

    [Fact]
    public async Task NoThemeField_TakesTheSystemTheme()
    {
        // v0.6.1 이전 파일이다 — 그 형식에는 theme 이 없다.
        WriteRawFile("""{ "startMode": "Fixed", "showHiddenItems": true }""");

        Assert.Equal(ThemeMode.System, (await CreateStore().LoadAsync(CancellationToken.None)).Theme);
    }

    // ── 터미널 선택 (docs/PRD-v2.md §20) ─────────────────────────
    //
    // 바로 위 §테마 주석이 적어 둔 그 자리다. 아래 왕복은 전부 **새 인스턴스**로 읽는다 —
    // 같은 인스턴스로 다시 읽으면 메모리를 읽게 되어 Document·SaveAsync·Parse 중 무엇을
    // 빠뜨려도 통과한다. 그것이 2026-08-12 에 다크모드가 새어 나간 방식이다.

    [Fact]
    public async Task SavedCustomTerminal_SurvivesARoundTrip()
    {
        var settings = new AppSettings
        {
            TerminalPreset = TerminalPreset.Custom,
            TerminalExecutable = "wezterm-gui.exe",
            TerminalArguments = "start --cwd \"{path}\"",
        };

        await CreateStore().SaveAsync(settings, CancellationToken.None);

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(TerminalPreset.Custom, loaded.TerminalPreset);
        Assert.Equal("wezterm-gui.exe", loaded.TerminalExecutable);
        Assert.Equal("start --cwd \"{path}\"", loaded.TerminalArguments);
    }

    [Fact]
    public async Task EveryPreset_SurvivesARoundTrip()
    {
        // 열거형을 돌려 확인한다 — 프리셋을 늘리고 저장을 빠뜨리면 여기서 잡힌다.
        foreach (var preset in Enum.GetValues<TerminalPreset>())
        {
            await CreateStore().SaveAsync(
                new AppSettings { TerminalPreset = preset }, CancellationToken.None);

            Assert.Equal(preset, (await CreateStore().LoadAsync(CancellationToken.None)).TerminalPreset);
        }
    }

    [Fact]
    public async Task SavedPreset_IsWrittenAsAReadableName()
    {
        // 숫자로 남기면 열거형 중간에 값이 끼는 날 저장 파일이 조용히 다른 뜻이 된다.
        await CreateStore().SaveAsync(
            new AppSettings { TerminalPreset = TerminalPreset.WindowsTerminal }, CancellationToken.None);

        Assert.Contains(
            "\"terminalPreset\": \"WindowsTerminal\"",
            await File.ReadAllTextAsync(FilePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPreset_FallsBackToWindowsTerminal()
    {
        // 손으로 고치다 오타가 났거나 옛/새 버전이 남긴 값이다. 던지면 설정 전체를 잃는다.
        WriteRawFile("""{ "terminalPreset": "Nonsense", "theme": "Dark", "showHiddenItems": true }""");

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(TerminalPreset.WindowsTerminal, loaded.TerminalPreset);
        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.True(loaded.ShowHiddenItems);
    }

    [Fact]
    public async Task AFileFromBeforeTerminalSettings_TakesTheDefaults()
    {
        // v0.8.x 가 실제로 남기던 형식이다 — 그 시절에는 터미널 항목이 없었다.
        WriteRawFile("""
            {
              "startMode": "Fixed",
              "startFolder": "C:\\work",
              "showHiddenItems": true,
              "theme": "Dark"
            }
            """);

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Equal(TerminalPreset.WindowsTerminal, loaded.TerminalPreset);
        Assert.Null(loaded.TerminalExecutable);
        Assert.Null(loaded.TerminalArguments);

        Assert.Equal(StartFolderMode.Fixed, loaded.StartMode);
        Assert.Equal(Loc(@"C:\work"), loaded.StartFolder);
        Assert.True(loaded.ShowHiddenItems);
        Assert.Equal(ThemeMode.Dark, loaded.Theme);
    }

    [Fact]
    public async Task BlankCustomExecutable_IsStoredAsNothing()
    {
        // 설정 창의 텍스트 상자를 비우면 빈 문자열이 온다. "" 와 null 을 다르게 다루면
        // '적지 않았다' 를 판정하는 자리마다 조건이 둘씩 는다.
        await CreateStore().SaveAsync(
            new AppSettings
            {
                TerminalPreset = TerminalPreset.Custom,
                TerminalExecutable = string.Empty,
                TerminalArguments = "   ",
            },
            CancellationToken.None);

        var loaded = await CreateStore().LoadAsync(CancellationToken.None);

        Assert.Null(loaded.TerminalExecutable);
        Assert.Null(loaded.TerminalArguments);
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
