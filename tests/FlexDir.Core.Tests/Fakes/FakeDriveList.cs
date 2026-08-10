using FlexDir.Core.Storage;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IDriveList"/> 의 fake. <see cref="Drives"/> 에 넣어 둔 것을 그대로 낸다 —
/// 트리 ViewModel 테스트가 이 기계의 실제 드라이브에 기대지 않게 하는 자리다.
/// </summary>
public sealed class FakeDriveList : IDriveList
{
    public List<DriveEntry> Drives { get; } = [];

    /// <summary>몇 번 물었는지. 트리가 펼칠 때마다 다시 열거하지 않는지를 여기서 본다.</summary>
    public int Asked { get; private set; }

    public ValueTask<IReadOnlyList<DriveEntry>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Asked++;

        return ValueTask.FromResult<IReadOnlyList<DriveEntry>>([.. Drives]);
    }
}
