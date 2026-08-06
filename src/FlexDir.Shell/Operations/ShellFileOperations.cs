using System.Runtime.InteropServices;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Interop;

namespace FlexDir.Shell.Operations;

/// <summary>
/// 복사·이동·삭제·이름변경·폴더생성을 <c>IFileOperation</c> COM 에 넘기는 구현체.
/// <para>
/// <b>진행률·충돌·오류 UI 를 우리가 만들지 않는다.</b> shell 이 자기 대화상자로 처리한다 —
/// 전작은 <c>FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT</c> 로 전부 끄고 조용히
/// 실패했다 (docs/SHELL_NOTES.md §파일 조작). 그래서 이 클래스가 주는 플래그는
/// <b>삭제를 휴지통으로 못박는 것</b>과 <b>새 폴더 이름 충돌</b> 둘뿐이다.
/// </para>
/// <para>
/// <b>목록을 고치지 않는다.</b> 조작 결과로 아무것도 돌려주지 않으며(폴더 생성만 예외)
/// 화면 갱신은 <c>IFolderWatcher</c> 가 한다. 진실원천은 파일시스템이다 (CLAUDE.md §4).
/// </para>
/// <para>
/// <b>취소는 시작 전까지만 통한다.</b> shell 에 넘긴 뒤에는 <c>IFileOperation</c> 을 밖에서
/// 멈출 방법이 없다 — 그때부터는 shell 의 진행률 대화상자가 사용자의 취소 수단이고,
/// 그렇게 취소된 조작은 오류가 아니다.
/// </para>
/// </summary>
public sealed partial class ShellFileOperations : IFileOperations, IDisposable
{
    /// <summary>
    /// 페인이 둘이므로(docs/DESIGN.md §1) 양쪽에서 하나씩 걸 수 있어야 한다. 조작은
    /// 대용량 복사에서 분 단위로 이어지고 그 동안 반대편 페인이 막히면 안 된다
    /// (docs/UI_GUIDE.md §금지 목록).
    /// </summary>
    private const int Workers = 2;

    private readonly StaWorkQueue worker = new(Workers, "file operations");

    private readonly Func<Request, Outcome> execute;

    /// <summary>
    /// shell 의 진행률·충돌·오류 대화상자가 뜰 소유 창. <b>Core 포트에는 창 핸들이 없으므로</b>
    /// (사용자 결정 2026-08-06 · <c>IContextMenuProvider</c> 와 같은 결정) <c>FlexDir.Host</c>
    /// 가 조립할 때 물려 준다. 없으면 0 — 그때 대화상자는 소유 창 없이 뜨고 작업표시줄에
    /// 항목이 하나 더 생긴다.
    /// </summary>
    private readonly Func<nint> ownerWindow;

    public ShellFileOperations()
        : this(static () => 0)
    {
    }

    /// <param name="owner">
    /// 대화상자의 소유 창을 내는 공급자. <b>부를 때마다 묻는다</b> — 창은 조작보다 늦게
    /// 생기고 완전 종료 뒤 재실행은 새 창이다 (ADR-003).
    /// </param>
    public ShellFileOperations(Func<nint> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        ownerWindow = owner;
        execute = Execute;
    }

    /// <summary>
    /// 실행 지점을 바꿔 끼운다. 실물은 파일을 실제로 옮기고 지우며 실패하면 shell 이
    /// 대화상자로 답을 기다린다 — 자동 테스트가 낼 수 있는 결과가 아니다. 바꿔 끼운
    /// 자리 위에서 재는 것은 <b>무엇을 어떤 플래그로 넘기는가</b> 와
    /// <b>shell 이 낸 HRESULT 를 어떻게 옮기는가</b> 다.
    /// </summary>
    internal ShellFileOperations(Func<Request, Outcome> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        ownerWindow = static () => 0;
        this.execute = execute;
    }

