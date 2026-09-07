using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.App.Threading;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Favorites;
using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.Storage;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 창 왼쪽의 폴더 트리 (docs/PRD-v2.md §10 · 사용자 결정 2026-08-10). 두 페인이 공용으로
/// 쓰고, 고른 폴더는 <see cref="NavigationRequested"/> 로 나간다 — 어느 페인이 갈지는
/// <see cref="WorkspaceViewModel"/> 이 정한다.
///
/// <para>
/// <b>루트는 드라이브와 매핑 드라이브에서 뽑은 서버뿐이다.</b> 네트워크 이웃을 훑지
/// 않는다 (사용자 결정) — 없는 서버 하나에 42초를 쓴다 (docs/PRD-v2.md §5 N-4 실측).
/// </para>
///
/// <para>
/// <b>페인이 움직여도 트리는 따라가지 않는다</b> (사용자 결정). 경로를 따라 노드를 차례로
/// 여는 것은 그 단계마다 저장소 호출이고, 페인이 어디 있는지는 breadcrumb 이 이미 말한다.
/// </para>
///
/// <para>
/// <b>자식은 펼칠 때 한 번만 읽는다.</b> 접었다 펴는 것으로 다시 열거하면 네트워크 폴더에서
/// 그때마다 멈춘다 — 갱신은 감시의 몫이지 펼치기의 몫이 아니다 (CLAUDE.md §4).
/// </para>
/// </summary>
public sealed partial class FolderTreeViewModel : ObservableObject
{
    /// <summary>
    /// 트리 폭의 하한·상한. 하한은 <see cref="WorkspaceViewModel.SplitterRatio"/> 와 같은
    /// 이유다 — 저장 파일이 손상되거나 스플리터를 끝까지 끌어도 트리가 사라지면 안 된다.
    /// </summary>
    private const double MinWidth = 120;

    private const double MaxWidth = 480;

    /// <summary>기억된 것이 없을 때의 폭. 목록의 긴 폴더 이름이 대체로 들어간다.</summary>
    public const double DefaultWidth = 220;

    private readonly IDriveList drives;
    private readonly INetworkPlaceList places;
    private readonly IFavoriteStore favoriteStore;
    private readonly IFolderSource folders;
    private readonly IUiDispatcher dispatcher;

    /// <summary>
    /// 즐겨찾기의 정본. <see cref="Roots"/> 앞쪽 <see cref="favorites"/>.Count 개가 평평한
    /// 저장 모델이고, 화면에서는 <see cref="FavoriteGroup"/> 아래에 같은 노드를 모은다.
    /// </summary>
    private readonly List<Favorite> favorites = [];

    private TreeNodeViewModel? editing;
    private bool isVisible = true;
    private double width = DefaultWidth;

    public FolderTreeViewModel(
        IDriveList drives,
        INetworkPlaceList places,
        IFavoriteStore favorites,
        IFolderSource folders,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(dispatcher);

        this.drives = drives;
        this.places = places;
        favoriteStore = favorites;
        this.folders = folders;
        this.dispatcher = dispatcher;
    }

    /// <summary>고른 폴더. 활성 페인이 여기로 간다 (사용자 결정 2026-08-10).</summary>
    public event EventHandler<LocationId>? NavigationRequested;

    /// <summary>화면에서 즐겨찾기를 한 단계 아래에 모으는 표시 전용 그룹.</summary>
    public FavoriteGroupViewModel FavoriteGroup { get; } = new();

    /// <summary>트리에 실제로 그릴 루트. 즐겨찾기 그룹 하나 뒤에 드라이브와 서버가 온다.</summary>
    public ObservableCollection<object> DisplayRoots { get; } = [];

    /// <summary>
    /// 숨김·시스템 폴더를 트리에 낼 것인가 (docs/PRD-v2.md §12). 판정은
    /// <see cref="ItemVisibility"/> 하나가 하고 목록도 같은 것을 쓴다 — 트리에는 있는데
    /// 목록에는 없는 폴더가 생기면 안 된다.
    ///
    /// <para>
    /// <b>바뀌었다고 스스로 다시 읽지 않는다.</b> 언제 다시 읽을지는
    /// <c>WorkspaceViewModel</c> 이 정한다 (<see cref="ReloadFoldersAsync"/>) — 페인이 같은
    /// 자리에서 같은 이유로 그렇게 한다.
    /// </para>
    /// </summary>
    public bool ShowHiddenItems { get; set; }

