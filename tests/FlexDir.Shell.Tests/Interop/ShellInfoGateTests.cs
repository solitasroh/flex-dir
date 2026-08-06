using FlexDir.Shell.Interop;

using Xunit;

namespace FlexDir.Shell.Tests.Interop;

/// <summary>
/// <c>SHGetFileInfoW</c> 를 프로세스 전체에서 한 번에 하나만 부르게 하는 관문.
/// <para>
/// 실물에서 <b>동시 호출이 그냥 0 을 냈다</b> — 예외도 오류 코드도 없이 아이콘 인덱스가
/// -1 로 온다. 두 페인이 같은 순간에 폴더 아이콘을 물었고 한쪽만 받았으며, 실패는 호출자의
/// 캐시에 남아 다시 묻지도 않았다 (docs/PRD.md §4). 그래서 그 페인의 아이콘이 통째로
/// 비어 있었다.
/// </para>
/// <para>
/// 직렬화 비용은 없다시피 하다: 두 호출자 모두 <c>SHGFI_USEFILEATTRIBUTES</c> 로 확장자
/// 연결 정보만 보므로 저장소에 닿지 않고, 조회는 확장자마다 한 번뿐이다.
/// </para>
/// </summary>
public class ShellInfoGateTests
{
    [Fact]
    public void Query_ReturnsTheResult()
    {
        Assert.Equal(42, ShellInfoGate.Query(() => 42));
    }

    [Fact]
    public void Query_LetsOnlyOneCallerIn()
    {
        var inside = 0;
        var overlapped = false;

        Parallel.For(0, 64, _ => ShellInfoGate.Query(() =>
        {
            if (Interlocked.Increment(ref inside) != 1)
            {
                overlapped = true;
            }

            // 겹칠 틈을 준다. 잠금이 없으면 이 사이에 다른 스레드가 들어온다.
            Thread.SpinWait(2000);

            Interlocked.Decrement(ref inside);

            return 0;
        }));

        Assert.False(overlapped, "SHGetFileInfo 관문에 둘 이상이 동시에 들어갔다.");
    }

    [Fact]
    public void Query_WhenTheCallThrows_LetsTheNextCallerIn()
    {
        // 예외로 관문이 닫히면 그 뒤의 모든 아이콘 조회가 영원히 매달린다.
        Assert.Throws<InvalidOperationException>(
            () => ShellInfoGate.Query<int>(() => throw new InvalidOperationException("실패")));

        Assert.Equal(7, ShellInfoGate.Query(() => 7));
    }

    [Fact]
    public void Query_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ShellInfoGate.Query<int>(null!));
    }
}
