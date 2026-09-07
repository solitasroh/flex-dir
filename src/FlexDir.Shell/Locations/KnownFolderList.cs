using System.Runtime.InteropServices;

using FlexDir.Core.Locations;

namespace FlexDir.Shell.Locations;

/// <summary>
/// 알려진 폴더 다섯을 세는 <see cref="IKnownFolderList"/> 구현체.
///
/// <para>
/// <b>COM 이 아니다</b> — <see cref="Settings.RegistrySystemThemeSource"/> ·
/// <see cref="Storage.SystemDriveList"/> 와 같은 자리다. STA 도 정리도 필요 없어
/// <see cref="IDisposable"/> 을 구현하지 않고, <c>AppComposition</c> 의 정리 목록에도
/// 들어가지 않는다.
/// </para>
///
/// <para>
/// <b>그래도 UI 스레드에서 부를 수 없다.</b> 리디렉션된 알려진 폴더(OneDrive · 도메인
/// 로밍 프로필)는 네트워크로 내려가 초 단위로 블로킹한다 (CLAUDE.md §3). 그래서 전부
/// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> 안에서 한다.
/// </para>
///
/// <para>
/// 라벨은 여기서 정한다 (<see cref="KnownFolder"/> 문서 참조). 경로의 마지막 조각을
/// 쓰면 OS 언어에 따라 <c>Desktop</c>/<c>바탕 화면</c> 으로 갈려 메뉴에 영문과 한글이
/// 섞인다.
/// </para>
/// </summary>
public sealed partial class KnownFolderList : IKnownFolderList
{
    /// <summary><c>FOLDERID_Downloads</c>. 다운로드만 <see cref="Environment.SpecialFolder"/> 에 없다.</summary>
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public async ValueTask<IReadOnlyList<KnownFolder>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(List, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<KnownFolder> List() =>
    [
        // 없는 폴더도 Location = null 로 자리를 지킨다 — 거르는 것은 ViewModel 의 일이다
        // (IKnownFolderList 계약). 순서는 KnownFolderKind 선언 순서 그대로다.
        Entry(KnownFolderKind.Home, "홈", static () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),

        // DesktopDirectory 다 — Desktop 은 가상 shell 폴더라 파일시스템 경로가 아닐 수 있다.
        Entry(KnownFolderKind.Desktop, "바탕화면", static () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
        Entry(KnownFolderKind.Documents, "문서", static () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        Entry(KnownFolderKind.Downloads, "다운로드", DownloadsPath),
        Entry(KnownFolderKind.Pictures, "사진", static () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
    ];

    /// <summary>
    /// 폴더 하나. 경로가 비거나(이 기계에 없다) 파싱되지 않거나 조회가 던지면
    /// <c>Location = null</c> 로 접는다 — 알려진 폴더를 못 읽는 것이 폴더를 못 여는
    /// 사건이 되면 안 된다 (<see cref="Settings.RegistrySystemThemeSource"/> 와 같은 판단).
    /// </summary>
    private static KnownFolder Entry(KnownFolderKind kind, string label, Func<string?> path)
    {
        try
        {
            var raw = path();

            return new KnownFolder(
                kind,
                label,
                !string.IsNullOrEmpty(raw) && LocationId.TryParse(raw, out var location, out _) ? location : null);
        }
        catch (Exception)
        {
            // 실패는 던지지 않는다 — 취소는 이 안에 토큰이 없어 여기로 올 수 없다.
            return new KnownFolder(kind, label, null);
        }
    }

    private static string? DownloadsPath()
    {
        var path = nint.Zero;

        try
        {
            return SHGetKnownFolderPath(DownloadsFolderId, 0, 0, out path) == 0
                ? Marshal.PtrToStringUni(path)
                : null;
        }
        finally
        {
            // 실패해도 부른다 — nint.Zero 면 no-op 이라 안전하고, 이렇게 해야 모든 경로에서
            // 해제가 보장된다 (SHGetKnownFolderPath 는 실패 시에도 해제를 요구한다).
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
