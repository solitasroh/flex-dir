using System.IO;

using FlexDir.App.Tests.Fakes;

using FlexDir.Host.Diagnostics;
using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// 창 표시에 계측을 두르는 자리 (phase B-2). <c>WindowShown</c> 은 활성화가 창 표시를
/// 맡긴 순간부터 창이 보일 때까지다 (docs/ARCHITECTURE.md §7 · 예산 100ms).
/// <para>
/// <c>Program</c> 의 표시 람다에 직접 재면 판단이 채점되지 않는 자리에 들어간다 —
/// <c>Program.cs</c> 는 TDD 가드의 검사 대상이 아니다.
/// </para>
/// <para>
/// 전부 <see cref="Path.GetTempPath"/> 아래에서 돈다. 실제
/// <c>%LOCALAPPDATA%\flex-dir\</c> 를 건드리면 게이트가 자기 테스트 실행을 계측으로 센다.
/// </para>
/// </summary>
public class WindowPresenterTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-presenter-tests", Guid.NewGuid().ToString("N"));

    private readonly ManualTimeProvider clock = new();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 정리 실패로 테스트를 실패로 만들지 않는다.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PresentAsync_ShowsTheWindow()
    {
        var shown = 0;
        var presenter = Presenter(_ =>
        {
            shown++;
            return Task.CompletedTask;
        });

        await presenter.PresentAsync(CancellationToken.None);

        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task PresentAsync_RecordsHowLongTheWindowTook()
    {
        var presenter = Presenter(_ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(42));
            return Task.CompletedTask;
        });

        await presenter.PresentAsync(CancellationToken.None);

        Assert.EndsWith("\tWindowShown\t42\t100\tok", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PresentAsync_MeasuresEveryPresentation()
    {
        // 첫 실행만이 아니다 — 상주 중 두 번째 실행도 "창을 요구한 순간" 이다 (ADR-003).
        var presenter = Presenter(_ => Task.CompletedTask);

        await presenter.PresentAsync(CancellationToken.None);
        await presenter.PresentAsync(CancellationToken.None);

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public async Task PresentAsync_WhenShowingFails_DoesNotRecord()
    {
        // 뜨지 않은 창의 시간을 남기면 그 수치를 나중에 사람이 믿는다.
        var presenter = Presenter(_ => throw new InvalidOperationException("창이 닫혔다"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => presenter.PresentAsync(CancellationToken.None));

        Assert.Empty(Lines());
    }

    [Fact]
    public void Arguments_AreInvalid_Throw()
    {
        var log = new PerformanceLog(root);

        Assert.Throws<ArgumentNullException>(() => new WindowPresenter(null!, log, clock));
        Assert.Throws<ArgumentNullException>(() => new WindowPresenter(_ => Task.CompletedTask, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new WindowPresenter(_ => Task.CompletedTask, log, null!));
    }

    private WindowPresenter Presenter(Func<CancellationToken, Task> show)
        => new(show, new PerformanceLog(root), clock);

    private string[] Lines()
    {
        var path = Path.Combine(root, PerformanceLog.FileName);

        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }
}
