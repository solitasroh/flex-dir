using CommunityToolkit.Mvvm.ComponentModel;

using FlexDir.Core.Model;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 목록의 한 줄.
/// <para>
/// 표시 문자열을 <b>여기서 만들지 않는다.</b> 포맷터는 <see cref="IFormatProvider"/>·
/// <see cref="TimeZoneInfo"/> 를 인자로 요구하고 유형 이름은 비동기다. 조립은
/// <see cref="PaneViewModel"/> 이 한다 — 줄마다 환경을 읽으면 같은 목록에 다른 형식이 섞인다.
/// </para>
/// </summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileItem item, string sizeText, string modifiedText, string typeText)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(sizeText);
        ArgumentNullException.ThrowIfNull(modifiedText);
        ArgumentNullException.ThrowIfNull(typeText);

        Item = item;
        SizeText = sizeText;
        ModifiedText = modifiedText;
        TypeText = typeText;
    }

    /// <summary>원본. 정렬·파일 조작·감시 갱신이 모두 이것을 본다.</summary>
    public FileItem Item { get; }

    public string Name => Item.Name;

    public bool IsDirectory => Item.IsDirectory;

    /// <summary><c>SizeFormatter.ForItem</c> 결과. 디렉터리는 빈 문자열이다.</summary>
    public string SizeText { get; }

    /// <summary><c>TimestampFormatter.Format</c> 결과.</summary>
    public string ModifiedText { get; }

    /// <summary><c>ITypeNameProvider</c> 결과.</summary>
    public string TypeText { get; }
}
