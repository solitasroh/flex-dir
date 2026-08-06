using FlexDir.Core.Locations;
using FlexDir.Core.Operations;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IContextMenuProvider"/> 의 fake. 기록만 한다 — 실물은 네이티브 메뉴를 띄우고
/// 사용자가 고를 때까지 메시지를 펌핑하므로 자동 테스트가 밟을 수 없다 (CLAUDE.md §5).
/// </summary>
public sealed class FakeContextMenuProvider : IContextMenuProvider
{
    private readonly List<MenuRequest> requests = [];

    public IReadOnlyList<MenuRequest> Requests => requests;

    /// <summary>다음 호출이 던질 예외. 실패 경로를 ViewModel 테스트가 밟게 한다.</summary>
    public Exception? Failure { get; set; }

    public Task ShowAsync(IReadOnlyList<LocationId> items, LocationId folder, ScreenPoint at, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        requests.Add(new MenuRequest([.. items], folder, at));

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

/// <summary>한 번의 메뉴 요청. 항목이 비어 있으면 폴더 배경 메뉴다.</summary>
public sealed record MenuRequest(IReadOnlyList<LocationId> Items, LocationId Folder, ScreenPoint At);
