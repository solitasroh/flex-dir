using System.Text.Json;
using System.Text.Json.Serialization;

using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;

namespace FlexDir.Shell.ViewState;

/// <summary>
/// 뷰 상태를 파일 하나에 담는 <see cref="IViewStateStore"/> 구현체.
/// 기본 위치는 <c>%LOCALAPPDATA%\flex-dir\</c> 다 (docs/ARCHITECTURE.md §4).
/// <para>
/// <b>뷰 상태는 캐시다</b> (CLAUDE.md §4). 읽지 못하면 기본값으로 가고 예외를 밖으로 내지
/// 않는다 — 폴더를 못 여는 것과 뷰 설정을 못 읽는 것은 사용자에게 전혀 다른 사건이며,
/// 후자로 목록을 막으면 파일이 사라진 것처럼 보인다.
/// </para>
/// </summary>
public sealed class JsonViewStateStore : IViewStateStore
{
    public const string FileName = "view-state.json";

    // 사람이 열어 고칠 수 있어야 한다. enum 을 숫자로 저장하면 멤버 순서를 바꾸는 순간
    // 저장된 뷰가 조용히 다른 뷰로 바뀐다.
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // 저장은 읽고-고치고-쓰는 순서라 겹치면 한쪽 변경이 사라진다.
    private readonly SemaphoreSlim writeGate = new(1, 1);

    private readonly string path;
    private readonly string temporaryPath;

