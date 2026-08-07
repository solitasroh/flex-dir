namespace FlexDir.Core.Updates;

/// <summary>
/// 피드에 있는 새 버전. <b>버전 문자열은 원문 그대로</b> 다 — 화면에 나가고 사용자가
/// 릴리스 목록과 대조하는 값이다.
/// </summary>
/// <exception cref="ArgumentException">버전이 비었을 때. 버전 없는 '새 버전' 은 아무것도 말하지 못한다.</exception>
public sealed class AvailableUpdate
{
    public AvailableUpdate(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Version = version;
    }

    public string Version { get; }
}

/// <summary>
/// 새 버전을 찾고 받아 두고 적용하는 포트 (docs/PRD-v2.md §9).
///
/// <para>
/// <b>셋으로 쪼갠 이유는 사용자가 고르기 때문이다</b> (사용자 결정 2026-08-07):
/// 새 버전이 있으면 알리고 <b>적용할지는 사용자가 정한다</b>. 확인·받기는 사용자가 모르는
/// 사이에 끝나 있어야 하고, 적용만 사용자의 클릭을 기다린다. 하나로 묶으면 그 사이에
/// 끼어들 자리가 없다.
/// </para>
///
/// <para>
/// <b>세 호출 전부 UI 스레드에서 부르지 않는다</b> (CLAUDE.md §3). 확인과 받기는 네트워크에
/// 닿아 얼마나 걸릴지 모르고, 적용은 프로세스를 교체한다.
/// </para>
///
/// <para>
/// <b>설치되지 않은 상태를 오류로 만들지 않는다.</b> 개발 중 실행(<c>dotnet run</c>·
/// 빌드 산출물 직접 실행)에는 업데이트 기제가 없다 — 그때 <see cref="CheckAsync"/> 는
/// 예외가 아니라 <see langword="null"/> 이다. 오류로 만들면 매 실행마다 알림이 뜬다.
/// </para>
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// 새 버전이 있는지 본다. 없으면 <see langword="null"/> — "없음" 을 빈 버전으로 말하지
    /// 않는다 (<c>IDriveSpace</c> 가 모르는 용량을 <see langword="null"/> 로 내는 것과 같다).
    /// </summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken ct);

    /// <summary>
    /// 패키지를 받아 둔다. 적용 전에 끝나 있어야 사용자의 클릭이 곧바로 반영된다 —
    /// 누르고 나서 받으면 "지금 설치" 가 몇 분짜리 조작이 된다.
    /// </summary>
    Task DownloadAsync(AvailableUpdate update, CancellationToken ct);

    /// <summary>
    /// 받아 둔 것을 적용하고 다시 띄운다. <b>돌아오지 않는다</b> — 이 호출 뒤의 코드는
    /// 실행되지 않는다고 보아야 한다.
    /// </summary>
    void ApplyAndRestart(AvailableUpdate update);
}
