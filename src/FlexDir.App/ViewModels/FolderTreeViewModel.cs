using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FlexDir.App.Threading;

using FlexDir.Core.Enumeration;
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
    private readonly IFolderSource folders;
    private readonly IUiDispatcher dispatcher;

    private bool isVisible = true;
    private double width = DefaultWidth;

    public FolderTreeViewModel(
        IDriveList drives,
        INetworkPlaceList places,
        IFolderSource folders,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(dispatcher);

        this.drives = drives;
        this.places = places;
        this.folders = folders;
        this.dispatcher = dispatcher;
    }

    /// <summary>고른 폴더. 활성 페인이 여기로 간다 (사용자 결정 2026-08-10).</summary>
    public event EventHandler<LocationId>? NavigationRequested;

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
        // 둘 다 저장소에 닿는다. 함께 기다린다 — 하나가 느려도 다른 쪽을 막지 않는다.
        var listing = drives.ListAsync(ct);
        var registered = await places.ListAsync(ct).ConfigureAwait(false);
        var listed = await listing.ConfigureAwait(false);

        var roots = new List<TreeNodeViewModel>(listed.Count + registered.Count);

        foreach (var drive in listed)
        {
            roots.Add(NewNode(drive.Root, drive.Label));
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

        var children = await ReadFoldersAsync(node.Location, ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() => node.Realize(children)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TreeNodeViewModel>> ReadFoldersAsync(LocationId folder, CancellationToken ct)
    {
        var names = new List<(string Name, LocationId Location)>();

        try
        {
            await foreach (var item in folders.EnumerateAsync(folder, ct).ConfigureAwait(false))
            {
                // 파일은 트리에 서지 않는다. 숨김·시스템 정책은 열거가 이미 정한 것을
                // 그대로 따른다 — 목록과 트리가 다른 것을 보여주면 안 된다.
                if (item.IsDirectory)
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

        return [.. names.Select(entry => NewNode(entry.Location, entry.Name))];
    }

    /// <summary>
    /// 노드를 만들며 트리로 돌아오는 두 통로를 물린다. 노드가 포트를 들지 않는 이유는
    /// <see cref="TreeNodeViewModel"/> 에 있다.
    /// </summary>
    private TreeNodeViewModel NewNode(LocationId location, string label)
        => new(
            location,
            label,
            node => ExpandAsync(node),
            node => NavigationRequested?.Invoke(this, node.Location));

    /// <summary>
    /// <c>Math.Clamp</c> 를 쓰지 않는 이유는 <see cref="WorkspaceViewModel"/> 과 같다 —
    /// NaN 은 모든 관계 비교가 false 라서 그대로 통과한다.
    /// </summary>
    private static double Clamp(double value)
        => value is >= MinWidth and <= MaxWidth
            ? value
            : value > MaxWidth ? MaxWidth : MinWidth;
}