    /// <param name="directory">상태 파일을 둘 폴더. 없으면 저장할 때 만든다.</param>
    public JsonViewStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        DirectoryPath = directory;
        path = Path.Combine(directory, FileName);
        temporaryPath = path + ".tmp";
    }

    /// <summary>기본 저장 위치. <c>%LOCALAPPDATA%\flex-dir\</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flex-dir");

    /// <summary>상태 파일이 놓인 폴더.</summary>
    public string DirectoryPath { get; }

    public async ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ct.ThrowIfCancellationRequested();

        var document = await ReadAsync(ct).ConfigureAwait(false);

        if (document.Folders is null || !document.Folders.TryGetValue(Key(folder), out var stored))
        {
            return null;
        }

        // 한 폴더의 값이 깨져도 나머지 기억은 살린다. 파일 전체를 버리면 폴더 하나를
        // 손으로 잘못 고친 대가로 수백 개 기억이 함께 사라진다.
        return ToFolderState(stored);
    }

    public async ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        await MutateAsync(
            document =>
            {
                var folders = document.Folders is null
                    ? []
                    : new Dictionary<string, FolderRecord>(document.Folders, StringComparer.Ordinal);

                // 접힌 그룹이 없으면 아예 쓰지 않는다 — 그룹화를 안 쓰는 폴더의 기록이
                // v1 이 쓰던 모양 그대로 남는다.
                folders[Key(folder)] = new FolderRecord(
                    state.Mode,
                    [.. state.Sort],
                    state.GroupBy,
                    state.Collapsed.Count == 0 ? null : [.. state.Collapsed]);

                return document with { Folders = folders };
            },
            ct).ConfigureAwait(false);
    }

    public async ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var document = await ReadAsync(ct).ConfigureAwait(false);

        // 전역 상태는 null 을 내지 않는다 — 호출자가 기본값을 조립하면 기본값이 두 군데에 생긴다.
        return ToGlobalState(document.Global) ?? GlobalViewState.Default;
    }

    public async ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        await MutateAsync(
            document => document with
            {
                // 경로는 표시형으로 저장한다 — \\?\ 접두사 없는 쪽이 파일을 읽는 사람에게 낫고
                // TryParse 가 도로 붙여 준다.
                // 단수 필드(leftFolder·rightFolder)는 더 이상 쓰지 않는다. 함께 쓰면 어느
                // 쪽이 진실인지 파일마다 갈리고, 읽는 쪽이 그 판정을 영영 들고 있게 된다.
                Global = new GlobalRecord(
                    state.SplitterRatio,
                    state.Window,
                    TreeVisible: state.TreeVisible,
                    TreeWidth: state.TreeWidth,
                    LeftColumns: state.LeftColumns,
                    RightColumns: state.RightColumns,
                    LeftTabs: ToRecord(state.LeftTabs),
                    RightTabs: ToRecord(state.RightTabs)),
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 폴더 키. Windows 파일시스템은 대소문자를 구분하지 않으므로 주소창에 소문자로
    /// 입력했다고 뷰 설정이 따로 생기면 "기억한다" 가 성립하지 않는다.
    /// </summary>
    private static string Key(LocationId folder) => folder.Value.ToUpperInvariant();

    private static FolderViewState? ToFolderState(FolderRecord stored)
    {
        try
        {
            // groupBy·collapsed 는 v1 이 쓴 파일에 없다. 없으면 그룹화가 꺼진 것으로 읽는다.
            return new FolderViewState(stored.Mode, stored.Sort ?? [], stored.GroupBy, stored.Collapsed);
        }
        catch (ArgumentException)
        {
            // 정렬이 비었거나 값이 깨졌다. 기억이 없는 것으로 본다.
            return null;
        }
    }

    private static GlobalViewState? ToGlobalState(GlobalRecord? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            // treeVisible·treeWidth 는 트리가 들어오기 전 파일에 없다. 없으면 보이는
            // 것으로 읽는다 — 기본값과 같은 자리다.
            return new GlobalViewState(
                stored.SplitterRatio,
                stored.Window,
                ToTabs(stored.LeftTabs, stored.LeftFolder),
                ToTabs(stored.RightTabs, stored.RightFolder),
                stored.TreeVisible ?? true,
                stored.TreeWidth ?? GlobalViewState.DefaultTreeWidth,
                stored.LeftColumns,
                stored.RightColumns);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 스플리터 비율이 범위를 벗어났다. 그대로 쓰면 페인이 사라진다.
            return null;
        }
    }

    private async ValueTask<Document> ReadAsync(CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            return JsonSerializer.Deserialize<Document>(text, Format) ?? new Document();
        }
        catch (FileNotFoundException)
        {
            return new Document();   // 첫 실행. 오류가 아니다.
        }
        catch (DirectoryNotFoundException)
        {
            return new Document();
        }
        catch (JsonException)
        {
            return new Document();   // 깨진 파일 — 기본값으로 간다. 저장하면 되살아난다.
        }
        catch (IOException)
        {
            return new Document();   // 잠김·읽기 실패. 캐시이므로 물러난다.
        }
        catch (UnauthorizedAccessException)
        {
            return new Document();
        }
    }

    private async ValueTask MutateAsync(Func<Document, Document> change, CancellationToken ct)
    {
        await writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var next = change(await ReadAsync(ct).ConfigureAwait(false));

            Directory.CreateDirectory(DirectoryPath);

            // 제자리에서 덮어쓰면 쓰다 죽었을 때 반쪽 파일이 남고, 그 파일은 다음 실행에서
            // 손상으로 읽혀 폴더별 기억이 통째로 날아간다. 임시 파일에 쓰고 옮긴다.
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(next, Format), ct)
                .ConfigureAwait(false);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            writeGate.Release();
        }
    }

    // 저장 형식은 Core 의 타입과 분리한다. 그쪽 생성자는 값을 검증하므로 역직렬화가
    // 곧바로 예외를 던지는데, 그러면 깨진 파일 하나가 폴더를 여는 길을 막는다.
    private sealed record Document
    {
        public GlobalRecord? Global { get; init; }

        public Dictionary<string, FolderRecord>? Folders { get; init; }
    }

    /// <summary>깨진 경로는 기억이 없는 것으로 다룬다 — 복원이 폴백으로 가면 된다.</summary>
    private static LocationId? ParseFolder(string? path)
        => path is not null && LocationId.TryParse(path, out var folder, out _) ? folder : null;

    /// <summary>
    /// 저장된 탭 목록 (docs/PRD-v2.md §17). <b>구버전 파일과의 호환이 여기서 끝난다</b> —
    /// 탭 목록이 없으면 단수 필드를 "탭 1개짜리 목록" 으로 읽고, ViewModel 은 두 모양을
    /// 구분하지 않는다.
    /// <para>
    /// 읽을 수 있는 탭이 하나도 없으면 <c>null</c> 이다. 탭 0개짜리 목록을 내면 복원이
    /// 그것을 <b>기억된 상태</b>로 받아 시작 폴더 규칙을 지나지 못한다 — 페인이 빈 채로 뜬다.
    /// </para>
    /// </summary>
    private static PaneTabsState? ToTabs(PaneTabsRecord? stored, string? legacyFolder)
    {
        if (stored?.Tabs is not { } records)
        {
            return ParseFolder(legacyFolder) is { } folder ? PaneTabsState.Single(folder) : null;
        }

        var tabs = new List<TabState>(records.Count);

        // 활성 탭이 어느 것인지는 번호가 아니라 인스턴스로 기억한다. 경로 하나가 깨져
        // 빠지면 그 뒤의 번호가 전부 하나씩 당겨지기 때문이다.
        var activeIndex = stored.ActiveIndex ?? 0;
        var active = -1;

        for (var index = 0; index < records.Count; index++)
        {
            // 탭 하나의 경로가 깨져도 나머지는 살린다 — 폴더별 기억을 한 폴더의 손상으로
            // 통째로 버리지 않는 것과 같은 판단이다.
            if (ParseFolder(records[index].Folder) is not { } folder)
            {
                continue;
            }

            if (index == activeIndex)
            {
                active = tabs.Count;
            }

            tabs.Add(new TabState(folder, records[index].IsPinned ?? false, records[index].Title));
        }

        // 활성이던 탭이 깨져 빠졌으면 첫 탭으로 간다. PaneTabsState 가 자르기도 하지만
        // 그것은 범위 밖일 때뿐이고, 여기서는 범위 안의 <b>다른</b> 탭을 가리키게 된다.
        return tabs.Count == 0 ? null : new PaneTabsState(tabs, active < 0 ? 0 : active);
    }

    private static PaneTabsRecord? ToRecord(PaneTabsState? state)
        => state is null
            ? null
            : new PaneTabsRecord(
                [.. state.Tabs.Select(tab => new TabRecord(
                    // 경로는 표시형으로 저장한다 — 전역 상태의 다른 경로와 같은 규칙이다.
                    tab.Folder.DisplayPath,
                    // 기본값은 아예 쓰지 않는다. 고정하지 않은 탭의 기록이 짧게 남는다.
                    tab.IsPinned ? true : null,
                    tab.Title))],
                state.ActiveIndex);

    /// <summary>
    /// <c>TreeVisible</c>·<c>TreeWidth</c> 는 선택적이다 — 트리가 들어오기 전 파일에는
    /// 없고, 없으면 트리가 보이는 것으로 읽힌다 (docs/PRD-v2.md §10).
    /// </summary>
    /// <param name="LeftFolder">
    /// 탭이 들어오기 전 파일의 단수 필드 (docs/PRD-v2.md §17). <b>읽기 전용이다</b> —
    /// 새로 쓰지 않고, 읽을 때만 "탭 1개짜리 목록" 으로 옮긴다 (<see cref="ToTabs"/>).
    /// </param>
    private sealed record GlobalRecord(
        double SplitterRatio,
        WindowPlacement? Window,
        string? LeftFolder = null,
        string? RightFolder = null,
        bool? TreeVisible = null,
        double? TreeWidth = null,
        PaneColumns? LeftColumns = null,
        PaneColumns? RightColumns = null,
        PaneTabsRecord? LeftTabs = null,
        PaneTabsRecord? RightTabs = null);

    private sealed record PaneTabsRecord(List<TabRecord>? Tabs, int? ActiveIndex = null);

    /// <summary>
    /// <c>IsPinned</c>·<c>Title</c> 은 선택적이다 — 고정하지 않고 이름도 안 바꾼 탭이
    /// 대부분이라, 없으면 기본값으로 읽어 파일을 짧게 유지한다.
    /// </summary>
    private sealed record TabRecord(string Folder, bool? IsPinned = null, string? Title = null);

    /// <summary>
    /// <c>GroupBy</c>·<c>Collapsed</c> 는 선택적이다 — v1 이 쓴 파일에는 없고, 없으면
    /// 그룹화가 꺼진 것으로 읽힌다 (docs/PRD-v2.md §6-1).
    /// </summary>
    private sealed record FolderRecord(
        ViewMode Mode,
        List<SortOrder>? Sort,
        SortKey? GroupBy = null,
        List<string>? Collapsed = null);
}
