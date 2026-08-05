using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Operations;

/// <summary>
/// 탐색기와 주고받는 클립보드 구현체. <c>CF_HDROP</c> 로 경로를 싣고
/// <c>Preferred DropEffect</c> 로 복사·잘라내기를 표시한다.
/// <para>
/// <b>자체 포맷을 만들지 않는다.</b> 탐색기에서 복사해 flex-dir 에서 붙여넣고 그 반대도
/// 성립해야 한다 (docs/PRD.md §2) — 그 호환이 이 클래스가 존재하는 이유다.
/// </para>
/// <para>
/// <b>OLE 가 아니라 Win32 클립보드를 쓴다.</b> <c>OleSetClipboard</c> 는 데이터 객체의
/// 수명을 우리 프로세스에 묶어 <c>OleFlushClipboard</c> 를 요구하지만
/// (docs/SHELL_NOTES.md §클립보드 함정 2), <c>SetClipboardData</c> 로 넘긴
/// <c>HGLOBAL</c> 은 시스템이 가져가 프로세스가 죽어도 남는다. 우리가 옮기는 것은
/// 항목의 <b>식별자</b>뿐이라 지연 렌더링이 필요 없다.
/// </para>
/// <para>
/// <b>포트가 동기인 이유</b>는 클립보드가 호출 스레드에 묶여 있어서다. 그렇다고 아무
/// 스레드에서나 불러도 되는 것은 아니므로 STA 워커로 넘기고 결과를 기다린다 — 기다림은
/// 유한하다(<see cref="OpenAttempts"/>번 시도하고 포기한다).
/// </para>
/// </summary>
public sealed partial class ShellClipboardBridge : IClipboardBridge, IDisposable
{
    /// <summary>클립보드는 한 번에 한 스레드만 연다. 워커가 여럿이면 서로를 막는다.</summary>
    private const int Workers = 1;

    private readonly StaWorkQueue worker = new(Workers, "clipboard");

    private readonly Func<uint, byte[]?> read;
    private readonly Action<IReadOnlyList<Blob>> write;

    public ShellClipboardBridge()
    {
        read = ReadFormat;
        write = WriteFormats;
    }

    /// <summary>
    /// 클립보드에 닿는 자리를 바꿔 끼운다. 실물을 쓰면 자동 테스트가 <b>사용자가 복사해
    /// 둔 것을 지운다</b> — 게이트가 돌 때마다 일어나서는 안 되는 일이다. 바꿔 끼운 자리
    /// 위에서 재는 것은 바이트 배치이고, 탐색기와의 호환은 그 배치가 전부다.
    /// </summary>
    internal ShellClipboardBridge(Func<uint, byte[]?> read, Action<IReadOnlyList<Blob>> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        this.read = read;
        this.write = write;
    }

    /// <summary>클립보드에 싣는 한 포맷의 바이트.</summary>
    internal sealed record Blob(uint Format, byte[] Data);

    /// <summary>
    /// <c>CFSTR_PREFERREDDROPEFFECT</c> 의 런타임 포맷 번호. 이름으로 등록하는 포맷이라
    /// 값이 고정이 아니다 — 같은 세션 안에서는 모든 앱이 같은 번호를 받는다.
    /// </summary>
    internal static uint DropEffectFormat { get; } = RegisterClipboardFormat("Preferred DropEffect");

    public void SetCopy(IReadOnlyList<LocationId> items) => Put(items, DropEffectCopy);

    public void SetCut(IReadOnlyList<LocationId> items) => Put(items, DropEffectMove);

    public bool TryGetPaste(out IReadOnlyList<LocationId> items, out bool isMove)
    {
        var found = Run(Read);

        items = found.Items;
        isMove = found.IsMove;

        return items.Count > 0;
    }

    public void Dispose() => worker.Dispose();

