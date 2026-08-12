using FlexDir.App.ViewModels;
using FlexDir.Core.ViewState;

namespace FlexDir.App.Tests;

/// <summary>
/// 2분할 워크스페이스를 좌·우로 읽는 helper (docs/PRD-v2.md §18).
/// <para>
/// <b>왜 필요해졌나:</b> v0.5.0 까지 페인은 <b>항상 둘</b>이었고 워크스페이스가
/// <c>Left</c>·<c>Right</c> 를 직접 냈다. 분할이 들어오며 기본이 1분할이 되고 자리는 번호가
/// 됐다 (사용자 결정 2026-08-12) — 그래서 두 페인을 전제로 쓴 테스트는 <b>먼저 벌려야</b> 한다.
/// </para>
/// <para>
/// <b>좌·우라는 이름을 테스트에 남긴 이유:</b> 2분할에서는 그것이 사실이다 —
/// <c>SplitLayout</c> 이 슬롯 0 을 왼쪽 열에, 슬롯 1 을 오른쪽 열에 앉힌다. 여기서 굳이
/// <c>Panes[0]</c> 로 읽으면 "반대편으로 복사" 를 검사하는 테스트가 무엇을 말하는지 흐려진다.
/// </para>
/// </summary>
public static class WorkspaceTestSupport
{
    /// <summary>
    /// 페인 둘로 벌린다. <b><c>RestoreAsync</c> 뒤에 불러야 한다</b> — 복원은 저장된 분할 수를
    /// 그대로 적용하므로 (기억이 없으면 1) 순서를 뒤집으면 도로 한 칸이 된다.
    /// </summary>
    public static WorkspaceViewModel Split2(this WorkspaceViewModel workspace)
    {
        workspace.SetSplitCommand.Execute(2);

        return workspace;
    }

    /// <summary>왼쪽 페인의 탭 목록. 2분할에서 슬롯 0 이다.</summary>
    public static PaneTabsViewModel LeftTabs(this WorkspaceViewModel workspace) => workspace.Panes[0];

    /// <summary>오른쪽 페인의 탭 목록. 2분할에서 슬롯 1 이다.</summary>
    public static PaneTabsViewModel RightTabs(this WorkspaceViewModel workspace) => workspace.Panes[1];

    /// <summary>왼쪽 페인의 활성 탭.</summary>
    public static PaneViewModel Left(this WorkspaceViewModel workspace) => workspace.Panes[0].Active;

    /// <summary>오른쪽 페인의 활성 탭.</summary>
    public static PaneViewModel Right(this WorkspaceViewModel workspace) => workspace.Panes[1].Active;

    /// <summary>
    /// 페인 둘을 기억한 전역 상태 — <b>분할 이전 저장 파일이 복원되는 모양</b>이다
    /// (<c>JsonViewStateStore.ToPanes</c> 가 좌·우 필드를 이렇게 접는다).
    /// </summary>
    /// <param name="second">
    /// <see langword="null"/> 이면 오른쪽 기억이 없다 — 그래도 분할은 둘이고, 모자란 자리는
    /// 워크스페이스가 활성 페인을 복제해 채운다.
    /// </param>
    public static GlobalViewState WithTwoPanes(
        this GlobalViewState state,
        PaneTabsState? first,
        PaneTabsState? second = null,
        PaneColumns? firstColumns = null,
        PaneColumns? secondColumns = null)
    {
        var panes = new List<PaneState>(2);

        if (first is not null)
        {
            panes.Add(new PaneState(first, firstColumns ?? PaneColumns.Default));
        }

        if (second is not null)
        {
            panes.Add(new PaneState(second, secondColumns ?? PaneColumns.Default));
        }

        return state with { Panes = panes.Count == 0 ? null : panes, PaneCount = 2 };
    }

    /// <summary>슬롯 <paramref name="slot"/> 의 탭 목록. 기억이 없으면 <see langword="null"/>.</summary>
    public static PaneTabsState? TabsAt(this GlobalViewState state, int slot)
        => state.Panes is { } panes && slot < panes.Count ? panes[slot].Tabs : null;

    /// <summary>슬롯 <paramref name="slot"/> 의 컬럼 폭. 기억이 없으면 <see langword="null"/>.</summary>
    public static PaneColumns? ColumnsAt(this GlobalViewState state, int slot)
        => state.Panes is { } panes && slot < panes.Count ? panes[slot].Columns : null;

    /// <summary>
    /// <b>2분할을 기억하고 있는</b> 저장소로 만든다 — v0.5.0 까지 쓰던 사람의 파일이 이
    /// 모양이다 (<c>JsonViewStateStore.ToPanes</c> 가 좌·우 필드를 그렇게 읽는다).
    /// <para>
    /// <see cref="Split2"/> 만으로는 모자라는 자리가 있어서 필요하다: <c>RestoreAsync</c> 는
    /// 저장된 분할 수를 그대로 적용하므로, 빈 저장소에서 복원하면 도로 1분할이 된다.
    /// 폴더를 기억시키는 테스트는 <see cref="WithTwoPanes"/> 로 저장하며 같은 값을 함께 쓴다.
    /// </para>
    /// </summary>
    public static TStore RememberingTwoPanes<TStore>(this TStore store)
        where TStore : IViewStateStore
    {
        // 인메모리라 그 자리에서 끝난다 — 필드 초기화에서 기다려도 막히지 않는다.
        store.SaveGlobalAsync(GlobalViewState.Default with { PaneCount = 2 }, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return store;
    }
}
