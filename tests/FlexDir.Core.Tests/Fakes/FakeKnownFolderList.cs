using FlexDir.Core.Locations;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IKnownFolderList"/> 의 fake. <see cref="Folders"/> 에 넣어 둔 것을 그대로 낸다 —
/// 툴바 ViewModel 테스트가 이 기계의 실제 사용자 폴더에 기대지 않게 하는 자리다.
/// </summary>
public sealed class FakeKnownFolderList : IKnownFolderList
{
    public List<KnownFolder> Folders { get; } = [];

    /// <summary>몇 번 물었는지. 탭마다 다시 묻지 않는지를 여기서 본다.</summary>
    public int Asked { get; private set; }

    public ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Asked++;

        return ValueTask.FromResult<IReadOnlyList<KnownFolder>>([.. Folders]);
    }
}
