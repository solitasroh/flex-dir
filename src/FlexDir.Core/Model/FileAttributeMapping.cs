namespace FlexDir.Core.Model;

/// <summary>
/// Win32 파일 속성 비트를 <see cref="FileItemFlags"/> 로 옮기는 순수 함수.
/// 상수 값만 알고 있을 뿐이며 P/Invoke·COM 은 없다 (CLAUDE.md §1).
/// </summary>
public static class FileAttributeMapping
{
    private const uint FileAttributeHidden = 0x00000002;
    private const uint FileAttributeSystem = 0x00000004;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileAttributeOffline = 0x00001000;

    // 자리표시자 비트는 OFFLINE 하나가 아니다 (docs/SHELL_NOTES.md §열거 함정 3).
    private const uint FileAttributeRecallOnOpen = 0x00040000;
    private const uint FileAttributeRecallOnDataAccess = 0x00400000;

    public static FileItemFlags FromWin32Attributes(uint attributes)
    {
        var flags = FileItemFlags.None;

        if ((attributes & FileAttributeDirectory) != 0)
        {
            flags |= FileItemFlags.Directory;
        }

        if ((attributes & FileAttributeHidden) != 0)
        {
            flags |= FileItemFlags.Hidden;
        }

        if ((attributes & FileAttributeSystem) != 0)
        {
            flags |= FileItemFlags.System;
        }

        if ((attributes & FileAttributeReparsePoint) != 0)
        {
            flags |= FileItemFlags.ReparsePoint;
        }

        if ((attributes & FileAttributeOffline) != 0)
        {
            flags |= FileItemFlags.Offline;
        }

        // 두 recall 비트는 같은 뜻이므로 하나의 플래그로 합친다.
        if ((attributes & (FileAttributeRecallOnDataAccess | FileAttributeRecallOnOpen)) != 0)
        {
            flags |= FileItemFlags.CloudPlaceholder;
        }

        return flags;
    }
}
