using FlexDir.Core.Tools;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IExternalToolCatalog"/> 의 fake. 넣어 둔 것만 답하고 나머지는
/// <c>null</c> 이다 — 실물의 "이 기계에 없다" 와 같은 자리다.
/// <para>
/// 요청 횟수를 세는 것이 이 fake 의 절반이다. 포트가 <b>캐시하지 않는</b> 계약이라
/// (상주 앱이라 창이 다시 보일 때마다 다시 묻는다) 호출자가 정말 다시 물었는지를
/// <see cref="EditorRequests"/>·<see cref="TerminalRequests"/> 로 채점한다.
/// </para>
/// </summary>
public sealed class FakeExternalToolCatalog : IExternalToolCatalog
{
    private readonly List<TerminalPreset> _terminalRequests = [];

    /// <summary>에디터의 실행 파일 경로. <c>null</c> 이면 이 기계에 없다.</summary>
    public string? Editor { get; set; }

    /// <summary>프리셋별 실행 파일 경로. 없는 프리셋은 <c>null</c> 로 답한다.</summary>
    public Dictionary<TerminalPreset, string?> Terminals { get; } = [];

    /// <summary>에디터를 몇 번 물었는지.</summary>
    public int EditorRequests { get; private set; }

    /// <summary>어느 프리셋을 어떤 순서로 물었는지.</summary>
    public IReadOnlyList<TerminalPreset> TerminalRequests => _terminalRequests;

    public ValueTask<string?> FindEditorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        EditorRequests++;

        return ValueTask.FromResult(Editor);
    }

    public ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _terminalRequests.Add(preset);

        // Custom 은 사용자가 적은 것이라 탐지 대상이 아니다 — 사전에 무엇이 있든 null 이다.
        // 실물도 같아야 하므로 fake 가 먼저 그렇게 답한다.
        if (preset == TerminalPreset.Custom)
        {
            return ValueTask.FromResult<string?>(null);
        }

        return ValueTask.FromResult(Terminals.TryGetValue(preset, out var path) ? path : null);
    }
}