    /// <summary>shell 에 넘길 조작 한 건. 계획과 COM 호출을 가르는 자리다.</summary>
    internal enum OperationKind
    {
        Copy,
        Move,
        Delete,
        Rename,
        NewFolder,
    }

    /// <param name="Destination">복사·이동의 대상 폴더, 폴더 생성의 부모. 그 밖에는 <c>null</c>.</param>
    /// <param name="Name">이름변경의 새 이름, 폴더 생성의 이름. 그 밖에는 <c>null</c>.</param>
    internal sealed record Request(
        OperationKind Kind,
        IReadOnlyList<LocationId> Items,
        LocationId? Destination,
        string? Name,
        uint Flags);

    /// <param name="HResult">shell 이 낸 결과. 음수면 실패다.</param>
    /// <param name="Created">폴더 생성이 실제로 만든 위치. 그 밖에는 <c>null</c>.</param>
    internal sealed record Outcome(int HResult, LocationId? Created);

    public Task CopyAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinationFolder);

        return sources.Count == 0
            ? Task.CompletedTask
            : Schedule(
                new Request(OperationKind.Copy, sources, destinationFolder, null, NoFlags),
                destinationFolder,
                ct);
    }

    public Task MoveAsync(IReadOnlyList<LocationId> sources, LocationId destinationFolder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinationFolder);

        return sources.Count == 0
            ? Task.CompletedTask
            : Schedule(
                new Request(OperationKind.Move, sources, destinationFolder, null, NoFlags),
                destinationFolder,
                ct);
    }

    public Task DeleteAsync(IReadOnlyList<LocationId> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Count == 0
            ? Task.CompletedTask
            : Schedule(
                new Request(OperationKind.Delete, items, null, null, DeleteFlags),
                items[0],
                ct);
    }

    public Task RenameAsync(LocationId item, string newName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        EnsureChildName(item, newName);

        return Schedule(
            new Request(OperationKind.Rename, [item], null, newName, NoFlags),
            item,
            ct);
    }

    public Task<LocationId> CreateFolderAsync(LocationId parentFolder, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parentFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // 이름이 겹치지 않으면 이것이 그대로 결과다. 겹치면 shell 이 다른 이름을 만들고
        // 그것을 sink 가 되받는다.
        var requested = parentFolder.Combine(name);

        ct.ThrowIfCancellationRequested();

        var request = new Request(OperationKind.NewFolder, [], parentFolder, name, NewFolderFlags);

        return worker.RunAsync(() => Perform(request, parentFolder) ?? requested, ct);
    }

    public void Dispose() => worker.Dispose();

    private Task Schedule(Request request, LocationId blame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return worker.RunAsync(() => Perform(request, blame), ct);
    }

    /// <summary>
    /// STA 워커 안에서 shell 을 부르고 결과를 포트의 오류로 옮긴다.
    /// </summary>
    /// <param name="blame">
    /// 실패를 어느 위치의 문제로 적을지. 복사·이동은 <b>대상 폴더</b>(쓸 수 없는 자리는
    /// 대개 그쪽이다), 삭제·이름변경은 항목, 폴더 생성은 부모다.
    /// </param>
    private LocationId? Perform(Request request, LocationId blame)
    {
        var outcome = execute(request);

        if (outcome.HResult >= 0)
        {
            return outcome.Created;
        }

        // 사용자가 shell 대화상자에서 취소한 것은 실패가 아니다. 오류로 만들면 방금
        // 스스로 한 선택을 상태표시줄에서 오류로 통보받는다.
        if (outcome.HResult is CancelledHResult or CopyEngineUserCancelled)
        {
            return outcome.Created;
        }

        throw Translate(outcome.HResult, blame);
    }

    /// <summary>
    /// 하위 이름으로 쓸 수 있는지 본다. 규칙의 정본은 <see cref="LocationId.Combine"/> 이므로
    /// 여기서 다시 쓰지 않는다 — 만들어진 값은 버린다.
    /// <para>
    /// 이름에 구분자가 섞이면 shell 은 이름 변경이 아니라 <b>다른 폴더로의 이동</b>을 한다.
    /// </para>
    /// </summary>
    private static void EnsureChildName(LocationId reference, string name) => reference.Combine(name);

    /// <summary>
    /// HRESULT 를 포트의 오류로 옮긴다.
    /// <para>
    /// <b>마스크를 <c>int</c> 로 못박는다.</b> <c>0xFFFF0000</c> 은 uint 리터럴이라 그대로
    /// 쓰면 양쪽이 long 으로 승격되고 음수 HRESULT 가 부호 확장돼 비교가 영원히 거짓이
    /// 된다 — 코드가 조용히 0 으로 남아 진단이 사라진다
    /// (<c>FileSystemFolderSource.Translate</c> 에서 실물을 돌려보고서야 드러났다).
    /// </para>
    /// </summary>
    private static LocationAccessException Translate(int hresult, LocationId location)
    {
        const int FacilityWin32 = unchecked((int)0x80070000);
        const int FacilityMask = unchecked((int)0xFFFF0000);

        // Win32 facility 가 아닌 HRESULT 에서 하위 16비트를 꺼내면 없는 코드를 지어낸다.
        var win32 = (hresult & FacilityMask) == FacilityWin32 ? hresult & 0xFFFF : 0;

        var kind = Win32ErrorMapping.Classify(win32);

        // None 을 그대로 넘기면 LocationAccessException 이 ArgumentException 을 내고
        // 진짜 원인이 사라진다.
        if (kind == LocationErrorKind.None)
        {
            kind = LocationErrorKind.Unknown;
        }

        return new LocationAccessException(kind, location, win32);
    }

    // ── 실제 COM 호출 ───────────────────────────────────────────────

    /// <summary>
    /// <b>COM 객체가 이 메서드 밖으로 나가지 않는다.</b> 여기서 만들고 여기서 놓는다
    /// (docs/SHELL_NOTES.md §아이콘 함정 3 과 같은 규칙). 나가는 것은
    /// <see cref="Outcome"/> — HRESULT 와 값 타입 위치뿐이다.
    /// </summary>
    private Outcome Execute(Request request)
    {
        var clsid = FileOperationClsid;
        var iid = typeof(IFileOperation).GUID;

        var hr = CoCreateInstance(in clsid, 0, ClsCtxInprocServer, in iid, out var raw);

        if (hr < 0 || raw == 0)
        {
            return new Outcome(hr < 0 ? hr : Unexpected, null);
        }

        using var operation = new ComRef<IFileOperation>(raw);

        var shell = operation.Instance;

        hr = shell.SetOperationFlags(request.Flags);

        if (hr < 0)
        {
            return new Outcome(hr, null);
        }

        // 소유 창을 주지 않으면 진행률·충돌 대화상자가 별도 작업표시줄 항목으로 뜨고 창
        // 뒤로 숨는다. 실패해도 조작을 접지 않는다 — 대화상자의 부모일 뿐이다.
        shell.SetOwnerWindow(ownerWindow());

        var opened = new List<ComRef<IShellItem>>();

        try
        {
            // 항목을 여는 단계에서 없는 경로·권한이 걸린다. shell 에 넘기기 전이므로
            // 오류 대화상자가 뜨지 않고 HRESULT 로만 온다.
            IShellItem? Open(LocationId location)
            {
                var itemIid = typeof(IShellItem).GUID;

                // shell 파서는 \\?\ 확장 접두사를 모른다. DisplayPath 를 준다.
                var opening = SHCreateItemFromParsingName(location.DisplayPath, 0, in itemIid, out var itemRaw);

                if (opening < 0 || itemRaw == 0)
                {
                    hr = opening < 0 ? opening : Unexpected;

                    return null;
                }

                var reference = new ComRef<IShellItem>(itemRaw);
                opened.Add(reference);

                return reference.Instance;
            }

            NewItemSink? sink = null;

            switch (request.Kind)
            {
                case OperationKind.Copy:
                case OperationKind.Move:
                {
                    if (Open(request.Destination!) is not { } destination)
                    {
                        return new Outcome(hr, null);
                    }

                    foreach (var source in request.Items)
                    {
                        if (Open(source) is not { } item)
                        {
                            return new Outcome(hr, null);
                        }

                        var queued = request.Kind == OperationKind.Copy
                            ? shell.CopyItem(item, destination, null, null)
                            : shell.MoveItem(item, destination, null, null);

                        if (queued < 0)
                        {
                            return new Outcome(queued, null);
                        }
                    }

                    break;
                }

                case OperationKind.Delete:
                {
                    foreach (var target in request.Items)
                    {
                        if (Open(target) is not { } item)
                        {
                            return new Outcome(hr, null);
                        }

                        var queued = shell.DeleteItem(item, null);

                        if (queued < 0)
                        {
                            return new Outcome(queued, null);
                        }
                    }

                    break;
                }

                case OperationKind.Rename:
                {
                    if (Open(request.Items[0]) is not { } item)
                    {
                        return new Outcome(hr, null);
                    }

                    var queued = shell.RenameItem(item, request.Name!, null);

                    if (queued < 0)
                    {
                        return new Outcome(queued, null);
                    }

                    break;
                }

                case OperationKind.NewFolder:
                {
                    if (Open(request.Destination!) is not { } parent)
                    {
                        return new Outcome(hr, null);
                    }

                    // 만들어진 이름은 shell 만 안다 — 이름이 겹치면 다른 이름이 된다.
                    sink = new NewItemSink();

                    var queued = shell.NewItem(parent, FileAttributeDirectory, request.Name!, null, sink);

                    if (queued < 0)
                    {
                        return new Outcome(queued, null);
                    }

                    break;
                }

                default:
                    return new Outcome(Unexpected, null);
            }

            var performed = shell.PerformOperations();

            if (sink is null)
            {
                // 항목별 실패는 shell 이 자기 대화상자로 이미 알렸다. PerformOperations 가
                // 성공을 내면 여기서 다시 말하지 않는다 (docs/UI_GUIDE.md §상태 표현).
                return new Outcome(performed, null);
            }

            if (sink.Created is { } created)
            {
                return new Outcome(0, created);
            }

            // 만들어지지 않았다. sink 가 받은 코드가 진짜 원인이고, PerformOperations 는
            // 항목이 실패해도 S_OK 를 낼 수 있다.
            return new Outcome(
                sink.Result < 0 ? sink.Result : performed < 0 ? performed : Unexpected,
                null);
        }
        finally
        {
            foreach (var reference in opened)
            {
                reference.Dispose();
            }
        }
    }

    private static bool TryGetPath(IShellItem item, out string path)
    {
        path = string.Empty;

        if (item.GetDisplayName(SigdnFileSysPath, out var raw) < 0 || raw == 0)
        {
            return false;
        }

        try
        {
            path = Marshal.PtrToStringUni(raw) ?? string.Empty;
        }
        finally
        {
            // shell 이 CoTaskMemAlloc 으로 준 것이다. 받는 쪽이 놓는다.
            Marshal.FreeCoTaskMem(raw);
        }

        return path.Length > 0;
    }

    /// <summary>
    /// RCW 와 원시 포인터를 함께 들고 둘 다 놓는다.
    /// <c>Marshal.GetObjectForIUnknown</c> 이 자기 참조를 따로 잡으므로
    /// <c>ReleaseComObject</c> 와 <c>Marshal.Release</c> 를 <b>둘 다</b> 해야 한다
    /// (<c>ShellThumbnailSource</c> 와 같은 규칙).
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

    /// <summary>
    /// 폴더 생성의 결과만 받는 콜백. shell 이 <b>실제로 만든 항목</b>을 여기로 알려준다 —
    /// 이름이 겹치면 요청한 이름과 다르다.
    /// <para>
    /// <c>IShellItem</c> 인자를 대부분 <c>nint</c> 로 받는 이유: 쓰지 않는 참조를 RCW 로
    /// 감싸면 그만큼 shell 객체가 GC 를 기다리며 살아 있는다. 유일하게 쓰는
    /// <see cref="PostNewItem"/> 의 마지막 인자만 인터페이스로 받는다.
    /// </para>
    /// </summary>
    private sealed class NewItemSink : IFileOperationProgressSink
    {
        public LocationId? Created { get; private set; }

        /// <summary>shell 이 낸 생성 결과. 0 이면 성공이다.</summary>
        public int Result { get; private set; }

        public int PostNewItem(
            uint flags,
            nint destinationFolder,
            string? newName,
            string? templateName,
            uint fileAttributes,
            int result,
            IShellItem? newItem)
        {
            Result = result;

            // 이름이 아니라 경로를 묻는다. newName 은 요청한 이름일 수 있다.
            if (newItem is not null
                && TryGetPath(newItem, out var path)
                && LocationId.TryParse(path, out var location, out _))
            {
                Created = location;
            }

            return Ok;
        }

        public int StartOperations() => Ok;

        public int FinishOperations(int result) => Ok;

        public int PreRenameItem(uint flags, nint item, string? newName) => Ok;

        public int PostRenameItem(uint flags, nint item, string? newName, int result, nint newlyCreated) => Ok;

        public int PreMoveItem(uint flags, nint item, nint destinationFolder, string? newName) => Ok;

        public int PostMoveItem(
            uint flags,
            nint item,
            nint destinationFolder,
            string? newName,
            int result,
            nint newlyCreated) => Ok;

        public int PreCopyItem(uint flags, nint item, nint destinationFolder, string? newName) => Ok;

        public int PostCopyItem(
            uint flags,
            nint item,
            nint destinationFolder,
            string? newName,
            int result,
            nint newlyCreated) => Ok;

        public int PreDeleteItem(uint flags, nint item) => Ok;

        public int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated) => Ok;

        public int PreNewItem(uint flags, nint destinationFolder, string? newName) => Ok;

        public int UpdateProgress(uint total, uint soFar) => Ok;

        public int ResetTimer() => Ok;

        public int PauseTimer() => Ok;

        public int ResumeTimer() => Ok;
    }

    // ── 상수 ────────────────────────────────────────────────────────

    private const int Ok = 0;
    private const int Unexpected = unchecked((int)0x8000FFFF);

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED).</summary>
    private const int CancelledHResult = unchecked((int)0x800704C7);

    /// <summary>COPYENGINE_E_USER_CANCELLED — 진행률 대화상자에서 취소했다.</summary>
    private const int CopyEngineUserCancelled = unchecked((int)0x80270000);

    private const uint NoFlags = 0;

    /// <summary>
    /// <c>FOF_ALLOWUNDO</c> 만으로는 부족하다. 드라이브의 휴지통 할당량을 넘으면 Windows 가
    /// <b>말없이 영구 삭제로 바꾼다</b> — <c>FOFX_RECYCLEONDELETE</c> 가 그것을 막는다
    /// (docs/SHELL_NOTES.md §파일 조작). 되돌릴 수 없는 조작은 v1 에 없다.
    /// </summary>
    private const uint DeleteFlags = FofAllowUndo | FofxRecycleOnDelete;

    /// <summary>이름이 겹쳐도 실패하지 않고 유일한 이름을 만든다.</summary>
    private const uint NewFolderFlags = FofRenameOnCollision;

    private const uint FofRenameOnCollision = 0x0008;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofxRecycleOnDelete = 0x00080000;

    private const uint FileAttributeDirectory = 0x00000010;
    private const uint ClsCtxInprocServer = 0x00000001;

    /// <summary><c>SIGDN_FILESYSPATH</c> — 파일시스템 경로. 표시 이름이 아니다.</summary>
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary><c>CLSID_FileOperation</c>.</summary>
    private static readonly Guid FileOperationClsid = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    // ── COM ─────────────────────────────────────────────────────────
    // [ComImport] 를 쓰는 이유는 ShellThumbnailSource 와 같다 — [GeneratedComInterface] 는
    // 어셈블리 전체에 DisableRuntimeMarshalling 을 요구하고, 그러면 런타임 마샬링에 기대는
    // 다른 interop 까지 함께 고쳐야 한다.
    //
    // <b>부르지 않는 vtable 슬롯도 순서대로 선언해야 한다.</b> COM 호출은 이름이 아니라
    // 순서로 간다 — 인자 없이 선언된 메서드들이 그 자리를 지킨다.

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise();

        void Unadvise();

        [PreserveSig]
        int SetOperationFlags(uint flags);

        void SetProgressMessage();

        void SetProgressDialog();

        void SetProperties();

        [PreserveSig]
        int SetOwnerWindow(nint owner);

        void ApplyPropertiesToItem();

        void ApplyPropertiesToItems();

        [PreserveSig]
        int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);

        void RenameItems();

        [PreserveSig]
        int MoveItem(
            IShellItem item,
            IShellItem destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            IFileOperationProgressSink? sink);

        void MoveItems();

        [PreserveSig]
        int CopyItem(
            IShellItem item,
            IShellItem destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? copyName,
            IFileOperationProgressSink? sink);

        void CopyItems();

        [PreserveSig]
        int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);

        void DeleteItems();

        [PreserveSig]
        int NewItem(
            IShellItem destinationFolder,
            uint fileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? templateName,
            IFileOperationProgressSink? sink);

        [PreserveSig]
        int PerformOperations();

        void GetAnyOperationsAborted();
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();

        void GetParent();

        [PreserveSig]
        int GetDisplayName(uint kind, out nint name);

        void GetAttributes();

        void Compare();
    }

    /// <summary>
    /// shell 이 <b>우리를</b> 부르는 인터페이스다. 그래서 여기서는 모든 메서드를 실제로
    /// 구현해야 한다 — 자리만 채운 선언으로는 CCW 가 만들어지지 않는다.
    /// </summary>
    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig]
        int StartOperations();

        [PreserveSig]
        int FinishOperations(int result);

        [PreserveSig]
        int PreRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig]
        int PostRenameItem(
            uint flags,
            nint item,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            int result,
            nint newlyCreated);

        [PreserveSig]
        int PreMoveItem(
            uint flags,
            nint item,
            nint destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig]
        int PostMoveItem(
            uint flags,
            nint item,
            nint destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            int result,
            nint newlyCreated);

        [PreserveSig]
        int PreCopyItem(
            uint flags,
            nint item,
            nint destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig]
        int PostCopyItem(
            uint flags,
            nint item,
            nint destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            int result,
            nint newlyCreated);

        [PreserveSig]
        int PreDeleteItem(uint flags, nint item);

        [PreserveSig]
        int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated);

        [PreserveSig]
        int PreNewItem(uint flags, nint destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig]
        int PostNewItem(
            uint flags,
            nint destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            [MarshalAs(UnmanagedType.LPWStr)] string? templateName,
            uint fileAttributes,
            int result,
            IShellItem? newItem);

        [PreserveSig]
        int UpdateProgress(uint total, uint soFar);

        [PreserveSig]
        int ResetTimer();

        [PreserveSig]
        int PauseTimer();

        [PreserveSig]
        int ResumeTimer();
    }

    // ── P/Invoke ────────────────────────────────────────────────────

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid clsid,
        nint outer,
        uint context,
        in Guid iid,
        out nint instance);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid iid,
        out nint item);
}
