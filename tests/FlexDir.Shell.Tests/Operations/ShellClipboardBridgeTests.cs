using System.Buffers.Binary;
using System.Text;

using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Operations;

using Xunit;

namespace FlexDir.Shell.Tests.Operations;

/// <summary>
/// 탐색기와 주고받는 클립보드 구현체. <c>CF_HDROP</c> + <c>Preferred DropEffect</c>.
/// <para>
/// <b>실제 클립보드를 건드리지 않는다.</b> 사용자가 복사해 둔 것을 게이트가 돌 때마다
/// 지우는 것은 자동 테스트가 낼 부작용이 아니다. 그래서 포맷 단위로 읽고 쓰는 자리를
/// 바꿔 끼우고 — 재는 것은 <b>바이트</b>다. 탐색기와의 호환은 그 바이트 배치가 전부이므로
/// 여기가 실물과 가장 가까운 자리이기도 하다.
/// </para>
/// <para>
/// 탐색기가 정말로 그 바이트를 받아들이는지는 <c>.harness/probe clipboard</c> 로
/// 사람이 확인한다 — 어느 자동 테스트도 그것을 대신할 수 없다.
/// </para>
/// </summary>
public sealed class ShellClipboardBridgeTests
{
    /// <summary><c>CF_HDROP</c>. 미리 정의된 포맷이라 값이 고정이다.</summary>
    private const uint CfHdrop = 15;

    /// <summary><c>DROPFILES</c> 구조체 크기. 파일 목록이 이만큼 뒤에서 시작한다.</summary>
    private const int DropFilesSize = 20;

    private const uint DropEffectCopy = 1;
    private const uint DropEffectMove = 2;
    private const uint DropEffectLink = 4;

    // ── 아파트먼트 ──────────────────────────────────────────────────
    // 포트가 동기인 이유는 클립보드가 호출 스레드에 묶여 있기 때문이지만, 그렇다고 아무
    // 스레드에서나 불러도 되는 것은 아니다 (docs/SHELL_NOTES.md §COM 아파트먼트).
    // xunit 의 스레드는 MTA 다 — 구현체가 스스로 STA 로 넘겨야 이 테스트가 통과한다.

    [Fact]
    public void SetCopy_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var clipboard = new ShellClipboardBridge(
            _ => null,
            _ => apartment = Thread.CurrentThread.GetApartmentState());

        clipboard.SetCopy([Location("a.txt")]);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public void TryGetPaste_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var clipboard = new ShellClipboardBridge(
            _ =>
            {
                apartment = Thread.CurrentThread.GetApartmentState();

                return null;
            },
            _ => { });

        clipboard.TryGetPaste(out _, out _);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    // ── 무엇을 싣는가 ───────────────────────────────────────────────

    [Fact]
    public void SetCopy_WritesHdropAndDropEffect()
    {
        var written = Written(clipboard => clipboard.SetCopy([Location("a.txt")]));

        Assert.Equal(2, written.Count);
        Assert.True(written.ContainsKey(CfHdrop));
        Assert.True(written.ContainsKey(ShellClipboardBridge.DropEffectFormat));
    }

    // 잘라내기 표시는 이 4바이트뿐이다. 없으면 탐색기가 복사로 붙여넣고 원본이 남는다
    // (docs/SHELL_NOTES.md §클립보드 함정 1).
    [Fact]
    public void SetCut_MarksTheDropEffectAsMove()
    {
        var written = Written(clipboard => clipboard.SetCut([Location("a.txt")]));

        Assert.Equal(DropEffectMove, Effect(written));
    }

    [Fact]
    public void SetCopy_MarksTheDropEffectAsCopy()
    {
        var written = Written(clipboard => clipboard.SetCopy([Location("a.txt")]));

        Assert.Equal(DropEffectCopy, Effect(written));
    }

