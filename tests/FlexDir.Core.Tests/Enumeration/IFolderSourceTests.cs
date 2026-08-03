using System.Diagnostics;
using System.IO;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Core.Tests.Fakes;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이고
/// 계약 검증은 전부 <see cref="FolderSourceContract"/> 에 있다 —
/// <c>FlexDir.Shell</c> 의 구현체 테스트도 같은 기반 클래스를 상속한다.
/// <para>
/// 아래 <see cref="FakeFolderSource"/> 전용 테스트는 계약이 아니라 주입 knob 을 본다.
/// 계약 쪽은 <see cref="IFolderSource"/> 만 보므로 여기가 fake 내부를 보는 유일한 자리다.
/// </para>
/// </summary>
public class FakeFolderSourceTests : FolderSourceContract
{
    protected override IFolderSource CreateSource(LocationId folder, IReadOnlyList<FileItem> items)
    {
        var source = new FakeFolderSource();
        source.Folders[folder] = [.. items];
        return source;
    }

    [Fact]
    public async Task FailureInjection_ThrowsAfterTheGivenItemCount()
    {
        var folder = Folder(@"C:\Temp");
        var source = new FakeFolderSource { FailureInjection = (1, LocationErrorKind.AccessDenied) };
        source.Folders[folder] = [Item(folder, "a.txt"), Item(folder, "b.txt")];

        var seen = new List<FileItem>();
        var error = await Assert.ThrowsAsync<LocationAccessException>(async () =>
        {
            await foreach (var item in source.EnumerateAsync(folder, CancellationToken.None))
            {
                seen.Add(item);
            }
        });

        Assert.Single(seen);
        Assert.Equal(LocationErrorKind.AccessDenied, error.Kind);
    }

    [Fact]
    public async Task ContractViolation_ThrowsSomethingOtherThanTheContractException()
    {
        var folder = Folder(@"C:\Temp");
        var failure = new IOException("핸들이 유효하지 않다.");
        var source = new FakeFolderSource { ContractViolation = (1, failure) };
        source.Folders[folder] = [Item(folder, "a.txt"), Item(folder, "b.txt")];

        var seen = new List<FileItem>();

        // 계약대로면 여기서 LocationAccessException 이 나온다. 구현체가 P/Invoke 위에 서므로
        // 그럼에도 다른 예외가 나올 수 있고, 호출자의 방어를 재려면 그 상황을 만들 수 있어야 한다.
        var thrown = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var item in source.EnumerateAsync(folder, CancellationToken.None))
            {
                seen.Add(item);
            }
        });

        Assert.Single(seen);
        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task EnumerateCalls_RecordsFoldersInCallOrder()
    {
        var first = Folder(@"C:\A");
        var second = Folder(@"C:\B");
        var source = new FakeFolderSource();
        source.Folders[first] = [Item(first, "a.txt")];
        source.Folders[second] = [Item(second, "b.txt")];

        await foreach (var _ in source.EnumerateAsync(first, CancellationToken.None))
        {
        }

        await foreach (var _ in source.EnumerateAsync(second, CancellationToken.None))
        {
        }

        Assert.Equal([first, second], source.EnumerateCalls);
    }

    [Fact]
    public async Task CancellationsObserved_CountsTheCanceledEnumeration()
    {
        var folder = Folder(@"C:\Temp");
        var source = new FakeFolderSource();
        source.Folders[folder] = [Item(folder, "a.txt"), Item(folder, "b.txt")];

        Assert.Equal(0, source.CancellationsObserved);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(folder, Canceled()))
            {
            }
        });

        Assert.Equal(1, source.CancellationsObserved);
    }

    [Fact]
    public async Task YieldDelay_SlowsEveryItem()
    {
        var folder = Folder(@"C:\Temp");
        var source = new FakeFolderSource { YieldDelayMilliseconds = 10 };
        source.Folders[folder] = [Item(folder, "a.txt"), Item(folder, "b.txt"), Item(folder, "c.txt")];

        var stopwatch = Stopwatch.StartNew();
        await foreach (var _ in source.EnumerateAsync(folder, CancellationToken.None))
        {
        }

        // 항목 3개 × 10ms = 30ms 이상이어야 한다. 타이머 해상도를 감안해 한 항목분만 요구한다 —
        // 재는 것은 "지연이 걸리는가" 이고 정확한 시간이 아니다.
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(10),
            $"지연이 걸리지 않았다: {stopwatch.Elapsed}");
    }
}