    /// <summary>드라이브 다음에 서버가 온다. 그 순서가 곧 화면 순서다.</summary>
    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    /// <summary>트리를 보이는가. 보조 모니터처럼 좁은 화면에서 가로 공간을 되찾는 길이다.</summary>
    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    /// <summary>
    /// 트리 폭. 범위를 벗어난 값은 예외 없이 가장 가까운 경계로 잘린다 —
    /// <see cref="WorkspaceViewModel.SplitterRatio"/> 와 같은 판단이다.
    /// </summary>
    public double Width
    {
        get => width;
        set => SetProperty(ref width, Clamp(value));
    }

    /// <summary>
    /// 루트를 다시 세운다. 드라이브가 붙거나 빠졌을 때도 이것을 부른다 —
    /// <b>더하지 않고 갈아 끼운다</b>.
    /// <para>
    /// 펼쳐 둔 모양은 유지되지 않는다. 드라이브 구성이 바뀌는 것은 드문 일이고, 유지하려면
    /// 어느 노드가 열려 있었는지를 경로로 되짚어야 하는데 그 되짚기가 전부 저장소 호출이다.
    /// </para>
    /// </summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        // 전부 저장소에 닿는다. 함께 기다린다 — 하나가 느려도 다른 쪽을 막지 않는다.
        var listing = drives.ListAsync(ct);
        var pinned = await favoriteStore.LoadAsync(ct).ConfigureAwait(false);
        var registered = await places.ListAsync(ct).ConfigureAwait(false);
        var listed = await listing.ConfigureAwait(false);

        var roots = new List<TreeNodeViewModel>(pinned.Count + listed.Count + registered.Count);

        // 저장 모델에서는 즐겨찾기를 앞에 둔다. 화면은 RefreshDisplayRoots 가 이 노드들을
        // 맨 위의 즐겨찾기 그룹 아래로 모은다.
        favorites.Clear();
        favorites.AddRange(pinned);

        foreach (var favorite in pinned)
        {
            roots.Add(NewNode(favorite.Path, favorite.Label, isFavorite: true));
        }

        foreach (var drive in listed)
        {
            // 매핑 드라이브는 경로만 보면 로컬이다 — 그 사실을 아는 곳이 여기뿐이다.
            roots.Add(NewNode(drive.Root, drive.Label, isNetwork: drive.Server is not null));
        }

        // 같은 NAS 를 두 글자에 매핑하는 것은 흔하다. 서버가 두 번 서면 같은 것을 두 번
        // 펼치게 된다.
        foreach (var server in listed.Select(drive => drive.Server).OfType<LocationId>().Distinct())
        {
            roots.Add(NewNode(server, server.DisplayPath));
        }

        // '네트워크 위치 추가' 로 등록한 곳. 맨 아래이고 <b>등록한 이름 그대로</b> 선다
        // (사용자 결정 2026-08-10) — 서버로 접으면 깊은 경로를 등록한 이유가 사라진다.
        foreach (var place in registered)
        {
            roots.Add(NewNode(place.Path, place.Label));
        }

