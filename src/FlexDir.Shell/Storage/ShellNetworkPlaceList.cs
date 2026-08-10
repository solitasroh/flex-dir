using System.Runtime.InteropServices;

using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.Storage;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Storage;

/// <summary>
/// '네트워크 위치 추가' 로 등록된 곳을 읽는 <see cref="INetworkPlaceList"/> 구현체.
///
/// <para>
/// Windows 는 그 마법사의 결과를 <c>%APPDATA%\Microsoft\Windows\Network Shortcuts\</c> 아래
/// <b>이름 폴더 하나 + 그 안의 <c>target.lnk</c></b> 로 저장한다 (2026-08-10 실측).
/// 드라이브 문자도 <c>WNet</c> 세션도 만들지 않으므로 <see cref="SystemDriveList"/> 로는
/// 원리적으로 보이지 않는다.
/// </para>
///
/// <para>
/// <b><c>Resolve</c> 를 부르지 않는다.</b> <c>IShellLink::Resolve</c> 는 링크가 살아 있는지
/// 확인하러 네트워크로 나가고, 죽은 서버 하나에서 수십 초를 쓴다 (CLAUDE.md §3).
/// <see cref="SlgpRawPath"/> 로 <b>저장된 문자열만</b> 꺼낸다 — 닿을 수 있는지는 실제로
/// 펼치거나 열 때 알면 된다.
/// </para>
///
/// <para>
/// <b>이름이 <c>Shell*</c> 인 이유</b>: COM 을 쓴다. 그래서 STA 워커를 들고
/// <see cref="IDisposable"/> 이다 — <see cref="SystemDriveList"/> 와 갈리는 지점이 그것이다.
/// </para>
///
/// <para>
/// <b>폴더 안의 <c>target.lnk</c> 만 본다.</b> 마법사가 만드는 모양이 그것이고, 실물에서 본
/// 것도 그것뿐이다. 다른 모양(<c>.lnk</c> 파일이 바로 놓인 경우)이 실제로 있는 것을 보면
/// 그때 늘린다 — <c>Win32ErrorMapping</c> 이 "실측한 코드만" 넣은 것과 같은 유보다.
/// </para>
/// </summary>
public sealed class ShellNetworkPlaceList : INetworkPlaceList, IDisposable
{
    /// <summary>마법사가 만드는 바로가기 파일 이름.</summary>
    private const string LinkName = "target.lnk";

    /// <summary>저장된 경로를 그대로 낸다 — 추적·확인을 하지 않는다는 뜻이다.</summary>
    private const uint SlgpRawPath = 0x4;

    /// <summary>UNC 는 <c>MAX_PATH</c> 를 넘지 않는다.</summary>
    private const int PathCapacity = 260;

    /// <summary>읽기 전용으로 연다 (<c>STGM_READ</c>).</summary>
    private const uint StgmRead = 0;

    /// <summary>시작할 때 한 번 읽는다. 여럿 둘 이유가 없다.</summary>
    private const int Workers = 1;

    private readonly StaWorkQueue worker = new(Workers, "network places");
    private readonly string directory;
    private readonly Func<string, string?> resolve;

    public ShellNetworkPlaceList()
        : this(DefaultDirectory, ResolveLink)
    {
    }

    /// <param name="resolve">
    /// 바로가기 파일 경로를 대상 경로로 바꾼다. 못 읽으면 <c>null</c>.
    /// 실물은 COM 이라 테스트가 이 지점을 바꿔 낀다.
    /// </param>
    internal ShellNetworkPlaceList(string directory, Func<string, string?> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(resolve);

        this.directory = directory;
        this.resolve = resolve;
    }

    private static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Network Shortcuts");

    public ValueTask<IReadOnlyList<NetworkPlace>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return new ValueTask<IReadOnlyList<NetworkPlace>>(worker.RunAsync(List, ct));
    }

    public void Dispose() => worker.Dispose();

    private IReadOnlyList<NetworkPlace> List()
    {
        string[] folders;

        try
        {
            folders = Directory.GetDirectories(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 한 번도 등록한 적 없는 기계에는 이 폴더가 아예 없다 (GetDirectories 가
            // DirectoryNotFoundException — IOException 의 하위다). 오류가 아니다.
            return [];
        }

        var places = new List<NetworkPlace>(folders.Length);

        foreach (var folder in folders)
        {
            var link = Path.Combine(folder, LinkName);

            if (!File.Exists(link))
            {
                // 사용자가 이 폴더 안에 뭔가 만들어 둔 것이다. 갈 곳이 없다.
                continue;
            }

            // 대상이 경로가 아닐 수 있다 (shell 네임스페이스 항목의 CLSID). 파싱되지
            // 않으면 트리에 올려도 눌러서 갈 곳이 없다.
            if (resolve(link) is { } target && LocationId.TryParse(target, out var path, out _))
            {
                places.Add(new NetworkPlace(path, Path.GetFileName(folder)));
            }
        }

        places.Sort((left, right) => NaturalStringComparer.Instance.Compare(left.Label, right.Label));

        return places;
    }

    /// <summary>
    /// 실물 바로가기 해석. <c>IShellLink</c> 는 STA 를 요구하므로 워커 안에서만 불린다.
    /// </summary>
    private static string? ResolveLink(string linkPath)
    {
        var link = (IShellLinkW)new ShellLink();

        try
        {
            ((IPersistFile)link).Load(linkPath, StgmRead);

            var buffer = new char[PathCapacity];

            // 찾기 데이터는 쓰지 않지만 넘겨야 한다 — NULL 을 주면 구현에 따라 거부한다.
            link.GetPath(buffer, buffer.Length, out _, SlgpRawPath);

            var end = Array.IndexOf(buffer, '\0');
            var target = new string(buffer, 0, end < 0 ? buffer.Length : end);

            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (COMException)
        {
            // 깨진 바로가기다. 트리에서 빠지는 것으로 충분하다.
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    // ── COM ─────────────────────────────────────────────────────────

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>
    /// <c>IShellLinkW</c> 중 <see cref="GetPath"/> 하나만 쓴다. <b>그래도 앞의 메서드를
    /// 순서대로 선언해야 한다</b> — COM 은 vtable 순서로 부르므로 빠지면 다른 함수가 불린다.
    /// </summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPArray)] char[] file,
            int capacity,
            out Win32FindDataW found,
            uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([Out][MarshalAs(UnmanagedType.LPArray)] char[] name, int capacity);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPArray)] char[] directory, int capacity);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out][MarshalAs(UnmanagedType.LPArray)] char[] arguments, int capacity);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation([Out][MarshalAs(UnmanagedType.LPArray)] char[] icon, int capacity, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        /// <summary>부르지 않는다 — 네트워크로 나간다 (위 §요약).</summary>
        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    /// <summary><c>GetPath</c> 가 채우는 자리. 값을 읽지 않지만 크기가 맞아야 한다.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindDataW
    {
        public uint Attributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Reserved0;
        public uint Reserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }
}
