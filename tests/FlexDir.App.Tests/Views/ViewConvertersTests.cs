using System.Globalization;

using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Presentation;
using FlexDir.Core.ViewState;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 선택·포커스·활성 페인을 그리는 변환기 (phase B-2).
/// <para>
/// 선택은 <c>PaneSelection</c> 이 이름 집합으로 들고 있고 행 인스턴스에는 없다 (ADR-011).
/// View 는 행마다 <c>MultiBinding</c> 으로 "내 이름이 집합에 있는가" 를 묻는다 — 가상화가
/// 실현한 행만 계산하므로 10만 항목에서도 선택 변경 비용이 보이는 행 수에 비례한다.
/// </para>
/// </summary>
public class ViewConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ── 이름 ∈ 선택 집합 ──────────────────────────────────────────

    [Theory]
    [InlineData("a.txt", true)]
    [InlineData("A.TXT", true)]   // 파일시스템과 같은 비교다 — 대소문자를 가리지 않는다
    [InlineData("b.txt", false)]
    public void NameMembership_ChecksTheSet(string name, bool expected)
    {
        var converter = new NameMembershipConverter();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.txt" };

        var result = converter.Convert([selected, name], typeof(bool), null, Culture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NameMembership_WithUnsetInputs_IsFalse()
    {
        // 바인딩이 아직 붙지 않으면 UnsetValue 가 들어온다. 예외를 내면 행 전체가 죽는다.
        var converter = new NameMembershipConverter();

        var result = converter.Convert(
            [System.Windows.DependencyProperty.UnsetValue, "a.txt"], typeof(bool), null, Culture);

        Assert.Equal(false, result);
    }

    // ── 이름 = 포커스 ─────────────────────────────────────────────

    [Theory]
    [InlineData("a.txt", "a.txt", true)]
    [InlineData("A.TXT", "a.txt", true)]
    [InlineData(null, "a.txt", false)]   // 포커스가 없다
    [InlineData("b.txt", "a.txt", false)]
    public void NameEquality_ComparesIgnoringCase(string? focused, string name, bool expected)
    {
        var converter = new NameEqualityConverter();

        // 인터페이스 시그니처는 null 을 약속하지 않지만 실제 바인딩은 null 을 실어 온다 —
        // 그 경우를 재는 테스트이므로 단언 연산자로 통과시킨다.
        var result = converter.Convert([focused!, name], typeof(bool), null, Culture);

        Assert.Equal(expected, result);
    }

    // ── 활성 페인 판정 ────────────────────────────────────────────

    [Fact]
    public void EnumEquality_ComparesTheValueToTheParameter()
    {
        var converter = new EnumEqualityConverter();

        Assert.Equal(true, converter.Convert(
            FlexDir.App.ViewModels.PaneSide.Left, typeof(bool), FlexDir.App.ViewModels.PaneSide.Left, Culture));
        Assert.Equal(false, converter.Convert(
            FlexDir.App.ViewModels.PaneSide.Right, typeof(bool), FlexDir.App.ViewModels.PaneSide.Left, Culture));
    }

    [Fact]
    public void EnumEquality_TargetingVisibility_MapsToVisibleAndCollapsed()
    {
        // 정렬 화살표가 이 변환기로 나타난다 — WPF 는 bool→Visibility 를 자동 변환하지 않는다.
        var converter = new EnumEqualityConverter();

        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert(
            FlexDir.Core.Sorting.SortKey.Name, typeof(System.Windows.Visibility), FlexDir.Core.Sorting.SortKey.Name, Culture));
        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert(
            FlexDir.Core.Sorting.SortKey.Size, typeof(System.Windows.Visibility), FlexDir.Core.Sorting.SortKey.Name, Culture));
    }

    // ── 뷰 모드 → 목록의 소스 (phase B-3 · ADR-016) ───────────────

    [Fact]
    public void ViewSource_GivesDetailsTheFlatItems()
    {
        // Details 는 합성 행을 지나지 않는다 — 10만 항목의 주 경로에 래퍼를 두지 않는다.
        var converter = new ViewSourceConverter();
        var items = new object();
        var rows = new object();

        Assert.Same(items, converter.Convert([ViewMode.Details, items, rows], typeof(object), null, Culture));
    }

    [Theory]
    [InlineData(ViewMode.List)]
    [InlineData(ViewMode.Tiles)]
    [InlineData(ViewMode.LargeIcons)]
    public void ViewSource_GivesTheWrapViewsTheCompositeRows(ViewMode mode)
    {
        var converter = new ViewSourceConverter();
        var items = new object();
        var rows = new object();

        Assert.Same(rows, converter.Convert([mode, items, rows], typeof(object), null, Culture));
    }

    [Fact]
    public void ViewSource_WithUnsetInputs_IsNull()
    {
        var converter = new ViewSourceConverter();

        Assert.Null(converter.Convert(
            [System.Windows.DependencyProperty.UnsetValue, new object(), new object()], typeof(object), null, Culture));
    }

    // ── 썸네일 픽셀 → 그릴 수 있는 그림 (phase B-3) ───────────────

    [Fact]
    public void ThumbnailImage_IsPremultipliedBgra()
    {
        // Bgra32 로 만들면 반투명 가장자리가 어둡게 번진다 (.harness/HANDOFF.md §phase B).
        var image = ThumbnailImageConverter.ToImage(Bitmap(2, 2));

        Assert.NotNull(image);
        Assert.Equal(System.Windows.Media.PixelFormats.Pbgra32, image.Format);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }

    [Fact]
    public void ThumbnailImage_KeepsThePixelsAndIsFrozen()
    {
        // 얼려야 UI 스레드 밖에서 만들어도 되고, 쓸 때마다 복사본이 생기지 않는다.
        var source = Bitmap(2, 1);
        var image = ThumbnailImageConverter.ToImage(source);

        Assert.NotNull(image);
        Assert.True(image.IsFrozen);

        var pixels = new byte[source.Pixels.Length];
        image.CopyPixels(pixels, source.Width * 4, 0);

        Assert.Equal(source.Pixels, pixels);
    }

    [Fact]
    public void ThumbnailImage_WithoutPixels_IsNull()
    {
        Assert.Null(ThumbnailImageConverter.ToImage(null));
    }

    [Fact]
    public void ThumbnailImage_PrefersTheThumbnailOverTheTypeIcon()
    {
        var converter = new ThumbnailImageConverter();
        var thumbnail = Bitmap(4, 4);

        var image = converter.Convert([thumbnail, Bitmap(2, 2)], typeof(object), null, Culture);

        Assert.Equal(4, Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image).PixelWidth);
    }

    [Fact]
    public void ThumbnailImage_WithoutAThumbnail_FallsBackToTheTypeIcon()
    {
        // 썸네일이 오기 전에 보이는 것이 형식 아이콘이고, 없거나 실패해도 남는 것이 그것이다.
        var converter = new ThumbnailImageConverter();

        var image = converter.Convert([null!, Bitmap(2, 2)], typeof(object), null, Culture);

        Assert.Equal(2, Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image).PixelWidth);
    }

    [Fact]
    public void ThumbnailImage_WithNothingYet_IsNull()
    {
        var converter = new ThumbnailImageConverter();

        Assert.Null(converter.Convert(
            [System.Windows.DependencyProperty.UnsetValue, null!], typeof(object), null, Culture));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        // 전부 단방향 표시용이다. 역방향이 생기면 View→ViewModel 동기화가 부활한다 (ADR-011).
        Assert.Throws<NotSupportedException>(() =>
            new NameMembershipConverter().ConvertBack(true, [typeof(object)], null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new NameEqualityConverter().ConvertBack(true, [typeof(object)], null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new EnumEqualityConverter().ConvertBack(true, typeof(object), null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new ViewSourceConverter().ConvertBack(true, [typeof(object)], null, Culture));
        Assert.Throws<NotSupportedException>(() =>
            new ThumbnailImageConverter().ConvertBack(true, [typeof(object)], null, Culture));
    }

    /// <summary>픽셀마다 다른 값을 넣는다 — 왕복이 뒤섞여도 드러나게.</summary>
    private static ThumbnailBitmap Bitmap(int width, int height)
        => new(width, height, [.. Enumerable.Range(0, width * height * 4).Select(index => (byte)(index + 1))]);
}
