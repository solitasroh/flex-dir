using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IFileOperations"/> 의 기록용 fake. 파일시스템을 건드리지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 실제 구현체(<c>IFileOperation</c> COM)는
/// <c>FlexDir.Shell</c> 의 몫이고 수동 검증 대상이다 (ADR-009).
/// </para>
/// <para>
/// 조작마다 별도 목록에 담는다 — ViewModel 테스트가 재는 것은 대개 "복사가 아니라
/// 이동이 불렸는가" 이고, 한 목록에 종류를 섞어 담으면 그 단정문이 필터부터 시작한다.
/// </para>
/// </summary>
public sealed class FakeFileOperations : IFileOperations
{
    public List<(IReadOnlyList<LocationId> Sources, LocationId Destination)> Copies { get; } = [];

    public List<(IReadOnlyList<LocationId> Sources, LocationId Destination)> Moves { get; } = [];

    public List<IReadOnlyList<LocationId>> Deletes { get; } = [];

    public List<(LocationId Item, string NewName)> Renames { get; } = [];

    public List<(LocationId Parent, string Name)> CreatedFolders { get; } = [];

    /// <summary>
    /// 모든 조작이 던질 예외. null 이면 성공한다. 같은 인스턴스를 매 호출에 다시 던지므로
    /// 호출한 조작에 맞는 위치로 테스트가 만들어 넣는다.
    /// <para>
    /// <see cref="LocationAccessException"/> 이 아니라 <see cref="Exception"/> 이다 — 계약을
    /// <b>어긴</b> 예외를 넣을 수 있어야 한다. 실물에서 그런 예외가 커맨드 밖으로 새어
    /// 프로세스가 죽었다 (2026-08-07). 좁혀 두면 그 상황을 테스트가 만들 수 없다
    /// (<c>FakeTypeNameProvider.Failure</c> 와 같은 이유).
    /// </para>
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// <see cref="CreateFolderAsync"/> 가 실제로 만든 이름. null 이면 요청한 이름을 그대로 쓴다.
    /// 이름이 겹쳐 구현체가 유일한 이름을 만든 상황을 재현하는 knob 이다.
    /// </summary>
    public string? CreatedFolderName { get; set; }

    /// <summary>
    /// 조작이 시작될 때 기다리는 관문. 기본값은 이미 완료된 Task 다 — 미완료 Task 를 넣으면
    /// "조작이 진행 중" 인 상태를 만들 수 있다 (반대편 페인이 그 사이에 움직이는지 본다).
    /// </summary>
    public Task Gate { get; set; } = Task.CompletedTask;

    public int CancellationsObserved { get; private set; }

    public async Task CopyAsync(
        IReadOnlyList<LocationId> sources,
        LocationId destinationFolder,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinationFolder);

        // 실패해도 기록은 남긴다 — "불렸는데 실패했다" 와 "아예 안 불렸다" 는 다른 사건이다.
        Copies.Add((sources, destinationFolder));

        await ArriveAsync(ct);
    }

    public async Task MoveAsync(
        IReadOnlyList<LocationId> sources,
        LocationId destinationFolder,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinationFolder);

        Moves.Add((sources, destinationFolder));

        await ArriveAsync(ct);
    }

    public async Task DeleteAsync(IReadOnlyList<LocationId> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);

        Deletes.Add(items);

        await ArriveAsync(ct);
    }

    public async Task RenameAsync(LocationId item, string newName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrEmpty(newName);

        Renames.Add((item, newName));

        await ArriveAsync(ct);
    }

    public async Task<LocationId> CreateFolderAsync(
        LocationId parentFolder,
        string name,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parentFolder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        CreatedFolders.Add((parentFolder, name));

        await ArriveAsync(ct);

        return parentFolder.Combine(CreatedFolderName ?? name);
    }

    /// <summary>관문을 지나고 취소를 관측한 뒤 주입된 실패를 던진다.</summary>
    private async Task ArriveAsync(CancellationToken ct)
    {
        await Gate;

        // 항목마다가 아니라 조작마다 관측한다 — 실제 구현체는 shell 에 한 번 넘긴다.
        if (ct.IsCancellationRequested)
        {
            CancellationsObserved++;
        }

        ct.ThrowIfCancellationRequested();

        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
