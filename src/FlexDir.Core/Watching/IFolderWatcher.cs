using FlexDir.Core.Locations;

namespace FlexDir.Core.Watching;

/// <summary>
/// 외부 변경을 알리는 포트 (docs/ARCHITECTURE.md §2). 구현체는 <c>FlexDir.Shell</c> 에 둔다 —
/// <c>FileSystemWatcher</c>(<c>ReadDirectoryChangesW</c>) 기반이고 수동 검증 대상이다
/// (CLAUDE.md §1·§5).
/// <para>
/// 이 포트가 있는 이유: 메모리 목록은 캐시이고 진실원천은 파일시스템이다. stale 목록은
/// 잔버그의 주요 원천이었다 (ADR-011). 갱신은 자동으로 일어나되 <b>선택과 스크롤 위치는
/// 유지한다</b> — 그 판단은 소비자의 몫이므로 포트는 무엇이 바뀌었는지만 알린다.
/// </para>
/// </summary>
public interface IFolderWatcher
{
    /// <summary>
    /// 폴더의 변경을 스트림으로 낸다. 취소되면 <b>정상 종료</b>한다 — 예외가 아니다.
    /// <para>
    /// 폴더를 떠나는 것은 정상 조작이고 그때마다 감시가 하나씩 끝난다. 취소를 예외로 만들면
    /// 모든 폴더 전환이 예외 경로를 타고, 그 예외를 삼키는 코드가 호출부마다 생긴다.
    /// 반대로 종료 신호를 놓치면 감시가 폴더마다 쌓인다.
    /// </para>
    /// <para>
    /// 디바운스·병합은 하지 않는다. 얼마나 모아서 처리할지는 소비자(ViewModel)의 정책이다.
    /// 포트가 정하면 그 정책을 테스트할 수 없다.
    /// </para>
    /// </summary>
    IAsyncEnumerable<FolderChange> WatchAsync(LocationId folder, CancellationToken ct);
}
