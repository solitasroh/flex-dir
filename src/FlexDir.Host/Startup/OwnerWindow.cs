using System.Windows;
using System.Windows.Interop;

namespace FlexDir.Host.Startup;

/// <summary>
/// shell 대화상자와 컨텍스트 메뉴의 소유 창을 쥐는 자리 (phase B-4).
/// <para>
/// <b>이 클래스가 있는 이유는 Core 포트에 창 핸들이 없기 때문이다</b> (사용자 결정
/// 2026-08-06 · <c>IContextMenuProvider</c>). <c>FlexDir.Core</c> 는 <c>HWND</c> 를 모르고
/// (CLAUDE.md §1) <c>FlexDir.Shell</c> 은 창을 모르므로, 둘을 다 아는 <c>Host</c> 가
/// 조립할 때 <see cref="Source"/> 를 물려 준다.
/// </para>
/// <para>
/// <b>핸들을 한 번 잡아 두고 값으로 낸다.</b> <see cref="WindowInteropHelper"/> 는 창을
/// 소유한 스레드에서만 읽을 수 있는데 shell 구현체는 STA 워커에서 묻는다 — 부를 때마다
/// WPF 에 물어보면 스레드 친화성 예외가 난다 (B-2 에서 창 배치 복원이 밟은 것과 같은 함정).
/// </para>
/// </summary>
public sealed class OwnerWindow
{
    private nint handle;

    /// <summary>
    /// 아직 창이 없으면 0 이다 — shell 은 그것을 "부모 없음" 으로 받는다. 창은 활성화가
    /// 만들므로 (<c>ActivationRouter</c>) 조립 시점에는 언제나 0 이다.
    /// </summary>
    public nint Handle => Volatile.Read(ref handle);

    /// <summary>
    /// 구현체가 들고 다닐 공급자. <b>값이 아니라 함수를 준다</b> — 조립 시점에는 창이
    /// 없으므로 값을 넘기면 0 이 박힌다.
    /// </summary>
    public Func<nint> Source => () => Handle;

    /// <summary>UI 스레드에서 잡은 핸들을 싣는다.</summary>
    public void Bind(nint window) => Volatile.Write(ref handle, window);

    /// <summary>
    /// 창의 핸들이 생기는 순간을 잡는다. <c>SourceInitialized</c> 가 HWND 가 존재하는 첫
    /// 시점이다 — 생성자에서 물으면 아직 0 이다.
    /// </summary>
    public void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.SourceInitialized += (_, _) => Bind(new WindowInteropHelper(window).Handle);
    }
}
