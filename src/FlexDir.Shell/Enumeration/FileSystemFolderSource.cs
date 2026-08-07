using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using FlexDir.Core.Enumeration;
using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;

namespace FlexDir.Shell.Enumeration;

/// <summary>
/// 실제 파일시스템을 읽는 <see cref="IFolderSource"/> 구현체.
///
/// <para>
/// <b>SHELL_NOTES 와 다른 선택을 했다.</b> §열거 는 <c>FindFirstFileExW</c> 를 직접
/// P/Invoke 하라고 적었고 그 근거는 두 가지였다 — <c>FindExInfoBasic</c> 으로 8.3 단축 이름
/// 조회를 건너뛰는 것, 그리고 <c>Directory.EnumerateFiles</c> 가 플래그를 못 주고 예외
/// 기반이라 대용량에 불리하다는 것. 여기서 쓰는 <see cref="FileSystemEnumerator{TResult}"/>
/// 는 <c>Directory.EnumerateFiles</c> 가 아니라 <b>그것의 밑에 있는 저수준 primitive</b> 이고,
/// Windows 에서 <c>NtQueryDirectoryFile</c> 로 내려가 8.3 이름을 아예 묻지 않는다.
/// 항목마다 예외를 던지지도 않는다(<see cref="EnumerationOptions.IgnoreInaccessible"/> ·
/// <c>ContinueOnError</c>). 즉 그 문서가 피하려던 두 대가를 unsafe 코드와 150줄의 interop
/// 없이 피한다.
/// </para>
/// <para>
/// 대신 포기한 것: shell 네임스페이스(내 PC · 네트워크)는 이 API 로 열거할 수 없다.
/// v1 은 로컬 파일시스템만 다루므로(ADR-010) 지금은 손해가 아니지만, v2 에서 PIDL 열거가
/// 필요해지면 <b>그때는 P/Invoke 를 따로 세워야 한다</b> — 이 클래스를 확장하는 것이 아니라.
/// </para>
/// </summary>
public sealed class FileSystemFolderSource : IFolderSource
{
    /// <summary>
    /// 한 번의 syscall 로 받아오는 디렉터리 정보 버퍼. 기본값보다 키운다 —
    /// SHELL_NOTES §열거 의 <c>FIND_FIRST_EX_LARGE_FETCH</c> 와 같은 목적이다.
    /// </summary>
    private const int FetchBufferSize = 64 * 1024;

    /// <summary>
    /// 소비자가 아직 안 읽은 항목을 이만큼까지만 앞질러 쌓는다. 10만 항목 폴더에서
    /// 목록 전체를 두 번 들고 있지 않으려면 상한이 필요하다.
    /// </summary>
    private const int PendingItemLimit = 512;

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,

        // 기본값은 Hidden|System 을 건너뛴다. 그대로 두면 숨김 파일이 목록에서 조용히
        // 사라진다 — 보여줄지 말지는 UI 정책이고, 열거는 있는 것을 다 내야 한다.
        AttributesToSkip = 0,

        // 오류를 삼키지 않는다. 권한 없는 폴더를 빈 폴더로 보여주면 사용자는 파일이
        // 사라진 줄 안다 (docs/PRD.md §4 는 사유를 상태표시줄에 내라고 정했다).
        IgnoreInaccessible = false,

