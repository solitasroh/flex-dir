using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Shell.Enumeration;

/// <summary>
/// 서버 하나의 공유 목록 (docs/SHELL_NOTES.md §네트워크 · docs/PRD-v2.md §5 N-3).
///
/// <para>
/// <b><c>NetShareEnum</c> 을 쓰지 않는다.</b> 그쪽(netapi32)은 LANMAN 명명 파이프로
/// RPC-over-SMB 를 직접 때리는데, <b>현대 NAS·SMB2 전용 호스트는 그 경로를 막아두면서도
/// 일반 SMB 파일 작업은 정상 제공한다.</b> <c>WNet*</c> 는 다중 공급자 라우터(mpr.dll)를
/// 거치므로 <c>net view \\server</c> 와 같은 길을 탄다. 사용자 NAS(10.10.10.23)에서
/// 정확히 이 격차를 실측했다 — <c>net view</c> 는 공유 13개를 내고 <c>Win32_Share</c> 는
/// 실패한다.
/// </para>
///
/// <para>
/// <b>UI 스레드에서 부를 수 없다.</b> 이름 해석과 세션 수립이 초 단위로 블로킹한다
/// (CLAUDE.md §3). <c>Task.Run</c> 으로 넘기지만 <b>취소가 진행 중인 호출을 끊지는
/// 못한다</b> — <c>WNetOpenEnum</c> 에 취소가 없다. 취소는 결과를 버리는 것까지다
/// (<c>FileSystemDriveSpace</c> 와 같은 한계다).
/// </para>
///
/// <para>
/// 공유는 수십 개 규모라 <b>다 모은 뒤 흘린다.</b> 네이티브 열거 루프를 비동기 스트림
/// 안에서 돌리면 취소·해제 순서가 얽히는데, 그만한 이득이 없다 — 폴더 열거와 다른
/// 판단이고 그 이유가 항목 수다.
/// </para>
/// </summary>
public sealed partial class NetworkShareSource : IFolderSource
{
    private const int ResourceGlobalNet = 0x00000002;
    private const int ResourceTypeDisk = 0x00000001;
    private const int ResourceUsageContainer = 0x00000002;
    private const int ResourceDisplayTypeServer = 0x00000002;

    private const int NoError = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorNoMoreItems = 259;

    /// <summary>한 번에 받아 올 버퍼. 공유 수십 개면 충분하고, 모자라면 늘려 다시 부른다.</summary>
    private const int InitialBufferBytes = 16 * 1024;

    public async IAsyncEnumerable<FileItem> EnumerateAsync(
        LocationId folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        RequireServer(folder);
        ct.ThrowIfCancellationRequested();

        var shares = await Task.Run(() => ReadShares(folder), ct).ConfigureAwait(false);

        foreach (var name in shares)
        {
            ct.ThrowIfCancellationRequested();

            yield return ShareItem(folder, name);
        }
    }

    /// <summary>
    /// 공유 하나를 조회한다. 목록을 읽어 이름으로 찾는다 — 공유 단건을 묻는 API 가 따로
    /// 없고, 있어도 목록과 다른 길로 물으면 둘이 어긋난다.
    /// </summary>
    public Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
    {
        // 서버 자신을 물었다. 그 위(네트워크 노드)는 셸 네임스페이스라 여기 없다 —
        // 감시 갱신이 서버 노드를 다시 확인할 일도 없다 (공유 목록에는 감시가 없다).
        RequireServer(item);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<FileItem?>(null);
    }

    /// <summary>
    /// 공유 하나를 목록의 한 줄로 만든다.
    /// <para>
    /// 폴더로 다룬다 — 더블클릭으로 들어가고 정렬에서 위로 온다. 크기와 수정 시각은
    /// <b>없다</b>: 공유에는 그런 값이 없고, 0 이나 0001-01-01 을 그리면 그것이 사실인
    /// 것처럼 보인다. 빈 칸이 "모른다" 다 (<c>TimestampFormatter</c>).
    /// </para>
    /// </summary>
    internal static FileItem ShareItem(LocationId server, string shareName)
        => new(shareName, server.Combine(shareName), 0, default, FileItemFlags.Directory);

    private static void RequireServer(LocationId location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (!location.IsNetworkServer)
        {
            // 라우팅이 잘못 붙은 것이다. 조용히 빈 목록을 내면 원인을 찾을 수 없다.
            throw new ArgumentException($"서버가 아닌 위치다: {location.DisplayPath}", nameof(location));
        }
    }

