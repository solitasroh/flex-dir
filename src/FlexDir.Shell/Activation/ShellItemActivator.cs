using System.Runtime.InteropServices;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Activation;

/// <summary>
/// 연결 프로그램을 여는 구현체. <c>ShellExecuteEx</c> 하나로 끝난다.
/// <para>
/// <b>기다리지 않는다.</b> 프로그램이 뜬 뒤의 수명은 우리 것이 아니므로 프로세스 핸들을
/// 요청하지 않는다(<c>SEE_MASK_NOCLOSEPROCESS</c> 없음) — 받으면 우리가 닫아야 하고,
/// 그 핸들은 큐 밖으로 나갈 수도 없다.
/// </para>
/// <para>
/// <b>오류 대화상자를 끄지 않는다.</b> <c>SEE_MASK_FLAG_NO_UI</c> 를 주면 연결 프로그램이
/// 없을 때 shell 이 '연결 프로그램' 대화상자를 띄우지 않고 조용히 1155 로 돌아온다 —
/// 더블클릭이 아무 일도 하지 않는 것처럼 보인다. 전작이 모든 대화상자를 껐다가 조용히
/// 실패한 자리와 같은 판단이다 (docs/SHELL_NOTES.md §파일 조작).
/// </para>
/// </summary>
public sealed partial class ShellItemActivator : IItemActivator, IDisposable
{
    /// <summary>
    /// 활성화는 한 번에 한 항목이지만(<c>PaneViewModel.ActivateAsync</c>), 대화상자가 뜨면
    /// 그 호출이 사용자의 답을 기다리며 막힌다. 하나뿐이면 그 동안 다음 실행이 큐에 갇힌다.
    /// </summary>
    private const int Workers = 2;

    private readonly StaWorkQueue worker = new(Workers, "activation");

    private readonly Func<LocationId, int?> launch;

    public ShellItemActivator() => launch = Launch;

    /// <summary>
    /// 실행 지점을 바꿔 끼운다. 실물은 성공하면 프로그램을 띄우고 실패하면 shell 이 자기
    /// 오류 대화상자를 띄우므로 — 둘 다 자동 테스트가 일으켜서는 안 되는 일이다 —
    /// 재는 것은 <b>shell 이 낸 코드를 어떻게 옮기는가</b> 뿐이다.
    /// </summary>
    /// <param name="launch">
    /// 실행을 시도하고 Win32 오류 코드를 낸다. <c>null</c> 이면 성공이다.
    /// </param>
    internal ShellItemActivator(Func<LocationId, int?> launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        this.launch = launch;
    }

    public Task ActivateAsync(LocationId item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();

        return worker.RunAsync(() => Activate(item), ct);
    }

    public void Dispose() => worker.Dispose();

    /// <summary>
    /// 큐가 값을 요구하므로 무엇이든 낸다. 이 결과를 읽는 곳은 없다 —
    /// 실패는 예외로만 나간다.
    /// </summary>
    private bool Activate(LocationId item)
    {
        if (launch(item) is not { } win32)
        {
            return true;
        }

        // 연결 프로그램이 없어서 shell 이 대화상자를 띄운 것(1155)과 사용자가 그것을 닫은
        // 것(1223)은 우리 쪽 실패가 아니다. 오류로 만들면 사용자가 방금 스스로 한 선택을
        // 상태표시줄에서 오류로 통보받는다.
        if (win32 is ErrorNoAssociation or ErrorCancelled)
        {
            return true;
        }

        var kind = Win32ErrorMapping.Classify(win32);

        // Classify 는 0 과 모르는 코드에 None 을 낸다. 그대로 넘기면
        // LocationAccessException 이 "오류가 아닌 것을 던졌다" 며 ArgumentException 을 내고
        // 진짜 원인이 사라진다 (FileSystemFolderSource.Translate 와 같은 자리).
        if (kind == LocationErrorKind.None)
        {
            kind = LocationErrorKind.Unknown;
        }

        throw new LocationAccessException(kind, item, win32);
    }

    /// <summary>
    /// 실제 <c>ShellExecuteEx</c>. 성공하면 <c>null</c>, 실패하면 Win32 코드다.
    /// <para>
    /// 작업 디렉터리를 항목의 부모로 준다 — 탐색기와 같다. 주지 않으면 실행된 프로그램이
    /// 프로세스의 현재 디렉터리를 물려받고, 상주 프로세스(ADR-003)에서 그것은 아무 관계
    /// 없는 폴더다.
    /// </para>
    /// </summary>
    private static int? Launch(LocationId item)
    {
        // shell 파서는 \\?\ 확장 접두사를 모른다. 다른 구현체와 같이 DisplayPath 를 준다.
        var file = Marshal.StringToHGlobalUni(item.DisplayPath);
        var directory = item.TryGetParent(out var parent)
            ? Marshal.StringToHGlobalUni(parent.DisplayPath)
            : 0;

        try
        {
            var info = new ShellExecuteInfo
            {
                Size = (uint)Marshal.SizeOf<ShellExecuteInfo>(),

                // IDLIST 는 항목의 IContextMenu 를 태워 탐색기의 더블클릭과 같은 동사를
                // 고르게 한다. NOASYNC 는 DDE 를 쓰는 낡은 프로그램(Office 계열)이 대화를
                // 끝낼 때까지 기다리게 한다 — 워커에 메시지 루프가 없어서 필요하다.
                Mask = SeeMaskInvokeIdList | SeeMaskNoAsync,
                File = file,
                Directory = directory,
                Show = SwShowNormal,
            };

            return ShellExecuteEx(ref info) ? null : Marshal.GetLastPInvokeError();
        }
        finally
        {
            Marshal.FreeHGlobal(file);

            if (directory != 0)
            {
                Marshal.FreeHGlobal(directory);
            }
        }
    }

    // ── 상수 ────────────────────────────────────────────────────────

    /// <summary>연결된 프로그램이 없다. shell 이 '연결 프로그램' 대화상자를 띄운 뒤 온다.</summary>
    private const int ErrorNoAssociation = 1155;

    /// <summary>사용자가 대화상자를 닫았다.</summary>
    private const int ErrorCancelled = 1223;

    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const uint SeeMaskNoAsync = 0x00000100;

    private const int SwShowNormal = 1;

    // ── P/Invoke ────────────────────────────────────────────────────

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>
    /// <c>SHELLEXECUTEINFOW</c>. 문자열을 <c>nint</c> 로 두는 이유: 구조체가 blittable
    /// 이어야 <c>LibraryImport</c> 가 <c>ref</c> 로 그대로 넘길 수 있다. 대신 수명을
    /// <see cref="Launch"/> 가 진다.
    /// <para>
    /// <b>부르지 않는 필드도 순서대로 선언해야 한다.</b> <c>Size</c> 에 넣는 값이
    /// 이 구조체의 크기이고, shell 은 그 크기로 버전을 판정한다 — 하나라도 빠지면
    /// 다른 구조체가 된다.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShellExecuteInfo
    {
        public uint Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint File;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public nint Instance;
        public nint IdList;
        public nint Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }
}
