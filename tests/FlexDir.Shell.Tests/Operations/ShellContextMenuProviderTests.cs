using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Operations;

using Xunit;

namespace FlexDir.Shell.Tests.Operations;

/// <summary>
/// <c>IContextMenu</c> 구현체 (phase B-4 · docs/SHELL_NOTES.md §컨텍스트 메뉴).
/// <para>
/// <b>실물 메뉴는 자동 테스트가 밟을 수 없다.</b> <c>TrackPopupMenuEx</c> 는 사용자가 고를
/// 때까지 돌아오지 않는 모달 루프이고, 그 안에서 메시지를 펌핑한다 — <c>--blame-hang</c> 이
/// 걸리면 실패가 아니라 매달림이 된다 (.harness/HANDOFF.md §규칙 5).
/// 그래서 <c>ShellFileOperations</c> 와 같은 방식으로 <b>실행 지점을 바꿔 끼우고</b>,
/// 여기서 재는 것은 <b>무엇을 어떤 창으로 넘기는가</b> 와 <b>HRESULT 를 어떻게 옮기는가</b> 다.
/// 실물은 프로브(<c>.harness/probe/ contextmenu</c>)와 사람이 본다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class ShellContextMenuProviderTests
{
    private static readonly ScreenPoint Somewhere = new(120, 340);

    // ── 무엇을 넘기는가 ───────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_ForItems_PassesTheirLeafNames()
    {
        // shell 은 폴더에 바인딩한 뒤 자식 이름으로 PIDL 을 만든다 — 전체 경로가 아니다.
        var seen = new List<ShellContextMenuProvider.Request>();
        using var provider = Provider(seen);

        await provider.ShowAsync(
            [Loc(@"C:\Temp\a.txt"), Loc(@"C:\Temp\b.txt")],
            Loc(@"C:\Temp"),
            Somewhere,
            CancellationToken.None);

        var request = Assert.Single(seen);

        Assert.Equal(["a.txt", "b.txt"], request.Names);
        Assert.Equal(@"C:\Temp", request.Folder.DisplayPath);
        Assert.Equal(Somewhere, request.At);
    }

    [Fact]
    public async Task ShowAsync_WithNoItems_AsksForTheBackgroundMenu()
    {
        // 항목 메뉴는 GetUIObjectOf, 배경 메뉴는 CreateViewObject — 다른 API 다
        // (docs/SHELL_NOTES.md §컨텍스트 메뉴). 이름이 비어 있는 것이 그 신호다.
        var seen = new List<ShellContextMenuProvider.Request>();
        using var provider = Provider(seen);

        await provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None);

        Assert.Empty(Assert.Single(seen).Names);
    }

    [Fact]
    public void LeafNames_DropsWhatIsNotAChildOfTheFolder()
    {
        // 함정 6 — 폴더 자신이나 다른 폴더의 항목이 섞이면 ParseDisplayName 이 엉뚱한 것을
        // 낸다. 빈 leaf 이름은 일부 네임스페이스에서 폴더 자신으로 해석된다.
        // "직계 자식인가" 로 거르면 그 두 가지가 함께 걸린다.
        Assert.Equal(
            ["a.txt"],
            ShellContextMenuProvider.LeafNames(
                [Loc(@"C:\Temp\a.txt"), Loc(@"C:\"), Loc(@"C:\Other\b.txt"), Loc(@"C:\Temp")],
                Loc(@"C:\Temp")));
    }

    [Fact]
    public void LeafNames_KeepsTheOrderTheSelectionHad()
    {
        Assert.Equal(
            ["b.txt", "a.txt"],
            ShellContextMenuProvider.LeafNames(
                [Loc(@"C:\Temp\b.txt"), Loc(@"C:\Temp\a.txt")],
                Loc(@"C:\Temp")));
    }

    // ── 오너드로 렌더링 (함정 1) ──────────────────────────────────

    [Theory]
    [InlineData(0x0117u, true)]   // WM_INITMENUPOPUP
    [InlineData(0x002Bu, true)]   // WM_DRAWITEM
    [InlineData(0x002Cu, true)]   // WM_MEASUREITEM
    [InlineData(0x0120u, true)]   // WM_MENUCHAR
    [InlineData(0x0010u, false)]  // WM_CLOSE — 넘기면 안 된다
    [InlineData(0x0000u, false)]  // WM_NULL
    public void IsMenuMessage_IsTheFourTheShellNeedsBack(uint message, bool expected)
    {
        // 이 넷을 IContextMenu2/3 로 넘기지 않으면 "Open With"·"공유" 같은 오너드로 항목이
        // 빈칸으로 그려진다 — 메뉴는 뜨는데 글자가 없다.
        Assert.Equal(expected, ShellContextMenuProvider.IsMenuMessage(message));
    }

    // ── 소유 창은 Host 가 쥔다 (사용자 결정 2026-08-06) ────────────

    [Fact]
    public async Task ShowAsync_AsksForTheOwnerWindowEveryTime()
    {
        // 창을 생성 시점에 잡아 두면 상주 프로세스에서 창이 다시 만들어질 때 낡은 핸들이
        // 남는다 (ADR-003 — 닫기는 숨기기이지만 완전 종료 뒤 재실행은 새 창이다).
        var handles = new Queue<nint>([11, 22]);
        var seen = new List<ShellContextMenuProvider.Request>();

        using var provider = new ShellContextMenuProvider(
            handles.Dequeue,
            request =>
            {
                seen.Add(request);
                return 0;
            });

        await provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None);
        await provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None);

        Assert.Equal([11, 22], [.. seen.Select(request => request.Owner)]);
    }

    // ── 실패를 옮기는 법 ──────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_WhenTheShellRefuses_IsALocationAccessFailure()
    {
        using var provider = new ShellContextMenuProvider(() => 0, _ => unchecked((int)0x80070005));

        var failure = await Assert.ThrowsAsync<LocationAccessException>(
            () => provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None));

        Assert.Equal(LocationErrorKind.AccessDenied, failure.Kind);
        Assert.Equal(5, failure.Win32Error);
    }

    [Fact]
    public async Task ShowAsync_WhenTheUserPicksNothing_IsNotAFailure()
    {
        // 메뉴를 닫기만 한 것은 정상이다. 오류로 만들면 방금 스스로 한 선택을
        // 상태표시줄에서 오류로 통보받는다.
        using var provider = new ShellContextMenuProvider(() => 0, _ => 0);

        await provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None);
    }

    [Fact]
    public async Task ShowAsync_WhenCancelledBeforeItStarts_DoesNotTouchTheShell()
    {
        var called = false;

        using var provider = new ShellContextMenuProvider(
            () => 0,
            _ =>
            {
                called = true;
                return 0;
            });

        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, cancelled.Token));

        Assert.False(called);
    }

    // ── 인자 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_WithoutItems_Throws()
    {
        using var provider = new ShellContextMenuProvider(() => 0, _ => 0);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.ShowAsync(null!, Loc(@"C:\Temp"), Somewhere, CancellationToken.None));
    }

    [Fact]
    public async Task ShowAsync_WithoutAFolder_Throws()
    {
        using var provider = new ShellContextMenuProvider(() => 0, _ => 0);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.ShowAsync([], null!, Somewhere, CancellationToken.None));
    }

    [Fact]
    public void Constructor_WithoutAnOwnerWindowSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ShellContextMenuProvider(null!));
    }

    // ── 아파트먼트 (docs/SHELL_NOTES.md §COM 아파트먼트) ───────────

    [Fact]
    public async Task ShowAsync_RunsOnAnStaThread()
    {
        // IContextMenu 는 STA 를 요구한다. Task.Run·스레드풀은 MTA 다 — 테스트가 이것을
        // 고정하지 않으면 다음 사람이 조용히 되돌린다 (.harness/HANDOFF.md §규칙 1).
        ApartmentState? apartment = null;

        using var provider = new ShellContextMenuProvider(
            () => 0,
            _ =>
            {
                apartment = Thread.CurrentThread.GetApartmentState();
                return 0;
            });

        await provider.ShowAsync([], Loc(@"C:\Temp"), Somewhere, CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    private static ShellContextMenuProvider Provider(List<ShellContextMenuProvider.Request> seen)
        => new(
            () => 0,
            request =>
            {
                seen.Add(request);
                return 0;
            });

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _));

        return location;
    }
}
