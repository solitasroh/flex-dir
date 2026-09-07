using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Tools;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="IExternalToolLauncher"/> 의 기록용 fake. 아무 프로세스도 띄우지 않는다
/// (<see cref="FakeItemActivator"/> 와 같은 이유 — 테스트가 실제로 창을 열면 안 된다).
/// <para>
/// <see cref="Launches"/> 가 이 fake 의 전부다: 무엇을 · 어떤 인자로 · 어느 폴더에서
/// 띄우라고 했는지. 셋 중 하나만 틀려도 실물에서는 엉뚱한 폴더에 터미널이 뜬다.
/// </para>
/// </summary>
public sealed class FakeExternalToolLauncher : IExternalToolLauncher
{
    private readonly List<(string Executable, string Arguments, LocationId Folder)> _launches = [];

    /// <summary>다음 실행이 낼 결과. 기본은 성공이다.</summary>
    public LocationErrorKind Result { get; set; } = LocationErrorKind.None;

    /// <summary>실행 요청을 순서대로 기록한다.</summary>
    public IReadOnlyList<(string Executable, string Arguments, LocationId Folder)> Launches => _launches;

    public ValueTask<LocationErrorKind> LaunchAsync(
        string executable,
        string arguments,
        LocationId workingFolder,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);

        ct.ThrowIfCancellationRequested();

        // 실패해도 기록은 남긴다 — "불렸는데 실패했다" 와 "아예 안 불렸다" 는 다른 사건이다.
        _launches.Add((executable, arguments, workingFolder));

        return ValueTask.FromResult(Result);
    }
}
