using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.App.Threading;

using FlexDir.Core.Updates;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 확인 한 번의 결과 (docs/PRD-v2.md §12). <b>사용자가 직접 눌렀을 때만 쓰인다</b> —
/// 시작할 때의 자동 확인은 결과를 묻지 않는다.
/// <para>
/// <b>'설치되지 않음' 이 없는 이유</b>: <c>IUpdateSource.CheckAsync</c> 는 개발 중 실행
/// (<c>dotnet run</c>·빌드 산출물)에서도 <see langword="null"/> 을 내므로 "최신" 과 구분되지
/// 않는다. 구분하려면 포트에 '설치되었는가' 를 물어야 하는데, 그것은 설정 창 하나 때문에
/// 포트를 넓히는 일이다.
/// </para>
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>피드에 우리보다 새 것이 없다.</summary>
    UpToDate,

    /// <summary>새 버전을 찾아 <b>받아 두기까지</b> 끝냈다. 알림 바가 떠 있다.</summary>
    Downloaded,

    /// <summary>피드에 닿지 못했거나 받다가 끊겼다. 다음에 다시 누르면 된다.</summary>
    Failed,
}

/// <summary>
/// 새 버전 알림 (docs/PRD-v2.md §9).
///
/// <para>
/// <b>알리고 사용자가 고른다</b> (사용자 결정 2026-08-07). 확인과 받기는 사용자가 모르는
/// 사이에 끝나고, 알림은 <b>받아 둔 뒤에야</b> 뜬다 — 누르고 나서 받으면 "지금 설치" 가
/// 몇 분짜리 조작이 된다.
/// </para>
///
/// <para>
/// <b>실패는 조용하다.</b> 피드에 못 닿는 것(사내망 밖·서버 꺼짐)은 정상 상황이고,
/// 업데이트는 <b>사용자가 요구한 작업이 아니다</b> — 손쓸 수 없는 알림을 매 실행마다
/// 띄우면 그것이 소음이 된다. 폴더를 못 열었을 때 사유를 반드시 내는 것과 다른 판단이다:
/// 그쪽은 사용자가 방금 요구한 일이다.
/// </para>
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateSource updates;
    private readonly IUiDispatcher dispatcher;

    private AvailableUpdate? ready;
    private bool isAvailable;
    private string? version;

    public UpdateViewModel(IUpdateSource updateSource, IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(updateSource);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        updates = updateSource;
        dispatcher = uiDispatcher;
    }

    /// <summary>알림 바가 보이는가. 받아 둔 것이 있을 때만 참이다.</summary>
    public bool IsAvailable
    {
        get => isAvailable;
        private set => SetProperty(ref isAvailable, value);
    }

    /// <summary>새 버전 문자열. 없으면 <see langword="null"/> 이다.</summary>
    public string? Version
    {
        get => version;
        private set => SetProperty(ref version, value);
    }

    /// <summary>
    /// 확인하고, 있으면 받아 두고, 그 뒤에 알린다.
    /// <para>
    /// <b>던지지 않는다.</b> 시작 경로가 이것을 기다리지 않고 띄우므로 (Host/Program),
    /// 예외가 나가면 아무도 기다리지 않는 Task 가 faulted 로 남는다.
    /// </para>
    /// </summary>
    public Task CheckAsync(CancellationToken ct) => CheckNowAsync(ct);

    /// <summary>
    /// 같은 일을 하되 <b>결과를 돌려준다</b>. 설정 창의 '확인' 이 부르는 자리다
    /// (docs/PRD-v2.md §12).
    /// <para>
    /// <b>같은 코드를 지나야 한다.</b> 시작 경로와 설정 창을 따로 구현하면 한쪽만 고치는
    /// 순간 갈린다 — 다른 것은 <b>말하느냐</b> 뿐이고, 말할지는 부르는 쪽이 정한다.
    /// </para>
    /// <para>
    /// <b>던지지 않는 것도 그대로다.</b> 취소는 <see cref="UpdateCheckOutcome.Failed"/> 로
    /// 돌아온다 — 종료 중이라면 그 값을 볼 화면이 이미 없고, 커맨드 밖으로 새면
    /// 프로세스가 죽는다.
    /// </para>
    /// </summary>
    public async Task<UpdateCheckOutcome> CheckNowAsync(CancellationToken ct)
    {
        try
        {
            var found = await updates.CheckAsync(ct).ConfigureAwait(false);

            if (found is null)
            {
                return UpdateCheckOutcome.UpToDate;
            }

            await updates.DownloadAsync(found, ct).ConfigureAwait(false);

            await dispatcher.InvokeAsync(() =>
            {
                ready = found;
                Version = found.Version;
                IsAvailable = true;
            }).ConfigureAwait(false);

            return UpdateCheckOutcome.Downloaded;
        }
        catch (OperationCanceledException)
        {
            // 종료 중이다. 정상 종료다.
            return UpdateCheckOutcome.Failed;
        }
        catch (Exception)
        {
            // 위 §요약 참조 — 자동 확인에서는 조용하다. 다음 실행이 다시 확인한다.
            return UpdateCheckOutcome.Failed;
        }
    }

    /// <summary>
    /// 지금 적용한다. <b>돌아오지 않는다</b> — 프로세스가 교체된다.
    /// 받아 둔 것이 없으면 아무 일도 하지 않는다 (알림을 접은 뒤의 클릭).
    /// </summary>
    [RelayCommand]
    private void Install()
    {
        if (ready is not { } update)
        {
            return;
        }

        updates.ApplyAndRestart(update);
    }

    /// <summary>
    /// 나중에. <b>거절이 아니라 미루기다</b> — 지금 적용하지 않고 알림만 접는다.
    /// <para>
    /// <b>미루는 대상은 재시작이지 설치가 아니다</b> (실측 2026-08-07). 받아 둔 패키지는
    /// 다음 실행의 <c>VelopackApp.Build().Run()</c> 이 시작 지점에서 적용하므로 그 실행은
    /// 이미 새 버전이고, 알림은 다시 뜨지 않는다. 이 결정이 지키려던 것은 "상주 앱을
    /// 마음대로 재시작하지 않는다" 였고 (docs/PRD-v2.md §9) 그것은 지켜진다 — 사용자가
    /// 스스로 끄고 켤 때 갱신된다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        ready = null;
        Version = null;
        IsAvailable = false;
    }
}
