using System.Runtime.CompilerServices;
using System.Threading.Channels;

using FlexDir.Core.Locations;
using FlexDir.Core.Watching;

namespace FlexDir.Shell.Watching;

/// <summary>
/// <see cref="FileSystemWatcher"/> 를 감싼 <see cref="IFolderWatcher"/> 구현체.
/// <para>
/// <c>ReadDirectoryChangesW</c> + IOCP 를 직접 다루지 않고 <see cref="FileSystemWatcher"/> 를
/// 쓴다 — 전작이 손으로 짰던 부분이며 그 함정(루프마다 OVERLAPPED 재초기화, CancelIoEx 만으로
/// 안 풀리는 종료)은 이 클래스가 이미 처리한다 (docs/SHELL_NOTES.md §폴더 감시).
/// 그 문서가 C# 에 남긴 조건은 둘이다: <b>버퍼를 키우고, Error 를 반드시 처리한다.</b>
/// </para>
/// </summary>
public sealed class FileSystemFolderWatcher : IFolderWatcher
{
    /// <summary><see cref="FileSystemWatcher"/> 가 받는 최소값.</summary>
    public const int MinimumBufferSize = 4096;

    /// <summary>
    /// 기본 버퍼 크기. 기본값(8KB)보다 키운다 — 파일 100개를 한 번에 복사하면 그것만으로
    /// 넘치고, 넘친 뒤에는 개별 변경이 유실돼 전체 새로고침으로 물러나야 한다.
    /// </summary>
    public const int DefaultBufferSize = 64 * 1024;

    private readonly Func<string, FileSystemWatcher> create;

    public FileSystemFolderWatcher(int bufferSize = DefaultBufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, MinimumBufferSize);

        create = path => new FileSystemWatcher(path) { InternalBufferSize = bufferSize };
    }

    /// <summary>
    /// 감시자를 만드는 자리를 바꿔 끼운다. 버퍼 오버플로는 OS 가 이벤트를 쏟아내는 속도에
    /// 기대야 해서 결정적으로 재현할 수 없다 — 테스트는 <c>Error</c> 를 직접 올린다.
    /// </summary>
    internal FileSystemFolderWatcher(Func<string, FileSystemWatcher> create)
    {
        ArgumentNullException.ThrowIfNull(create);

        this.create = create;
    }

    public IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // 반복자이므로 본문은 첫 MoveNextAsync 에서 돈다. 없는 폴더·권한 없는 폴더의 실패가
        // 호출 지점이 아니라 스트림에서 나오는 것은 의도다 — 소비자가 열거 실패와 같은
        // 자리에서 잡는다.
        return Stream(folder, ct);
    }

    private async IAsyncEnumerable<FolderChange> Stream(
        LocationId folder,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 무한 채널이다. 이벤트는 FileSystemWatcher 의 스레드에서 오고 소비자는 UI 쪽에서
        // 읽는다. 여기서 막으면 그 스레드가 밀리고, 밀리면 버퍼가 넘친다.
        var changes = Channel.CreateUnbounded<FolderChange>(
            new UnboundedChannelOptions { SingleReader = true });

        using var watcher = create(folder.DisplayPath);

        // LastWrite·Size 가 빠지면 내용만 바뀐 파일이 조용히 stale 로 남는다.
        watcher.NotifyFilter =
            NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;

        // 목록은 폴더 하나를 보여준다. 하위 트리를 끌어오면 보이지도 않는 항목 때문에
        // 갱신이 돌고, 큰 트리에서는 그것만으로 UI 가 바쁘다.
        watcher.IncludeSubdirectories = false;

        watcher.Created += (_, e) => Publish(changes, FolderChangeKind.Added, e.Name);
        watcher.Deleted += (_, e) => Publish(changes, FolderChangeKind.Removed, e.Name);
        watcher.Changed += (_, e) => Publish(changes, FolderChangeKind.Changed, e.Name);
        watcher.Renamed += (_, e) =>
            changes.Writer.TryWrite(
                new FolderChange(FolderChangeKind.Renamed, e.Name ?? string.Empty, e.OldName ?? string.Empty));

        // Error 는 버퍼 오버플로이거나 감시 자체가 끊긴 것이다. 어느 쪽이든 개별 변경으로
        // 흉내낼 수 없으니 전체 새로고침 신호로 올린다 (CLAUDE.md §4 — 진실원천은 파일시스템).
        // 삼키면 목록이 파일시스템과 어긋난 채로 남는다.
        watcher.Error += (_, _) => changes.Writer.TryWrite(FolderChange.Overflowed);

        watcher.EnableRaisingEvents = true;

        while (true)
        {
            FolderChange change;

            try
            {
                change = await changes.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 취소는 정상 종료다. 예외로 내면 폴더 전환마다 예외 경로를 탄다.
                yield break;
            }
            catch (ChannelClosedException)
            {
                yield break;
            }

            yield return change;
        }
    }

    private static void Publish(Channel<FolderChange> channel, FolderChangeKind kind, string? name) =>
        channel.Writer.TryWrite(new FolderChange(kind, name ?? string.Empty));
}
