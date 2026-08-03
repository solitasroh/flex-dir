using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Formatting;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
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
        var docs = Folder(@"C:\Temp\Docs", "a.txt", "ghost.txt");
        var pics = Folder(@"C:\Temp\Pics", "p1.jpg");
        await using var pane = CreatePane();

        await pane.NavigateAsync(docs);
        await pane.NavigateAsync(pics);

        // ghost.txt 는 Docs 에만 있다. 낡은 감시가 살아 있으면 Docs 기준으로 해석돼
        // 지금 보이는 Pics 목록에 붙는다.
        watcher.Push(new FolderChange(FolderChangeKind.Added, "ghost.txt"));
        Add(pics, "p2.jpg");
        watcher.Push(new FolderChange(FolderChangeKind.Added, "p2.jpg"));

        await WaitAsync(
            pane,
            () => pane.Items.Any(row => row.Name == "p2.jpg"),
            "새 폴더의 변경은 반영된다");

        Assert.Equal(["p1.jpg", "p2.jpg"], pane.Items.Select(row => row.Name));
        Assert.Equal(pics, pane.CurrentLocation);
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
