using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FlexDir.Core.Presentation;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Presentation;

/// <summary>
/// <c>SHGetFileInfoW</c> 로 "유형" 컬럼 문자열을 얻는 구현체.
/// <para>
/// <b>파일을 건드리지 않는다.</b> 존재하지 않는 센티널 이름(<c>"x.txt"</c>)에
/// <c>SHGFI_USEFILEATTRIBUTES</c> 를 함께 주면 shell 은 파일을 열지 않고 확장자 연결 정보만
/// 본다 (docs/SHELL_NOTES.md §아이콘). 그래서 네트워크 경로나 클라우드 자리표시자에서도
/// 멈추지 않는다.
/// </para>
/// <para>
/// <b>확장자마다 한 번만 묻는다.</b> 그것이 전작에서 대용량 폴더 성능의 가장 큰 승리였다.
/// 10만 항목 폴더에 확장자가 열 종류면 shell 호출도 열 번이다.
/// </para>
/// </summary>
public sealed partial class ShellTypeNameProvider : ITypeNameProvider, IDisposable
{
    private readonly ConcurrentDictionary<(string Extension, bool IsDirectory), string> names = new();

    // 스레드 하나로 충분하다. 확장자마다 한 번만 묻고 그 뒤로는 캐시가 답하므로 큐가 길지 않다.
    private readonly StaWorkQueue worker = new(1, "type-names");

    private readonly Func<string, bool, string> lookup;

    public ShellTypeNameProvider() => lookup = Query;

    /// <summary>
    /// 조회를 바꿔 끼운다. shell 이 내는 문구는 로케일과 설치된 프로그램에 따라 달라져
    /// 캐시·실패 처리를 결정적으로 재려면 이 자리가 필요하다.
    /// </summary>
    internal ShellTypeNameProvider(Func<string, bool, string> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        this.lookup = lookup;
    }

    public ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ct.ThrowIfCancellationRequested();

        var key = (extension.ToLowerInvariant(), isDirectory);

        // 캐시에 있으면 워커로 나가지 않는다. 목록 한 줄마다 스레드 풀을 건드리면
        // 그것만으로 대용량 폴더가 느려진다.
        if (names.TryGetValue(key, out var cached))
        {
            return ValueTask.FromResult(cached);
        }

        // shell 호출은 동기 블로킹이고(docs/SHELL_NOTES.md §아이콘 함정 1) UI 스레드에 두면
        // 안 된다 (CLAUDE.md §3). 그리고 스레드풀이 아니라 STA 워커여야 한다 —
        // SHGetFileInfo 는 STA 를 요구한다 (docs/SHELL_NOTES.md §COM 아파트먼트).
        return new ValueTask<string>(worker.RunAsync(() => Resolve(key), ct));
    }

    public void Dispose() => worker.Dispose();

    private string Resolve((string Extension, bool IsDirectory) key)
    {
        string name;

        try
        {
            name = lookup(key.Extension, key.IsDirectory) ?? string.Empty;
        }
        catch (Exception)
        {
            // 계약대로 실패는 빈 문자열이다. COM·P/Invoke 위에 서므로 무엇이든 나올 수 있고,
            // 유형 이름 하나 때문에 폴더가 통째로 열리지 않는 것은 어느 쪽이 틀렸든 잘못된 결과다.
            name = string.Empty;
        }

        // 실패도 캐시한다 — "확장자마다 한 번" 은 실패에도 적용된다. 그러지 않으면 실패하는
        // 확장자가 목록에 100개 있을 때 조회도 100번 나간다.
        names[key] = name;

        return name;
    }

    private static string Query(string extension, bool isDirectory)
    {
        var sentinel = isDirectory || extension.Length == 0 ? SentinelName : SentinelName + "." + extension;
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;

        // 동시 호출이 예외도 오류 코드도 없이 0 을 낸다 (ShellInfoGate). 아이콘 조회와
        // 같은 API 라 서로 부딪히기도 한다 — 열거와 스크롤이 동시에 일어나는 것이 정상이다.
        return ShellInfoGate.Query(() =>
        {
            var info = default(ShellFileInfo);

            var handle = SHGetFileInfo(
                sentinel,
                attributes,
                ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiTypeName | ShgfiUseFileAttributes);

            if (handle == 0)
            {
                return string.Empty;
            }

            // szTypeName 은 고정 길이 버퍼이고 남는 자리는 정의되지 않는다. NUL 까지만 읽는다.
            ReadOnlySpan<ushort> raw = info.TypeName;
            var type = MemoryMarshal.Cast<ushort, char>(raw);
            var end = type.IndexOf('\0');

            return (end < 0 ? type : type[..end]).ToString();
        });
    }

    /// <summary>
    /// 실제로 존재하지 않아도 되는 이름. <c>SHGFI_USEFILEATTRIBUTES</c> 가 붙으면 shell 은
    /// 이것을 열지 않고 확장자만 본다.
    /// </summary>
    private const string SentinelName = "x";

    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint ShgfiTypeName = 0x00000400;
    private const uint ShgfiUseFileAttributes = 0x00000010;

    // SHGFI_ICON 을 주지 않으므로 HICON 이 만들어지지 않는다 — 해제할 것도 없다.
    // 아이콘을 얻는 경로(IThumbnailSource)에서는 DestroyIcon 이 필요하다
    // (docs/SHELL_NOTES.md §아이콘 함정 3).
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo info,
        uint sizeOfInfo,
        uint flags);

    /// <summary>
    /// <c>SHFILEINFOW</c>. 고정 길이 문자 버퍼는 <see cref="InlineArrayAttribute"/> 로 담는다.
    /// <para>
    /// 원소가 <c>char</c> 가 아니라 <c>ushort</c> 인 이유: 런타임 마샬링에서 <c>char</c> 는
    /// blittable 이 아니다(ANSI 로 마샬링될 수 있다). 그래서 구조체째로 넘길 수 없고,
    /// 어셈블리 전체에 <c>DisableRuntimeMarshalling</c> 을 걸어야 한다 — 그러면 나중에 붙일
    /// COM interop 방식까지 묶인다. UTF-16 코드 단위를 그대로 담고 읽을 때만
    /// <c>char</c> 로 캐스팅한다.
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
}
