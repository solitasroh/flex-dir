using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// shell 대화상자·컨텍스트 메뉴의 소유 창을 쥐는 자리 (phase B-4).
/// <para>
/// <b>Core 포트에 창 핸들이 없기 때문에 이것이 있다</b> (사용자 결정 2026-08-06).
/// <c>FlexDir.Core</c> 는 <c>HWND</c> 를 모르므로 Host 가 창을 쥐고 shell 구현체에
/// 물려 준다.
/// </para>
/// <para>
/// 값 하나를 두 스레드가 나눠 쓴다 — UI 스레드가 쓰고 STA 워커가 읽는다. 그래서 실제
/// <c>Window</c> 를 붙이는 것은 WPF 가 필요해 사람이 확인하고, 여기서는 <b>창이 아직
/// 없을 때 무엇을 내는가</b> 를 고정한다.
/// </para>
/// </summary>
public class OwnerWindowTests
{
    [Fact]
    public void Handle_BeforeAnyWindow_IsNothing()
    {
        // shell 은 0 을 "부모 없음" 으로 받는다 — 대화상자가 소유 창 없이 뜰 뿐 실패하지 않는다.
        // 창은 활성화가 만들므로 (ActivationRouter) 조립 시점에는 언제나 여기다.
        Assert.Equal(0, new OwnerWindow().Handle);
    }

    [Fact]
    public void Handle_AfterBinding_IsThatWindow()
    {
        var owner = new OwnerWindow();

        owner.Bind(4242);

        Assert.Equal(4242, owner.Handle);
    }

    [Fact]
    public void Handle_WhenTheWindowIsRemade_IsTheNewOne()
    {
        // 완전 종료 뒤 재실행은 새 창이다. 생성 시점에 잡아 두면 낡은 핸들이 남는다.
        var owner = new OwnerWindow();

        owner.Bind(11);
        owner.Bind(22);

        Assert.Equal(22, owner.Handle);
    }

    [Fact]
    public void Source_HandsOutAFuncTheShellCanHold()
    {
        // 구현체는 Func<nint> 를 들고 부를 때마다 묻는다 — 값을 넘기면 조립 시점의 0 이 박힌다.
        var owner = new OwnerWindow();
        var source = owner.Source;

        owner.Bind(7);

        Assert.Equal(7, source());
    }

    [Fact]
    public void Track_WithoutAWindow_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OwnerWindow().Track(null!));
    }
}
