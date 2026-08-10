using System.Runtime.InteropServices;

using FlexDir.Core.Locations;
using FlexDir.Core.Storage;

namespace FlexDir.Shell.Storage;

/// <summary>
/// 이 기계의 드라이브를 세는 <see cref="IDriveList"/> 구현체.
///
/// <para>
/// <b>이름이 <c>Shell*</c> 이 아닌 이유</b>는 <see cref="FileSystemDriveSpace"/> 와 같다 —
/// COM 이 아니라 <see cref="DriveInfo"/> 와 <c>mpr.dll</c> 이다. STA 도 정리도 필요 없다.
/// </para>
///
/// <para>
/// <b>그래도 UI 스레드에서 부를 수 없다.</b> <see cref="DriveInfo.GetDrives"/> 자체는 싸지만
/// 드라이브마다 묻는 <c>IsReady</c>·<c>VolumeLabel</c> 이 저장소에 닿는다 — 연결이 끊긴
/// 매핑 드라이브와 빈 카드 리더에서 초 단위로 블로킹한다 (CLAUDE.md §3). 그래서 전부
/// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> 안에서 한다.
/// </para>
///
/// <para>
/// <b>네트워크 이웃을 뒤지지 않는다</b> (사용자 결정 2026-08-10). 트리의 서버 항목은
/// 매핑된 드라이브에서만 나온다 — <c>WNetOpenEnum</c> 으로 주변을 훑으면 없는 서버 하나에
/// 42초를 쓴다 (docs/PRD-v2.md §5 N-4 실측). <c>WNetGetConnection</c> 은 이미 맺어진
/// 연결의 이름을 되읽을 뿐이라 그 위험이 없다.
/// </para>
/// </summary>
public sealed partial class SystemDriveList : IDriveList
{
    /// <summary>
    /// <c>WNetGetConnection</c> 이 쓸 버퍼의 문자 수. UNC 는 <c>MAX_PATH</c> 를 넘지 않는다.
    /// </summary>
    private const int RemoteNameCapacity = 260;

    private const int NoError = 0;

    public async ValueTask<IReadOnlyList<DriveEntry>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(List, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<DriveEntry> List()
    {
        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            // 드라이브를 못 세는 것은 폴더를 못 여는 사건이 아니다 (IDriveList 계약).
            return [];
        }

        var entries = new List<DriveEntry>(drives.Length);

        foreach (var drive in drives)
        {
            // 파싱되지 않는 루트는 트리에 올릴 수 없다 — 클릭해도 갈 곳이 없다.
            if (LocationId.TryParse(drive.Name, out var root, out _))
            {
                entries.Add(new DriveEntry(root, Label(drive), ServerOf(drive)));
            }
        }

        return entries;
    }

    /// <summary>
    /// <c>볼륨이름 (C:)</c>. 볼륨 이름이 없거나 읽을 수 없으면 유형 이름으로 대신한다 —
    /// 빈 라벨은 트리에 빈 줄로 선다. 문자를 항상 붙이는 이유는 같은 이름의 볼륨이 둘일 수
    /// 있어서다.
    /// </summary>
    private static string Label(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd('\\');
        var volume = VolumeLabel(drive);

        return $"{(string.IsNullOrWhiteSpace(volume) ? KindName(drive.DriveType) : volume)} ({letter})";
    }

    /// <summary>
    /// 볼륨 이름. 준비되지 않은 드라이브에서 물으면 던지므로 예외로 흐름을 만들지 않는다 —
    /// <see cref="FileSystemDriveSpace"/> 가 <c>IsReady</c> 를 먼저 보는 것과 같은 자리다.
    /// </summary>
    private static string? VolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.VolumeLabel : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            return null;
        }
    }

    private static string KindName(DriveType type) => type switch
    {
        DriveType.Fixed => "로컬 디스크",
        DriveType.Removable => "이동식 디스크",
        DriveType.Network => "네트워크 드라이브",
        DriveType.CDRom => "DVD 드라이브",
        DriveType.Ram => "RAM 디스크",
        _ => "드라이브",
    };

    /// <summary>
    /// 매핑된 드라이브가 붙어 있는 서버 (<c>Z:</c> → <c>\\10.10.10.23</c>).
    /// 네트워크 드라이브가 아니면 <see langword="null"/> 이다.
    /// <para>
    /// 공유까지가 아니라 <b>서버까지</b>인 이유: 트리의 네트워크 항목은 서버이고, 그 아래
    /// 공유 목록은 <c>RoutingFolderSource</c> 가 <c>IsNetworkServer</c> 로 갈라 낸다
    /// (docs/PRD-v2.md §5 N-3).
    /// </para>
    /// </summary>
    private static LocationId? ServerOf(DriveInfo drive)
    {
        if (drive.DriveType != DriveType.Network)
        {
            return null;
        }

        var remote = RemoteName(drive.Name.TrimEnd('\\'));

        if (remote is null
            || !LocationId.TryParse(remote, out var share, out _)
            || share.Server is not { } server)
        {
            return null;
        }

        return LocationId.TryParse(server, out var id, out _) ? id : null;
    }

    /// <summary>매핑의 원격 이름 (<c>\\server\share</c>). 매핑이 아니면 <c>null</c>.</summary>
    private static string? RemoteName(string localName)
    {
        var buffer = new char[RemoteNameCapacity];
        var length = buffer.Length;

        // 끊긴 매핑은 여기서 오류를 낸다 — 그것도 "서버를 모른다" 로 접는다. 트리에
        // 죽은 서버를 세우면 펼칠 때 42초를 기다리게 된다.
        if (WNetGetConnection(localName, buffer, ref length) != NoError)
        {
            return null;
        }

        var end = Array.IndexOf(buffer, '\0');

        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    [LibraryImport("mpr.dll", EntryPoint = "WNetGetConnectionW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetGetConnection(string localName, [Out] char[] remoteName, ref int length);
}
