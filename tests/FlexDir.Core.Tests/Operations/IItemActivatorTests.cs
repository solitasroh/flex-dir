using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Operations;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이다 —
/// 실제 구현체(<c>ShellExecuteEx</c>)는 <c>FlexDir.Shell</c> 의 몫이고 수동 검증
/// 대상이다 (ADR-009).
/// <para>
/// ViewModel 테스트가 이 fake 로 재는 것은 대개 <b>불리지 않았다</b> 는 쪽이다 —
/// 폴더 진입이 활성화 포트를 타면 shell 이 새 탐색기 창을 띄운다. 그 단정문의 근거가
/// <see cref="FakeItemActivator.Activations"/> 이므로 기록이 먼저 믿을 만해야 한다.
/// </para>
/// </summary>
public class FakeItemActivatorTests
{
    [Fact]
    public async Task ActivateAsync_RecordsTheItem()
    {
        var activator = new FakeItemActivator();
        var item = Loc(@"C:\Temp\a.txt");

        await activator.ActivateAsync(item, CancellationToken.None);

        Assert.Equal(item, Assert.Single(activator.Activations));
    }

    [Fact]
    public async Task ActivateAsync_RecordsEveryCallInOrder()
    {
        var activator = new FakeItemActivator();
        var first = Loc(@"C:\Temp\a.txt");
        var second = Loc(@"C:\Temp\b.txt");

        await activator.ActivateAsync(first, CancellationToken.None);
        await activator.ActivateAsync(second, CancellationToken.None);

        Assert.Equal([first, second], activator.Activations);
    }

    [Fact]
    public async Task Failure_IsThrownAndTheCallIsStillRecorded()
    {
        var item = Loc(@"C:\Temp\a.txt");
        var activator = new FakeItemActivator
        {
            Failure = new LocationAccessException(LocationErrorKind.NotFound, item),
        };

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            () => activator.ActivateAsync(item, CancellationToken.None));

        Assert.Equal(LocationErrorKind.NotFound, error.Kind);
        Assert.Single(activator.Activations);
    }

    [Fact]
    public async Task ActivateAsync_ObservesCancellation()
    {
        var activator = new FakeItemActivator();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => activator.ActivateAsync(Loc(@"C:\Temp\a.txt"), cts.Token));

        Assert.Equal(1, activator.CancellationsObserved);
    }

    [Fact]
    public async Task Activator_IsUsableThroughThePortAlone()
    {
        // 호출부는 fake 를 모른다. 포트만으로 쓸 수 있어야 한다.
        IItemActivator port = new FakeItemActivator();

        await port.ActivateAsync(Loc(@"C:\Temp\a.txt"), CancellationToken.None);
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
