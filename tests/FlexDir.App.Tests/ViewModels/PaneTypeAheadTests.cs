using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// type-ahead — 문자 키로 그 이름에 점프한다 (docs/DESIGN.md §9).
/// <para>
/// 리셋 판정은 주입한 <see cref="TimeProvider"/> 로만 한다. 시한 안의 연속 입력은
/// 접두어로 쌓이고(0.5초 뒤 문자), 시한을 넘기면 새 검색이다(1.5초 뒤 문자).
/// </para>
/// <para>
/// 표시 순서는 이름 자연 정렬이다: alpha · banana · beta · bravo · cherry.
/// </para>
/// </summary>
public class PaneTypeAheadTests
{
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly FakeThumbnailSource thumbnails = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly FakeContextMenuProvider contextMenus = new();
    private readonly InlineUiDispatcher dispatcher = new();
    private readonly ManualTimeProvider clock = new();

    [Fact]
    public async Task TypeAhead_JumpsToTheFirstMatchAndSelectsIt()
    {
        var pane = await OpenAsync();

        pane.TypeAhead('b');

        Assert.Equal("banana.txt", pane.FocusedName);
        Assert.Equal(["banana.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task TypeAhead_HalfASecondLater_ExtendsThePrefix()
    {
        var pane = await OpenAsync();
        pane.TypeAhead('b');

        clock.Advance(TimeSpan.FromSeconds(0.5));
        pane.TypeAhead('r');

        Assert.Equal("bravo.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_WhilePrefixing_StaysPutWhenTheCurrentItemStillMatches()
    {
        var pane = await OpenAsync();
        pane.TypeAhead('b');

        clock.Advance(TimeSpan.FromSeconds(0.5));
        pane.TypeAhead('a');

        // "ba" 는 banana 가 그대로 일치한다 — 접두어를 넓히는 중에는 제자리다.
        Assert.Equal("banana.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_AfterTheReset_StartsANewSearch()
    {
        var pane = await OpenAsync();
        pane.TypeAhead('b');

        clock.Advance(TimeSpan.FromSeconds(1.5));
        pane.TypeAhead('a');

        // 접두어가 "ba" 가 아니라 "a" 다 — 포커스 다음부터 돌아 alpha 로 감는다.
        Assert.Equal("alpha.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_RepeatingTheSameCharacter_CyclesThroughTheMatches()
    {
        var pane = await OpenAsync();

        pane.TypeAhead('b');
        Assert.Equal("banana.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(1.5));
        pane.TypeAhead('b');
        Assert.Equal("beta.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(1.5));
        pane.TypeAhead('b');
        Assert.Equal("bravo.txt", pane.FocusedName);

        // 끝까지 갔으면 처음으로 감는다.
        clock.Advance(TimeSpan.FromSeconds(1.5));
        pane.TypeAhead('b');
        Assert.Equal("banana.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_RepeatingTheSameCharacterQuickly_AlsoCycles()
    {
        var pane = await OpenAsync();

        // 사람은 1초를 세고 다시 누르지 않는다 — 연타가 기본이다.
        pane.TypeAhead('b');
        Assert.Equal("banana.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('b');
        Assert.Equal("beta.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('b');
        Assert.Equal("bravo.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('b');
        Assert.Equal("banana.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_RepeatingTheSameCharacter_PrefersARealPrefixMatch()
    {
        var pane = await OpenWithAsync("alpha.txt", "banana.txt", "bb.txt");

        pane.TypeAhead('b');
        Assert.Equal("banana.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('b');

        // "bb" 가 실제로 있으면 순환보다 접두어가 이긴다.
        Assert.Equal("bb.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_AfterCyclingOnARepeat_TakesTheNextCharacterAsTheSecondLetter()
    {
        var pane = await OpenWithAsync("alpha.txt", "banana.txt", "beta.txt");

        pane.TypeAhead('b');
        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('b');
        Assert.Equal("beta.txt", pane.FocusedName);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        pane.TypeAhead('a');

        // 순환한 뒤의 접두어는 "bb" 가 아니라 "b" 다 — 이어지는 문자는 "ba" 를 만든다.
        Assert.Equal("banana.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_IsCaseInsensitive()
    {
        var pane = await OpenAsync();

        pane.TypeAhead('B');

        Assert.Equal("banana.txt", pane.FocusedName);
    }

    [Fact]
    public async Task TypeAhead_WithNoMatch_DoesNotMove()
    {
        var pane = await OpenAsync();
        pane.TypeAhead('b');

        clock.Advance(TimeSpan.FromSeconds(0.5));
        pane.TypeAhead('z');

        // "bz" 는 아무것도 아니다. 포커스도 선택도 그대로다.
        Assert.Equal("banana.txt", pane.FocusedName);
        Assert.Equal(["banana.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public void TypeAhead_OnAnEmptyList_DoesNothing()
    {
        var pane = CreatePane();

        pane.TypeAhead('a');

        Assert.Null(pane.FocusedName);
        Assert.Equal(0, pane.Selection.Count);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private Task<PaneViewModel> OpenAsync()
        => OpenWithAsync("alpha.txt", "banana.txt", "beta.txt", "bravo.txt", "cherry.txt");

    private async Task<PaneViewModel> OpenWithAsync(params string[] names)
    {
        Assert.True(LocationId.TryParse(@"C:\Temp", out var folder, out var error), $"파싱 실패: {error}");

        source.Folders[folder] = [.. names
            .Select(name => new FileItem(
                name,
                folder.Combine(name),
                1024,
                DateTimeOffset.UnixEpoch,
                FileItemFlags.None))];

        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        return pane;
    }

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus, clock);
}
