namespace FlexDir.Shell.Interop;

/// <summary>
/// <c>SHGetFileInfoW</c> 를 프로세스 전체에서 한 번에 하나만 부르게 하는 관문.
/// <para>
/// <b>동시 호출이 조용히 실패한다.</b> 두 STA 워커가 같은 순간에 부르면 한쪽이 예외도 오류
/// 코드도 없이 <c>0</c> 을 내고 아이콘 인덱스가 -1 로 온다. 실물에서 두 페인이 같은 순간에
/// 폴더 아이콘을 물었고 한쪽만 받았다 — 그리고 실패는 호출자 캐시에 남아 다시 묻지 않으므로
/// (docs/PRD.md §4) 그 페인의 아이콘이 끝까지 비어 있었다.
/// </para>
/// <para>
/// 대가가 없다시피 한 이유: 호출자 둘(<c>ShellTypeNameProvider</c>·
/// <c>ShellThumbnailSource</c>)이 모두 <c>SHGFI_USEFILEATTRIBUTES</c> 로 확장자 연결 정보만
/// 보므로 저장소에 닿지 않고 (docs/SHELL_NOTES.md §아이콘), 조회 자체가 확장자마다 한 번뿐이다.
/// 진짜 오래 걸리는 호출(썸네일 조회)은 이 관문을 지나지 않는다.
/// </para>
/// <para>
/// <see cref="StaWorkQueue"/> 로는 풀리지 않는다 — 그것은 아파트먼트를 보장할 뿐이고
/// 워커가 넷이라 동시성이 그대로 남는다.
/// </para>
/// </summary>
internal static class ShellInfoGate
{
    private static readonly Lock Sync = new();

    internal static T Query<T>(Func<T> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        lock (Sync)
        {
            return call();
        }
    }
}
