using FlexDir.Core.Locations;
using FlexDir.Core.Operations;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IClipboardBridge"/> 의 메모리 fake. Windows 클립보드를 건드리지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 탐색기와 상호 호환되는 실제 구현체
/// (<c>CFSTR_SHELLIDLIST</c> + <c>Preferred DropEffect</c>)는 <c>FlexDir.Shell</c> 의
/// 몫이고 수동 검증 대상이다 (ADR-009).
/// </para>
/// <para>
/// 붙여넣은 뒤에도 내용을 비우지 않는다 — 잘라내기 후 붙여넣기가 클립보드를 비우는지는
/// shell 의 정책이고, 포트는 그것을 규정하지 않는다.
/// </para>
/// </summary>
public sealed class FakeClipboardBridge : IClipboardBridge
{
    private IReadOnlyList<LocationId> content = [];
    private bool contentIsMove;
    private bool hasContent;

    /// <summary><see cref="SetCopy"/> 로 들어온 항목 묶음을 순서대로 기록한다.</summary>
    public List<IReadOnlyList<LocationId>> CopySets { get; } = [];

    /// <summary><see cref="SetCut"/> 로 들어온 항목 묶음을 순서대로 기록한다.</summary>
    public List<IReadOnlyList<LocationId>> CutSets { get; } = [];

    /// <summary><see cref="TryGetPaste"/> 가 불린 횟수.</summary>
    public int PasteQueries { get; private set; }

    public void SetCopy(IReadOnlyList<LocationId> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CopySets.Add(items);
        Put(items, isMove: false);
    }

    public void SetCut(IReadOnlyList<LocationId> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CutSets.Add(items);
        Put(items, isMove: true);
    }

    public bool TryGetPaste(out IReadOnlyList<LocationId> items, out bool isMove)
    {
        PasteQueries++;

        // 비었을 때도 out 인자는 쓸 수 있는 값이어야 한다 — 호출자가 false 를 무시하고
        // 열거해도 NullReferenceException 이 나지 않는다.
        items = hasContent ? content : [];
        isMove = hasContent && contentIsMove;

        return hasContent;
    }

    private void Put(IReadOnlyList<LocationId> items, bool isMove)
    {
        content = items;
        contentIsMove = isMove;
        hasContent = true;
    }
}
