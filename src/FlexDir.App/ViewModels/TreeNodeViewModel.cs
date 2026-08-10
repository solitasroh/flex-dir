using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FlexDir.Core.Locations;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 폴더 트리의 노드 하나 (docs/PRD-v2.md §10). 자기 위치와 자식만 안다 — 자식을 실제로
/// 읽어 오는 것은 <see cref="FolderTreeViewModel"/> 이고 여기서는 콜백으로 부탁한다.
/// <para>
/// <b>노드가 <c>IFolderSource</c> 를 직접 들지 않는 이유</b>: 그러면 노드 하나를 만들 때마다
/// 포트를 물려야 하고, 노드 테스트가 열거 fake 를 요구하게 된다. 여기 있는 것은 "언제
/// 물어보는가" 이고 "무엇이 오는가" 는 트리의 몫이다.
/// </para>
/// <para>
/// <b>더미 자식을 넣지 않는다.</b> 지연 로딩 트리가 확장 화살표를 그리려고 흔히 쓰는
/// 수법이지만, 목록에 없는 가짜 항목이 섞이면 (CLAUDE.md §4) 선택과 키보드 이동이 그것을
/// 밟는다. 대신 <see cref="CanExpand"/> 를 View 가 본다.
/// </para>
/// </summary>
public sealed partial class TreeNodeViewModel : ObservableObject
{
    private readonly Func<TreeNodeViewModel, Task>? expand;
    private readonly Action<TreeNodeViewModel>? select;

    private string label;
    private string editingLabel = string.Empty;
    private bool isEditing;
    private bool isExpanded;
    private bool isSelected;
    private bool canExpand = true;

    /// <param name="expand">
    /// 자식이 필요할 때 부른다. <b>결과를 기다리지 않는다</b> — 바인딩 세터는 동기이고
    /// 열거는 저장소에 닿기 때문이다 (CLAUDE.md §3). 실패를 삼키는 것은 부르는 쪽의 몫이다.
    /// </param>
    /// <param name="select">
    /// 선택됐을 때 부른다. 해제될 때는 부르지 않는다 — TreeView 는 선택이 옮겨갈 때 이전
    /// 노드를 먼저 해제하므로, 해제까지 탐색을 일으키면 폴더 하나에 페인이 두 번 움직인다.
    /// </param>
    /// <param name="isFavorite">
    /// 사용자가 고정한 항목인가. 제거·이름 바꾸기·순서 바꾸기가 붙는 것은 이쪽뿐이라
    /// 메뉴가 이 값으로 갈린다 (docs/PRD-v2.md §10-2).
    /// </param>
    public TreeNodeViewModel(
        LocationId location,
        string label,
        Func<TreeNodeViewModel, Task>? expand = null,
        Action<TreeNodeViewModel>? select = null,
        bool isFavorite = false)
    {
        ArgumentNullException.ThrowIfNull(location);

        Location = location;
        this.label = label;
        this.expand = expand;
        this.select = select;
        IsFavorite = isFavorite;
    }

    /// <summary>이 노드가 가리키는 폴더. 선택하면 활성 페인이 여기로 간다.</summary>
    public LocationId Location { get; }

    /// <summary>
    /// 트리에 그릴 이름. 드라이브는 <c>로컬 디스크 (C:)</c>, 폴더는 폴더 이름이다.
    /// <b>즐겨찾기만 바뀐다</b> — 나머지는 시스템이 주는 이름이라 우리가 정할 것이 없다.
    /// </summary>
    public string Label
    {
        get => label;
        internal set => SetProperty(ref label, value);
    }

    /// <summary>사용자가 고정한 항목인가. 트리 맨 위에 서고 메뉴가 다르다.</summary>
    public bool IsFavorite { get; }

    /// <summary>
    /// 이름을 고치는 중인가. View 가 이것으로 편집기를 연다 — 목록의 이름변경이
    /// <c>RenamingName</c> 으로 여는 것과 같은 구도다 (docs/DESIGN.md §9-1).
    /// </summary>
    public bool IsEditing
    {
        get => isEditing;
        private set => SetProperty(ref isEditing, value);
    }

    /// <summary>
    /// 편집기 안의 글자. 확정하기 전에는 <see cref="Label"/> 을 건드리지 않는다 —
    /// 타이핑하는 동안 트리의 이름이 따라 바뀌면 취소할 자리가 없다.
    /// </summary>
    public string EditingLabel
    {
        get => editingLabel;
        set => SetProperty(ref editingLabel, value);
    }

    /// <summary>편집기를 연다. 지난 입력이 남지 않게 지금 이름을 다시 싣는다.</summary>
    internal void BeginEditing()
    {
        EditingLabel = Label;
        IsEditing = true;
    }

    /// <summary>편집기를 닫는다. <b>멱등이다</b> — 확정으로 닫힌 뒤에도 포커스 상실이 한 번 더 온다.</summary>
    internal void EndEditing() => IsEditing = false;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>자식을 이미 읽어 왔는가. 접었다 펴는 것이 다시 열거하지 않게 하는 값이다.</summary>
    public bool IsRealized { get; private set; }

    /// <summary>
    /// 펼칠 수 있는가. <b>읽어 오기 전에는 참이다</b> — 하위 폴더가 있는지 미리 알려면 그
    /// 폴더를 열어 봐야 하고, 트리에 선 모든 노드에 그것을 하면 저장소 호출이 폭발한다.
    /// 탐색기도 같은 낙관을 쓴다. 열어 본 뒤 비어 있으면 그때 내린다.
    /// </summary>
    public bool CanExpand
    {
        get => canExpand;
        private set => SetProperty(ref canExpand, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!SetProperty(ref isExpanded, value) || !value || IsRealized)
            {
                return;
            }

            // 기다리지 않는다 (위 §expand). 트리가 예외를 잡으므로 여기서 관측되지 않는
            // 예외가 되지 않는다.
            _ = expand?.Invoke(this);
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value) && value)
            {
                select?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// 읽어 온 자식으로 채운다. 비어 있으면 <see cref="CanExpand"/> 가 내려간다 —
    /// 열어 봤더니 하위 폴더가 없었다는 뜻이고, 화살표를 남기면 눌러도 아무 일이 없다.
    /// </summary>
    public void Realize(IReadOnlyList<TreeNodeViewModel> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        Children.Clear();

        foreach (var child in children)
        {
            Children.Add(child);
        }

        IsRealized = true;
        CanExpand = children.Count > 0;
    }
}
