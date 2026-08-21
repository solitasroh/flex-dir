using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Presentation;

/// <summary>
/// 목록에 그릴 그림을 shell 에서 얻는 구현체. 두 메서드가 서로 다른 API 위에 선다 —
/// 형식 아이콘은 시스템 이미지 리스트, 썸네일은 <c>IShellItemImageFactory</c> 다.
/// <para>
/// <b>네이티브 핸들이 이 파일 밖으로 나가지 않는다.</b> <c>HICON</c>·<c>HBITMAP</c>·DC 는
/// 전부 <see cref="StaWorkQueue"/> 작업 안에서 만들고 그 안에서 해제한다. 전작이 종료
/// 시점에 아이콘을 흘린 자리가 여기다 (docs/SHELL_NOTES.md §아이콘 함정 3).
/// </para>
/// <para>
/// <b>캐시를 만들지 않는다.</b> shell 캐시를 쓰고, 확장자별 캐시는 호출자
/// (<c>ThumbnailRequestScheduler</c>)가 든다 (docs/UI_GUIDE.md §금지 목록).
/// </para>
/// <para>
/// <b>알파는 미리 곱해져 있다(premultiplied).</b> 두 경로 모두 GDI 의 <c>AlphaBlend</c>
/// 계열을 거치므로 View 는 <c>PixelFormats.Bgra32</c> 가 아니라 <c>Pbgra32</c> 로
/// <c>WriteableBitmap</c> 을 만들어야 한다. 틀리면 반투명 가장자리가 어둡게 번진다.
/// </para>
/// </summary>
public sealed partial class ShellThumbnailSource : IThumbnailSource, IDisposable
{
    /// <summary>
    /// shell 호출은 동기 블로킹이고 네트워크·클라우드 항목에서 초 단위로 멈춘다
    /// (docs/SHELL_NOTES.md §아이콘 함정 1). 호출자가 동시 요청을 넷으로 묶으므로
    /// (<c>ThumbnailRequestScheduler.MaxConcurrentRequests</c>) 워커도 넷이면 충분하다.
    /// </summary>
    private const int Workers = 4;

    private readonly StaWorkQueue worker = new(Workers, "thumbnails");

    private readonly Func<string, bool, int, ThumbnailBitmap?> typeIcon;
    private readonly Func<LocationId, int, ThumbnailBitmap?> thumbnail;

    public ShellThumbnailSource()
    {
        typeIcon = QueryTypeIcon;
        thumbnail = QueryThumbnail;
    }

    /// <summary>
    /// 조회를 바꿔 끼운다. 실물 shell 은 아파트먼트·실패 경로를 결정적으로 재게 해 주지
    /// 않는다 — 어떤 기계에서도 반드시 실패하는 입력이 없고, STA 여부는 결과에 드러나지
    /// 않는다 (<c>Task.Run</c> 위에서도 대부분 그냥 성공한다).
    /// </summary>
    internal ShellThumbnailSource(
        Func<string, bool, int, ThumbnailBitmap?> typeIcon,
        Func<LocationId, int, ThumbnailBitmap?> thumbnail)
    {
        ArgumentNullException.ThrowIfNull(typeIcon);
        ArgumentNullException.ThrowIfNull(thumbnail);

        this.typeIcon = typeIcon;
        this.thumbnail = thumbnail;
    }

    public ValueTask<ThumbnailBitmap?> GetThumbnailAsync(LocationId item, int requestedSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
        ct.ThrowIfCancellationRequested();

        return new ValueTask<ThumbnailBitmap?>(
            worker.RunAsync(() => Guarded(() => thumbnail(item, requestedSize)), ct));
    }

    public ValueTask<ThumbnailBitmap?> GetTypeIconAsync(
        string extension,
        bool isDirectory,
        int requestedSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
        ct.ThrowIfCancellationRequested();

        // 호출자의 캐시 키는 이미 소문자지만(FileItem.Extension) 포트를 직접 쓰는 쪽이
        // 있을 수 있다. .JPG 와 .jpg 가 갈리면 같은 그림을 두 번 사 온다.
        var key = extension.ToLowerInvariant();

        return new ValueTask<ThumbnailBitmap?>(
            worker.RunAsync(() => Guarded(() => typeIcon(key, isDirectory, requestedSize)), ct));
    }

