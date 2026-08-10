using FlexDir.Core.Model;

namespace FlexDir.Core.Enumeration;

/// <summary>
/// 숨김·시스템 항목을 보여줄 것인가 (docs/PRD-v2.md §12).
///
/// <para>
/// <b>열거가 아니라 여기가 정한다.</b> <see cref="IFolderSource"/> 구현체는 있는 것을 다
/// 내주고 (<c>FileSystemFolderSource</c> 는 <c>AttributesToSkip = 0</c> 이다) 무엇을
/// 그릴지는 UI 정책이다. 열거에 필터를 넣으면 설정을 바꿀 때마다 폴더를 다시 읽어야 하고,
/// 트리와 목록이 같은 포트를 쓰면서 다른 정책을 요구할 자리가 생긴다.
/// </para>
///
/// <para>
/// <b>목록과 트리가 이 함수 하나를 나눠 쓴다.</b> 조건을 두 ViewModel 에 각각 쓰면 한쪽만
/// 고치는 순간 트리에는 있는데 목록에는 없는 폴더가 생긴다.
/// </para>
/// </summary>
public static class ItemVisibility
{
    /// <summary>숨김으로 치는 속성. <b>둘을 함께 본다</b> — 토글이 하나이기 때문이다.</summary>
    private const FileItemFlags Concealed = FileItemFlags.Hidden | FileItemFlags.System;

    /// <param name="showHidden">사용자가 '숨김·시스템 파일 보기' 를 켰는가 (<c>AppSettings</c>).</param>
    public static bool IsVisible(FileItem item, bool showHidden)
    {
        ArgumentNullException.ThrowIfNull(item);

        return showHidden || (item.Flags & Concealed) == 0;
    }
}
