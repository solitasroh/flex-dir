using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Presentation;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Watching;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 외부 변경을 목록에 반영한다 (ADR-011 · CLAUDE.md §4).
/// <para>
/// 여기서 재는 것의 중심은 <b>선택이 살아남는가</b> 다 — "갱신 때마다 선택이 풀리면 감시가
/// 없느니만 못하다". 그래서 이름 변경이 선택을 새 이름으로 잇는지, 갱신 중에 선택이 한 번도
/// 비지 않는지, 목록 알림에 <c>Reset</c> 이 섞이지 않는지를 함께 본다 (WPF <c>ListView</c> 는
/// <c>Reset</c> 에서 선택을 버린다).
/// </para>
/// <para>
/// 감시는 배경에서 도는 스트림이라 테스트가 완료 시점을 직접 잡을 수 없다. 재우지 않고
/// <b>알림으로</b> 기다린다 (<see cref="WaitAsync"/>) — <c>Task.Delay</c> 로 기다리면 테스트가
/// 실행 속도에 의존한다. 상한(<see cref="Limit"/>)은 대기 시간이 아니라 매달린 테스트를
/// 실패로 바꾸는 장치다.
/// </para>
/// </summary>
public class PaneWatcherTests
{
    /// <summary>매달린 테스트를 실패로 바꾼다. 정상 동작이면 이 시간에 닿지 않는다.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    private readonly FakeFolderSource source = new();
    private readonly FakeFolderWatcher watcher = new();
    private readonly FakeTypeNameProvider typeNames = new();
    private readonly InMemoryViewStateStore viewStates = new();
    private readonly FakeFileOperations operations = new();
    private readonly FakeClipboardBridge clipboard = new();
    private readonly FakeItemActivator activator = new();
    private readonly InlineUiDispatcher dispatcher = new();

    // ── 개별 변경 ─────────────────────────────────────────────────