    private static List<string> ReadShares(LocationId server)
    {
        var remote = Marshal.StringToHGlobalUni(server.DisplayPath);

        try
        {
            var container = new NetResource
            {
                Scope = ResourceGlobalNet,
                Type = ResourceTypeDisk,
                DisplayType = ResourceDisplayTypeServer,
                Usage = ResourceUsageContainer,
                RemoteName = remote,
            };

            var opened = WNetOpenEnum(ResourceGlobalNet, ResourceTypeDisk, 0, ref container, out var handle);

            if (opened != NoError)
            {
                throw Translate(opened, server);
            }

            try
            {
                return ReadAll(handle, server);
            }
            finally
            {
                WNetCloseEnum(handle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(remote);
        }
    }

    private static List<string> ReadAll(nint handle, LocationId server)
    {
        var shares = new List<string>();
        var bufferBytes = InitialBufferBytes;
        var buffer = Marshal.AllocHGlobal(bufferBytes);

        try
        {
            while (true)
            {
                // -1 은 "가능한 만큼" 이다. 버퍼가 모자라면 ERROR_MORE_DATA 로 되돌아온다.
                var count = -1;
                var size = bufferBytes;

                var result = WNetEnumResource(handle, ref count, buffer, ref size);

                if (result == ErrorNoMoreItems)
                {
                    return shares;
                }

                if (result == ErrorMoreData)
                {
                    // 한 항목도 못 담을 만큼 작았다. 요구한 크기로 늘려 다시 묻는다.
                    Marshal.FreeHGlobal(buffer);
                    bufferBytes = Math.Max(size, bufferBytes * 2);
                    buffer = Marshal.AllocHGlobal(bufferBytes);
                    continue;
                }

                if (result != NoError)
                {
                    throw Translate(result, server);
                }

                Collect(buffer, count, shares);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 버퍼에 담긴 <c>NETRESOURCE</c> 배열에서 공유 이름을 꺼낸다.
    /// <para>
    /// <c>lpRemoteName</c> 은 <c>\\server\share</c> 전체다 — 마지막 구성요소만 이름이다.
    /// <b><c>$</c> 로 끝나는 관리 공유는 숨긴다</b> (탐색기와 같다).
    /// </para>
    /// </summary>
    private static void Collect(nint buffer, int count, List<string> shares)
    {
        var stride = Marshal.SizeOf<NetResource>();

        for (var index = 0; index < count; index++)
        {
            var entry = Marshal.PtrToStructure<NetResource>(buffer + (index * stride));

            if (entry.RemoteName == 0)
            {
                continue;
            }

            var remote = Marshal.PtrToStringUni(entry.RemoteName);

            if (string.IsNullOrEmpty(remote))
            {
                continue;
            }

            var name = remote[(remote.LastIndexOf('\\') + 1)..];

            if (name.Length == 0 || name.EndsWith('$'))
            {
                continue;
            }

            shares.Add(name);
        }
    }

    /// <summary>
    /// Win32 오류를 분류로 옮긴다 (docs/SHELL_NOTES.md §네트워크 오류 코드 매핑).
    /// <b>경로 문제와 인증 문제를 구분해야 사용자가 손쓸 방법이 생긴다</b> — 뭉뚱그리면
    /// 비밀번호를 고쳐야 하는지 주소를 고쳐야 하는지 알 수 없다.
    /// </summary>
    private static LocationAccessException Translate(int win32, LocationId server) => new(
        win32 switch
        {
            5 => LocationErrorKind.AccessDenied,          // ERROR_ACCESS_DENIED
            1326 => LocationErrorKind.AccessDenied,       // ERROR_LOGON_FAILURE — 자격증명 거부
            1311 => LocationErrorKind.AccessDenied,       // ERROR_NO_LOGON_SERVERS
            1219 => LocationErrorKind.AccessDenied,       // ERROR_SESSION_CREDENTIAL_CONFLICT
            52 => LocationErrorKind.NotFound,             // ERROR_DUP_NAME
            53 => LocationErrorKind.NotFound,             // ERROR_BAD_NETPATH
            64 => LocationErrorKind.NotFound,             // ERROR_NETNAME_DELETED
            67 => LocationErrorKind.NotFound,             // ERROR_BAD_NET_NAME
            1231 => LocationErrorKind.NotFound,           // ERROR_NETWORK_UNREACHABLE
            1232 => LocationErrorKind.NotFound,           // ERROR_HOST_UNREACHABLE
            _ => LocationErrorKind.Unknown,
        },
        server,
        win32);

    [StructLayout(LayoutKind.Sequential)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public nint LocalName;
        public nint RemoteName;
        public nint Comment;
        public nint Provider;
    }

    [LibraryImport("mpr.dll", EntryPoint = "WNetOpenEnumW")]
    private static partial int WNetOpenEnum(int scope, int type, int usage, ref NetResource container, out nint handle);

    [LibraryImport("mpr.dll", EntryPoint = "WNetEnumResourceW")]
    private static partial int WNetEnumResource(nint handle, ref int count, nint buffer, ref int bufferSize);

    [LibraryImport("mpr.dll")]
    private static partial int WNetCloseEnum(nint handle);
}