        ReturnSpecialDirectories = false,
        BufferSize = FetchBufferSize,
    };

    public IAsyncEnumerable<FileItem> EnumerateAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return Stream(folder, ct);
    }

    public Task<FileItem?> TryGetItemAsync(LocationId item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();

        // 동기 파일시스템 호출이다. 네트워크 경로에서는 초 단위로 멈추므로 UI 스레드에
        // 두면 안 된다 (CLAUDE.md §3).
        return Task.Run(() => Stat(item), ct);
    }

    private async IAsyncEnumerable<FileItem> Stream(
        LocationId folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var items = Channel.CreateBounded<FileItem>(new BoundedChannelOptions(PendingItemLimit)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // 디렉터리 읽기는 동기 블로킹이다. 워커로 밀어내지 않으면 이 반복자를 돌리는
        // 스레드(=UI 스레드)가 그대로 멈춘다 (CLAUDE.md §3).
        var producer = Task.Run(() => ProduceAsync(items.Writer, folder, ct), CancellationToken.None);

        await foreach (var item in items.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            // ReadAllAsync 는 버퍼에 남은 항목을 먼저 비운다 — 이미 채널에 들어온 것은
            // 취소 뒤에도 흘러나온다. 폴더를 떠난 뒤 이전 폴더의 항목이 새로 그려지는
            // 경로가 여기다. 항목마다 다시 관측한다.
            ct.ThrowIfCancellationRequested();

            yield return item;
        }

        // 실패는 여기서 나온다. 예외를 채널로 흘리면 소비자가 ChannelClosedException 을
        // 풀어 안을 봐야 하고, 그러면 포트 계약이 채널 구현에 묶인다.
        await producer.ConfigureAwait(false);
    }

    /// <summary>
    /// 열거를 시작하지도 못한 실패를 한 번 더 물어볼 것인가.
    ///
    /// <para>
    /// <b>SMB 는 삭제 중인 디렉터리를 <c>ACCESS_DENIED</c> 로 낸다.</b> 서버가 삭제를
    /// 접수하고 아직 끝내지 않은 동안(delete-pending) 새로 여는 요청이 전부 그렇게 튕긴다.
    /// 실측 (2026-08-07 · <c>\\10.10.10.23</c>): 삭제 후 <b>367ms 에 5</b>,
    /// <b>414ms 에 3</b> — 창이 50ms 남짓이다. 감시 알림에 곧장 재열거하는 우리가 정확히
    /// 그 창에 들어갔다. 로컬 NTFS 에는 그 중간 상태가 없어 곧장 3 이 온다.
    /// </para>
    /// <para>
    /// 결과는 <b>거짓말</b>이었다 — 사라진 폴더에 "액세스가 거부되었습니다" 가 뜨고,
    /// <see cref="LocationErrorKind.NotFound"/> 가 아니므로 상위로 올라가지도 않아
    /// (docs/PRD.md §4) 빈 목록과 틀린 사유 앞에 갇혔다.
    /// </para>
    /// <para>
    /// 그래서 <b>네트워크 경로의 권한 실패만</b> 한 번 더 묻는다. 진짜 권한 오류는 다시
    /// 물어도 같은 답이라 정확도를 잃지 않고, 값은 실패 경로의 지연 하나다. 다른 실패는
    /// 이미 답이 나온 것이라 다시 묻지 않는다 — 없는 서버는 <b>한 번이 42초</b>다
    /// (실측, CLAUDE.md §3).
    /// </para>
    /// </summary>
    internal static bool ShouldRetryOpen(Exception error, LocationId folder)
        => folder.IsNetwork && error is UnauthorizedAccessException;

    /// <summary>delete-pending 이 걷히기를 기다리는 시간. 실측 창(약 50ms)의 다섯 배다.</summary>
    private static readonly TimeSpan DeletePendingDelay = TimeSpan.FromMilliseconds(250);

    private static async Task ProduceAsync(
        ChannelWriter<FileItem> writer,
        LocationId folder,
        CancellationToken ct)
    {
        ItemEnumerator? enumerator = null;

        try
        {
            try
            {
                enumerator = new ItemEnumerator(folder, Options);
            }
            catch (Exception error) when (ShouldRetryOpen(error, folder))
            {
                // 아직 한 항목도 내지 않았을 때만 여기 온다 — 열거 도중에 다시 열면 앞의
                // 항목이 두 번 나간다. 열거자 생성자가 여는 자리라 그 조건이 저절로 선다.
                await Task.Delay(DeletePendingDelay, ct).ConfigureAwait(false);

                enumerator = new ItemEnumerator(folder, Options);
            }

            while (enumerator.MoveNext())
            {
                // 항목마다 관측한다. 네트워크 경로에서는 한 항목이 초 단위로 걸리므로
                // 폴더를 떠난 뒤에도 계속 흐르면 워커가 그것에 붙잡힌다.
                ct.ThrowIfCancellationRequested();

                await writer.WriteAsync(enumerator.Current, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 취소는 소비자 쪽 ReadAllAsync 가 이미 관측한다. 여기서 다시 던지면
            // 아무도 기다리지 않는 Task 가 faulted 로 남는다.
        }
        catch (Exception error)
        {
            throw Translate(error, folder, enumerator?.LastError ?? 0);
        }
        finally
        {
            enumerator?.Dispose();
            writer.Complete();
        }
    }

    private static FileItem? Stat(LocationId item)
    {
        var path = item.DisplayPath;

        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            // 없음은 null 이다. 감시 알림과 실제 상태가 어긋나는 것은 정상 상황이며
            // (CLAUDE.md §4) 예외로 만들면 갱신 경로가 오류 경로가 된다.
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // 없음과 권한 없음은 구분해야 한다 — 사용자가 손쓸 방법이 다르다.
            throw new LocationAccessException(LocationErrorKind.AccessDenied, item, AccessDeniedCode);
        }
        catch (IOException error)
        {
            throw Translate(error, item, 0);
        }

        var flags = FileAttributeMapping.FromWin32Attributes((uint)attributes);
        var size = 0L;
        DateTimeOffset modified;

        try
        {
            modified = File.GetLastWriteTimeUtc(path);

            // 폴더 크기는 계산하지 않는다 (docs/PRD.md §3 — v1 범위 밖).
            if ((flags & FileItemFlags.Directory) == 0)
            {
                size = new FileInfo(path).Length;
            }
        }
        catch (FileNotFoundException)
        {
            return null;   // 속성을 읽은 뒤 사라졌다
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        return new FileItem(item.Name, item, size, modified, flags);
    }

    private const int AccessDeniedCode = 5;

    /// <summary>
    /// 파일시스템 예외를 포트의 오류로 옮긴다. 열거자가 잡아둔 Win32 코드를 먼저 쓰고,
    /// 없으면 HRESULT 에서 꺼낸다.
    /// </summary>
    private static LocationAccessException Translate(Exception error, LocationId location, int captured)
    {
        var win32 = captured;

        // 마스크를 int 로 못박아야 한다. 0xFFFF0000 은 uint 리터럴이라 그대로 쓰면 양쪽이
        // long 으로 승격되고, 음수 HResult 가 부호 확장돼(0xFFFFFFFF80070005) 비교가 영원히
        // 거짓이 된다 — 코드가 조용히 0 으로 남아 진단이 사라진다.
        const int FacilityWin32 = unchecked((int)0x80070000);
        const int FacilityMask = unchecked((int)0xFFFF0000);

        if (win32 == 0 && (error.HResult & FacilityMask) == FacilityWin32)
        {
            win32 = error.HResult & 0xFFFF;
        }

        var kind = Win32ErrorMapping.Classify(win32);

        // 코드를 못 얻는 경로가 있다 — 열거자 생성자가 실패하면 그 인스턴스를 받지 못해
        // 잡아둔 코드에 닿을 수 없다. 그때 None 을 그대로 넘기면 LocationAccessException 이
        // "오류가 아닌 것을 던졌다" 며 ArgumentException 을 내고, 진짜 원인이 사라진다.
        if (kind == LocationErrorKind.None)
        {
            kind = error switch
            {
                DirectoryNotFoundException or FileNotFoundException => LocationErrorKind.NotFound,
                UnauthorizedAccessException => LocationErrorKind.AccessDenied,
                _ => LocationErrorKind.Unknown,
            };
        }

        return new LocationAccessException(kind, location, win32);
    }

    /// <summary>
    /// 디렉터리 항목을 <see cref="FileItem"/> 으로 바꾸는 열거자.
    /// 실패한 Win32 코드를 잡아두는 것이 <c>ContinueOnError</c> 를 재정의하는 이유다 —
    /// 기본 동작은 코드를 잃고 <see cref="IOException"/> 계열로 바꿔버린다.
    /// </summary>
    private sealed class ItemEnumerator : FileSystemEnumerator<FileItem>
    {
        private readonly LocationId folder;

        public ItemEnumerator(LocationId folder, EnumerationOptions options)
            : base(folder.DisplayPath, options) => this.folder = folder;

        public int LastError { get; private set; }

        protected override FileItem TransformEntry(ref FileSystemEntry entry)
        {
            var name = entry.FileName.ToString();
            var flags = FileAttributeMapping.FromWin32Attributes((uint)entry.Attributes);

            return new FileItem(
                name,
                folder.Combine(name),
                entry.IsDirectory ? 0 : entry.Length,
                entry.LastWriteTimeUtc,
                flags);
        }

        protected override bool ContinueOnError(int error)
        {
            LastError = error;

            return false;
        }
    }
}
