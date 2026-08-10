using System.Runtime.InteropServices;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Operations;

/// <summary>
/// shell 컨텍스트 메뉴를 띄우는 구현체 (docs/SHELL_NOTES.md §컨텍스트 메뉴).
/// <para>
/// <b>소유 창을 밖에서 받는다.</b> <c>FlexDir.Core</c> 의 포트에는 창 핸들이 없고
/// (사용자 결정 2026-08-06) <c>FlexDir.Host</c> 가 조립할 때 공급자를 물려 준다. 그 핸들은
/// shell 확장이 띄우는 대화상자의 부모로만 쓴다 — <b>메뉴 자신의 주인은 우리가 만드는 숨은
/// 창</b>이다. 메뉴 루프는 그것을 띄운 스레드에서 돌아야 하고 오너드로 메시지도 그 스레드의
/// 창 프로시저로 오는데, WPF 창은 UI 스레드 소유라 STA 워커에서 쓸 수 없다.
/// </para>
/// <para>
/// <b>저장소에 닿는 부분이 전부 UI 스레드 밖이다</b> (CLAUDE.md §3). 폴더 바인딩·PIDL
/// 파싱·<c>QueryContextMenu</c>·<c>InvokeCommand</c> 는 네트워크 경로에서 초 단위로
/// 블로킹된다. 메뉴가 떠 있는 동안 워커 하나가 통째로 묶이는 것은 의도다 — 컨텍스트
/// 메뉴는 한 번에 하나다.
/// </para>
/// </summary>
public sealed partial class ShellContextMenuProvider : IContextMenuProvider, IDisposable
{
    /// <summary>메뉴는 한 번에 하나다. 늘려도 두 번째는 첫 번째가 닫힐 때까지 뜰 수 없다.</summary>
    private const int Workers = 1;

    private readonly StaWorkQueue worker = new(Workers, "context menu");

    private readonly Func<nint> ownerWindow;

    private readonly Func<Request, MenuOutcome> show;

    /// <param name="ownerWindow">
    /// shell 대화상자의 부모가 될 창. <b>부를 때마다 묻는다</b> — 생성 시점에 잡아 두면
    /// 완전 종료 뒤 재실행이 만든 새 창 대신 낡은 핸들이 남는다 (ADR-003).
    /// </param>
    public ShellContextMenuProvider(Func<nint> ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);

