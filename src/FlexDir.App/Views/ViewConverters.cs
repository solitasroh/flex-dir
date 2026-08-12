using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FlexDir.App.ViewModels;

using FlexDir.Core.Presentation;
using FlexDir.Core.ViewState;

namespace FlexDir.App.Views;

/// <summary>
/// 행이 선택돼 있는가 — <c>[선택 이름 집합, 내 이름]</c> 을 받아 bool 을 낸다.
/// <para>
/// 선택은 <c>PaneSelection</c> 이 이름 집합으로 들고 있고 행 인스턴스에는 없다 (ADR-011).
/// 행마다 <c>IsSelected</c> 를 두고 페인이 밀어 넣으면 선택 변경마다 전체 목록을 훑는다 —
/// <c>MultiBinding</c> 은 가상화가 실현한 행만 다시 계산하므로 비용이 보이는 행 수에
/// 비례한다.
/// </para>
/// </summary>
public sealed class NameMembershipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 바인딩이 아직 붙지 않으면 UnsetValue 가 들어온다. 예외를 내면 행 전체가 죽는다.
        if (values is not [IReadOnlyCollection<string> selected, string name])
        {
            return false;
        }

        // 집합은 OrdinalIgnoreCase 로 만들어져 있다 (PaneSelection). Contains 로 충분하다.
        return selected.Contains(name);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다 — 역방향 동기화가 생기면 ADR-011 위반이다.");
}

/// <summary>
/// 행이 포커스 항목인가 — <c>[FocusedName, 내 이름]</c> 을 받아 bool 을 낸다.
/// 비교는 파일시스템과 같이 대소문자를 가리지 않는다.
/// </summary>
public sealed class NameEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string focused, string name])
        {
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(focused, name);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다 — 역방향 동기화가 생기면 ADR-011 위반이다.");
}

/// <summary>
/// 두 값이 <b>같은 인스턴스</b>인가 — <c>[이 탭, 페인의 활성 탭]</c> 을 받아 bool 을 낸다
/// (docs/DESIGN.md §1-1 활성 탭 색).
/// <para>
/// 탭은 자기가 활성인지 모른다 — 그 상태의 소유자는 페인(<c>PaneTabsViewModel.Active</c>)
/// 이고, 탭에 <c>IsActive</c> 를 따로 두면 진실이 둘이 된다. 이름이나 폴더로 가릴 수도 없다:
/// 같은 폴더를 여러 탭에 열 수 있다 (docs/PRD-v2.md §17).
/// </para>
/// </summary>
public sealed class SameInstanceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 컨테이너가 만들어지는 사이에 한쪽만 와 있는 순간이 있다. 둘 다 null 인 것을 "같다"
        // 로 읽으면 그 순간 모든 탭이 활성으로 그려진다.
        return values is [{ } left, { } right] && ReferenceEquals(left, right);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다 — 역방향 동기화가 생기면 ADR-011 위반이다.");
}

/// <summary>
/// 값이 매개변수와 같은가. 정렬 화살표 표시(<c>Sort[0].Key</c> = 컬럼 키)와 뷰 모드 판정이
/// 쓴다. 대상이 <c>Visibility</c> 면 그대로 매핑한다 — WPF 는 bool→Visibility 를 자동
/// 변환하지 않는다.
/// <para>
/// 활성 페인 판정은 여기가 아니다 — 인스턴스 비교라 <see cref="SameInstanceConverter"/> 다
/// (분할이 들어오며 <c>PaneSide</c> 열거형이 사라졌다, docs/PRD-v2.md §18).
/// </para>
/// </summary>
public sealed class EnumEqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = Equals(value, parameter);

        if (targetType == typeof(Visibility))
        {
            return equal ? Visibility.Visible : Visibility.Collapsed;
        }

        return equal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다.");
}