    [Fact]
    public async Task Added_InsertsTheRowAtItsSortedPosition()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        Add(folder, "b.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Added, "b.txt"));

        await WaitAsync(pane, () => pane.Items.Count == 3, "새 항목이 목록에 들어온다");

        // 맨 끝에 붙이지 않는다 — 정렬 위치가 곧 그 항목의 자리다.
        Assert.Equal(["a.txt", "b.txt", "c.txt"], pane.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Removed_DropsTheRowAndTheSelection()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.Selection.Toggle("b.txt");

        Remove(folder, "b.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Removed, "b.txt"));

        await WaitAsync(
            pane,
            () => pane.Items.Count == 1 && pane.Selection.Count == 1,
            "사라진 항목이 목록과 선택에서 함께 빠진다");

        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(["a.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task Renamed_CarriesTheSelectionToTheNewName()
    {
        // ADR-011 의 핵심이다. 이름 변경을 제거+추가로 처리하면 여기서 선택이 풀린다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");

        Rename(folder, "a.txt", "z.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Renamed, "z.txt", "a.txt"));

        await WaitAsync(
            pane,
            () => pane.Selection.SelectedNames.Contains("z.txt"),
            "선택이 새 이름으로 이어진다");

        Assert.Equal(["b.txt", "z.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(["z.txt"], pane.Selection.SelectedNames);
    }

    [Fact]
    public async Task Changed_OnAnotherRow_LeavesTheSelectionAlone()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        var before = pane.Items[1].SizeText;

        Change(folder, "b.txt", size: 4096);
        watcher.Push(new FolderChange(FolderChangeKind.Changed, "b.txt"));

        await WaitAsync(
            pane,
            () => pane.Items.Any(row => row.Item.Size == 4096),
            "바뀐 항목을 다시 읽어 반영한다");

        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(["a.txt"], pane.Selection.SelectedNames);
        Assert.Equal("a.txt", pane.Selection.Anchor);

        // 표시 문자열은 줄을 만들 때 굳는다 — 다시 만들지 않으면 크기 컬럼이 옛 값으로 남는다.
        Assert.NotEqual(before, pane.Items[1].SizeText);
    }

    [Fact]
    public async Task WhenTheItemIsAlreadyGone_ItLeavesTheList()
    {
        // 알림과 실제가 어긋나는 것은 정상이다. 진실원천은 파일시스템이다 (CLAUDE.md §4).
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        // '바뀌었다' 는 알림이 오지만 다시 읽으면 없다.
        Remove(folder, "b.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Changed, "b.txt"));

        await WaitAsync(
            pane,
            () => pane.StatusText == StatusSummary.ForItems(1, Culture),
            "다시 읽을 수 없는 항목은 목록에서 빠진다");

        Assert.Equal(["a.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 선택 유지 ─────────────────────────────────────────────────

    [Fact]
    public async Task Updates_NeverEmptyTheSelection()
    {
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("b.txt");

        // 갱신이 지나가는 동안의 모든 시점을 본다 — 중간에 한 번이라도 0 이 되면 View 의
        // 선택이 그 순간 날아간다.
        var observed = new List<int>();
        pane.Items.CollectionChanged += (_, _) => observed.Add(pane.Selection.Count);
        pane.Selection.PropertyChanged += (_, _) => observed.Add(pane.Selection.Count);

        Rename(folder, "b.txt", "b2.txt");
        Remove(folder, "c.txt");
        Add(folder, "d.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Renamed, "b2.txt", "b.txt"));
        watcher.Push(new FolderChange(FolderChangeKind.Removed, "c.txt"));
        watcher.Push(new FolderChange(FolderChangeKind.Added, "d.txt"));

        await WaitAsync(
            pane,
            () => pane.Items.Count == 3 && pane.Selection.SelectedNames.Contains("b2.txt"),
            "세 변경이 모두 반영되고 선택이 이어진다");

        Assert.Equal(["a.txt", "b2.txt", "d.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(["b2.txt"], pane.Selection.SelectedNames);
        Assert.NotEmpty(observed);
        Assert.DoesNotContain(0, observed);
    }

    [Fact]
    public async Task Updates_DoNotRaiseAResetNotification()
    {
        // Reset 은 WPF ListView 의 선택을 버린다. 그러면 이 갱신 경로의 목적이 무너진다.
        var folder = Folder(@"C:\Temp", "a.txt", "c.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("c.txt");

        var actions = new List<NotifyCollectionChangedAction>();
        pane.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        Add(folder, "b.txt");
        Remove(folder, "a.txt");
        Rename(folder, "c.txt", "d.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Added, "b.txt"));
        watcher.Push(new FolderChange(FolderChangeKind.Removed, "a.txt"));
        watcher.Push(new FolderChange(FolderChangeKind.Renamed, "d.txt", "c.txt"));

        await WaitAsync(
            pane,
            () => pane.Items.Count == 2 && pane.Selection.SelectedNames.Contains("d.txt"),
            "추가·제거·이름변경이 한 번에 반영된다");

        Assert.Equal(["b.txt", "d.txt"], pane.Items.Select(row => row.Name));
        Assert.NotEmpty(actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    // ── 오버플로 ──────────────────────────────────────────────────

    [Fact]
    public async Task Overflow_RereadsTheWholeFolderAndKeepsWhatIsStillThere()
    {
        // 유실된 이벤트를 무시하면 목록이 파일시스템과 어긋난 채 남는다
        // (docs/SHELL_NOTES.md §폴더 감시).
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);
        pane.Selection.SelectSingle("a.txt");
        pane.Selection.Toggle("b.txt");

        Remove(folder, "b.txt");
        Add(folder, "z.txt");
        watcher.PushOverflow();

        await WaitAsync(
            pane,
            () => pane.StatusText == StatusSummary.ForSelection(2, 1, 1024, Culture),
            "전체를 다시 읽고 남은 항목의 선택을 복원한다");

        Assert.Equal(["a.txt", "z.txt"], pane.Items.Select(row => row.Name));

        // 개별 알림이 없으므로 다시 읽는 것 말고는 맞출 방법이 없다.
        Assert.Equal([folder, folder], source.EnumerateCalls);

        // 사라진 이름만 떨어지고 남은 선택은 그대로다.
        Assert.Equal(["a.txt"], pane.Selection.SelectedNames);
    }

    // ── 묶음 처리 ─────────────────────────────────────────────────

    [Fact]
    public async Task AHundredNotifications_AreAllApplied()
    {
        // 파일 100개를 복사하면 알림이 100개 이상 온다. 묶음 경계(64개)에서 알림을 잃으면
        // 목록이 파일시스템과 어긋난 채 남는다.
        var folder = Folder(@"C:\Temp", "a.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        for (var index = 0; index < 100; index++)
        {
            var name = $"new{index:D3}.txt";

            Add(folder, name);
            watcher.Push(new FolderChange(FolderChangeKind.Added, name));
        }

        await WaitAsync(pane, () => pane.Items.Count == 101, "알림 100개가 모두 반영된다");

        Assert.Equal(101, pane.Items.Count);
        Assert.Equal(100, pane.Items.Count(row => row.Name.StartsWith("new", StringComparison.Ordinal)));
    }

    // ── 열거와의 관계 ─────────────────────────────────────────────

    [Fact]
    public async Task ChangesDuringEnumeration_AreAppliedWhenItFinishes()
    {
        // 열거가 끝난 뒤에 감시를 걸면 그 사이의 변경을 잃는다.
        var folder = Folder(@"C:\Temp", "a.txt", "c.txt");
        source.YieldDelayMilliseconds = 20;
        await using var pane = CreatePane();

        var opened = pane.NavigateAsync(folder);

        Add(folder, "b.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Added, "b.txt"));

        await opened;
        await WaitAsync(pane, () => pane.Items.Count == 3, "열거 중에 온 변경이 열거 후에 반영된다");

        Assert.Equal(["a.txt", "b.txt", "c.txt"], pane.Items.Select(row => row.Name));
    }

    // ── 폴더 이탈 ─────────────────────────────────────────────────

    [Fact]
    public async Task MovingToAnotherFolder_CancelsThePreviousWatch()
    {
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p.jpg");
        await using var pane = CreatePane();

        await pane.NavigateAsync(docs);
        await pane.NavigateAsync(pics);

        // 구독이 쌓이면 폴더를 옮길수록 같은 변경이 중복으로 들어온다.
        await WaitForAsync(
            () => watcher.CancellationsObserved >= 1,
            "이전 폴더의 감시가 취소된다");

        Assert.Equal([docs, pics], watcher.WatchCalls);
    }

    [Fact]
    public async Task AStaleWatch_DoesNotTouchTheNewFolderList()
    {
        // 이전 폴더의 변경이 새 폴더 목록에 섞이는 것은 전작 잔버그의 원천이었다.
        //
        // 폴더를 옮긴 뒤에 낡은 스트림에 밀어넣는 것으로는 이것을 재지 못한다 — 그 스트림은
        // 이미 취소돼 읽는 사람이 없다. 새는 창은 낡은 감시가 <b>이미 변경을 집어 다시 읽는
        // 중</b>일 때 폴더가 바뀌는 경우다. 조회는 syscall 한 번이라 취소가 중간에 끊지 못하고,
        // 그래서 세대 판정(WatchRun.IsStale) 없이는 낡은 폴더의 항목이 새 목록에 붙는다.
        var docs = Folder(@"C:\Temp\Docs", "a.txt");
        var pics = Folder(@"C:\Temp\Pics", "p1.jpg");
        var held = new HeldItemReads(source);
        await using var pane = new PaneViewModel(
            held, watcher, typeNames, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

        await pane.NavigateAsync(docs);

        // 감시는 호출자의 스레드 밖에서 시작된다 (UI 스레드에서 shell 을 부르지 않는다).
        await WaitForAsync(() => watcher.Current is not null, "Docs 의 감시가 걸린다");
        var stale = watcher.Current!;

        // ghost.txt 는 Docs 에만 있다. 확장자는 이미 조회된 것을 쓴다 — 새 확장자면 유형 이름
        // 조회가 취소를 관측해 낡은 적용이 그 자리에서 죽고, 그러면 이 테스트가 세대 판정이
        // 아니라 취소를 재게 된다.
        Add(docs, "ghost.txt");
        stale.Push(new FolderChange(FolderChangeKind.Added, "ghost.txt"));

        // 낡은 감시가 ghost.txt 를 다시 읽는 중이다. 여기서 폴더를 옮긴다.
        await held.Reached.WaitAsync(Limit);
        await pane.NavigateAsync(pics);

        // 이제서야 조회가 값을 들고 돌아온다. 토큰은 이미 취소돼 있다.
        held.Open();

        // 낡은 감시 루프가 완전히 끝날 때까지 기다린다 — 그러지 않으면 아직 적용되지 않은
        // 것을 "반영되지 않았다" 고 보는 헛된 단정이 된다.
        await stale.Finished.WaitAsync(Limit);

        Assert.Equal(["p1.jpg"], pane.Items.Select(row => row.Name));
        Assert.Equal(pics, pane.CurrentLocation);
        Assert.Equal(StatusSummary.ForItems(1, Culture), pane.StatusText);
    }

    // ── 유형 이름 캐시 ────────────────────────────────────────────

    [Fact]
    public async Task TypeNameLookups_ArriveFromTheWatchAndTheEnumerationAtOnce()
    {
        // 유형 이름 캐시를 두 경로가 <b>동시에</b> 만진다 — 감시 갱신(ApplyAsync→RowAsync)과
        // 새 폴더의 열거(FillAsync→BuildRowsAsync→RowAsync)다. RowAsync 가 의도적으로
        // dispatcher 밖이라 UI 스레드가 이 둘을 직렬화해 주지 않는다.
        //
        // 이 테스트가 재는 것은 그 창이 <b>실재한다</b> 는 것뿐이다. 자료 경합 자체는 한 번의
        // 실행으로 재현되지 않으므로, 캐시의 잠금이 왜 필요한지를 여기서 못 박는다 —
        // 이 창이 없다고 믿으면 잠금이 불필요해 보여 지워진다.
        var docs = Folder(@"C:\Temp\Docs", "a.md");
        var pics = Folder(@"C:\Temp\Pics", "b.jpg");
        var held = new HeldTypeNames(typeNames, "aa", "jpg");
        await using var pane = new PaneViewModel(
            source, watcher, held, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

        await pane.NavigateAsync(docs);
        await WaitForAsync(() => watcher.Current is not null, "Docs 의 감시가 걸린다");
        var stale = watcher.Current!;

        Add(docs, "ghost.aa");
        stale.Push(new FolderChange(FolderChangeKind.Added, "ghost.aa"));

        // 감시 갱신이 'aa' 의 유형 이름을 조회하는 중이다.
        await WaitForAsync(() => held.InFlight >= 1, "감시 갱신이 유형 이름을 조회한다");

        // 그 조회가 아직 끝나지 않은 채로 폴더를 옮긴다. 열거는 'jpg' 를 조회해야 첫 줄을
        // 만들 수 있으므로 같은 시점에 캐시로 들어온다.
        var opening = pane.NavigateAsync(pics);

        await WaitForAsync(() => held.InFlight >= 2, "열거가 같은 시점에 유형 이름을 조회한다");

        held.Open();
        await opening;
        await stale.Finished.WaitAsync(Limit);

        Assert.Equal(2, held.PeakInFlight);

        // 낡은 감시의 갱신은 버려진다. 새 폴더의 목록은 그대로다.
        Assert.Equal(["b.jpg"], pane.Items.Select(row => row.Name));
        Assert.Equal("JPG", Assert.Single(pane.Items).TypeText);
    }

    // ── 감시가 죽었을 때 ──────────────────────────────────────────

    [Fact]
    public async Task AFailingWatch_StopsWatchingWithoutMarkingAnError()
    {
        // 목록은 여전히 유효하다. 오류로 표시하면 파일이 사라진 것처럼 보이고, 감시가 죽은
        // 것은 사용자가 손쓸 수 있는 일이 아니다 — 새로 고침으로 회복한다.
        var folder = Folder(@"C:\Temp", "a.txt", "b.txt");
        var failing = new FailingFolderWatcher();
        await using var pane = new PaneViewModel(
            source, failing, typeNames, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

        await pane.NavigateAsync(folder);
        await failing.Asked.WaitAsync(Limit);

        Assert.Equal(["a.txt", "b.txt"], pane.Items.Select(row => row.Name));
        Assert.Equal(PaneStatus.Idle, pane.Status);
        Assert.Equal(StatusSummary.ForItems(2, Culture), pane.StatusText);
    }

    [Fact]
    public async Task AfterAFailedEnumeration_WatchUpdatesDoNotBuildAList()
    {
        // 열거가 실패한 폴더다. 알림만으로 목록을 채우면 상태표시줄에는 사유가 남아 있는데
        // 항목은 보이는 상태가 된다 — 읽지도 못한 폴더의 내용으로 보인다. 열거 실패 시 목록을
        // 비우는 이유(FillAsync)와 같다.
        var locked = Folder(@"C:\Temp\Locked", "secret.txt");
        source.FailureInjection = (0, LocationErrorKind.AccessDenied);
        var pane = CreatePane();

        var opening = pane.NavigateAsync(locked);

        await WaitForAsync(() => watcher.Current is not null, "감시가 걸린다");
        var watching = watcher.Current!;
        watching.Push(new FolderChange(FolderChangeKind.Added, "secret.txt"));

        await opening;

        Assert.Equal(PaneStatus.Error, pane.Status);

        // 적용하지 않고 감시를 접는다 — 이 폴더에서 올 수 있는 것은 오해를 부르는 갱신뿐이고,
        // 회복은 새로 고침이다 (그때 감시도 새로 걸린다).
        await WaitForAsync(
            () => watching.Finished.IsCompleted,
            "열거가 실패한 폴더의 감시를 접는다");

        Assert.Empty(pane.Items);
        Assert.Equal(
            LocationErrorMessages.Describe(LocationErrorKind.AccessDenied, locked),
            pane.StatusText);

        await pane.DisposeAsync();
    }

    // ── 상태표시줄 ────────────────────────────────────────────────

    [Fact]
    public async Task Updates_RecomputeTheStatusText()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        Assert.Equal(StatusSummary.ForItems(1, Culture), pane.StatusText);

        Add(folder, "b.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Added, "b.txt"));

        await WaitAsync(
            pane,
            () => pane.StatusText == StatusSummary.ForItems(2, Culture),
            "갱신 후 항목 수가 맞다");

        Assert.Equal(2, pane.Items.Count);
    }

    [Fact]
    public async Task WhenTheLastItemGoes_ThePaneReportsAnEmptyFolder()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        await using var pane = CreatePane();
        await pane.NavigateAsync(folder);

        Remove(folder, "a.txt");
        watcher.Push(new FolderChange(FolderChangeKind.Removed, "a.txt"));

        await WaitAsync(pane, () => pane.Status == PaneStatus.Empty, "빈 폴더가 됐음을 알린다");

        Assert.Empty(pane.Items);
        Assert.Equal(StatusSummary.Empty, pane.StatusText);
    }

    // ── 정리 ──────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_EndsTheWatchAndTheEnumeration()
    {
        var folder = Folder(@"C:\Temp", "a.txt");
        var pane = CreatePane();
        await pane.NavigateAsync(folder);

        // 여기서 상한에 걸리면 감시나 열거의 종료 신호를 놓친 것이다 — 실패하지 않고
        // 매달리는 쪽이 더 나쁘다.
        await pane.DisposeAsync().AsTask().WaitAsync(Limit);

        Assert.Equal(1, watcher.CancellationsObserved);
    }

    // ── 기다리기 ──────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="reached"/> 가 참이 될 때까지 페인의 알림으로 기다린다. 재우지 않는다 —
    /// <c>Task.Delay</c> 로 기다리면 테스트가 실행 속도에 의존해 간헐적으로 실패한다.
    /// </summary>
    private static async Task WaitAsync(PaneViewModel pane, Func<bool> reached, string expectation)
    {
        // 알림은 감시 루프의 스레드에서 온다. 이어붙은 테스트 코드를 그 스레드에서 그대로
        // 돌리면 갱신을 미는 손이 멈춘다.
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            if (reached())
            {
                signal.TrySetResult();
            }
        }

        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Check();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => Check();

        pane.Items.CollectionChanged += OnItemsChanged;
        pane.PropertyChanged += OnPropertyChanged;
        pane.Selection.PropertyChanged += OnPropertyChanged;

        try
        {
            // 구독하기 전에 이미 반영됐을 수 있다.
            Check();

            await signal.Task.WaitAsync(Limit);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"갱신을 기다리다 상한을 넘겼다: {expectation}");
        }
        finally
        {
            pane.Items.CollectionChanged -= OnItemsChanged;
            pane.PropertyChanged -= OnPropertyChanged;
            pane.Selection.PropertyChanged -= OnPropertyChanged;
        }
    }

    /// <summary>
    /// 감시 스트림의 종료는 페인이 아니라 감시 루프가 관측한다 — 알릴 대상이 없으므로
    /// 조건을 직접 되묻는다. 이 경로만 이렇게 기다린다 (목록·선택은 알림으로 기다린다).
    /// </summary>
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

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private PaneViewModel CreatePane()
        => new(source, watcher, typeNames, viewStates, operations, clipboard, activator, dispatcher, Culture, TimeZoneInfo.Utc);

    private LocationId Folder(string path, params string[] names)
    {
        var folder = Loc(path);
        source.Folders[folder] = [.. names.Select(name => Entry(folder, name))];
        return folder;
    }

    /// <summary>
    /// 폴더의 항목을 바꾼다. 목록을 제자리에서 고치지 않고 <b>새 목록으로 교체</b>한다 —
    /// 열거가 진행 중이면 그 반복자가 같은 목록을 훑고 있다.
    /// </summary>
    private void Add(LocationId folder, string name)
        => source.Folders[folder] = [.. source.Folders[folder], Entry(folder, name)];

    private void Remove(LocationId folder, string name)
        => source.Folders[folder] = [.. Without(folder, name)];

    private void Rename(LocationId folder, string from, string to)
        => source.Folders[folder] = [.. Without(folder, from), Entry(folder, to)];

    private void Change(LocationId folder, string name, long size)
        => source.Folders[folder] = [.. Without(folder, name), Entry(folder, name, size)];

    private IEnumerable<FileItem> Without(LocationId folder, string name)
        => source.Folders[folder].Where(item => item.Name != name);

    private static FileItem Entry(LocationId folder, string name, long size = 1024)
        => new(name, folder.Combine(name), size, DateTimeOffset.UnixEpoch, FileItemFlags.None);

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }

    /// <summary>
    /// 지정한 확장자의 유형 이름 조회를 문 앞에 세우는 덧옷. 두 경로를 <b>같은 시점에</b>
    /// 캐시 안에 세워야 동시 진입이 실재함을 잴 수 있다.
    /// </summary>
    private sealed class HeldTypeNames(ITypeNameProvider inner, params string[] extensions)
        : ITypeNameProvider
    {
        private readonly HashSet<string> held = [.. extensions];
        private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock gate = new();

        private int inFlight;

        /// <summary>지금 문 앞에 서 있는 조회의 수.</summary>
        public int InFlight
        {
            get
            {
                lock (gate)
                {
                    return inFlight;
                }
            }
        }

        /// <summary>동시에 문 앞에 서 있던 최대 수.</summary>
        public int PeakInFlight { get; private set; }

        public void Open() => open.TrySetResult();

        public async ValueTask<string> GetTypeNameAsync(
            string extension,
            bool isDirectory,
            CancellationToken ct)
        {
            if (isDirectory || !held.Contains(extension))
            {
                return await inner.GetTypeNameAsync(extension, isDirectory, ct);
            }

            lock (gate)
            {
                inFlight++;
                PeakInFlight = Math.Max(PeakInFlight, inFlight);
            }

            try
            {
                // 취소를 관측하지 않는다. 붙잡힌 조회가 취소로 풀리면 두 경로를 같은 시점에
                // 세울 수 없다 — 실제 shell 조회도 시작한 뒤에는 취소가 끊지 못한다.
                await open.Task;
            }
            finally
            {
                lock (gate)
                {
                    inFlight--;
                }
            }

            return await inner.GetTypeNameAsync(extension, isDirectory, ct);
        }
    }

    /// <summary>
    /// 단건 조회를 문 앞에 세우는 덧옷. <see cref="Reached"/> 로 도착을 보고 <see cref="Open"/>
    /// 으로 통과시킨다 — 그 사이에 폴더를 옮기면 낡은 감시가 조회 결과를 들고 돌아오는 상황이
    /// 만들어진다.
    /// <para>
    /// 통과할 때 취소를 관측하지 않는 이유: 실제 <c>TryGetItemAsync</c> 는 syscall 한 번이라
    /// 시작한 조회를 취소가 중간에 끊지 못한다. 그 창이 곧 낡은 감시가 새 목록을 건드릴 수
    /// 있는 창이고, <c>WatchRun.IsStale</c> 이 막는 것이 그것이다.
    /// </para>
    /// </summary>
    private sealed class HeldItemReads(IFolderSource inner) : IFolderSource
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>조회가 문에 닿은 시점.</summary>
        public Task Reached => reached.Task;

        public void Open() => open.TrySetResult();

        public IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct)
            => inner.EnumerateAsync(folder, ct);

        public async Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
        {
            reached.TrySetResult();

            await open.Task;

            return await inner.TryGetItemAsync(item, CancellationToken.None);
        }
    }

    /// <summary>
    /// 감시를 걸 수 없는 구현체. 실제로는 핸들이 모자라거나 폴더가 사라진 경우다 —
    /// 목록은 이미 채워져 있으므로 오류로 표시할 일이 아니다.
    /// </summary>
    private sealed class FailingFolderWatcher : IFolderWatcher
    {
        private readonly TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>감시를 물어본 시점. 실패는 그 자리에서 일어난다.</summary>
        public Task Asked => asked.Task;

        public IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct)
        {
            asked.TrySetResult();

            throw new IOException("감시를 시작할 수 없다.");
        }
    }
}
