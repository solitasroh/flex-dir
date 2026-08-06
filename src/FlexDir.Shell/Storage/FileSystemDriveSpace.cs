using FlexDir.Core.Locations;
using FlexDir.Core.Storage;

namespace FlexDir.Shell.Storage;

/// <summary>
/// 실제 볼륨을 재는 <see cref="IDriveSpace"/> 구현체.
///
/// <para>
/// <b>이름이 <c>Shell*</c> 이 아닌 이유</b>: COM 을 쓰지 않는다. <see cref="DriveInfo"/> 는
/// <c>GetDiskFreeSpaceEx</c> 로 내려가므로 STA 도 <c>StaWorkQueue</c> 도 필요 없다 —
/// <c>FileSystemFolderSource</c>·<c>FileSystemFolderWatcher</c> 와 같은 자리다.
/// </para>
///
/// <para>
/// <b>하지만 UI 스레드에서 부를 수는 없다.</b> 이 호출은 저장소에 닿고, 네트워크 경로와
/// 응답하지 않는 이동식 볼륨에서는 초 단위로 블로킹한다 (CLAUDE.md §3). 그래서
/// <see cref="Task.Run(Action, CancellationToken)"/> 으로 넘긴다 — 아파트먼트가 아니라
/// <b>블로킹</b>이 여기서 막는 것이다 (docs/HANDOFF 규칙 8: 둘은 다른 문제다).
/// </para>
///
/// <para>
/// <b>취소가 조회 자체를 끊지는 못한다.</b> <c>GetDiskFreeSpaceEx</c> 에 취소가 없어서
/// 이미 시작한 호출은 끝까지 간다 — 취소는 결과를 버리는 것까지다. 상태표시줄에 쓰는
/// 곁다리 값이라 그 정도로 충분하고, 그 이상은 스레드를 죽이는 이야기가 된다.
/// </para>
/// </summary>
public sealed class FileSystemDriveSpace : IDriveSpace
{
    public async ValueTask<DriveSpace?> MeasureAsync(LocationId location, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var root = Path.GetPathRoot(location.DisplayPath);

        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        return await Task.Run(() => Measure(root), ct).ConfigureAwait(false);
    }

    private static DriveSpace? Measure(string root)
    {
        try
        {
            var drive = new DriveInfo(root);

            // IsReady 를 먼저 본다. 빈 카드 리더나 마운트되지 않은 볼륨에서 용량을 물으면
            // 던진다 — 예외로 흐름을 만들지 않는다.
            return drive.IsReady
                ? new DriveSpace(drive.AvailableFreeSpace, drive.TotalSize)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 사라진 드라이브·권한 없는 볼륨·해석할 수 없는 루트. 전부 "모른다" 로 접는다
            // (IDriveSpace 계약: 실패는 던지지 않는다).
            return null;
        }
    }
}