    // DROPFILES 의 fWide 가 0 이면 받는 쪽이 ANSI 로 읽는다 — 한글 경로가 깨진다.
    [Fact]
    public void Hdrop_IsWideAndStartsAfterTheHeader()
    {
        var written = Written(clipboard => clipboard.SetCopy([Location("한글.txt")]));

        var data = written[CfHdrop];

        Assert.Equal((uint)DropFilesSize, BinaryPrimitives.ReadUInt32LittleEndian(data));
        Assert.NotEqual(0, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)));
    }

    // 목록은 항목마다 널, 마지막에 널 하나가 더 붙는다. 그 끝표시가 없으면 받는 쪽이
    // 목록 밖을 읽는다.
    [Fact]
    public void Hdrop_IsDoubleNullTerminated()
    {
        var written = Written(clipboard => clipboard.SetCopy([Location("a.txt"), Location("b.txt")]));

        var paths = Paths(written[CfHdrop]);

        Assert.Equal([@"C:\Temp\flex-dir\a.txt", @"C:\Temp\flex-dir\b.txt"], paths);
    }

    // 탐색기의 파서는 \\?\ 확장 접두사를 모른다. 내부 표현을 그대로 실으면 붙여넣기가
    // 조용히 실패한다.
    [Fact]
    public void Hdrop_CarriesDisplayPaths()
    {
        var written = Written(clipboard => clipboard.SetCopy([Location("a.txt")]));

        var path = Assert.Single(Paths(written[CfHdrop]));

        Assert.DoesNotContain(@"\\?\", path);
        Assert.Equal(@"C:\Temp\flex-dir\a.txt", path);
    }

    // ── 왕복 ────────────────────────────────────────────────────────

    [Fact]
    public void Copy_ThenPaste_ReturnsTheSameItems()
    {
        var items = new[] { Location("a.txt"), Location("하위\\b.txt") };

        var clipboardBytes = new Dictionary<uint, byte[]>();

        using var clipboard = Backed(clipboardBytes);

        clipboard.SetCopy(items);

        Assert.True(clipboard.TryGetPaste(out var pasted, out var isMove));
        Assert.Equal(items, pasted);
        Assert.False(isMove);
    }

    [Fact]
    public void Cut_ThenPaste_IsAMove()
    {
        var clipboardBytes = new Dictionary<uint, byte[]>();

        using var clipboard = Backed(clipboardBytes);

        clipboard.SetCut([Location("a.txt")]);

        Assert.True(clipboard.TryGetPaste(out _, out var isMove));
        Assert.True(isMove);
    }

    // ── 읽기 ────────────────────────────────────────────────────────

    // 다른 앱이 클립보드를 채우는 것은 관측할 수 없다 — 아무것도 없는 상태가 정상이다.
    // false 를 낼 때도 목록은 비어 있어야 한다. null 을 내면 반환값을 무시한 호출부가 터진다.
    [Fact]
    public void TryGetPaste_WithNothingOnTheClipboard_IsFalseWithAnEmptyList()
    {
        using var clipboard = new ShellClipboardBridge(_ => null, _ => { });

        Assert.False(clipboard.TryGetPaste(out var items, out var isMove));
        Assert.NotNull(items);
        Assert.Empty(items);
        Assert.False(isMove);
    }

    // Preferred DropEffect 를 싣지 않는 앱이 있다. 그때는 복사다 — 이동으로 오해하면
    // 남의 파일이 사라진다.
    [Fact]
    public void TryGetPaste_WithoutADropEffect_IsACopy()
    {
        using var clipboard = new ShellClipboardBridge(
            format => format == CfHdrop ? Hdrop(@"C:\Temp\flex-dir\a.txt") : null,
            _ => { });

        Assert.True(clipboard.TryGetPaste(out _, out var isMove));
        Assert.False(isMove);
    }

    [Fact]
    public void TryGetPaste_WithALinkDropEffect_IsNotAMove()
    {
        using var clipboard = Holding(Hdrop(@"C:\Temp\flex-dir\a.txt"), DropEffectLink);

        Assert.True(clipboard.TryGetPaste(out _, out var isMove));
        Assert.False(isMove);
    }

    // 탐색기는 잘라내기에 DROPEFFECT_MOVE 만 싣지만, 다른 앱은 COPY 와 함께 싣기도 한다.
    [Fact]
    public void TryGetPaste_WithMoveAmongOtherEffects_IsAMove()
    {
        using var clipboard = Holding(Hdrop(@"C:\Temp\flex-dir\a.txt"), DropEffectMove | DropEffectCopy);

        Assert.True(clipboard.TryGetPaste(out _, out var isMove));
        Assert.True(isMove);
    }

    // v1 은 로컬 경로만 다룬다 (ADR-010). 네트워크 경로가 섞여 왔다고 나머지까지 버리면
    // 사용자는 아무 이유 없이 붙여넣기가 안 되는 것을 본다.
    [Fact]
    public void TryGetPaste_SkipsPathsItCannotRead()
    {
        // UNC 는 더 이상 예가 아니다 — 이제 읽는다 (docs/PRD-v2.md §5 N-1).
        // 대체 데이터 스트림 표기는 여전히 위치가 아니다.
        using var clipboard = Holding(
            Hdrop(@"C:\Temp\a.txt:stream", @"C:\Temp\flex-dir\b.txt"),
            DropEffectCopy);

        Assert.True(clipboard.TryGetPaste(out var items, out _));
        Assert.Equal(Location("b.txt"), Assert.Single(items));
    }

    [Fact]
    public void TryGetPaste_WithOnlyUnreadablePaths_IsFalse()
    {
        using var clipboard = Holding(Hdrop(@"C:\Temp\a.txt:stream"), DropEffectCopy);

        Assert.False(clipboard.TryGetPaste(out var items, out _));
        Assert.Empty(items);
    }

    // 탐색기에서 네트워크 경로를 복사해 붙여넣는 자리다. v1 은 이것을 버렸다 —
    // 사용자에게는 "붙여넣기가 안 된다" 로 보인다 (docs/PRD-v2.md §5 N-1).
    [Fact]
    public void TryGetPaste_ReadsUncPaths()
    {
        using var clipboard = Holding(Hdrop(@"\\10.10.10.23\공유\a.txt"), DropEffectCopy);

        Assert.True(clipboard.TryGetPaste(out var items, out _));
        Assert.Equal(@"\\10.10.10.23\공유\a.txt", Assert.Single(items).DisplayPath);
    }

    // 아무 앱이나 클립보드에 아무 바이트나 실을 수 있다. 헤더보다 짧으면 읽지 않는다.
    [Fact]
    public void TryGetPaste_WithAMalformedHdrop_IsFalse()
    {
        using var clipboard = new ShellClipboardBridge(
            format => format == CfHdrop ? new byte[] { 1, 2, 3 } : null,
            _ => { });

        Assert.False(clipboard.TryGetPaste(out var items, out _));
        Assert.Empty(items);
    }

    // fWide 가 0 인 목록은 ANSI 다. v1 은 읽지 않는다 — 코드페이지를 짐작해 경로를
    // 만들어내는 것보다 붙여넣지 않는 편이 안전하다.
    [Fact]
    public void TryGetPaste_WithAnAnsiHdrop_IsFalse()
    {
        var wide = Hdrop(@"C:\Temp\flex-dir\a.txt");

        BinaryPrimitives.WriteInt32LittleEndian(wide.AsSpan(16), 0);

        using var clipboard = Holding(wide, DropEffectCopy);

        Assert.False(clipboard.TryGetPaste(out var items, out _));
        Assert.Empty(items);
    }

    // ── 방어 ────────────────────────────────────────────────────────

    // 선택이 비었는데 클립보드를 비우면 사용자가 다른 앱에서 복사해 둔 것이 사라진다.
    [Fact]
    public void EmptySelection_DoesNotTouchTheClipboard()
    {
        var calls = 0;

        using var clipboard = new ShellClipboardBridge(_ => null, _ => calls++);

        clipboard.SetCopy([]);
        clipboard.SetCut([]);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void NullItems_Throw()
    {
        using var clipboard = new ShellClipboardBridge(_ => null, _ => { });

        Assert.Throws<ArgumentNullException>(() => clipboard.SetCopy(null!));
        Assert.Throws<ArgumentNullException>(() => clipboard.SetCut(null!));
    }

    [Fact]
    public void AfterDispose_Throws()
    {
        var clipboard = new ShellClipboardBridge(_ => null, _ => { });

        clipboard.Dispose();

        Assert.Throws<ObjectDisposedException>(() => clipboard.SetCopy([Location("a.txt")]));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var clipboard = new ShellClipboardBridge(_ => null, _ => { });

        clipboard.Dispose();
        clipboard.Dispose();
    }

    [Fact]
    public void ImplementsThePort()
    {
        using var clipboard = new ShellClipboardBridge();

        Assert.IsAssignableFrom<IClipboardBridge>(clipboard);
    }

    // 등록 포맷은 미리 정의된 값(0..0x00FF)과 겹치지 않는 범위에서 온다.
    // 0 이면 RegisterClipboardFormat 이 실패한 것이고, 그러면 잘라내기 표시가 사라진다.
    [Fact]
    public void DropEffectFormat_IsRegistered()
    {
        Assert.True(ShellClipboardBridge.DropEffectFormat >= 0xC000, $"{ShellClipboardBridge.DropEffectFormat}");
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    private static Dictionary<uint, byte[]> Written(Action<ShellClipboardBridge> act)
    {
        var written = new Dictionary<uint, byte[]>();

        using var clipboard = Backed(written);

        act(clipboard);

        return written;
    }

    /// <summary>메모리 클립보드. 쓴 것을 그대로 읽는다.</summary>
    private static ShellClipboardBridge Backed(Dictionary<uint, byte[]> storage)
        => new(
            format => storage.TryGetValue(format, out var data) ? data : null,
            blobs =>
            {
                storage.Clear();

                foreach (var blob in blobs)
                {
                    storage[blob.Format] = blob.Data;
                }
            });

    private static ShellClipboardBridge Holding(byte[] hdrop, uint effect)
    {
        var storage = new Dictionary<uint, byte[]>
        {
            [CfHdrop] = hdrop,
            [ShellClipboardBridge.DropEffectFormat] = BitConverter.GetBytes(effect),
        };

        return Backed(storage);
    }

    private static uint Effect(Dictionary<uint, byte[]> written)
        => BinaryPrimitives.ReadUInt32LittleEndian(written[ShellClipboardBridge.DropEffectFormat]);

    /// <summary>구현체와 <b>독립적으로</b> DROPFILES 를 만든다. 같은 코드로 만들어 같은
    /// 코드로 읽으면 배치가 틀려도 왕복은 성립한다.</summary>
    private static byte[] Hdrop(params string[] paths)
    {
        var body = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var bytes = new byte[DropFilesSize + body.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, DropFilesSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        body.CopyTo(bytes, DropFilesSize);

        return bytes;
    }

    private static string[] Paths(byte[] hdrop)
    {
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdrop);
        var text = Encoding.Unicode.GetString(hdrop, offset, hdrop.Length - offset);

        Assert.EndsWith("\0\0", text);

        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static LocationId Location(string name)
    {
        var path = Path.Combine(@"C:\Temp\flex-dir", name);

        Assert.True(LocationId.TryParse(path, out var location, out _), path);

        return location;
    }
}
