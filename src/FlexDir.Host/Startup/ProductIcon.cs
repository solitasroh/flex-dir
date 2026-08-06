using System.Drawing;

namespace FlexDir.Host.Startup;

/// <summary>
/// 제품 아이콘. 어셈블리에 박아 둔 다중 해상도 <c>.ico</c> 에서 요청한 크기의 프레임을 꺼낸다.
/// <para>
/// <b>왜 크기를 물어야 하는가</b>: 트레이·작업표시줄·Alt+Tab 이 서로 다른 프레임을 쓴다.
/// 한 장을 늘리거나 줄여 쓰면 16 에서 뭉개지거나 256 에서 흐려진다 —
/// <c>assets\flex-dir.ico</c> 는 16·32·48·256 을 담고 있고 16·32 는 축소가 아니라
/// 직접 그린 것이다 (<c>scripts\make-ico.ps1</c>).
/// </para>
/// <para>
/// <b>부를 때마다 새 인스턴스를 낸다.</b> 공유하면 먼저 <c>Dispose</c> 한 쪽이 나머지의
/// 핸들을 죽인다 — 트레이와 창이 같은 아이콘을 들기 때문에 실제로 일어나는 일이다.
/// </para>
/// </summary>
internal static class ProductIcon
{
    /// <summary>
    /// 박아 둔 리소스의 이름. 기본 규칙(루트 네임스페이스 + 파일 이름)을 따르므로
    /// <c>csproj</c> 의 <c>LogicalName</c> 이 이 값을 고정한다.
    /// </summary>
    public const string ResourceName = "FlexDir.Host.flex-dir.ico";

    /// <summary>알림 영역이 묻는 크기. 배율이 걸리면 Windows 가 더 큰 프레임을 따로 묻는다.</summary>
    private const int TraySize = 16;

    public static Icon LoadTray() => Load(TraySize);

    public static Icon Load(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(size, 0);

        // 스트림은 프레임을 고르는 동안에만 필요하다. Icon 은 자기 사본을 든다.
        using var stream = typeof(ProductIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"제품 아이콘 리소스가 없다: {ResourceName}. csproj 의 EmbeddedResource 를 본다.");

        return new Icon(stream, size, size);
    }
}
