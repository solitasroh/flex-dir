using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.Core.Tests.Enumeration;

/// <summary>
/// <see cref="IFolderSource"/> 구현체가 반드시 만족해야 하는 성질.
/// <para>
/// 테스트 클래스가 아니라 기반 클래스다 (abstract 라서 xunit 이 수집하지 않는다).
/// <c>FlexDir.Shell</c> 의 <c>FindFirstFileEx</c> 기반 구현체 테스트가 이 클래스를 상속해
/// 같은 검증을 받는 것이 존재 이유다 — 그래서 여기서는 <see cref="IFolderSource"/> 만 본다.
/// 구현체의 내부(주입 knob·사전·핸들)를 들여다보면 재사용이 되지 않는다.
/// </para>
/// </summary>
public abstract class FolderSourceContract
{
    /// <summary>
    /// 이 계약이 열거할 폴더. <b>구현체가 정한다</b> — 계약이 경로를 고정하면 실제
    /// 파일시스템 구현체는 그 폴더가 실물로 존재해야 하고, 고정된 경로를 테스트가 만들고
    /// 지우는 것은 사용자의 폴더를 지울 수 있다. fake 는 아무 경로나 낸다.
    /// </summary>
    protected abstract LocationId SourceFolder { get; }

    /// <summary>
    /// <paramref name="folder"/> 에 <paramref name="items"/> 가 들어 있는 구현체를 낸다.
    /// 실제 구현체 테스트는 임시 디렉터리를 만들어 같은 이름의 파일을 두면 된다 —
    /// 계약이 보는 것은 <see cref="FileItem.Name"/> 과 <see cref="FileItem.Location"/> 뿐이다.
    /// </summary>
    protected abstract IFolderSource CreateSource(LocationId folder, IReadOnlyList<FileItem> items);

    // ── 열거 ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisteredItems_AreAllYielded()
    {
        var folder = SourceFolder;
        var source = CreateSource(folder, [Item(folder, "a.txt"), Item(folder, "b.txt"), Item(folder, "c.txt")]);

        var names = new List<string>();
        await foreach (var item in source.EnumerateAsync(folder, CancellationToken.None))
        {
            names.Add(item.Name);
        }

        // 순서는 계약이 아니다 — 파일시스템이 준 순서를 그대로 흘리고 정렬은
        // FlexDir.Core.Sorting 이 맡는다. 그래서 이름으로 맞춰 비교한다.
        Assert.Equal(["a.txt", "b.txt", "c.txt"], names.Order(StringComparer.Ordinal));
    }

    // ── 취소 ────────────────────────────────────────────────────────
    // 네트워크 경로에서는 열거 한 항목이 초 단위로 걸린다. 취소를 항목마다 관측하지
    // 않는 구현은 폴더를 떠난 뒤에도 계속 흘러 UI 를 붙잡는다 (CLAUDE.md §3).

    [Fact]
    public async Task CanceledToken_Enumerate_Throws()
    {
        var folder = SourceFolder;
        var source = CreateSource(folder, [Item(folder, "a.txt")]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(folder, Canceled()))
            {
            }
        });
    }

    [Fact]
    public async Task CancelingMidEnumeration_StopsYielding()
    {
        var folder = SourceFolder;
        var source = CreateSource(folder, [Item(folder, "a.txt"), Item(folder, "b.txt"), Item(folder, "c.txt")]);

        using var cts = new CancellationTokenSource();
        var seen = new List<FileItem>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in source.EnumerateAsync(folder, cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        });

        Assert.Single(seen);
    }

    // ── 실패 ────────────────────────────────────────────────────────
    // 오류는 예외로 낸다. 결과 스트림에 오류 항목을 섞으면 모든 소비자가 항목마다
    // 분기해야 한다.

    [Fact]
    public async Task UnknownFolder_ThrowsNotFound()
    {
        var folder = SourceFolder;
        var source = CreateSource(folder, [Item(folder, "a.txt")]);
        var missing = folder.Combine("__flex-dir-missing__");

        var error = await Assert.ThrowsAsync<LocationAccessException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(missing, CancellationToken.None))
            {
            }
        });

        Assert.Equal(LocationErrorKind.NotFound, error.Kind);
        Assert.Equal(missing, error.Location);
    }

    // ── 단건 조회 ───────────────────────────────────────────────────
    // 감시 알림과 실제 상태가 어긋나는 것은 정상 상황이다 (CLAUDE.md §4).
    // 없음을 예외로 만들면 정상 흐름이 예외 경로가 된다.

    [Fact]
    public async Task TryGetItem_MissingItem_ReturnsNull()
    {
        var folder = SourceFolder;
        var source = CreateSource(folder, [Item(folder, "a.txt")]);

        Assert.Null(await source.TryGetItemAsync(folder.Combine("gone.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task TryGetItem_ExistingItem_ReturnsIt()
    {
        var folder = SourceFolder;
        var expected = Item(folder, "a.txt");
        var source = CreateSource(folder, [expected]);

        var actual = await source.TryGetItemAsync(expected.Location, CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Location, actual.Location);

        // Size·ModifiedUtc 는 비교하지 않는다 — 실제 구현체는 파일시스템이 준 값을 담는다.
    }

    protected static CancellationToken Canceled() => new(canceled: true);

    protected static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);
        return location;
    }

    protected static FileItem Item(LocationId folder, string name)
        => new(name, folder.Combine(name), 0, DateTimeOffset.UnixEpoch, FileItemFlags.None);
}
