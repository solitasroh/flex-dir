using FlexDir.Core.Locations;
using FlexDir.Core.Storage;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IDriveSpace"/> 의 fake. <see cref="Spaces"/> 에 넣어 둔 위치만 답하고
/// 나머지는 <c>null</c> 이다 — 실물의 "알 수 없음" 과 같은 자리다.
/// </summary>
public sealed class FakeDriveSpace : IDriveSpace
{
    public Dictionary<LocationId, DriveSpace> Spaces { get; } = [];

    /// <summary>물어본 위치를 순서대로 남긴다. 배선이 정말 불렸는지를 여기서 본다.</summary>
    public List<LocationId> Asked { get; } = [];

    public ValueTask<DriveSpace?> MeasureAsync(LocationId location, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Asked.Add(location);

        return ValueTask.FromResult(Spaces.TryGetValue(location, out var space) ? space : (DriveSpace?)null);
    }
}
