using System.Globalization;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 분할 — 1~4 페인 (docs/PRD-v2.md §18 · 사용자 결정 2026-08-12).
/// <para>
/// <b>1분할이 기본화면이다.</b> 그래서 이 파일은 다른 워크스페이스 테스트와 달리 저장소를
/// 2분할로 미리 채우지 않는다 (<c>WorkspaceTestSupport.RememberingTwoPanes</c>) — 여기서
/// 재는 것이 바로 그 기본값과 늘리고 줄이는 일이다.
/// </para>
/// <para>
/// 화면에 실제로 그려지는 배치는 <c>Views/SplitLayoutTests</c> 가 본다. 여기는 소유 구조 —
/// <b>접기는 닫기가 아니다</b>, <b>'다른 페인' 은 직전에 활성이던 페인이다</b>, 그리고
/// <b>분할 상태는 기억된다</b> 셋이 축이다.
/// </para>
/// </summary>
public class WorkspaceSplitTests
{
    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

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

    // ── 기본화면 (사용자 결정 2026-08-12) ──────────────────────────

    [Fact]
    public void New_IsOnePane()
    {
        var workspace = CreateWorkspace();

        Assert.Equal(1, workspace.SplitCount);
        Assert.Single(workspace.Panes);
        Assert.Same(workspace.Panes[0], workspace.ActivePane);
    }

    [Fact]
    public void New_HasNoOtherPane()
    {
        // 1분할에는 갈 곳이 없다 — 자기 자신을 내면 '반대편으로 복사' 가 제자리 복사가 되고,
        // 그것은 조용히 파일을 부르는 일이다.
        var workspace = CreateWorkspace();

        Assert.Null(workspace.OtherPane);
        Assert.Null(workspace.OtherTab);
    }

    [Fact]
    public void New_CannotClosePaneButCanSplit()
    {
        var workspace = CreateWorkspace();

        Assert.False(workspace.CanClosePane);
        Assert.True(workspace.CanSplit);
    }

    [Fact]
    public async Task RestoreAsync_WithoutMemory_StaysAtOnePane()
    {
        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(1, workspace.SplitCount);
    }

    // ── 늘리기 ────────────────────────────────────────────────────

    [Fact]
    public void SetSplit_Grows()
    {
        var workspace = CreateWorkspace();

        workspace.SetSplitCommand.Execute(3);

        Assert.Equal(3, workspace.SplitCount);
        Assert.Equal(3, workspace.Panes.Count);
    }

    [Fact]
    public async Task SetSplit_ANewPane_ClonesTheActivePanesFolder()
    {
        // 사용자 결정 2026-08-12: 기억된 자리가 없으면 활성 페인을 복제한다. 빈 페인이
        // 생기면 분할할 때마다 주소를 다시 쳐야 한다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var workspace = CreateWorkspace();
        await workspace.Panes[0].Active.NavigateAsync(docs);

        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);

