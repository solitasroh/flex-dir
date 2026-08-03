using CommunityToolkit.Mvvm.ComponentModel;

using FlexDir.Core.Model;
using FlexDir.Core.Presentation;

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
    private ThumbnailBitmap? icon;
    private ThumbnailBitmap? thumbnail;

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

    /// <summary>
    /// 형식 아이콘. 확장자마다 같은 인스턴스를 공유한다 (<see cref="ThumbnailRequestScheduler"/>).
    /// <para>
    /// 썸네일이 오기 전에 보이는 것이 이것이고, 썸네일이 없거나 실패해도 남는 것이 이것이다 —
    /// 자리를 비워두지 않는다 (docs/UI_GUIDE.md §상태 표현).
    /// </para>
    /// </summary>
    public ThumbnailBitmap? Icon
    {
        get => icon;
        internal set => SetProperty(ref icon, value);
    }

    /// <summary>
    /// 실제 썸네일. <c>null</c> 이면 <see cref="Icon"/> 을 쓴다.
    /// <para>
    /// 보이는 범위를 벗어나면 <c>null</c> 로 되돌아간다 — BGRA 버퍼가 항목당 36KB 라
    /// 대용량 폴더에서 들고 있을 수 없다. 그래서 이 대입은 알림을 내야 한다: 바인딩이
    /// 형식 아이콘으로 되돌아가지 못하면 해제된 버퍼의 그림이 그대로 남는다.
    /// </para>
    /// </summary>
    public ThumbnailBitmap? Thumbnail
    {
        get => thumbnail;
        internal set => SetProperty(ref thumbnail, value);
    }

    /// <summary>
    /// 썸네일을 물어본 결과가 났는가. <b>실패도 시도로 센다</b> — 실패하는 항목은 계속
    /// 실패하고, 스크롤할 때마다 다시 시도하면 워커가 그것으로 막힌다 (docs/PRD.md §4).
    /// <para>
    /// 알림을 내지 않는다. 화면에 나가는 값이 아니라 재요청을 막는 스케줄러의 기록이다.
    /// </para>
    /// </summary>
    public bool ThumbnailAttempted { get; internal set; }
}
