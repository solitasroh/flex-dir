using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.Core.Tests.Tools;

/// <summary>
/// 탐지 포트의 계약을 fake 로 한 번 통과시킨다. 실제 구현체는 <c>FlexDir.Shell</c> 의
/// 몫이다 (<c>IKnownFolderListTests</c> 와 같은 구도 — 포트에는
/// 동작이 없으므로 채점하는 것은 <b>계약</b>이다).
/// <para>
/// 계약 셋: ① 못 찾으면 예외가 아니라 <c>null</c> ② 취소만 예외 ③ 캐시하지 않는다.
/// </para>
/// </summary>
public class FakeExternalToolCatalogTests
{
    // ── 못 찾는 것은 사건이 아니다 ──────────────────────────────────

    [Fact]
    public async Task FindEditorAsync_WithNothingInstalled_IsNullNotThrow()
    {
        // 외부 도구는 곁다리다. 없다는 것이 예외가 되면 호출자가 매번 try 로 감싸야 하고
        // 그 자리가 늘어나면 언젠가 하나가 폴더 여는 길까지 끊는다.
        var catalog = new FakeExternalToolCatalog();

        Assert.Null(await catalog.FindEditorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FindEditorAsync_GivesWhatWasPutIn()
    {
        var catalog = new FakeExternalToolCatalog { Editor = @"C:\Program Files\Microsoft VS Code\Code.exe" };

        Assert.Equal(@"C:\Program Files\Microsoft VS Code\Code.exe", await catalog.FindEditorAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal)]
    [InlineData(TerminalPreset.PowerShell7)]
    [InlineData(TerminalPreset.WindowsPowerShell)]
    [InlineData(TerminalPreset.CommandPrompt)]
    [InlineData(TerminalPreset.GitBash)]
    [InlineData(TerminalPreset.Custom)]
    public async Task FindTerminalAsync_AnswersEveryPreset_WithoutThrowing(TerminalPreset preset)
    {
        // 여섯 값 전부가 물어볼 수 있는 대상이다 — 하나라도 던지면 설정 목록을 그리다 멈춘다.
        var catalog = new FakeExternalToolCatalog();

        Assert.Null(await catalog.FindTerminalAsync(preset, CancellationToken.None));
    }

    [Fact]
    public async Task FindTerminalAsync_GivesWhatWasPutIn()
    {
        var catalog = new FakeExternalToolCatalog();
        catalog.Terminals[TerminalPreset.WindowsTerminal] = @"C:\Program Files\WindowsApps\wt.exe";

        Assert.Equal(
            @"C:\Program Files\WindowsApps\wt.exe",
            await catalog.FindTerminalAsync(TerminalPreset.WindowsTerminal, CancellationToken.None));
    }

    /// <summary>
    /// 사용자가 적은 경로는 탐지의 대상이 아니라 <b>입력</b>이다. 여기서 찾으려 들면
    /// 오타를 조용히 고쳐 버린다 — 사용자가 적은 그대로 띄우고 실패를 보여야 한다.
    /// </summary>
    [Fact]
    public async Task FindTerminalAsync_Custom_IsAlwaysNull()
    {
        var catalog = new FakeExternalToolCatalog();
        catalog.Terminals[TerminalPreset.Custom] = @"C:\tools\my-shell.exe";

        Assert.Null(await catalog.FindTerminalAsync(TerminalPreset.Custom, CancellationToken.None));
    }

    // ── 취소만 예외다 ───────────────────────────────────────────────

    [Fact]
    public async Task FindEditorAsync_WithCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeExternalToolCatalog().FindEditorAsync(cts.Token));
    }

    [Fact]
    public async Task FindTerminalAsync_WithCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FakeExternalToolCatalog().FindTerminalAsync(TerminalPreset.PowerShell7, cts.Token));
    }

    // ── 캐시하지 않는다 ─────────────────────────────────────────────

    /// <summary>
    /// 상주 앱(ADR-003)이라 창이 다시 보일 때마다 다시 묻는다 — 그 사이에 사용자가
    /// VS Code 를 설치하거나 지웠을 수 있다. 두 번 물으면 <b>두 번 세야</b> 한다.
    /// </summary>
    [Fact]
    public async Task FindEditorAsync_AskedTwice_CountsTwice()
    {
        var catalog = new FakeExternalToolCatalog();

        await catalog.FindEditorAsync(CancellationToken.None);
        await catalog.FindEditorAsync(CancellationToken.None);

        Assert.Equal(2, catalog.EditorRequests);
    }

    [Fact]
    public async Task FindTerminalAsync_RecordsEveryRequestInOrder()
    {
        var catalog = new FakeExternalToolCatalog();

        await catalog.FindTerminalAsync(TerminalPreset.GitBash, CancellationToken.None);
        await catalog.FindTerminalAsync(TerminalPreset.GitBash, CancellationToken.None);
        await catalog.FindTerminalAsync(TerminalPreset.CommandPrompt, CancellationToken.None);

        Assert.Equal(
            [TerminalPreset.GitBash, TerminalPreset.GitBash, TerminalPreset.CommandPrompt],
            catalog.TerminalRequests);
    }
}