/// <summary>
/// 컬럼 폭(픽셀)을 <see cref="GridLength"/> 로 편다. <b>행이 헤더를 따라오게 하는 값이다</b>
/// (docs/DESIGN.md §2) — 끌 수 있는 것은 헤더뿐이고 (<c>Views/ColumnSync.cs</c>) 행은
/// 같은 값에 단방향으로 묶인다.
/// <para>
/// 쓸 수 없는 값(레이아웃 전의 0, NaN)은 <c>Auto</c> 로 접는다. 0 을 그대로 펴면 그 열의
/// 글자가 통째로 사라진다.
/// </para>
/// </summary>
public sealed class ColumnWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width && double.IsFinite(width) && width > 0
            ? new GridLength(width)
            : GridLength.Auto;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("행은 헤더를 따라올 뿐이다 — 끄는 것은 헤더에서 한다.");
}

/// <summary>
/// 뷰 모드가 목록의 소스를 고른다 — <c>[ViewMode, Items, Rows, DetailRows]</c> 를 받는다
/// (ADR-016 · docs/PRD-v2.md §6-1).
/// <para>
/// 소스가 여럿인 것이 의도다. 그룹화를 안 쓰는 Details 는 평평한 <c>Items</c> 를 그대로
/// 쓰고 (10만 항목의 주 경로에 래퍼를 두지 않는다), wrap 뷰 3종은 합성 행, 그룹화를 켠
/// Details 만 헤더가 섞인 투영 위에 선다. 목록 컨트롤은 하나이고 바뀌는 것은 소스와
/// <c>DataTemplate</c> 뿐이다 (ADR-002).
/// </para>
/// </summary>
public sealed class ViewSourceConverter : IMultiValueConverter
{
    /// <summary>
    /// Details 는 평평한 항목, wrap 뷰 3종은 합성 행이다 (ADR-016). 넷째는 그룹 투영으로,
    /// <b>비어 있으면 그룹화가 꺼진 것</b>이라 Details 가 v1 과 같은 경로를 탄다
    /// (docs/PRD-v2.md §6-1). 켜짐 여부를 따로 묻지 않는 이유는 신호가 둘이면 어긋날 수
    /// 있어서다 — 투영이 곧 답이다.
    /// </summary>
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values is [ViewMode mode, var items, var rows, var grouped]
            ? mode != ViewMode.Details
                ? rows
                : grouped is IReadOnlyList<DetailRowViewModel> { Count: > 0 } ? grouped : items
            : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다.");
}

/// <summary>
/// 픽셀을 그릴 수 있는 그림으로 바꾼다 — <c>[Thumbnail, Icon]</c> 을 받아 앞의 것을
/// 우선한다.
/// <para>
/// 변환이 View 의 일인 이유: <c>FlexDir.Core</c> 는 WPF 를 모르므로 포트가 BGRA 버퍼를
/// 낸다 (<see cref="ThumbnailBitmap"/>). 썸네일이 없으면 형식 아이콘이 남고, 둘 다 아직
/// 없으면 빈칸이다 — 형식 아이콘이 먼저 채워지므로 빈칸은 잠깐이다
/// (<c>ThumbnailRequestScheduler</c>).
/// </para>
/// </summary>
public sealed class ThumbnailImageConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => ToImage(values.OfType<ThumbnailBitmap>().FirstOrDefault());

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다.");

    /// <summary>
    /// BGRA 버퍼를 <see cref="WriteableBitmap"/> 으로 옮긴다.
    /// <para>
    /// <b><see cref="PixelFormats.Pbgra32"/> 다 — <c>Bgra32</c> 가 아니다.</b> shell 이 주는
    /// 픽셀은 알파가 곱해진 값이라, 곱하지 않은 형식으로 읽으면 반투명 가장자리가 어둡게
    /// 번진다 (.harness/HANDOFF.md §phase B).
    /// </para>
    /// <para>
    /// 얼려서 낸다. 그래야 UI 스레드가 아닌 곳에서 만들어도 되고 쓸 때마다 복사본이 생기지
    /// 않는다 — 큰 아이콘 하나가 256×256(256KB)까지 온다.
    /// </para>
    /// </summary>
    internal static BitmapSource? ToImage(ThumbnailBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        var image = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null);

        image.WritePixels(
            new Int32Rect(0, 0, bitmap.Width, bitmap.Height),
            bitmap.Pixels,
            bitmap.Width * 4,
            0);

        image.Freeze();

        return image;
    }
}