        await dispatcher.InvokeAsync(() =>
        {
            Roots.Clear();

            foreach (var root in roots)
            {
                Roots.Add(root);
            }

            RefreshDisplayRoots();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 노드의 자식을 읽어 채운다. 이미 읽었으면 아무 일도 하지 않는다.
    /// <para>
    /// <b>실패는 빈 폴더로 접는다.</b> 권한 없는 폴더·사라진 드라이브·응답 없는 서버가 전부
    /// 여기로 온다 — 트리는 곁다리이고, 못 읽는 것이 앱을 멈추는 사건이 되면 안 된다.
    /// 그 폴더를 정말 열려고 하면 페인이 사유를 상태표시줄에 낸다.
    /// </para>
    /// </summary>
    public async Task ExpandAsync(TreeNodeViewModel node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.IsRealized)
        {
            return;
        }

        // 네트워크라는 사실은 부모가 안다 — 매핑 드라이브 아래는 경로만으로 알 수 없어서,
        // 물려받지 않으면 한 단계만 내려가도 로컬처럼 보인다.
        var children = await ReadFoldersAsync(node.Location, node.IsNetwork, ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() => node.Realize(children)).ConfigureAwait(false);
    }

    /// <summary>트리에 펼쳐진 노드를 깊이와 관계없이 모두 접는다. 읽어 둔 자식은 유지한다.</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        FavoriteGroup.IsExpanded = false;

        foreach (var root in Roots)
        {
            Collapse(root);
        }
    }

    private static void Collapse(TreeNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            Collapse(child);
        }

