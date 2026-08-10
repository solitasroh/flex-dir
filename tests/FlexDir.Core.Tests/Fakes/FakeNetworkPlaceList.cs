using FlexDir.Core.Storage;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="INetworkPlaceList"/> 의 fake. <see cref="Places"/> 에 넣어 둔 것을 그대로 낸다.
/// </summary>
public sealed class FakeNetworkPlaceList : INetworkPlaceList
{
    public List<NetworkPlace> Places { get; } = [];

    public int Asked { get; private set; }

    public ValueTask<IReadOnlyList<NetworkPlace>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Asked++;

        return ValueTask.FromResult<IReadOnlyList<NetworkPlace>>([.. Places]);
    }
}
