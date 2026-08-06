using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using FlexDir.Host.Diagnostics;

using Xunit;

namespace FlexDir.Host.Tests.Diagnostics;

/// <summary>
/// 폴더 전환부터 첫 항목이 목록에 붙을 때까지를 재는 관찰자 (phase B-2 ·
/// <c>FirstItem</c> 예산 150ms). 페인 밖에서 잰다 — 계측 파일은 Host 의 것이고
/// (docs/ARCHITECTURE.md §7), App 이 그것을 알면 참조 방향이 뒤집힌다.
/// <para>
/// 전부 <see cref="Path.GetTempPath"/> 아래에서 돈다. 실제
/// <c>%LOCALAPPDATA%\flex-dir\</c> 를 건드리면 게이트가 자기 테스트 실행을 계측으로 센다.
/// </para>
/// </summary>
public class FirstItemMeterTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-meter-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly ManualTimeProvider clock = new();

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
            // 정리 실패로 테스트를 실패로 만들지 않는다.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OpeningAFolder_RecordsFirstItem()
    {
        var pane = CreatePane();
        using var meter = new FirstItemMeter(pane, new PerformanceLog(root), clock);

        await pane.NavigateAsync(Folder(@"C:\Temp", "a.txt"));
        await meter.Recording;

        Assert.EndsWith("\tFirstItem\t0\t150\tok", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyFolder_RecordsNothing()
    {
        // 첫 항목이 없는 폴더에는 잴 것이 없다. 0ms 로 남기면 평균이 좋아 보이게 거짓말한다.
        var pane = CreatePane();
        using var meter = new FirstItemMeter(pane, new PerformanceLog(root), clock);

        await pane.NavigateAsync(Folder(@"C:\Empty"));
        await meter.Recording;

        Assert.Empty(Lines());
    }

    [Fact]
    public async Task AWatcherUpdateAfterAnEmptyOpen_IsNotAFirstItem()
    {
        // 빈 폴더를 연 지 한참 뒤에 파일이 생기면 감시가 항목을 붙인다. 그것을 "첫 항목
        // 도착" 으로 재면 폴더 전환과 무관한 분 단위 수치가 perf.log 에 남는다.
        var pane = CreatePane();
        using var meter = new FirstItemMeter(pane, new PerformanceLog(root), clock);

        await pane.NavigateAsync(Folder(@"C:\Empty"));
        clock.Advance(TimeSpan.FromMinutes(5));
        pane.Items.Add(Row(@"C:\Empty", "late.txt"));
        await meter.Recording;

        Assert.Empty(Lines());
    }

    [Fact]
    public async Task EveryFolderChange_IsMeasured()
    {
        var pane = CreatePane();
        using var meter = new FirstItemMeter(pane, new PerformanceLog(root), clock);

        await pane.NavigateAsync(Folder(@"C:\A", "a.txt"));
        await pane.NavigateAsync(Folder(@"C:\B", "b.txt"));
        await meter.Recording;

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public async Task AfterDispose_RecordsNothing()
    {
        var pane = CreatePane();
        var meter = new FirstItemMeter(pane, new PerformanceLog(root), clock);
        meter.Dispose();

        await pane.NavigateAsync(Folder(@"C:\Temp", "a.txt"));
        await meter.Recording;

        Assert.Empty(Lines());
    }

    [Fact]
    public void Arguments_AreInvalid_Throw()
    {
        var pane = CreatePane();
        var log = new PerformanceLog(root);

        Assert.Throws<ArgumentNullException>(() => new FirstItemMeter(null!, log, clock));
        Assert.Throws<ArgumentNullException>(() => new FirstItemMeter(pane, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new FirstItemMeter(pane, log, null!));
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private PaneViewModel CreatePane() => new(
        source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator,
        dispatcher, CultureInfo.InvariantCulture, TimeZoneInfo.Utc, new FakeContextMenuProvider());

    private LocationId Folder(string path, params string[] names)
    {
        Assert.True(LocationId.TryParse(path, out var folder, out var error), $"파싱 실패: {error}");
        source.Folders[folder] =
            [.. names.Select(name => new FileItem(
                name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None))];

        return folder;
    }

    /// <summary>감시 갱신이 붙이는 행. 페인을 거치지 않고 목록에 직접 넣어 시점을 정한다.</summary>
    private static FileItemViewModel Row(string path, string name)
    {
        Assert.True(LocationId.TryParse(path, out var folder, out var error), $"파싱 실패: {error}");

        return new FileItemViewModel(
            new FileItem(name, folder.Combine(name), 1024, DateTimeOffset.UnixEpoch, FileItemFlags.None),
            "1 KB", "2026-08-05", "파일");
    }

    private string[] Lines()
    {
        var path = Path.Combine(root, PerformanceLog.FileName);

        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }
}
