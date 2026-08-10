using FlexDir.Core.Favorites;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IFavoriteStore"/> 의 fake. 저장한 것을 그대로 돌려준다.
/// </summary>
public sealed class FakeFavoriteStore : IFavoriteStore
{
    private IReadOnlyList<Favorite> saved = [];

    /// <summary>저장 횟수. 조작마다 남기는지(=창을 강제 종료해도 남는지)를 여기서 본다.</summary>
    public int Saves { get; private set; }

    /// <summary>다음 저장에서 던질 예외. 실패해도 화면이 흔들리지 않는지 재는 데 쓴다.</summary>
    public Exception? SaveFailure { get; set; }

    public IReadOnlyList<Favorite> Current => saved;

    public ValueTask<IReadOnlyList<Favorite>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return ValueTask.FromResult(saved);
    }

    public ValueTask SaveAsync(IReadOnlyList<Favorite> favorites, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ct.ThrowIfCancellationRequested();

        if (SaveFailure is { } failure)
        {
            throw failure;
        }

        Saves++;
        saved = [.. favorites];

        return ValueTask.CompletedTask;
    }
}