        Assert.Equal(docs, workspace.Panes[1].Active.CurrentLocation);
    }

    [Fact]
    public void SetSplit_KeepsTheActivePane()
    {
        // 분할은 화면 배치를 바꾸는 일이지 "어디서 일하는가" 를 바꾸는 일이 아니다.
        var workspace = CreateWorkspace();
        var first = workspace.Panes[0];

        workspace.SetSplitCommand.Execute(4);

        Assert.Same(first, workspace.ActivePane);
    }

    [Fact]
    public void SetSplit_TheNewPane_BecomesTheOtherPane()
    {
        // 2분할에서 '다른 페인' 은 정확히 예전의 "반대편" 이어야 한다 — 한 번도 가 보지
        // 않았어도 그렇다 (MRU 가 비어 있는 상태).
        var workspace = CreateWorkspace();

        workspace.SetSplitCommand.Execute(2);

        Assert.Same(workspace.Panes[1], workspace.OtherPane);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(5, 4)]
    public void SetSplit_OutsideTheRange_IsClamped(int asked, int expected)
    {
        // 커맨드 매개변수는 바인딩이 서기 전에 엉뚱한 값으로 올 수 있다. 던지면 창이 죽는다.
        var workspace = CreateWorkspace();

        workspace.SetSplitCommand.Execute(asked);

        Assert.Equal(expected, workspace.SplitCount);
    }

    // ── 접기 — 닫기가 아니다 (사용자 결정 2026-08-12) ──────────────

    [Fact]
    public void SetSplit_Shrinking_FoldsInsteadOfClosing()
    {
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(4);
        var folded = workspace.Panes[3];

        workspace.SetSplitCommand.Execute(1);

        Assert.Single(workspace.Panes);
        Assert.Equal(4, workspace.AllPanes.Count);
        Assert.Same(folded, workspace.AllPanes[3]);
    }

    [Fact]
    public async Task SetSplit_Unfolding_BringsBackTheSameFolder()
    {
        // "다시 폄면 그대로" 가 접기를 고른 이유의 전부다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        var workspace = CreateWorkspace();
        await workspace.Panes[0].Active.NavigateAsync(docs);

        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);
        await workspace.Panes[1].Active.NavigateAsync(pics);

        workspace.SetSplitCommand.Execute(1);
        await workspace.SplitWork.WaitAsync(Limit);

        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);

        Assert.Equal(pics, workspace.Panes[1].Active.CurrentLocation);
    }

    [Fact]
    public async Task SetSplit_Folding_ReleasesTheWatch()
    {
        // §13 이 값을 치르고 배운 것이다 — 화면에 없는 것이 감시를 들면 폭주가 보이지 않는
        // 자리에서 돈다. 게다가 상태표시줄 문구조차 못 보는 자리에서 뜬다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var workspace = CreateWorkspace();

        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);
        await workspace.Panes[1].Active.NavigateAsync(docs);

        // 감시 스트림이 <b>실제로 돌기 시작할 때까지</b> 기다린다. NavigateAsync 는 그것을
        // 기다리지 않으므로 (감시 루프는 배경에서 선다), 여기서 잡지 않으면 아직 걸리지도
        // 않은 감시를 놓는 것을 보고 "끊겼다" 로 읽는다 — 통과하지만 아무것도 재지 않는다.
        await WaitForAsync(() => watcher.Current is not null, "감시가 걸린다");

        var watching = watcher.Current!;

        workspace.SetSplitCommand.Execute(1);
        await workspace.SplitWork.WaitAsync(Limit);

        // 접기가 끝났다는 것은 감시 루프가 <b>실제로 끝났다</b>는 뜻이어야 한다 — 요청만
        // 보내고 돌아오면 그 루프가 화면에 없는 페인에서 계속 돈다 (docs/PRD-v2.md §13).
        await watching.Finished.WaitAsync(Limit);
    }

    [Fact]
    public async Task SetSplit_FoldingTheActivePane_MovesTheActiveOneIntoView()
    {
        // 활성 표시가 화면 밖에 있으면 키보드가 어디로 가는지 볼 수 없다.
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);

        workspace.ActivateCommand.Execute(workspace.Panes[1]);
        var second = workspace.Panes[1];

        workspace.SetSplitCommand.Execute(1);

        Assert.NotSame(second, workspace.ActivePane);
        Assert.Contains(workspace.ActivePane, workspace.Panes);
    }

    // ── 페인 닫기 ─────────────────────────────────────────────────

    [Fact]
    public void ClosePane_AtOnePane_DoesNothing()
    {
        var workspace = CreateWorkspace();

        workspace.ClosePaneCommand.Execute(workspace.Panes[0]);

        Assert.Equal(1, workspace.SplitCount);
    }

    [Fact]
    public void ClosePane_TheOneAskedFor_LeavesTheScreen()
    {
        // 프리셋 배치에서 슬롯은 앞에서부터 차므로 실제로 접히는 것은 언제나 맨 뒤다.
        // 지목한 것이 맨 뒤가 아니면 맨 뒤와 맞바꾼다 — 사라지는 것은 지목한 그 페인이다.
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(3);

        var middle = workspace.Panes[1];

        workspace.ClosePaneCommand.Execute(middle);

        Assert.Equal(2, workspace.SplitCount);
        Assert.DoesNotContain(middle, workspace.Panes);
    }

    [Fact]
    public void ClosePane_WithoutATarget_ClosesTheActiveOne()
    {
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(2);
        workspace.ActivateCommand.Execute(workspace.Panes[1]);

        var active = workspace.ActivePane;

        workspace.ClosePaneCommand.Execute(null);

        Assert.DoesNotContain(active, workspace.Panes);
    }

    // ── '다른 페인' 은 직전에 활성이던 페인이다 (MRU) ──────────────

    [Fact]
    public void OtherPane_IsTheMostRecentlyActiveVisibleOne()
    {
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(4);

        // 1 → 2 → 0 순서로 다녀왔다. 0번에서 보면 직전은 2번이다.
        workspace.ActivateCommand.Execute(workspace.Panes[1]);
        workspace.ActivateCommand.Execute(workspace.Panes[2]);
        workspace.ActivateCommand.Execute(workspace.Panes[0]);

        Assert.Same(workspace.Panes[2], workspace.OtherPane);
    }

    [Fact]
    public void OtherPane_SkipsFoldedPanes()
    {
        // 화면에 없는 페인으로 복사하면 어디로 갔는지 볼 수 없다.
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(3);

        workspace.ActivateCommand.Execute(workspace.Panes[2]);
        workspace.ActivateCommand.Execute(workspace.Panes[0]);

        workspace.SetSplitCommand.Execute(2);

        Assert.Same(workspace.Panes[1], workspace.OtherPane);
    }

    [Fact]
    public async Task CopyToOtherPane_AtOnePane_DoesNothing()
    {
        // 갈 곳이 없다. 자기 자신으로 복사하면 조용히 파일을 부른다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var workspace = CreateWorkspace();
        await workspace.Panes[0].Active.NavigateAsync(docs);
        workspace.Panes[0].Active.Selection.SelectSingle("a.txt");

        await workspace.CopyToOtherPaneCommand.ExecuteAsync(null);

        Assert.Empty(operations.Copies);
    }

    // ── 페인 전환은 화면 순서다 ───────────────────────────────────

    [Fact]
    public void SwitchPane_CyclesInScreenOrder()
    {
        // MRU 가 아니다 — 같은 키를 분할 수만큼 누르면 제자리로 와야 4분할에서 길을 잃지 않는다.
        var workspace = CreateWorkspace();
        workspace.SetSplitCommand.Execute(3);

        workspace.SwitchPaneCommand.Execute(null);
        Assert.Same(workspace.Panes[1], workspace.ActivePane);

        workspace.SwitchPaneCommand.Execute(null);
        Assert.Same(workspace.Panes[2], workspace.ActivePane);

        workspace.SwitchPaneCommand.Execute(null);
        Assert.Same(workspace.Panes[0], workspace.ActivePane);
    }

    [Fact]
    public void SwitchPane_AtOnePane_DoesNothing()
    {
        var workspace = CreateWorkspace();
        var only = workspace.ActivePane;

        workspace.SwitchPaneCommand.Execute(null);

        Assert.Same(only, workspace.ActivePane);
    }

    // ── 메뉴 (docs/PRD-v2.md §18) ─────────────────────────────────

    [Fact]
    public void SplitOptions_MarkTheCurrentCount()
    {
        var workspace = CreateWorkspace();

        Assert.Equal([true, false, false, false], workspace.SplitOptions.Select(option => option.IsSelected));

        workspace.SetSplitCommand.Execute(3);

        Assert.Equal([false, false, true, false], workspace.SplitOptions.Select(option => option.IsSelected));
    }

    [Fact]
    public void SplitOptions_CarryTheCountAsTheCommandParameter()
    {
        // XAML 에 "2" 를 적으면 문자열이고 RelayCommand<int> 는 그것을 받으면 던진다.
        Assert.Equal([1, 2, 3, 4], CreateWorkspace().SplitOptions.Select(option => option.Count));
    }

    // ── 기억한다 (사용자 결정 2026-08-12) ──────────────────────────

    [Fact]
    public async Task PersistAsync_SavesTheSplitShapeAndTheFoldedPanes()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        var workspace = CreateWorkspace();
        await workspace.Panes[0].Active.NavigateAsync(docs);

        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);
        await workspace.Panes[1].Active.NavigateAsync(pics);

        workspace.SetSplitCommand.Execute(1);
        await workspace.SplitWork.WaitAsync(Limit);

        await workspace.PersistAsync();

        var saved = await viewStates.LoadGlobalAsync(CancellationToken.None);

        // 보이는 것은 하나지만 기억은 둘이다 — 그러지 않으면 접기가 재시작을 건너며 닫기가 된다.
        Assert.Equal(1, saved.PaneCount);
        Assert.Equal(2, saved.Panes!.Count);
        Assert.Equal(pics, saved.Panes[1].Tabs.Tabs[0].Folder);
    }

    [Fact]
    public async Task RestoreAsync_BringsBackTheSplitShape()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default with
            {
                PaneCount = 3,
                RowRatio = 0.6,
                Panes = [new PaneState(PaneTabsState.Single(docs))],
            },
            CancellationToken.None);

        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(3, workspace.SplitCount);
        Assert.Equal(3, workspace.Panes.Count);
        Assert.Equal(0.6, workspace.RowRatio);
    }

    [Fact]
    public async Task RestoreAsync_AFoldedPane_DoesNotOpenItsFolder()
    {
        // 화면에 없는 페인이 감시를 들면 §13 의 폭주가 보이지 않는 자리에서 시작한다.
        var shown = Folder(@"C:\Temp\Shown", "a.txt");
        var folded = Folder(@"C:\Temp\Folded", "b.txt");
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default with
            {
                PaneCount = 1,
                Panes = [new PaneState(PaneTabsState.Single(shown)), new PaneState(PaneTabsState.Single(folded))],
            },
            CancellationToken.None);

        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(1, workspace.SplitCount);
        Assert.Equal(2, workspace.AllPanes.Count);
        Assert.DoesNotContain(folded, watcher.WatchCalls);

        // 그래도 폴더는 실려 있다 — 펴면 그리로 간다.
        workspace.SetSplitCommand.Execute(2);
        await workspace.SplitWork.WaitAsync(Limit);

        Assert.Equal(folded, workspace.Panes[1].Active.CurrentLocation);
    }

    [Fact]
    public async Task RestoreAsync_WithFewerRememberedPanesThanTheCount_FillsTheRest()
    {
        // 4분할을 처음 켜면 기억된 페인이 모자란다. 남는 자리는 활성 페인을 복제한다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        await viewStates.SaveGlobalAsync(
            GlobalViewState.Default with { PaneCount = 4, Panes = [new PaneState(PaneTabsState.Single(docs))] },
            CancellationToken.None);

        var workspace = CreateWorkspace();

        await workspace.RestoreAsync(null);

        Assert.Equal(4, workspace.Panes.Count);
        Assert.Equal(docs, workspace.Panes[0].Active.CurrentLocation);
    }

    /// <summary>배경에서 서는 일을 기다린다 (<c>PaneWatcherTests</c> 와 같은 수).</summary>
    private static async Task WaitForAsync(Func<bool> reached, string expectation)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (reached())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"기다리다 상한을 넘겼다: {expectation}");
    }

    private WorkspaceViewModel CreateWorkspace() => new(CreatePane, viewStates);

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, thumbnails, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc, contextMenus);

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);

        source.Folders[folder] =
        [
            .. names.Select(name => new FileItem(
                name, folder.Combine(name), 1, DateTimeOffset.UnixEpoch, FileItemFlags.None)),
        ];

        return folder;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }
}
