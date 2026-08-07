using FlexDir.Core.Updates;

// Velopack.Sources 를 끌어오지 않는다 — 거기에도 IUpdateSource 가 있어 우리 포트와
// 이름이 부딪힌다. 쓰는 것은 GithubSource 하나뿐이라 전체 이름으로 적는다.
using Velopack;

namespace FlexDir.Shell.Updates;

/// <summary>
/// Velopack 을 <see cref="IUpdateSource"/> 에 끼운다 (docs/PRD-v2.md §9).
///
/// <para>
/// <b>포트가 Velopack 을 모르게 하는 것이 이 클래스의 존재 이유다.</b> Core 는 순수
/// .NET 이고(CLAUDE.md §1) <c>UpdateInfo</c>·<c>VelopackAsset</c> 은 이 어셈블리 밖으로
/// 나가지 않는다. 밖으로 나가는 것은 버전 문자열뿐이다.
/// </para>
///
/// <para>
/// <b>설치되지 않은 실행에서는 아무것도 하지 않는다.</b> <c>dotnet run</c>·빌드 산출물
/// 직접 실행·테스트 실행기가 전부 그 상태이고, 거기엔 업데이트 기제가 없다.
/// <c>IsInstalled</c> 를 <b>네트워크보다 먼저</b> 본다 — 순서를 뒤집으면 개발 중 매
/// 실행이 피드 왕복만큼 늦어진다 (CLAUDE.md §3).
/// </para>
///
/// <para>
/// <b>확인이 낸 것만 받고 적용한다.</b> Velopack 은 받을 대상을 <c>UpdateInfo</c> 로
/// 쥐는데 그것은 포트를 지날 수 없으므로(위) 마지막 확인 결과를 여기 담아 둔다.
/// 확인하지 않은 버전을 받으라는 요청은 <b>조용히 성공시키지 않는다</b> — 그러면
/// "지금 설치" 가 아무 일도 하지 않는다.
/// </para>
/// </summary>
public sealed class VelopackUpdateSource : IUpdateSource
{
    private readonly string feed;

    private UpdateManager? manager;

    /// <summary>마지막 확인이 찾은 것. 받기·적용이 이것을 쓴다.</summary>
    private UpdateInfo? found;

    /// <param name="feedUrl">
    /// 릴리스가 올라가는 곳. GitHub 저장소 주소다 — 저장소가 public 이라 토큰이 없다
    /// (사용자 결정 2026-08-07).
    /// </param>
    public VelopackUpdateSource(string feedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        feed = feedUrl;
    }

    /// <summary>
    /// <c>UpdateManager</c> 를 <b>생성자에서 만들지 않는다.</b> 그 생성자는
    /// <c>VelopackApp.Build().Run()</c> 이 세우는 locator 를 요구해서, 부트스트랩 전에 만들면
    /// 던진다 — 테스트 실행기와 <c>dotnet run</c> 이 정확히 그 상태다. 조립이 그 자리에서
    /// 터지면 <b>업데이트와 무관한 앱 전체가 못 뜬다.</b>
    /// <para>
    /// locator 가 없다는 것은 "설치된 앱으로 실행되지 않았다" 와 같은 말이므로 그대로
    /// <see langword="null"/> 을 낸다 — 예외로 다룰 일이 아니다.
    /// </para>
    /// </summary>
    private UpdateManager? Manager()
    {
        if (!Velopack.Locators.VelopackLocator.IsCurrentSet)
        {
            return null;
        }

        return manager ??= new UpdateManager(
            new Velopack.Sources.GithubSource(feed, accessToken: null, prerelease: false));
    }

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 설치 여부를 네트워크보다 먼저 본다. 뒤집으면 개발 중 매 실행이 피드 왕복만큼
        // 늦어진다 (CLAUDE.md §3).
        if (Manager() is not { IsInstalled: true } installed)
        {
            return null;
        }

        // 네트워크에 닿는다. Velopack 의 확인에는 취소가 없으므로 워커로 밀고, 취소는
        // 결과를 버리는 것까지다 (NetworkShareSource·FileSystemDriveSpace 와 같은 한계).
        var update = await Task.Run(() => installed.CheckForUpdatesAsync(), ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        found = update;

        // 다운그레이드는 갱신이 아니다. 피드에서 릴리스를 내리면 여기로 온다 —
        // 그것을 '새 버전' 으로 그리면 사용자가 뒤로 가는 설치를 승인하게 된다.
        if (update is null || update.IsDowngrade)
        {
            return null;
        }

        return new AvailableUpdate(update.TargetFullRelease.Version.ToString());
    }

    public Task DownloadAsync(AvailableUpdate update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        ct.ThrowIfCancellationRequested();

        var target = Pending(update);

        return Installed().DownloadUpdatesAsync(target, progress: null, cancelToken: ct);
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        // 돌아오지 않는다 — 프로세스가 교체된다.
        Installed().ApplyUpdatesAndRestart(Pending(update).TargetFullRelease);
    }

    /// <summary>
    /// 받기·적용이 쓰는 매니저. 여기까지 왔다면 <see cref="Pending"/> 이 이미 확인 결과를
    /// 통과시켰으므로 설치된 실행이다 — 아니면 그쪽이 먼저 던진다.
    /// </summary>
    private UpdateManager Installed()
        => Manager() ?? throw new InvalidOperationException("설치된 실행이 아니다. 업데이트를 적용할 수 없다.");

    /// <summary>
    /// 확인이 찾아 둔 것과 요청이 같은지 본다. 다르면 배선이 어긋난 것이다 —
    /// 조용히 넘기면 엉뚱한 버전을 설치하거나 아무 일도 일어나지 않는다.
    /// </summary>
    private UpdateInfo Pending(AvailableUpdate update)
    {
        if (found is not { } pending
            || !string.Equals(pending.TargetFullRelease.Version.ToString(), update.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"확인이 찾은 버전이 아니다: '{update.Version}'. 받기·적용은 CheckAsync 가 낸 것만 받는다.");
        }

        return pending;
    }
}
