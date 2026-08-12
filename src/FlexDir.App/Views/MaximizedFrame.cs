using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace FlexDir.App.Views;

/// <summary>
/// 최대화한 창에서 화면 밖으로 나간 테두리 몫을 내용에서 덜어내는 attached behavior.
///
/// <para>
/// <b>왜 필요한가</b> (2026-08-12 실측 · 사용자 신고). <c>WS_THICKFRAME</c> 창을 최대화하면
/// Windows 는 창 rect 를 작업영역보다 사방으로 리사이즈 테두리(<c>SM_CXSIZEFRAME</c> +
/// <c>SM_CXPADDEDBORDER</c> · 100% 에서 8px)만큼 크게 잡는다. 보통 창에서는 그 띠가 비-클라이언트
/// 영역이라 상관없지만, <see cref="WindowChrome"/> 는 클라이언트를 창 rect 전체로 넓힌다 —
/// 그래서 <b>내용이 창 (0,0) 에서 시작한 채 위·좌·우·아래 8px 이 화면 밖으로 나간다.</b>
/// 32px 캡션 줄이 24px 만 보이고 아이콘이 화면 맨 위에 붙는 것이 그 증상이다.
/// </para>
///
/// <para>
/// <b>PrintWindow 캡처로는 이것을 볼 수 없다.</b> 잘려나간 그 8px 자리를 보이지 않는
/// 리사이즈 테두리가 검게 덮어써서, 마치 내용이 (8,8) 에서 시작한 것처럼 찍힌다. 2026-08-06 에
/// "최대화 여백은 손대지 않는다" 로 결론 낸 것이 이 착시다. 가르는 방법은 UI Automation 으로
/// <b>캡션 글리프의 화면 좌표</b>를 재서 작업영역 위끝과 비교하는 것이다 — 16x16 글리프는
/// 32px 줄에 중앙 정렬이라 위 여백이 8px 이어야 하는데, 최대화에서는 0 이었다.
/// </para>
///
/// <para>
/// <b>WM_GETMINMAXINFO 로 창 자체를 줄이지 않는 이유</b>: <see cref="WindowChrome"/> 도 같은
/// 메시지를 다뤄서 누가 이기는지가 훅 등록 순서에 달린다. 2026-08-06 에 그 길로 갔다가
/// "값은 맞게 오는데 결과가 안 바뀐다" 로 걷어낸 기록이 있다. 레이아웃 쪽에서 덜어내면
/// 메시지 순서와 무관하게 결정적이다.
/// </para>
/// </summary>
public static class MaximizedFrame
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(MaximizedFrame),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 창 rect 가 작업영역 밖으로 나간 몫. 최대화하지 않은 창은 나갈 것이 없어 0 이다.
    /// <para>
    /// 음수로 내려가지 않는다 — 음수 <c>Margin</c> 은 WPF 에서 예외가 아니라 내용을 오히려
    /// 화면 밖으로 밀어내는 쪽으로 조용히 동작한다.
    /// </para>
    /// </summary>
    internal static Thickness Overflow(Rect window, Rect work)
        => new(
            Math.Max(0, work.Left - window.Left),
            Math.Max(0, work.Top - window.Top),
            Math.Max(0, window.Right - work.Right),
            Math.Max(0, window.Bottom - work.Bottom));

    /// <summary>덜어낸 만큼 캡션의 조작 띠도 내린다. 원래 높이를 여기 적어 둔다.</summary>
    private static readonly DependencyProperty BaseCaptionHeightProperty = DependencyProperty.RegisterAttached(
        "BaseCaptionHeight",
        typeof(double?),
        typeof(MaximizedFrame),
        new PropertyMetadata(null));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다
        // (WindowCaption·SplitterSync 와 같은 이유).
        window.SourceInitialized += (_, _) => Apply(window);
        window.StateChanged += (_, _) => Apply(window);

        // 최대화한 채로 모니터를 건너가면(Win+Shift+방향) 작업영역이 통째로 달라진다.
        window.LocationChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        var chrome = WindowChrome.GetWindowChrome(window);

        // 우리가 고치기 전의 값이 원본이다. 두 번째부터는 이미 적어 둔 것을 쓴다.
        if (chrome is not null && window.GetValue(BaseCaptionHeightProperty) is null)
        {
            window.SetValue(BaseCaptionHeightProperty, chrome.CaptionHeight);
        }

        var overflow = window.WindowState == WindowState.Maximized
            ? MeasureOverflow(window)
            : default;

        root.Margin = overflow;

        // 내용을 8 내렸으므로 캡션 띠도 그만큼 길어져야 눈에 보이는 캡션 줄 전체가 끌린다.
        // 그대로 두면 아래 8px 이 끌리지도 더블클릭되지도 않는다.
        if (chrome is not null && window.GetValue(BaseCaptionHeightProperty) is double baseHeight)
        {
            chrome.CaptionHeight = baseHeight + overflow.Top;
        }
    }

    private static Thickness MeasureOverflow(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source
            || !GetWindowRect(source.Handle, out var bounds)
            || MonitorFromWindow(source.Handle, MonitorDefaultToNearest) is var monitor && monitor == IntPtr.Zero)
        {
            return default;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(monitor, ref info))
        {
            return default;
        }

        // 물리 픽셀로 재고 DIP 로 옮긴다. 배율이 다른 모니터에서 숫자를 그대로 쓰면
        // 덜어내는 양이 틀린다 — 이 기계는 둘 다 100% 라 눈에 안 띄는 자리다.
        var toDip = source.CompositionTarget.TransformFromDevice;

        return Overflow(ToRect(bounds, toDip), ToRect(info.Work, toDip));
    }

    private static Rect ToRect(Rectangle value, System.Windows.Media.Matrix toDip)
        => new(
            toDip.Transform(new Point(value.Left, value.Top)),
            toDip.Transform(new Point(value.Right, value.Bottom)));

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle Monitor;
        public Rectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rectangle bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
