using FlexDir.Core.Locations;
using FlexDir.Core.ViewState;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IViewStateStore"/> 의 사전 기반 fake. 디스크를 쓰지 않는다.
/// <para>
/// 프로덕션 어셈블리(<c>src/</c>)에 두지 않는 이유: 테스트용 구현체가 섞이면
/// DI 조립에서 실수로 주입될 수 있다. 실제 구현체(<c>%LOCALAPPDATA%\flex-dir\</c> 저장)는
/// <c>FlexDir.Shell</c> 의 몫이다 (docs/ARCHITECTURE.md §4).
/// </para>
/// <para>
/// 폴더 키는 <see cref="LocationId"/> 를 그대로 쓴다 — 동등성·해시가 이미
/// <see cref="StringComparison.OrdinalIgnoreCase"/> 다.
/// </para>
/// </summary>
public sealed class InMemoryViewStateStore : IViewStateStore
{
    private readonly Dictionary<LocationId, FolderViewState> folders = [];

    private GlobalViewState global = GlobalViewState.Default;

    public ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ct.ThrowIfCancellationRequested();

        return new(folders.GetValueOrDefault(folder));
    }

    public ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        folders[folder] = state;
        return ValueTask.CompletedTask;
    }

    public ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return new(global);
    }

    public ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        global = state;
        return ValueTask.CompletedTask;
    }
}