        node.IsExpanded = false;
    }

    /// <summary>
    /// 이미 펼쳐 둔 노드를 다시 읽는다. 숨김 설정이 바뀌었을 때 부른다.
    ///
    /// <para>
    /// <b><see cref="LoadAsync"/> 를 다시 부르지 않는다.</b> 그쪽은 루트를 통째로 세우므로
    /// 펼쳐 둔 폴더가 전부 접힌다 — 설정 하나 바꿨다고 보고 있던 자리를 잃으면 안 된다
    /// (즐겨찾기가 앞부분만 갈아 끼우는 것과 같은 판단 · docs/PRD-v2.md §10-2).
    /// </para>
    ///
    /// <para>
    /// <b>펼치지 않은 노드는 건드리지 않는다.</b> 트리에 선 모든 폴더를 읽으면 지연 로딩을
    /// 고른 이유가 사라진다 (CLAUDE.md §3) — 다음에 펼칠 때 새 설정으로 읽힌다.
    /// </para>
    /// </summary>
    public async Task ReloadFoldersAsync(CancellationToken ct = default)
    {
        // 지금 찍는다. 아래에서 자식을 갈아 끼우는 동안 Roots 가 흔들리지 않아야 한다.
        var opened = Roots.Where(root => root.IsRealized).ToList();

        foreach (var root in opened)
        {
            await ReloadAsync(root, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 노드 하나와 <b>그 아래 펼쳐 둔 것들</b>을 다시 읽는다.
    /// <para>
    /// 무엇이 열려 있었는지를 <b>갈아 끼우기 전에</b> 찍어 둔다 — <c>Realize</c> 가 자식
    /// 인스턴스를 통째로 바꾸므로 그 뒤에는 물어볼 대상이 없다.
    /// </para>
    /// </summary>
    private async Task ReloadAsync(TreeNodeViewModel node, CancellationToken ct)
    {
        var reopen = node.Children
            .Where(child => child.IsRealized)
            .Select(child => child.Location)
            .ToHashSet();

        var children = await ReadFoldersAsync(node.Location, node.IsNetwork, ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() => node.Realize(children)).ConfigureAwait(false);

        foreach (var child in children)
        {
            if (!reopen.Contains(child.Location))
            {
                continue;
            }

            // 먼저 채우고 나서 편다. 순서를 뒤집으면 IsExpanded 세터가 ExpandAsync 를
            // 기다리지 않고 걸어(TreeNodeViewModel) 같은 폴더를 두 번 읽는다.
            await ReloadAsync(child, ct).ConfigureAwait(false);

            await dispatcher.InvokeAsync(() => child.IsExpanded = true).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<TreeNodeViewModel>> ReadFoldersAsync(
        LocationId folder,
        bool isNetwork,
        CancellationToken ct)
    {
        var names = new List<(string Name, LocationId Location)>();

        try
        {
            await foreach (var item in folders.EnumerateAsync(folder, ct).ConfigureAwait(false))
            {
                // 파일은 트리에 서지 않는다. 숨김·시스템은 목록과 <b>같은 판정</b>을 지난다
                // (ItemVisibility) — 열거는 있는 것을 다 내므로 여기서 거르지 않으면
                // $Recycle.Bin 이 트리에만 남는다.
                if (item.IsDirectory && ItemVisibility.IsVisible(item, ShowHiddenItems))
                {
                    names.Add((item.Name, item.Location));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 예외 종류로 가르지 않는다 — 이 자리에 필요한 답은 "무엇이 잘못됐나" 가 아니라
            // "무엇을 그릴까" 이고, 답은 어느 경우든 '아무것도 없음' 이다.
            return [];
        }

        names.Sort((left, right) => NaturalStringComparer.Instance.Compare(left.Name, right.Name));

        return [.. names.Select(entry => NewNode(entry.Location, entry.Name, isNetwork: isNetwork))];
    }

    // ── 즐겨찾기 (docs/PRD-v2.md §10-2) ──────────────────────────────

    /// <summary>
    /// 폴더를 즐겨찾기 그룹에 고정한다. 이미 있으면 아무 일도 하지 않는다 — 같은 곳이 두 번
    /// 서면 어느 것을 지워야 할지 알 수 없다.
    /// </summary>
    public Task AddFavoriteAsync(LocationId path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (IndexOfFavorite(path) >= 0)
        {
            return Task.CompletedTask;
        }

        // 이름은 폴더 이름으로 시작한다. 바꾸는 것은 사용자 몫이다.
        favorites.Add(new Favorite(path, path.Name));

        return ApplyAsync(ct);
    }

    /// <summary>
    /// 끌어다 놓은 경로들을 고정한다 (목록 → 트리 드래그).
    /// <para>
    /// <b>폴더만 들어간다.</b> 파일을 고정하면 트리에서 펼칠 수도 없고 골라도 페인이 열지
    /// 못한다. 무엇이 폴더인지는 파일시스템에 물어야 알고 그 물음은 저장소에 닿으므로
    /// (CLAUDE.md §3) 여기서 한다 — View 는 경로 문자열만 넘긴다.
    /// </para>
    /// <para>
    /// 없는 경로도 빠진다. <c>TryGetItemAsync</c> 가 존재 확인을 겸한다.
    /// 저장은 <b>마지막에 한 번</b>이다 — 열 개를 끌어다 놓고 열 번 쓰지 않는다.
    /// </para>
    /// </summary>
    public async Task AddFavoritesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = false;

        foreach (var path in paths)
        {
            if (!LocationId.TryParse(path, out var location, out _) || IndexOfFavorite(location) >= 0)
            {
                continue;
            }

            if (await IsFolderAsync(location, ct).ConfigureAwait(false))
            {
                favorites.Add(new Favorite(location, location.Name));
                added = true;
            }
        }

        if (added)
        {
            await ApplyAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 실제로 있는 폴더인가. 못 읽으면 아니라고 본다 — 트리에 올려도 갈 수 없는 것은
    /// 없는 것과 같다.
    /// </summary>
    private async Task<bool> IsFolderAsync(LocationId location, CancellationToken ct)
    {
        // 루트(드라이브·서버)는 부모가 없어 '그 폴더의 항목' 으로 물을 수가 없다.
        // 그리고 루트는 늘 폴더다 — 물어볼 필요 자체가 없다.
        if (!location.TryGetParent(out _))
        {
            return true;
        }

        try
        {
            return await folders.TryGetItemAsync(location, ct).ConfigureAwait(false) is { IsDirectory: true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>고정을 푼다. 즐겨찾기가 아닌 노드에는 아무 일도 하지 않는다.</summary>
    public Task RemoveFavoriteAsync(TreeNodeViewModel node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = node.IsFavorite ? IndexOfFavorite(node.Location) : -1;

        if (index < 0)
        {
            return Task.CompletedTask;
        }

        favorites.RemoveAt(index);

        return ApplyAsync(ct);
    }

    /// <summary>
    /// 즐겨찾기의 이름을 바꾼다. 경로는 그대로다 — 같은 이름의 폴더가 여럿일 때
    /// (<c>build</c> 가 셋일 수 있다) 그것을 가르는 유일한 수단이다.
    /// <para>
    /// 비어 있으면 그대로 둔다. 편집을 지우고 확정한 것은 취소로 본다 — 빈 라벨은 트리에
    /// 빈 줄로 선다.
    /// </para>
    /// </summary>
    public Task RenameFavoriteAsync(TreeNodeViewModel node, string label, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = node.IsFavorite ? IndexOfFavorite(node.Location) : -1;

        if (index < 0 || string.IsNullOrWhiteSpace(label))
        {
            return Task.CompletedTask;
        }

        favorites[index] = favorites[index] with { Label = label.Trim() };

        return ApplyAsync(ct);
    }

    /// <summary>
    /// 즐겨찾기를 <paramref name="offset"/> 만큼 위(음수)·아래(양수)로 옮긴다.
    /// 끝을 넘어가면 아무 일도 하지 않는다 — 감아 돌면 맨 위를 한 번 더 누른 사람이
    /// 맨 아래에서 그것을 찾게 된다.
    /// </summary>
    public Task MoveFavoriteAsync(TreeNodeViewModel node, int offset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = node.IsFavorite ? IndexOfFavorite(node.Location) : -1;
        var target = index + offset;

        if (index < 0 || offset == 0 || target < 0 || target >= favorites.Count)
        {
            return Task.CompletedTask;
        }

        var moved = favorites[index];
        favorites.RemoveAt(index);
        favorites.Insert(target, moved);

        return ApplyAsync(ct);
    }

    // ── 트리 메뉴가 부르는 것 ────────────────────────────────────────
    //
    // 메서드가 이미 있는데 커맨드를 따로 두는 이유: 메뉴는 노드 하나만 넘길 수 있고
    // (ICommand 의 파라미터는 하나다) 위·아래는 같은 메서드의 부호만 다르다.

    /// <summary>평범한 노드를 고정한다. 트리 우클릭의 진입점이다.</summary>
    [RelayCommand]
    private Task PinAsync(TreeNodeViewModel node, CancellationToken ct)
        => AddFavoriteAsync(node.Location, ct);

    [RelayCommand]
    private Task UnpinAsync(TreeNodeViewModel node, CancellationToken ct)
        => RemoveFavoriteAsync(node, ct);

    [RelayCommand]
    private Task MoveUpAsync(TreeNodeViewModel node, CancellationToken ct)
        => MoveFavoriteAsync(node, -1, ct);

    [RelayCommand]
    private Task MoveDownAsync(TreeNodeViewModel node, CancellationToken ct)
        => MoveFavoriteAsync(node, +1, ct);

    /// <summary>
    /// 즐겨찾기만 이름을 고칠 수 있다 — 나머지는 시스템이 주는 이름이다.
    /// <para>
    /// 편집 중인 노드를 여기서 기억한다. 목록의 이름변경이 <c>RenamingName</c> 을 두는 것과
    /// 같은 구도이며, 그래야 편집기가 확정할 때 <b>글자만</b> 넘기면 된다 — 편집기는 목록과
    /// 공용이고 (<c>RenameEditor</c>) 그쪽은 노드를 모른다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void BeginRename(TreeNodeViewModel node)
    {
        if (!node.IsFavorite)
        {
            return;
        }

        editing = node;
        node.BeginEditing();
    }

    [RelayCommand]
    private async Task CommitRenameAsync(string? label, CancellationToken ct)
    {
        if (editing is not { } node)
        {
            return;
        }

        // 먼저 닫는다. 이름 반영이 실패하더라도 편집기가 열린 채로 남으면 안 된다.
        editing = null;
        node.EndEditing();

        await RenameFavoriteAsync(node, label ?? string.Empty, ct).ConfigureAwait(false);
    }

    /// <summary>포커스 상실도 여기로 온다 (docs/DESIGN.md §9-1). 그래서 멱등이다.</summary>
    [RelayCommand]
    private void CancelRename()
    {
        editing?.EndEditing();
        editing = null;
    }

    /// <summary>이름 비교 규칙은 파일시스템과 같다 — <c>C:\work</c> 와 <c>C:\WORK</c> 는 같은 곳이다.</summary>
    private int IndexOfFavorite(LocationId path)
        => favorites.FindIndex(favorite => favorite.Path.Equals(path));

    /// <summary>
    /// 바뀐 즐겨찾기를 화면과 파일에 반영한다.
    /// <para>
    /// <b>루트를 다시 세우지 않고 앞부분만 갈아 끼운다.</b> 통째로 세우면 펼쳐 둔 폴더가
    /// 전부 접힌다 — 즐겨찾기 하나 넣었다고 보고 있던 자리를 잃으면 안 된다.
    /// </para>
    /// <para>
    /// <b>저장 실패를 삼킨다.</b> 화면에서 방금 한 조작이 사라지는 것이 더 나쁘고, 다음
    /// 조작이 목록 전체를 다시 저장하므로 한 번의 실패가 영구적이지 않다.
    /// </para>
    /// </summary>
    private async Task ApplyAsync(CancellationToken ct)
    {
        var shown = favorites.Count;
        var nodes = favorites.Select(favorite => NewNode(favorite.Path, favorite.Label, isFavorite: true)).ToList();

        await dispatcher.InvokeAsync(() =>
        {
            while (Roots.Count > 0 && Roots[0].IsFavorite)
            {
                Roots.RemoveAt(0);
            }

            for (var index = 0; index < shown; index++)
            {
                Roots.Insert(index, nodes[index]);
            }

            RefreshDisplayRoots();
        }).ConfigureAwait(false);

        try
        {
            await favoriteStore.SaveAsync([.. favorites], ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // 위 §요약. 다음 조작이 다시 시도한다.
        }
    }

    /// <summary>
    /// 노드를 만들며 트리로 돌아오는 두 통로를 물린다. 노드가 포트를 들지 않는 이유는
    /// <see cref="TreeNodeViewModel"/> 에 있다.
    /// </summary>
    private TreeNodeViewModel NewNode(
        LocationId location,
        string label,
        bool isFavorite = false,
        bool isNetwork = false)
        => new(
            location,
            label,
            node => ExpandAsync(node),
            node => NavigationRequested?.Invoke(this, node.Location),
            isFavorite,
            isNetwork);

    /// <summary>평평한 저장 모델을 즐겨찾기 그룹 하나가 있는 화면 모델로 투영한다.</summary>
    private void RefreshDisplayRoots()
    {
        FavoriteGroup.ReplaceChildren(Roots.Where(node => node.IsFavorite));

        DisplayRoots.Clear();
        DisplayRoots.Add(FavoriteGroup);

        foreach (var root in Roots.Where(node => !node.IsFavorite))
        {
            DisplayRoots.Add(root);
        }
    }

    /// <summary>
    /// <c>Math.Clamp</c> 를 쓰지 않는 이유는 <see cref="WorkspaceViewModel"/> 과 같다 —
    /// NaN 은 모든 관계 비교가 false 라서 그대로 통과한다.
    /// </summary>
    private static double Clamp(double value)
        => value is >= MinWidth and <= MaxWidth
            ? value
            : value > MaxWidth ? MaxWidth : MinWidth;
}

/// <summary>
/// 경로 없이 즐겨찾기 노드만 묶는 표시용 트리 루트. 폴더가 아니므로 선택해도 탐색하지 않는다.
/// </summary>
public sealed partial class FavoriteGroupViewModel : ObservableObject
{
    private bool isExpanded;
    private bool isSelected;
    private bool canExpand;

    public string Label => "즐겨찾기";

    public LocationId? Location => null;

    public bool IsGroup => true;

    public bool IsFavorite => false;

    public bool IsNetwork => false;

    public bool CanExpand
    {
        get => canExpand;
        private set => SetProperty(ref canExpand, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    internal void ReplaceChildren(IEnumerable<TreeNodeViewModel> children)
    {
        Children.Clear();

        foreach (var child in children)
        {
            Children.Add(child);
        }

        CanExpand = Children.Count > 0;

        if (!CanExpand)
        {
            IsExpanded = false;
        }
    }
}