        this.ownerWindow = ownerWindow;
        show = Show;
    }

    /// <summary>
    /// 실행 지점을 바꿔 끼운다. 실물은 <c>TrackPopupMenuEx</c> 로 사용자를 기다리는 모달
    /// 루프이므로 자동 테스트가 밟으면 매달림이 된다 (.harness/HANDOFF.md §규칙 5).
    /// 바꿔 끼운 자리 위에서 재는 것은 <b>무엇을 어떤 창으로 넘기는가</b> 다.
    /// </summary>
    internal ShellContextMenuProvider(Func<nint> ownerWindow, Func<Request, MenuOutcome> show)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(show);

        this.ownerWindow = ownerWindow;
        this.show = show;
    }

    /// <param name="Names">
    /// 메뉴를 낼 항목들의 <b>자식 이름</b>. 비어 있으면 <paramref name="Folder"/> 의 배경
    /// 메뉴다 — shell 에서 둘은 다른 API 다 (<c>GetUIObjectOf</c> 대 <c>CreateViewObject</c>).
    /// </param>
    internal sealed record Request(
        nint Owner,
        LocationId Folder,
        IReadOnlyList<string> Names,
        ScreenPoint At,
        IReadOnlyList<string> AppCommands);

    /// <summary>
    /// 메뉴가 남긴 것. <paramref name="AppCommand"/> 는 사용자가 고른 앱 항목의 인덱스이며,
    /// shell verb 를 골랐거나 그냥 닫았으면 <c>null</c> 이다.
    /// </summary>
    internal readonly record struct MenuOutcome(int HResult, int? AppCommand);

    public Task<int?> ShowAsync(
        IReadOnlyList<LocationId> items,
        LocationId folder,
        ScreenPoint at,
        IReadOnlyList<string> appCommands,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(appCommands);

        var names = LeafNames(items, folder);

        return worker.RunAsync(
            () =>
            {
                var outcome = show(new Request(ownerWindow(), folder, names, at, appCommands));

                // 사용자가 아무것도 고르지 않고 닫은 것은 실패가 아니다.
                if (outcome.HResult < 0)
                {
                    throw Translate(outcome.HResult, folder);
                }

                return outcome.AppCommand;
            },
            ct);
    }

    /// <summary>실패한 HRESULT 를 그대로 싣는다. 0 이면 알 수 없는 실패로 본다.</summary>
    private static MenuOutcome Failed(int hresult) => new(hresult < 0 ? hresult : Unexpected, null);

    public void Dispose() => worker.Dispose();

    /// <summary>
    /// 메뉴를 낼 자식 이름들. <b>그 폴더의 직계 자식이 아닌 것은 버린다</b> — 폴더 자신이나
    /// 다른 폴더의 항목이 섞이면 <c>ParseDisplayName</c> 이 엉뚱한 것을 내고, 빈 leaf 이름은
    /// 일부 네임스페이스에서 <b>폴더 자신</b>으로 해석된다
    /// (docs/SHELL_NOTES.md §컨텍스트 메뉴 함정 6).
    /// </summary>
    internal static string[] LeafNames(IReadOnlyList<LocationId> items, LocationId folder)
        => [.. items
            .Where(item => item.TryGetParent(out var parent) && parent.Equals(folder))
            .Select(item => item.Name)];

    /// <summary>
    /// 메뉴가 떠 있는 동안 <c>IContextMenu2/3</c> 로 넘겨야 하는 메시지인가
    /// (docs/SHELL_NOTES.md §컨텍스트 메뉴 함정 1).
    /// <para>
    /// 넘기지 않으면 "Open With"·"공유" 같은 <b>오너드로 항목이 빈칸으로 그려진다</b> —
    /// 메뉴는 뜨는데 글자가 없다.
    /// </para>
    /// </summary>
    internal static bool IsMenuMessage(uint message)
        => message is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar;

    /// <summary>
    /// HRESULT 를 포트의 오류로 옮긴다. 마스크를 <c>int</c> 로 못박는 이유는
    /// <c>ShellFileOperations.Translate</c> 와 같다 — uint 리터럴을 그대로 쓰면 음수
    /// HRESULT 가 부호 확장돼 비교가 영원히 거짓이 된다.
    /// </summary>
    private static LocationAccessException Translate(int hresult, LocationId location)
    {
        const int FacilityWin32 = unchecked((int)0x80070000);
        const int FacilityMask = unchecked((int)0xFFFF0000);

        var win32 = (hresult & FacilityMask) == FacilityWin32 ? hresult & 0xFFFF : 0;

        var kind = Win32ErrorMapping.Classify(win32);

        if (kind == LocationErrorKind.None)
        {
            kind = LocationErrorKind.Unknown;
        }

        return new LocationAccessException(kind, location, win32);
    }

    // ── 실제 COM 호출 ───────────────────────────────────────────────

    /// <summary>
    /// <b>COM 객체도 네이티브 핸들도 이 호출 밖으로 나가지 않는다</b>
    /// (<c>StaWorkQueue</c> 의 규칙). 나가는 것은 HRESULT 하나다.
    /// </summary>
    private static MenuOutcome Show(Request request)
    {
        var hr = SHGetDesktopFolder(out var desktopRaw);

        if (hr < 0 || desktopRaw == 0)
        {
            return Failed(hr);
        }

        using var desktop = new ComRef<IShellFolder>(desktopRaw);

        // shell 파서는 \\?\ 확장 접두사를 모른다. DisplayPath 를 준다.
        hr = desktop.Instance.ParseDisplayName(0, 0, request.Folder.DisplayPath, 0, out var folderPidl, 0);

        if (hr < 0 || folderPidl == 0)
        {
            return Failed(hr);
        }

        try
        {
            var folderIid = typeof(IShellFolder).GUID;

            hr = desktop.Instance.BindToObject(folderPidl, 0, in folderIid, out var folderRaw);

            if (hr < 0 || folderRaw == 0)
            {
                return Failed(hr);
            }

            using var folder = new ComRef<IShellFolder>(folderRaw);

            return Build(folder.Instance, request);
        }
        finally
        {
            CoTaskMemFree(folderPidl);
        }
    }

    /// <summary>
    /// 메뉴 객체를 얻는다. <b>항목 메뉴와 배경 메뉴는 다른 API 다</b> —
    /// <c>GetUIObjectOf</c> 대 <c>CreateViewObject</c> (docs/SHELL_NOTES.md §컨텍스트 메뉴).
    /// </summary>
    private static MenuOutcome Build(IShellFolder folder, Request request)
    {
        var children = new List<nint>(request.Names.Count);

        try
        {
            foreach (var name in request.Names)
            {
                // 함정 7 — 방금 삭제된 파일이 선택에 남아 있을 수 있다. 그 항목만 건너뛰고
                // 메뉴는 살린다. 하나 때문에 전체를 실패시키면 사용자는 살아 있는 항목에도
                // 손을 못 댄다.
                if (folder.ParseDisplayName(request.Owner, 0, name, 0, out var child, 0) >= 0 && child != 0)
                {
                    children.Add(child);
                }
            }

            // 고른 것이 전부 사라졌다. 낼 메뉴가 없으니 조용히 물러난다 — 배경 메뉴를
            // 대신 띄우면 사용자가 겨냥한 적 없는 항목들이 뜬다.
            if (request.Names.Count > 0 && children.Count == 0)
            {
                return new MenuOutcome(Ok, null);
            }

            var menuIid = typeof(IContextMenu).GUID;

            var hr = request.Names.Count == 0
                ? folder.CreateViewObject(request.Owner, in menuIid, out var menuRaw)
                : folder.GetUIObjectOf(request.Owner, (uint)children.Count, [.. children], in menuIid, 0, out menuRaw);

            if (hr < 0 || menuRaw == 0)
            {
                return Failed(hr);
            }

            using var menu = new ComRef<IContextMenu>(menuRaw);

            return Track(menu.Instance, request);
        }
        finally
        {
            foreach (var child in children)
            {
                CoTaskMemFree(child);
            }
        }
    }

    /// <summary>
    /// 메뉴를 띄우고 고른 것을 실행한다.
    /// <para>
    /// <b>후킹은 <c>InvokeCommand</c> 동안에도 유지된다</b> (함정 2) — shell verb 가 중첩
    /// 메뉴를 펌프하면서 같은 메시지를 또 낸다. 그래서 숨은 창과 싱크가 <c>Invoke</c> 뒤에
    /// 정리된다.
    /// </para>
    /// </summary>
    private static MenuOutcome Track(IContextMenu menu, Request request)
    {
        var popup = CreatePopupMenu();

        if (popup == 0)
        {
            return new MenuOutcome(Unexpected, null);
        }

        var sink = new MenuMessageSink(menu);

        try
        {
            // verb ID 범위는 1 ~ 0x7FFF (함정 3). 앱 항목은 그 위를 쓴다.
            var hr = menu.QueryContextMenu(popup, 0, MinVerbId, MaxVerbId, CmfNormal);

            if (hr < 0)
            {
                return new MenuOutcome(hr, null);
            }

            InsertAppCommands(popup, request.AppCommands);

            using var host = new MenuHostWindow(request.Owner, sink);

            // 소유 창을 foreground 로 올리지 않으면 메뉴 밖을 눌러도 닫히지 않는다
            // (KB135788). 숨은 창은 WPF 창이 소유하므로 활성 표시는 그대로 남는다.
            SetForegroundWindow(host.Handle);

            var chosen = TrackPopupMenuEx(popup, TpmReturnCmd | TpmRightButton, request.At.X, request.At.Y, host.Handle, 0);

            // 같은 KB 의 나머지 절반 — 메뉴 루프가 완전히 풀리게 한다.
            PostMessage(host.Handle, WmNull, 0, 0);

            // 아무것도 고르지 않고 닫았다. 실패가 아니다.
            if (chosen == 0)
            {
                return new MenuOutcome(Ok, null);
            }

            // 우리 항목이면 shell 에 넘기지 않는다. 실행은 <b>메뉴가 완전히 풀린 뒤</b>에
            // 부른 쪽이 한다 (함정 8) — 여기서 곧바로 하면 TrackPopupMenuEx 가 아직
            // 자기 루프 안이다.
            if (AppCommandOf(chosen) is { } appCommand)
            {
                return new MenuOutcome(Ok, appCommand);
            }

            return new MenuOutcome(Invoke(menu, request, (uint)chosen), null);
        }
        finally
        {
            DestroyMenu(popup);
        }
    }

    /// <summary>
    /// 앱 자체 항목을 메뉴 <b>맨 위</b>에 넣는다. 아래는 shell 이 세운 것 그대로다.
    /// <para>
    /// 위치로 넣는다(<c>MF_BYPOSITION</c>) — shell 이 첫 항목에 무슨 ID 를 줬는지 알 필요가
    /// 없다. 구분선은 우리 것과 shell 것을 눈으로 가른다.
    /// </para>
    /// </summary>
    private static void InsertAppCommands(nint popup, IReadOnlyList<string> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        // 뒤에서부터 0 번 자리에 넣으면 준 순서대로 선다.
        InsertMenu(popup, 0, MfByPosition | MfSeparator, 0, null);

        for (var index = commands.Count - 1; index >= 0; index--)
        {
            InsertMenu(popup, 0, MfByPosition | MfString, (nuint)(AppVerbBase + index), commands[index]);
        }
    }

    /// <summary>
    /// 고른 것이 앱 항목이면 그 인덱스, shell verb 면 <c>null</c>.
    /// </summary>
    internal static int? AppCommandOf(int chosen)
        => chosen >= AppVerbBase ? chosen - (int)AppVerbBase : null;

    /// <summary>
    /// 고른 verb 를 실행한다.
    /// <para>
    /// <b>ID 로 부를 때 <c>lpVerbW</c> 를 채우지 않는다</b> (함정 4). 일부 확장은 그 필드가
    /// non-null 이면 <c>IS_INTRESOURCE</c> 인데도 문자열로 역참조해서 <b>크래시한다</b> —
    /// 그래서 <c>CMIC_MASK_UNICODE</c> 도 주지 않는다.
    /// </para>
    /// </summary>
    private static int Invoke(IContextMenu menu, Request request, uint chosen)
    {
        var info = new InvokeCommandInfoEx
        {
            Size = (uint)Marshal.SizeOf<InvokeCommandInfoEx>(),
            Mask = CmicMaskPtInvoke,
            Window = request.Owner,
            Verb = (nint)(chosen - MinVerbId),
            Show = SwShowNormal,
            InvokeX = request.At.X,
            InvokeY = request.At.Y,
        };

        return menu.InvokeCommand(ref info);
    }

    /// <summary>
    /// 오너드로 메시지를 <c>IContextMenu2/3</c> 로 넘기는 싱크 (함정 1).
    /// <c>IContextMenu3</c> 를 먼저 묻는다 — <c>WM_MENUCHAR</c> 를 유니코드로 다룬다.
    /// </summary>
    private sealed class MenuMessageSink
    {
        private readonly IContextMenu2? menu2;

        private readonly IContextMenu3? menu3;

        /// <summary>
        /// <b>여기서 얻은 참조를 따로 놓지 않는다.</b> <c>as</c> 는 같은 RCW 를 다른 타입으로
        /// 돌려줄 뿐이라 <c>ReleaseComObject</c> 를 부르면 <see cref="ComRef{T}"/> 가 이미
        /// 세고 있는 것을 한 번 더 놓는다 — 과다 해제는 나중에 엉뚱한 자리에서 죽는다.
        /// 수명은 메뉴를 만든 <see cref="ComRef{T}"/> 하나가 쥔다.
        /// </summary>
        public MenuMessageSink(IContextMenu menu)
        {
            menu3 = menu as IContextMenu3;
            menu2 = menu3 is null ? menu as IContextMenu2 : null;
        }

        public bool TryHandle(uint message, nint wParam, nint lParam, out nint result)
        {
            result = 0;

            if (!IsMenuMessage(message))
            {
                return false;
            }

            if (menu3 is not null)
            {
                return menu3.HandleMenuMsg2(message, wParam, lParam, out result) >= 0;
            }

            // IContextMenu2 는 결과를 내지 않는다 — WM_MENUCHAR 는 여기서 다룰 수 없다.
            return menu2 is not null && menu2.HandleMenuMsg(message, wParam, lParam) >= 0;
        }
    }

    /// <summary>
    /// 메뉴의 주인이 될 숨은 창. <b>WPF 창을 쓸 수 없다</b> — 메뉴 루프와 오너드로 메시지는
    /// 창을 소유한 스레드로 가는데 그것은 UI 스레드이고, 거기서 <c>QueryContextMenu</c> 를
    /// 부르면 저장소에 닿는 호출이 UI 스레드로 온다 (CLAUDE.md §3).
    /// <para>
    /// 대신 WPF 창을 <b>소유자</b>로 준다. 그래야 이 창이 foreground 가 돼도 앱의 활성
    /// 표시가 유지되고 작업표시줄에 항목이 하나 더 생기지 않는다.
    /// </para>
    /// </summary>
    private sealed class MenuHostWindow : IDisposable
    {
        /// <summary>
        /// 창 프로시저는 프로세스 수명 내내 살아 있어야 한다 — GC 가 델리게이트를 걷어가면
        /// 다음 메시지에서 죽는다.
        /// </summary>
        private static readonly WindowProc Proc = Dispatch;

        private static readonly nint ClassName = RegisterOnce();

        /// <summary>
        /// 지금 메뉴를 띄운 싱크. 메뉴는 스레드당 하나뿐이므로 (모달 루프) 이것으로 충분하다.
        /// </summary>
        [ThreadStatic]
        private static MenuMessageSink? active;

        public MenuHostWindow(nint owner, MenuMessageSink sink)
        {
            active = sink;

            Handle = CreateWindowExW(WsExToolWindow, ClassName, 0, WsPopup, 0, 0, 0, 0, owner, 0, 0, 0);
        }

        public nint Handle { get; }

        public void Dispose()
        {
            active = null;

            if (Handle != 0)
            {
                DestroyWindow(Handle);
            }
        }

        private static nint Dispatch(nint window, uint message, nint wParam, nint lParam)
            => active is { } sink && sink.TryHandle(message, wParam, lParam, out var result)
                ? result
                : DefWindowProcW(window, message, wParam, lParam);

        private static nint RegisterOnce()
        {
            // 클래스 이름은 프로세스 수명 내내 살아 있어야 한다. 해제하지 않는 것이 의도다.
            var name = Marshal.StringToHGlobalUni("FlexDirContextMenuHost");

            var windowClass = new WindowClass
            {
                Procedure = Marshal.GetFunctionPointerForDelegate(Proc),
                ClassName = name,
            };

            // 이미 등록돼 있으면 0 이 온다. 이름은 그대로 쓸 수 있다.
            RegisterClassW(in windowClass);

            return name;
        }
    }

    /// <summary>
    /// RCW 와 원시 포인터를 함께 들고 둘 다 놓는다. <c>Marshal.GetObjectForIUnknown</c> 이
    /// 자기 참조를 따로 잡으므로 <c>ReleaseComObject</c> 와 <c>Marshal.Release</c> 를
    /// <b>둘 다</b> 해야 한다 (<c>ShellFileOperations.ComRef</c> 와 같은 규칙).
    /// </summary>
    private sealed class ComRef<T> : IDisposable
        where T : class
    {
        private readonly nint raw;

        public ComRef(nint raw)
        {
            this.raw = raw;
            Instance = (T)Marshal.GetObjectForIUnknown(raw);
        }

        public T Instance { get; }

        public void Dispose()
        {
            Marshal.ReleaseComObject(Instance);
            Marshal.Release(raw);
        }
    }

    // ── 네이티브 선언 ───────────────────────────────────────────────

    private const int Ok = 0;

    private const int Unexpected = unchecked((int)0x8000FFFF);

    /// <summary>shell verb ID 범위 (함정 3). 앱 자체 항목은 이 위를 쓴다.</summary>
    private const uint MinVerbId = 1;

    private const uint MaxVerbId = 0x7FFF;

    /// <summary>
    /// 앱 자체 항목의 첫 ID. <b>shell 범위를 넘어야 한다</b> (함정 3) — 겹치면 사용자가 고른
    /// 우리 항목을 shell 이 자기 verb 로 알아듣는다.
    /// </summary>
    private const uint AppVerbBase = MaxVerbId + 1;

    private const uint MfString = 0x00000000;

    private const uint MfSeparator = 0x00000800;

    private const uint MfByPosition = 0x00000400;

    private const uint CmfNormal = 0x00000000;

    private const uint WmNull = 0x0000;

    private const uint WmMeasureItem = 0x002C;

    private const uint WmDrawItem = 0x002B;

    private const uint WmInitMenuPopup = 0x0117;

    private const uint WmMenuChar = 0x0120;

    private const uint TpmRightButton = 0x0002;

    private const uint TpmReturnCmd = 0x0100;

    private const uint CmicMaskPtInvoke = 0x20000000;

    private const int SwShowNormal = 1;

    private const uint WsPopup = 0x80000000;

    private const uint WsExToolWindow = 0x00000080;

    private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
    }

    /// <summary>
    /// <c>CMINVOKECOMMANDINFOEX</c>. 필드 순서가 계약이다 — 쓰지 않는 것도 자리를 지켜야
    /// 하고, <c>Size</c> 가 shell 에게 EX 형태임을 알린다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfoEx
    {
        public uint Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;

        /// <summary>함정 4 — ID 로 부를 때 이 셋을 채우지 않는다. 채우면 확장이 크래시한다.</summary>
        public nint VerbW;

        public nint ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public int InvokeX;
        public int InvokeY;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetDesktopFolder(out nint folder);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint memory);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    /// <summary>
    /// 앱 항목을 넣는다. <c>InsertMenuW</c> 다 — 항목 이름이 한글이라 ANSI 판을 쓰면 깨진다.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "InsertMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InsertMenu(nint menu, uint position, uint flags, nuint id, string? item);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassW(in WindowClass windowClass);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(
        uint exStyle,
        nint className,
        nint windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint owner,
        nint menu,
        nint instance,
        nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    // [ComImport] 를 쓰는 이유는 ShellFileOperations 와 같다 — [GeneratedComInterface] 는
    // 어셈블리 전체에 DisableRuntimeMarshalling 을 요구해 다른 interop 까지 묶는다.
    // 부르지 않는 vtable 슬롯도 순서대로 선언해야 한다. COM 호출은 이름이 아니라 순서로 간다.
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            nint window,
            nint bindContext,
            [MarshalAs(UnmanagedType.LPWStr)] string displayName,
            nint eaten,
            out nint pidl,
            // ULONG* 다. uint 로 두면 x64 에서 0 이 우연히 통할 뿐 값을 줄 수는 없다.
            nint attributes);

        [PreserveSig]
        int EnumObjects(nint window, uint flags, out nint enumerator);

        [PreserveSig]
        int BindToObject(nint pidl, nint bindContext, in Guid iid, out nint instance);

        [PreserveSig]
        int BindToStorage(nint pidl, nint bindContext, in Guid iid, out nint instance);

        [PreserveSig]
        int CompareIDs(nint parameters, nint left, nint right);

        [PreserveSig]
        int CreateViewObject(nint window, in Guid iid, out nint instance);

        // [MarshalAs(LPArray)] 가 빠지면 안 된다. COM 인터페이스에서 배열 파라미터의 기본
        // 마샬링은 SafeArray 다 (P/Invoke 는 LPArray) — shell 은 PIDL 포인터 배열을
        // 기대하므로 SAFEARRAY 를 받아 그대로 역참조하고 프로세스가 죽는다.
        // B-4 실물에서 우클릭 첫 시도가 GetUIObjectOf 안에서 AV 로 끝났다.
        [PreserveSig]
        int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] nint[] pidls, ref uint attributes);

        [PreserveSig]
        int GetUIObjectOf(
            nint window,
            uint count,
            [MarshalAs(UnmanagedType.LPArray)] nint[] pidls,
            in Guid iid,
            nint reserved,
            out nint instance);

        [PreserveSig]
        int GetDisplayNameOf(nint pidl, uint flags, nint name);

        [PreserveSig]
        int SetNameOf(nint window, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint renamed);
    }

    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(nint menu, uint indexMenu, uint idFirst, uint idLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref InvokeCommandInfoEx info);

        [PreserveSig]
        int GetCommandString(nint id, uint type, nint reserved, nint name, uint max);
    }

    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(nint menu, uint indexMenu, uint idFirst, uint idLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref InvokeCommandInfoEx info);

        [PreserveSig]
        int GetCommandString(nint id, uint type, nint reserved, nint name, uint max);

        [PreserveSig]
        int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }

    [ComImport]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(nint menu, uint indexMenu, uint idFirst, uint idLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref InvokeCommandInfoEx info);

        [PreserveSig]
        int GetCommandString(nint id, uint type, nint reserved, nint name, uint max);

        [PreserveSig]
        int HandleMenuMsg(uint message, nint wParam, nint lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }
}
