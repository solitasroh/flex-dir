using FlexDir.Core.Locations;

namespace FlexDir.Core.Model;

/// <summary>
/// 목록의 한 줄이 가진 성질. Win32 파일 속성 비트 중 목록·썸네일이 실제로 쓰는 것만
/// 옮겨 담는다 (<see cref="FileAttributeMapping"/>).
/// </summary>
[Flags]
public enum FileItemFlags
{
    None = 0,
    Directory = 1 << 0,
    Hidden = 1 << 1,
    System = 1 << 2,
    ReparsePoint = 1 << 3,
    Offline = 1 << 4,
    CloudPlaceholder = 1 << 5,
}

/// <summary>
/// 목록의 한 줄. 열거·정렬·표시·썸네일이 모두 이 타입을 본다.
/// 순수 값 타입이며 파일시스템에 접근하지 않는다 — 속성은 열거 계층이 채워 넣는다.
/// 표시용 문자열(<c>1.2 MB</c> 같은 것)은 여기 두지 않는다. 로케일에 묶이면
/// 정렬이 표시 형식에 영향받는다.
/// </summary>
public sealed record FileItem(
    string Name,
    LocationId Location,
    long Size,
    DateTimeOffset ModifiedUtc,
    FileItemFlags Flags)
{
    public bool IsDirectory { get; } = (Flags & FileItemFlags.Directory) != 0;

    /// <summary>
    /// 점을 포함하지 않는 소문자 확장자. 유형 컬럼과 아이콘 캐시가 이것을 키로 쓰므로
    /// <c>.JPG</c> 와 <c>.jpg</c> 가 갈라지면 안 된다.
    /// 선행 '.' 은 확장자 구분자로 보지 않는다 — <c>.bashrc</c> 는 확장자가 없다
    /// (docs/SHELL_NOTES.md §열거 함정 4). <c>report.tar.gz</c> 는 "gz" 다.
    /// </summary>
    public string Extension { get; } = ExtensionOf(Name, Flags);

    public string NameWithoutExtension { get; } = NameWithoutExtensionOf(Name, Flags);

    /// <summary>
    /// 내용을 건드리면 클라우드 다운로드가 트리거되는 항목.
    /// 썸네일·미리보기가 이 항목을 건너뛰는 근거다 (docs/SHELL_NOTES.md §열거 함정 3).
    /// </summary>
    public bool IsContentAccessRisky { get; }
        = (Flags & (FileItemFlags.Offline | FileItemFlags.CloudPlaceholder)) != 0;

    private static string ExtensionOf(string name, FileItemFlags flags)
    {
        var dot = ExtensionDotIndex(name, flags);
        return dot < 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
    }

    private static string NameWithoutExtensionOf(string name, FileItemFlags flags)
    {
        var dot = ExtensionDotIndex(name, flags);
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>
    /// 확장자 구분자 '.' 의 위치. 확장자가 없으면 -1.
    /// <see cref="System.IO.Path.GetExtension"/> 에 위임하지 않는다 — 그쪽은
    /// <c>.bashrc</c> 를 확장자 "bashrc" 로 본다.
    /// 디렉터리는 확장자가 없다. <c>My.Folder</c> 를 확장자 "Folder" 로 보면 안 된다.
    /// </summary>
    private static int ExtensionDotIndex(string name, FileItemFlags flags)
    {
        if ((flags & FileItemFlags.Directory) != 0)
        {
            return -1;
        }

        var dot = name.LastIndexOf('.');

        // dot == 0 은 선행 '.' 뿐인 이름이다.
        return dot <= 0 ? -1 : dot;
    }
}
