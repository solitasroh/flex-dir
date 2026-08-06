using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// 제품 아이콘 (사용자 결정 2026-08-06). 트레이가 <c>SystemIcons.Application</c> 을 임시로
/// 쓰고 있었고, 그것을 이 리소스로 바꾼다.
/// <para>
/// <b>여기서 채점하는 것은 "리소스가 실려 있는가" 하나다.</b> 아이콘이 예쁜지는 사람이 본다
/// (CLAUDE.md §5). 하지만 리소스 이름이 어긋나면 트레이가 <b>조용히</b> 빈 아이콘이 되거나
/// 생성자가 던지고, 그것은 자동으로 잡을 수 있다 — csproj 의 <c>EmbeddedResource</c> 를
/// 지우거나 파일을 옮기면 여기서 걸린다.
/// </para>
/// </summary>
public class ProductIconTests
{
    [Fact]
    public void TheIcon_IsEmbeddedInTheAssembly()
    {
        // 이름이 어긋나는 것이 이 클래스가 막는 유일한 실패다. 그것을 먼저 못박는다.
        Assert.Contains(
            ProductIcon.ResourceName,
            typeof(ProductIcon).Assembly.GetManifestResourceNames());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    public void Load_GivesTheFrameThatWasAskedFor(int size)
    {
        // 크기마다 다른 그림이 실려 있다는 것이 다중 해상도 .ico 의 존재 이유다.
        // 한 장짜리 .ico 를 넣으면 여기서 갈린다.
        using var icon = ProductIcon.Load(size);

        Assert.Equal(size, icon.Width);
        Assert.Equal(size, icon.Height);
    }

    [Fact]
    public void Tray_IsTheSixteenPixelFrame()
    {
        // 알림 영역이 묻는 크기다. 96 DPI 기준이고, 배율이 걸리면 Windows 가 32 를 묻는다.
        using var tray = ProductIcon.LoadTray();

        Assert.Equal(16, tray.Width);
    }

    [Fact]
    public void EachLoad_GivesACallerOwnedIcon()
    {
        // 공유 인스턴스를 돌려주면 트레이가 Dispose 한 뒤 창이 죽은 핸들을 든다.
        using var first = ProductIcon.LoadTray();
        using var second = ProductIcon.LoadTray();

        Assert.NotSame(first, second);
    }
}
