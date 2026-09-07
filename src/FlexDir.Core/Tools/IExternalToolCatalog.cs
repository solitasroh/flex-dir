namespace FlexDir.Core.Tools;

/// <summary>
/// 이 기계에 설치된 외부 도구를 찾는 포트. 구현체는 <c>FlexDir.Shell</c> 에 둔다.
/// <para>
/// <b>왜 실행(<see cref="IExternalToolLauncher"/>)과 나뉘어 있나</b>: 탐지는 여러 번
/// 반복되고 실패해도 조용해야 하지만, 실행은 한 번이고 실패하면 사용자에게 말해야 한다.
/// 한 포트에 섞으면 "조용한 실패" 와 "말해야 하는 실패" 의 계약이 충돌한다 —
/// 열거(<see cref="Enumeration.IFolderSource"/>)와 조작(<see cref="Operations.IFileOperations"/>)을
/// 가른 것과 같은 수다.
/// </para>
/// <para>
/// <b>계약 1 — 실패는 던지지 않는다. 못 찾으면 <see langword="null"/> 이다.</b>
/// 취소만 예외로 나온다. 외부 도구는 곁다리이고, 못 찾는 것이 폴더를 못 여는 사건이
/// 되면 안 된다 (<see cref="Locations.IKnownFolderList"/> 와 같은 계약).
/// </para>
/// <para>
/// <b>계약 2 — 여러 번 불릴 수 있다. 결과를 캐시하지 마라.</b> 상주 앱(ADR-003)이라
/// 창이 다시 보일 때마다 다시 묻는다 — 그 사이에 사용자가 VS Code 를 설치하거나
/// 지웠을 수 있다. 캐시하면 지운 뒤에도 버튼이 살아 있다.
/// </para>
/// <para>
/// <b>계약 3 — UI 스레드에서 부를 수 없다</b> (CLAUDE.md §3). 레지스트리·파일시스템
/// 조회이고, <c>%ProgramFiles%</c> 가 리디렉션된 기계에서는 얼마나 걸릴지 모른다.
/// </para>
/// </summary>
public interface IExternalToolCatalog
{
    /// <summary>
    /// 에디터(VS Code)의 실행 파일 전체 경로. <b>없으면 <see langword="null"/></b> 이며
    /// 그것은 오류가 아니다 — 버튼이 비활성일 뿐이다.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됐을 때. 이것만 나온다.</exception>
    ValueTask<string?> FindEditorAsync(CancellationToken ct);

    /// <summary>
    /// 프리셋이 쓰는 터미널의 실행 파일 전체 경로. <b>없으면 <see langword="null"/></b> 이다.
    /// <para>
    /// <b><see cref="TerminalPreset.Custom"/> 은 언제나 <see langword="null"/> 이다.</b>
    /// 사용자가 적은 경로는 탐지의 대상이 아니라 입력이다 — 여기서 찾으려 들면 오타를
    /// 조용히 고쳐 버리고, 사용자는 자기가 적은 것이 무시된 줄 모른다.
    /// </para>
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 가 취소됐을 때. 이것만 나온다.</exception>
    ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct);
}