    private void Put(IReadOnlyList<LocationId> items, uint effect)
    {
        ArgumentNullException.ThrowIfNull(items);

        // 빈 선택으로 클립보드를 비우지 않는다 — 사용자가 다른 앱에서 복사해 둔 것이
        // 사라진다. CanExecute 로 막지만 그것과 실행 사이에 감시 갱신이 끼어들 수 있다.
        if (items.Count == 0)
        {
            return;
        }

        var paths = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            // 탐색기의 파서는 \\?\ 확장 접두사를 모른다. 다른 구현체와 같이 DisplayPath 를 준다.
            paths[index] = items[index].DisplayPath;
        }

        var blobs = new Blob[]
        {
            new(CfHdrop, EncodeDropFiles(paths)),
            new(DropEffectFormat, BitConverter.GetBytes(effect)),
        };

        Run(() =>
        {
            write(blobs);

            return true;
        });
    }

    private Paste Read()
    {
        if (read(CfHdrop) is not { } data)
        {
            return new Paste([], false);
        }

        var items = DecodeDropFiles(data);

        // 표시가 없으면 복사다. 이동으로 오해하면 남의 파일이 사라진다.
        var effect = read(DropEffectFormat) is { Length: >= 4 } marker
            ? BinaryPrimitives.ReadUInt32LittleEndian(marker)
            : DropEffectCopy;

        return new Paste(items, (effect & DropEffectMove) != 0);
    }

    /// <summary>
    /// STA 워커에서 돌리고 기다린다. UI 스레드가 잠깐 멈추지만 옮기는 것은 경로 문자열뿐이고,
    /// 클립보드를 열지 못하면 <see cref="OpenAttempts"/>번 만에 포기하므로 기다림이 유한하다.
    /// </summary>
    private T Run<T>(Func<T> job) => worker.RunAsync(job, CancellationToken.None).GetAwaiter().GetResult();

    private sealed record Paste(IReadOnlyList<LocationId> Items, bool IsMove);

    // ── 포맷 ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>DROPFILES</c> 헤더 + 널로 끊은 경로 목록 + 끝을 알리는 널 하나.
    /// <para>
    /// <c>fWide</c> 를 세우지 않으면 받는 쪽이 ANSI 로 읽어 한글 경로가 깨진다.
    /// </para>
    /// </summary>
    private static byte[] EncodeDropFiles(IReadOnlyList<string> paths)
    {
        var list = new StringBuilder();

        foreach (var path in paths)
        {
            list.Append(path).Append('\0');
        }

        list.Append('\0');

        var body = Encoding.Unicode.GetBytes(list.ToString());
        var bytes = new byte[DropFilesSize + body.Length];

        // pFiles — 목록이 시작하는 offset. pt(4..11) 와 fNC(12..15) 는 0 이다.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, DropFilesSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(FileWideOffset), 1);

        body.CopyTo(bytes, DropFilesSize);

        return bytes;
    }

    /// <summary>
    /// 아무 앱이나 클립보드에 아무 바이트나 실을 수 있다. 읽을 수 없으면 빈 목록이다 —
    /// 예외를 던지면 붙여넣기 한 번이 창을 무너뜨린다.
    /// </summary>
    private static IReadOnlyList<LocationId> DecodeDropFiles(byte[] data)
    {
        if (data.Length <= DropFilesSize)
        {
            return [];
        }

        // ANSI 목록은 읽지 않는다. 코드페이지를 짐작해 경로를 만들어내는 것보다
        // 붙여넣지 않는 편이 안전하다.
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(FileWideOffset)) == 0)
        {
            return [];
        }

        var offset = BinaryPrimitives.ReadUInt32LittleEndian(data);

        if (offset < DropFilesSize || offset >= (uint)data.Length)
        {
            return [];
        }

        var list = Encoding.Unicode.GetString(data, (int)offset, data.Length - (int)offset);
        var items = new List<LocationId>();

        foreach (var path in list.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // v1 이 읽을 수 없는 경로(UNC 등)는 건너뛴다 — 하나 때문에 나머지까지 버리면
            // 사용자는 이유 없이 붙여넣기가 안 되는 것을 본다 (ADR-010).
            if (LocationId.TryParse(path, out var location, out _))
            {
                items.Add(location);
            }
        }

        return items;
    }

    // ── 실제 클립보드 ───────────────────────────────────────────────

    /// <summary>
    /// 클립보드를 비우고 넘겨받은 포맷을 전부 싣는다.
    /// <para>
    /// <b>실패하면 <c>HGLOBAL</c> 은 우리 것이다.</b> <c>SetClipboardData</c> 가 성공해야
    /// 소유권이 시스템으로 넘어간다 (docs/SHELL_NOTES.md §클립보드 함정 1).
    /// </para>
    /// </summary>
    private static void WriteFormats(IReadOnlyList<Blob> blobs)
    {
        // 다른 앱이 쥐고 있으면 조용히 포기한다. 사용자는 다시 누를 수 있고,
        // 여기서 예외를 던지면 Ctrl+C 한 번이 창을 무너뜨린다.
        if (!TryOpenClipboard())
        {
            return;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return;
            }

            foreach (var blob in blobs)
            {
                var memory = Allocate(blob.Data);

                if (memory == 0)
                {
                    return;
                }

                if (SetClipboardData(blob.Format, memory) == 0)
                {
                    GlobalFree(memory);

                    return;
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[]? ReadFormat(uint format)
    {
        // 없는 포맷 때문에 클립보드를 열지 않는다 — 여는 동안 다른 앱이 막힌다.
        if (!IsClipboardFormatAvailable(format) || !TryOpenClipboard())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(format);

            if (handle == 0)
            {
                return null;
            }

            var size = (int)GlobalSize(handle);

            if (size <= 0)
            {
                return null;
            }

            var pointer = GlobalLock(handle);

            if (pointer == 0)
            {
                return null;
            }

            try
            {
                var bytes = new byte[size];

                Marshal.Copy(pointer, bytes, 0, size);

                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            // 핸들은 클립보드 것이다. 닫기만 하고 해제하지 않는다.
            CloseClipboard();
        }
    }

    /// <summary><c>GMEM_MOVEABLE</c> 로 잡아야 클립보드가 받는다.</summary>
    private static nint Allocate(byte[] data)
    {
        var memory = GlobalAlloc(GmemMoveable, (nuint)data.Length);

        if (memory == 0)
        {
            return 0;
        }

        var pointer = GlobalLock(memory);

        if (pointer == 0)
        {
            GlobalFree(memory);

            return 0;
        }

        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            GlobalUnlock(memory);
        }

        return memory;
    }

    /// <summary>
    /// 클립보드는 한 번에 한 프로세스만 연다. 다른 앱이 쥐고 있으면 즉시 실패하므로
    /// 몇 번 다시 해 보고 포기한다 — 무한히 기다리면 UI 스레드가 그만큼 멈춘다.
    /// </summary>
    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(0))
            {
                return true;
            }

            Thread.Sleep(OpenRetryDelayMs);
        }

        return false;
    }

    // ── 상수 ────────────────────────────────────────────────────────

    /// <summary><c>CF_HDROP</c>. 미리 정의된 포맷이라 값이 고정이다.</summary>
    private const uint CfHdrop = 15;

    /// <summary><c>DROPFILES</c> 의 크기. 파일 목록이 이만큼 뒤에서 시작한다.</summary>
    private const int DropFilesSize = 20;

    /// <summary><c>DROPFILES.fWide</c> 의 위치 — pFiles(4) + pt(8) + fNC(4).</summary>
    private const int FileWideOffset = 16;

    private const uint DropEffectCopy = 1;
    private const uint DropEffectMove = 2;

    private const uint GmemMoveable = 0x0002;

    private const int OpenAttempts = 5;
    private const int OpenRetryDelayMs = 20;

    // ── P/Invoke ────────────────────────────────────────────────────

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);
}