    /// <summary>
    /// 아직 조회하지 않는다 — <c>null</c> 은 "아이콘이 없다" 라는 이 포트의 정상 상태로
    /// 접히므로 호출자는 이대로도 안전하다. 던지는 임시 구현을 두지 않는 이유다: 실제
    /// 조회가 채워지기 전에 불려도 사건이 되면 안 된다. 인자 검증만 계약대로 먼저 한다.
    /// </summary>
    public ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
        ct.ThrowIfCancellationRequested();

        return ValueTask.FromResult<ThumbnailBitmap?>(null);
    }

    public void Dispose() => worker.Dispose();

    /// <summary>
    /// 계약대로 실패는 예외가 아니라 <c>null</c> 이다 (docs/PRD.md §4). COM·P/Invoke 위에
    /// 서므로 무엇이 나올지 미리 알 수 없고, 그림 하나 때문에 목록이 무너지면 안 된다.
    /// </summary>
    private static ThumbnailBitmap? Guarded(Func<ThumbnailBitmap?> job)
    {
        try
        {
            return job();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    // ── 형식 아이콘 ─────────────────────────────────────────────────

    /// <summary>
    /// <b>파일을 건드리지 않는다.</b> 존재하지 않는 센티널 이름에
    /// <c>SHGFI_USEFILEATTRIBUTES</c> 를 함께 주면 shell 은 확장자 연결 정보만 본다
    /// (docs/SHELL_NOTES.md §아이콘). 네트워크 경로나 클라우드 자리표시자에서도 멈추지 않는다.
    /// <para>
    /// 아이콘이 아니라 <b>인덱스</b>를 받는 이유: <c>SHGFI_ICON</c> 은 16·32 밖에 내지
    /// 못하는데 큰 아이콘 뷰는 96 을 쓴다 (docs/DESIGN.md §2). 인덱스는 어느 크기의
    /// 시스템 이미지 리스트에서든 같은 항목을 가리킨다.
    /// </para>
    /// </summary>
    private static ThumbnailBitmap? QueryTypeIcon(string extension, bool isDirectory, int requestedSize)
    {
        var sentinel = isDirectory || extension.Length == 0
            ? SentinelName
            : SentinelName + "." + extension;

        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;

        // 형식 아이콘 경로 <b>전체</b>가 동시 호출에 약하다 (<see cref="ShellInfoGate"/>):
        // SHGetFileInfo 는 조용히 0 을 내고, 그 뒤의 시스템 이미지 리스트는 던진다. 워커가
        // 넷이라 두 페인이 같은 순간에 물으면 한쪽이 아이콘을 통째로 잃었다 — 그리고 실패는
        // 호출자 캐시에 남아 다시 묻지 않는다.
        return ShellInfoGate.Query<ThumbnailBitmap?>(() =>
        {
            var info = default(ShellFileInfo);

            var list = SHGetFileInfo(
                sentinel,
                attributes,
                ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiSysIconIndex | ShgfiUseFileAttributes);

            return list == 0 ? null : FromImageList(ImageListFor(requestedSize), info.IconIndex);
        });
    }

    /// <summary>
    /// 요청 크기를 담을 수 있는 가장 작은 시스템 이미지 리스트. 큰 것을 골라 줄여 쓰면
    /// 버퍼가 커지고(점보는 256×256 = 256KB), 작은 것을 늘려 쓰면 뿌옇게 그려진다.
    /// 실제 픽셀 크기는 DPI 를 따르므로 여기서 정하는 것은 <b>어느 리스트인가</b>뿐이다.
    /// </summary>
    private static int ImageListFor(int requestedSize) => requestedSize switch
    {
        <= 16 => ShilSmall,
        <= 32 => ShilLarge,
        <= 48 => ShilExtraLarge,
        _ => ShilJumbo,
    };

    private static ThumbnailBitmap? FromImageList(int list, int index)
    {
        var iid = typeof(IImageList).GUID;

        if (SHGetImageList(list, in iid, out var raw) != 0 || raw == 0)
        {
            return null;
        }

        try
        {
            // 이미지 리스트 자체는 시스템 소유다. 참조만 놓고 파괴하지 않는다.
            var images = (IImageList)Marshal.GetObjectForIUnknown(raw);

            try
            {
                if (images.GetIcon(index, IldTransparent, out var icon) != 0 || icon == 0)
                {
                    return null;
                }

                try
                {
                    return FromIcon(icon);
                }
                finally
                {
                    DestroyIcon(icon);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(images);
            }
        }
        finally
        {
            // GetObjectForIUnknown 이 자기 참조를 따로 잡았으므로 원래 것은 여기서 놓는다.
            Marshal.Release(raw);
        }
    }

    private static ThumbnailBitmap? FromIcon(nint icon)
        => TryMeasureIcon(icon, out var width, out var height) ? Compose(icon, width, height) : null;

    /// <summary>
    /// 아이콘의 실제 크기. <c>GetIconInfo</c> 를 <b>크기를 읽는 데만</b> 쓴다 — 이 함수는
    /// <c>hbmColor</c>·<c>hbmMask</c> 의 <b>복사본을 새로 만들어</b> 주므로 해제 대상이
    /// 늘어난다 (docs/SHELL_NOTES.md §아이콘 함정 3). 픽셀은 그 비트맵이 아니라
    /// <see cref="Compose"/> 의 <c>DrawIconEx</c> 로 얻는다.
    /// </summary>
    private static bool TryMeasureIcon(nint icon, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!GetIconInfo(icon, out var info))
        {
            return false;
        }

        try
        {
            var source = info.Color != 0 ? info.Color : info.Mask;

            if (source == 0)
            {
                return false;
            }

            var bitmap = default(NativeBitmap);

            if (GetObject(source, Marshal.SizeOf<NativeBitmap>(), ref bitmap) == 0)
            {
                return false;
            }

            width = bitmap.Width;

            // 컬러 비트맵이 없으면 마스크뿐인 흑백 아이콘이다. AND 와 XOR 가 세로로 붙어
            // 마스크 높이가 아이콘 높이의 두 배다.
            height = info.Color != 0 ? bitmap.Height : bitmap.Height / 2;

            return width > 0 && height > 0;
        }
        finally
        {
            if (info.Color != 0)
            {
                DeleteObject(info.Color);
            }

            if (info.Mask != 0)
            {
                DeleteObject(info.Mask);
            }
        }
    }

    /// <summary>
    /// 아이콘을 32bpp top-down DIB 에 합성한다. <c>DI_NORMAL</c> 이 이미지와 마스크를 함께
    /// 처리하므로 레거시 1비트 마스크 아이콘의 투명도가 이 경로에서만 제대로 나온다.
    /// </summary>
    private static ThumbnailBitmap? Compose(nint icon, int width, int height)
    {
        var dc = CreateCompatibleDC(0);

        if (dc == 0)
        {
            return null;
        }

        try
        {
            var header = TopDown(width, height);
            var section = CreateDIBSection(dc, in header, DibRgbColors, out var bits, 0, 0);

            if (section == 0 || bits == 0)
            {
                return null;
            }

            try
            {
                var previous = SelectObject(dc, section);

                try
                {
                    if (!DrawIconEx(dc, 0, 0, icon, width, height, 0, 0, DiNormal))
                    {
                        return null;
                    }
                }
                finally
                {
                    SelectObject(dc, previous);
                }

                // GDI 는 명령을 모아 두었다가 실행한다. 비우지 않고 읽으면 빈 픽셀을 본다.
                GdiFlush();

                var pixels = new byte[width * height * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);

                Opaquify(pixels);

                return new ThumbnailBitmap(width, height, pixels);
            }
            finally
            {
                DeleteObject(section);
            }
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    /// <summary>
    /// 알파가 전부 0 이면 불투명으로 되돌린다.
    /// <para>
    /// 레거시 아이콘(32bpp 알파가 없는 것)은 <c>DrawIconEx</c> 가 <c>BitBlt</c> 로 그리는데
    /// <c>BitBlt</c> 는 알파 바이트를 건드리지 않는다 — DIB 를 0 으로 시작했으므로 결과가
    /// 완전 투명이 되어 <b>아이콘이 보이지 않는다.</b> 32bpp 아이콘은 <c>AlphaBlend</c> 를
    /// 거쳐 알파가 남으므로 이 보정에 걸리지 않는다.
    /// </para>
    /// <para>
    /// 대가: 진짜로 전부 투명한 아이콘이 검은 사각형이 된다. 그런 아이콘은 화면에서
    /// 구분되지 않으므로 보이지 않는 쪽보다 낫다고 봤다.
    /// </para>
    /// </summary>
    private static void Opaquify(byte[] pixels)
    {
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                return;
            }
        }

        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 0xFF;
        }
    }

    // ── 썸네일 ──────────────────────────────────────────────────────

    /// <summary>
    /// 항목의 <b>실제</b> 썸네일. 없으면 <c>null</c> 이다.
    /// <para>
    /// <c>SIIGBF_THUMBNAILONLY</c> 가 그 계약을 만든다 — 주지 않으면 썸네일이 없을 때
    /// shell 이 형식 아이콘을 대신 돌려주고, 그러면 아이콘 경로와 겹쳐 호출자의
    /// "실패는 재시도하지 않는다"(docs/PRD.md §4)가 뜻을 잃는다.
    /// </para>
    /// </summary>
    private static ThumbnailBitmap? QueryThumbnail(LocationId item, int requestedSize)
    {
        // shell 파서는 \\?\ 확장 접두사를 모른다. 다른 구현체와 같이 DisplayPath 를 준다.
        var iid = typeof(IShellItemImageFactory).GUID;

        // 없는 파일·파싱 실패가 여기서 걸린다. 예외가 아니라 null 이다.
        if (SHCreateItemFromParsingName(item.DisplayPath, 0, in iid, out var raw) != 0 || raw == 0)
        {
            return null;
        }

        try
        {
            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(raw);

            try
            {
                var size = new NativeSize { Width = requestedSize, Height = requestedSize };

                if (factory.GetImage(size, SiigbfThumbnailOnly, out var bitmap) != 0 || bitmap == 0)
                {
                    return null;
                }

                try
                {
                    return FromBitmap(bitmap);
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    /// <summary>
    /// <c>HBITMAP</c> 을 BGRA32 로 읽는다. 32bpp <c>BI_RGB</c> top-down 헤더를 주면
    /// <c>GetDIBits</c> 가 알파를 보존한 채 원하는 배치로 변환해 준다.
    /// </summary>
    private static ThumbnailBitmap? FromBitmap(nint bitmap)
    {
        var native = default(NativeBitmap);

        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), ref native) == 0)
        {
            return null;
        }

        var width = native.Width;

        // 원본이 bottom-up 이면 Height 가 음수로 온다. 배치는 우리가 헤더로 지정하므로
        // 여기서는 크기만 본다.
        var height = Math.Abs(native.Height);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var dc = CreateCompatibleDC(0);

        if (dc == 0)
        {
            return null;
        }

        try
        {
            var header = TopDown(width, height);
            var pixels = new byte[width * height * 4];

            var copied = GetDIBits(
                dc,
                bitmap,
                0,
                (uint)height,
                ref MemoryMarshal.GetArrayDataReference(pixels),
                ref header,
                DibRgbColors);

            return copied == height ? new ThumbnailBitmap(width, height, pixels) : null;
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    /// <summary>
    /// 위에서 아래로 채우는 32bpp <c>BI_RGB</c> 헤더. 높이가 <b>음수</b>인 것이 top-down
    /// 지시다 — <c>ThumbnailBitmap</c> 이 그 배치를 요구한다.
    /// </summary>
    private static BitmapInfo TopDown(int width, int height) => new()
    {
        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
        Width = width,
        Height = -height,
        Planes = 1,
        BitCount = 32,
        Compression = BiRgb,
    };

    // ── 상수 ────────────────────────────────────────────────────────

    /// <summary>
    /// 실제로 존재하지 않아도 되는 이름. <c>SHGFI_USEFILEATTRIBUTES</c> 가 붙으면 shell 은
    /// 이것을 열지 않고 확장자만 본다.
    /// </summary>
    private const string SentinelName = "x";

    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint ShgfiSysIconIndex = 0x00004000;
    private const uint ShgfiUseFileAttributes = 0x00000010;

    private const int ShilLarge = 0;        // SM_CXICON — 100% DPI 에서 32
    private const int ShilSmall = 1;        // SM_CXSMICON — 100% DPI 에서 16
    private const int ShilExtraLarge = 2;   // 48
    private const int ShilJumbo = 4;        // 256

    private const uint IldTransparent = 0x00000001;

    /// <summary>실제 썸네일만. 없으면 실패로 돌아온다 — 아이콘으로 대체되지 않는다.</summary>
    private const uint SiigbfThumbnailOnly = 0x00000008;

    private const uint DiNormal = 0x00000003;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;

    // ── COM ─────────────────────────────────────────────────────────

    /// <summary>
    /// <c>[ComImport]</c> 를 쓰는 이유: <c>[GeneratedComInterface]</c> 는 어셈블리 전체에
    /// <c>DisableRuntimeMarshalling</c> 을 요구하고, 그러면 <see cref="ShellFileInfo"/> 처럼
    /// 런타임 마샬링에 기대는 다른 interop 까지 함께 고쳐야 한다.
    /// </summary>
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, uint flags, out nint bitmap);
    }

    /// <summary>
    /// 시스템 이미지 리스트. <b>앞의 일곱 메서드는 vtable 자리를 맞추기 위한 것</b>이며
    /// 부르지 않으므로 인자를 선언하지 않는다 — COM 호출은 이름이 아니라 순서로 간다.
    /// </summary>
    [ComImport]
    [Guid("46eb5926-582e-4017-9fdf-e8998daa0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        void Add();

        void ReplaceIcon();

        void SetOverlayImage();

        void Replace();

        void AddMasked();

        void Draw();

        void Remove();

        [PreserveSig]
        int GetIcon(int index, uint flags, out nint icon);
    }

    // ── P/Invoke ────────────────────────────────────────────────────

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo info,
        uint sizeOfInfo,
        uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetImageList(int list, in Guid riid, out nint images);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid riid,
        out nint item);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(
        nint dc,
        int x,
        int y,
        nint icon,
        int width,
        int height,
        uint frame,
        nint brush,
        uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(
        nint dc,
        in BitmapInfo info,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(nint handle, int size, ref NativeBitmap bitmap);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(
        nint dc,
        nint bitmap,
        uint start,
        uint lines,
        ref byte pixels,
        ref BitmapInfo info,
        uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();

    // ── 구조체 ──────────────────────────────────────────────────────

    /// <summary>
    /// <c>SHFILEINFOW</c>. <c>ShellTypeNameProvider</c> 에도 같은 선언이 있다 — 두 파일이
    /// 각자의 마샬링을 들고 있는 편이 낫다고 봤다. 크기가 <c>SHGetFileInfo</c> 인자로 들어가므로
    /// <b>레이아웃을 줄일 수 없고</b>, 공용으로 빼면 동작 없는 선언만 담은 파일이 생긴다
    /// (CLAUDE.md §6). 한쪽을 고치면 다른 쪽도 본다.
    /// <para>
    /// 원소가 <c>char</c> 가 아니라 <c>ushort</c> 인 이유: <c>char</c> 는 런타임 마샬링에서
    /// blittable 이 아니라 <c>SYSLIB1051</c> 이 난다.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public DisplayNameBuffer DisplayName;
        public TypeNameBuffer TypeName;
    }

    [InlineArray(260)]
    private struct DisplayNameBuffer
    {
        private ushort element;
    }

    [InlineArray(80)]
    private struct TypeNameBuffer
    {
        private ushort element;
    }

    /// <summary><c>ICONINFO</c>. <c>Mask</c>·<c>Color</c> 는 <b>받는 쪽이 해제한다.</b></summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    /// <summary><c>BITMAP</c>. <c>GetObject</c> 가 채운다.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int PixelsPerMeterX;
        public int PixelsPerMeterY;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    /// <summary>
    /// <c>BITMAPINFO</c> — 헤더 뒤에 색 표 한 칸이 붙는 정의다. 32bpp <c>BI_RGB</c> 는 그
    /// 칸을 쓰지 않지만, 자리를 두지 않으면 GDI 가 구조체 밖을 읽는다.
    /// <c>Size</c> 에는 <b>헤더 크기만</b> 넣는다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int PixelsPerMeterX;
        public int PixelsPerMeterY;
        public uint ColorsUsed;
        public uint ColorsImportant;
        public uint FirstColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }
}
