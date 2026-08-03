namespace FlexDir.Core.Watching;

/// <summary>
/// 외부 변경의 종류.
/// </summary>
public enum FolderChangeKind
{
    Added,
    Removed,
    Changed,
    Renamed,

    /// <summary>
    /// 감시 버퍼가 넘쳐 개별 이벤트가 유실됐다. 소비자는 전체 새로고침으로 폴백해야 한다.
    /// <para>
    /// 별도 종류로 두는 이유: <c>FileSystemWatcher</c> 는 <c>InternalBufferSize</c> 를 넘으면
    /// <c>Error</c> 이벤트를 내고 그 사이 이벤트를 유실한다 (docs/SHELL_NOTES.md §폴더 감시).
    /// 유실된 이벤트를 <see cref="Changed"/> 같은 개별 변경으로 흉내내면 목록이 파일시스템과
    /// 어긋난 채로 남는다. 진실원천은 파일시스템이므로(CLAUDE.md §4) 그때는 전체를 다시 읽어야
    /// 하고, 그 신호가 이것이다.
    /// </para>
    /// </summary>
    Overflow,
}

/// <summary>
/// 감시가 낸 변경 하나.
/// <para>
/// <see cref="Name"/>·<see cref="OldName"/> 은 폴더 안의 이름이며 전체 경로가 아니다 —
/// 감시 대상 폴더는 소비자가 이미 알고 있고, 경로를 중복으로 실으면 폴더 이름이 바뀔 때
/// 두 곳이 어긋난다. <see cref="FolderChangeKind.Renamed"/> 만 <see cref="OldName"/> 을
/// 가지며, <see cref="FolderChangeKind.Overflow"/> 는 둘 다 빈 문자열이다.
/// </para>
/// <para>
/// 값 타입(record)인 이유: 디바운스·병합은 소비자(ViewModel)의 정책이고, 그 정책은 같은
/// 변경을 접는 것에서 시작한다. 포트가 모아서 주면 소비자가 정책을 고를 수 없다.
/// </para>
/// </summary>
public sealed record FolderChange(FolderChangeKind Kind, string Name, string? OldName = null)
{
    /// <summary>
    /// 이벤트 유실 신호. 이름을 담지 않는다 — 무엇이 바뀌었는지 모르는 것이 오버플로다.
    /// </summary>
    public static FolderChange Overflowed { get; } =
        new(FolderChangeKind.Overflow, string.Empty, string.Empty);
}
