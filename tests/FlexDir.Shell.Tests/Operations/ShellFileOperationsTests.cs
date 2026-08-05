using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Operations;
using FlexDir.Shell.Operations;

using Xunit;

namespace FlexDir.Shell.Tests.Operations;

/// <summary>
/// <c>IFileOperation</c> COM 으로 복사·이동·삭제·이름변경·폴더생성을 하는 구현체.
/// <para>
/// <b>두 층으로 나눠 잰다.</b> 위층은 "무엇을 어떤 플래그로 shell 에 넘기는가" 이고
/// 실행 지점을 바꿔 끼워 shell 없이 잰다 — 특히 <b>삭제 플래그</b>가 그렇다. 실물로
/// 재려면 파일이 실제로 휴지통에 들어가야 하고, 게이트가 돌 때마다 사용자의 휴지통에
/// 쓰레기가 쌓이는 것은 자동 테스트가 낼 부작용이 아니다.
/// </para>
/// <para>
/// 아래층은 COM 배관이며 <c>%TEMP%</c> 아래에서 <b>실제로</b> 돌린다 — 복사·이동·
/// 이름변경·폴더생성은 되돌릴 수 있고 대화상자를 부르지 않는다. 삭제만 빠진다
/// (<c>.harness/probe recycle</c> 로 사람이 확인한다).
/// </para>
/// <para>
/// 실패 경로를 실물로 재지 않는 이유: shell 은 실패를 자기 오류 대화상자로 알리고
/// 그것은 답을 기다리며 블로킹한다 — <c>--blame-hang</c> 에 걸린다. 다만 <b>항목을
/// 여는 단계</b>(<c>SHCreateItemFromParsingName</c>)는 UI 없이 HRESULT 만 내므로
/// 없는 원본 하나는 실물로 잰다.
/// </para>
/// </summary>
public sealed class ShellFileOperationsTests : IDisposable
{
    // FOF_* — docs/SHELL_NOTES.md §파일 조작
    private const uint FofSilent = 0x0004;
    private const uint FofRenameOnCollision = 0x0008;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;

    // HRESULT_FROM_WIN32(x) = 0x80070000 | x
    private const int NotFoundHResult = unchecked((int)0x80070002);
    private const int PathNotFoundHResult = unchecked((int)0x80070003);
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int NotReadyHResult = unchecked((int)0x80070015);
    private const int SharingHResult = unchecked((int)0x80070020);
    private const int CancelledHResult = unchecked((int)0x800704C7);
    private const int CopyEngineUserCancelled = unchecked((int)0x80270000);
    private const int GenericFailure = unchecked((int)0x80004005);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "flex-dir-tests", Guid.NewGuid().ToString("N"));

    public ShellFileOperationsTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // shell 이 아직 폴더를 쥐고 있을 수 있다. 임시 폴더라 남아도 무해하다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── 아파트먼트 ──────────────────────────────────────────────────
    // IFileOperation 은 STA 를 요구한다 (docs/SHELL_NOTES.md §COM 아파트먼트).
    // 스레드풀은 MTA 이므로 Task.Run 으로 감싸면 규칙 위반이다.

    [Fact]
    public async Task Copy_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var operations = Recording(_ => apartment = Thread.CurrentThread.GetApartmentState());

        await operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task Move_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var operations = Recording(_ => apartment = Thread.CurrentThread.GetApartmentState());

        await operations.MoveAsync([Location("a.txt")], Location("대상"), CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task Delete_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var operations = Recording(_ => apartment = Thread.CurrentThread.GetApartmentState());

        await operations.DeleteAsync([Location("a.txt")], CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task Rename_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var operations = Recording(_ => apartment = Thread.CurrentThread.GetApartmentState());

        await operations.RenameAsync(Location("a.txt"), "b.txt", CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task CreateFolder_RunsOnAnStaThread()
    {
        var apartment = ApartmentState.Unknown;

        using var operations = Recording(_ => apartment = Thread.CurrentThread.GetApartmentState());

        await operations.CreateFolderAsync(Location("부모"), "새 폴더", CancellationToken.None);

        Assert.Equal(ApartmentState.STA, apartment);
    }

    // ── 무엇을 넘기는가 ─────────────────────────────────────────────

    [Fact]
    public async Task Copy_HandsSourcesAndDestinationToTheShell()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        var sources = new[] { Location("a.txt"), Location("b.txt") };
        var destination = Location("대상");

        await operations.CopyAsync(sources, destination, CancellationToken.None);

        var request = Assert.Single(requests);

        Assert.Equal(ShellFileOperations.OperationKind.Copy, request.Kind);
        Assert.Equal(sources, request.Items);
        Assert.Equal(destination, request.Destination);
    }

    [Fact]
    public async Task Move_HandsSourcesAndDestinationToTheShell()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        var sources = new[] { Location("a.txt") };
        var destination = Location("대상");

        await operations.MoveAsync(sources, destination, CancellationToken.None);

        var request = Assert.Single(requests);

        Assert.Equal(ShellFileOperations.OperationKind.Move, request.Kind);
        Assert.Equal(sources, request.Items);
        Assert.Equal(destination, request.Destination);
    }

    // 한 번에 넘긴다. 항목마다 조작을 만들면 shell 이 진행률·충돌 대화상자를 항목 수만큼
    // 띄운다 — 사용자가 같은 질문에 여러 번 답하게 된다.
    [Fact]
    public async Task Delete_HandsEveryItemToOneOperation()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        var items = new[] { Location("a.txt"), Location("b.txt"), Location("c.txt") };

        await operations.DeleteAsync(items, CancellationToken.None);

        var request = Assert.Single(requests);

        Assert.Equal(ShellFileOperations.OperationKind.Delete, request.Kind);
        Assert.Equal(items, request.Items);
    }

    [Fact]
    public async Task Rename_HandsTheItemAndTheNewName()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        var item = Location("옛이름.txt");

        await operations.RenameAsync(item, "새이름.txt", CancellationToken.None);

        var request = Assert.Single(requests);

        Assert.Equal(ShellFileOperations.OperationKind.Rename, request.Kind);
        Assert.Equal(item, Assert.Single(request.Items));
        Assert.Equal("새이름.txt", request.Name);
    }

    [Fact]
    public async Task CreateFolder_HandsTheParentAndTheName()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        var parent = Location("부모");

        await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None);

        var request = Assert.Single(requests);

        Assert.Equal(ShellFileOperations.OperationKind.NewFolder, request.Kind);
        Assert.Equal(parent, request.Destination);
        Assert.Equal("새 폴더", request.Name);
    }

    // ── 플래그 ──────────────────────────────────────────────────────

    // <b>이 테스트가 되돌릴 수 없는 사고를 막는다.</b> FOF_ALLOWUNDO 만으로는 부족하다 —
    // 드라이브의 휴지통 할당량을 넘으면 Windows 가 말없이 영구 삭제로 바꾼다.
    // FOFX_RECYCLEONDELETE 가 그것을 강제한다 (docs/SHELL_NOTES.md §파일 조작).
    [Fact]
    public async Task Delete_ForcesTheRecycleBin()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        await operations.DeleteAsync([Location("a.txt")], CancellationToken.None);

        var flags = Assert.Single(requests).Flags;

        Assert.Equal(FofAllowUndo, flags & FofAllowUndo);
        Assert.Equal(FofxRecycleOnDelete, flags & FofxRecycleOnDelete);
    }

    // 전작은 FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT 로 모든 대화상자를 끄고
    // 조용히 실패했다. flex-dir 는 반대로 간다 — 충돌·오류는 shell 것을 그대로 쓴다
    // (docs/SHELL_NOTES.md §파일 조작 · docs/PRD.md §4).
    [Theory]
    [InlineData(nameof(IFileOperations.CopyAsync))]
    [InlineData(nameof(IFileOperations.MoveAsync))]
    [InlineData(nameof(IFileOperations.DeleteAsync))]
    [InlineData(nameof(IFileOperations.RenameAsync))]
    [InlineData(nameof(IFileOperations.CreateFolderAsync))]
    public async Task NoOperation_SilencesTheShell(string operation)
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        await InvokeAsync(operations, operation);

        var flags = Assert.Single(requests).Flags;

        Assert.Equal(0u, flags & FofSilent);
        Assert.Equal(0u, flags & FofNoConfirmation);
        Assert.Equal(0u, flags & FofNoErrorUi);
    }

    // 이름이 겹쳐도 실패하지 않고 유일한 이름을 만든다 — 포트가 "돌아온 위치를 보라" 고
    // 적은 것이 이 플래그를 전제한다.
    [Fact]
    public async Task CreateFolder_AsksTheShellToRenameOnCollision()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        await operations.CreateFolderAsync(Location("부모"), "새 폴더", CancellationToken.None);

        var flags = Assert.Single(requests).Flags;

        Assert.Equal(FofRenameOnCollision, flags & FofRenameOnCollision);
    }

    // 복사·이동에는 실행취소를 붙이지 않는다 — v1 에 실행취소 명령이 없고, 붙이면
    // 탐색기의 Ctrl+Z 가 우리가 한 일을 되돌리게 된다.
    [Fact]
    public async Task Copy_UsesNoExtraFlags()
    {
        var requests = new List<ShellFileOperations.Request>();

        using var operations = Recording(requests.Add);

        await operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None);

        Assert.Equal(0u, Assert.Single(requests).Flags);
    }

    // ── 만들어진 폴더 ───────────────────────────────────────────────

    // 이름이 겹치면 shell 이 다른 이름으로 만든다. 요청한 이름을 그대로 돌려주면
    // 호출자가 없는 항목의 이름 편집을 연다 (PaneViewModel.CreateFolderAsync).
    [Fact]
    public async Task CreateFolder_ReturnsWhatTheShellActuallyCreated()
    {
        var parent = Location("부모");
        var created = parent.Combine("새 폴더 (2)");

        using var operations = new ShellFileOperations(_ => new ShellFileOperations.Outcome(0, created));

        var result = await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None);

        Assert.Equal(created, result);
    }

    // 겹치지 않으면 shell 이 이름을 바꿀 이유가 없다. 보고가 없는 성공은 그 경우다.
    [Fact]
    public async Task CreateFolder_WithoutAReport_FallsBackToTheRequestedName()
    {
        var parent = Location("부모");

        using var operations = new ShellFileOperations(_ => new ShellFileOperations.Outcome(0, null));

        var result = await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None);

        Assert.Equal(parent.Combine("새 폴더"), result);
    }

    // ── 실패 ────────────────────────────────────────────────────────

    // HRESULT 에서 Win32 코드를 꺼내는 자리다. 마스크를 int 로 못박지 않으면 음수
    // HResult 가 부호 확장돼 비교가 영원히 거짓이 되고, 코드가 조용히 0 으로 남는다
    // (FileSystemFolderSource.Translate 에서 실물을 돌려보고서야 드러났던 결함).
    [Theory]
    [InlineData(NotFoundHResult, LocationErrorKind.NotFound, 2)]
    [InlineData(PathNotFoundHResult, LocationErrorKind.NotFound, 3)]
    [InlineData(AccessDeniedHResult, LocationErrorKind.AccessDenied, 5)]
    [InlineData(NotReadyHResult, LocationErrorKind.DeviceNotReady, 21)]
    [InlineData(SharingHResult, LocationErrorKind.Sharing, 32)]
    public async Task Failure_CarriesTheWin32Code(int hresult, LocationErrorKind kind, int win32)
    {
        using var operations = Failing(hresult);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None));

        Assert.Equal(kind, error.Kind);
        Assert.Equal(win32, error.Win32Error);
    }

    // Win32 facility 가 아닌 HRESULT 에서 하위 16비트를 꺼내면 없는 코드를 지어낸다.
    // E_FAIL 의 0x4005 는 Win32 오류 16389 가 아니다.
    [Fact]
    public async Task FailureOutsideWin32Facility_HasNoCode()
    {
        using var operations = Failing(GenericFailure);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None));

        Assert.Equal(LocationErrorKind.Unknown, error.Kind);
        Assert.Equal(0, error.Win32Error);
    }

    // 사용자가 shell 의 진행률 대화상자에서 취소한 것은 오류가 아니다. 상태표시줄에
    // 오류를 적으면 방금 스스로 한 선택을 오류로 통보받는다.
    [Theory]
    [InlineData(CancelledHResult)]
    [InlineData(CopyEngineUserCancelled)]
    public async Task UserCancellation_IsNotAnError(int hresult)
    {
        using var operations = Failing(hresult);

        await operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None);
    }

    [Fact]
    public async Task Failure_CarriesTheItemItFailedOn()
    {
        var item = Location("a.txt");

        using var operations = Failing(AccessDeniedHResult);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await operations.DeleteAsync([item], CancellationToken.None));

        Assert.Equal(item, error.Location);
    }

    // 폴더 생성 실패는 대상 폴더를 가리켜야 한다 — 만들려던 항목은 아직 없다.
    [Fact]
    public async Task CreateFolderFailure_CarriesTheParent()
    {
        var parent = Location("부모");

        using var operations = Failing(AccessDeniedHResult);

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await operations.CreateFolderAsync(parent, "새 폴더", CancellationToken.None));

        Assert.Equal(parent, error.Location);
    }

    // ── 빈 요청·취소·방어 ──────────────────────────────────────────

    // 선택이 비었는데 shell 을 부르면 빈 진행률 대화상자가 뜬다. CanExecute 로 막지만
    // 그것과 실행 사이에 감시 갱신이 끼어들 수 있다 (PaneViewModel).
    [Fact]
    public async Task EmptySelection_DoesNotReachTheShell()
    {
        var calls = 0;

        using var operations = Recording(_ => calls++);

        await operations.CopyAsync([], Location("대상"), CancellationToken.None);
        await operations.MoveAsync([], Location("대상"), CancellationToken.None);
        await operations.DeleteAsync([], CancellationToken.None);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CanceledToken_DoesNotReachTheShell()
    {
        var calls = 0;

        using var operations = Recording(_ => calls++);

        var canceled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await operations.CopyAsync([Location("a.txt")], Location("대상"), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await operations.DeleteAsync([Location("a.txt")], canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await operations.CreateFolderAsync(Location("부모"), "새 폴더", canceled));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        using var operations = Recording(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await operations.CopyAsync(null!, Location("대상"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await operations.CopyAsync([Location("a.txt")], null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await operations.DeleteAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await operations.RenameAsync(null!, "b.txt", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await operations.CreateFolderAsync(null!, "새 폴더", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankName_Throws(string name)
    {
        using var operations = Recording(_ => { });

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await operations.RenameAsync(Location("a.txt"), name, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await operations.CreateFolderAsync(Location("부모"), name, CancellationToken.None));
    }

    // 이름에 구분자가 섞이면 shell 은 이름 변경이 아니라 <b>다른 폴더로의 이동</b>을 한다.
    // 사용자가 이름 칸에 경로를 붙여넣는 것은 흔한 일이다.
    [Theory]
    [InlineData(@"하위\b.txt")]
    [InlineData("하위/b.txt")]
    [InlineData("..")]
    [InlineData("b:txt")]
    public async Task NameThatIsNotAChildName_Throws(string name)
    {
        var calls = 0;

        using var operations = Recording(_ => calls++);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await operations.RenameAsync(Location("a.txt"), name, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await operations.CreateFolderAsync(Location("부모"), name, CancellationToken.None));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AfterDispose_Throws()
    {
        var operations = Recording(_ => { });

        operations.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await operations.DeleteAsync([Location("a.txt")], CancellationToken.None));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var operations = Recording(_ => { });

        operations.Dispose();
        operations.Dispose();
    }

    [Fact]
    public void ImplementsThePort()
    {
        using var operations = new ShellFileOperations();

        Assert.IsAssignableFrom<IFileOperations>(operations);
    }

    // ── COM 배관 (실물 shell · %TEMP% 안에서만) ────────────────────
    // 위의 테스트는 전부 실행 지점을 바꿔 끼운 것이라 vtable 이 한 칸 어긋나도 초록이다.
    // 아래가 그것을 잡는다. 삭제만 빠진다 — 휴지통은 사용자의 것이다.

    [Fact]
    public async Task Really_CreatesAFolder()
    {
        using var operations = new ShellFileOperations();

        var created = await operations.CreateFolderAsync(Root, "새 폴더", CancellationToken.None);

        Assert.True(Directory.Exists(created.DisplayPath), created.DisplayPath);
        Assert.Equal("새 폴더", created.Name);
    }

    // 여기가 progress sink 를 고정한다 — shell 이 만든 이름을 되받지 못하면 두 번째
    // 호출이 첫 번째와 같은 위치를 낸다.
    [Fact]
    public async Task Really_MakesAUniqueNameOnCollision()
    {
        using var operations = new ShellFileOperations();

        var first = await operations.CreateFolderAsync(Root, "새 폴더", CancellationToken.None);
        var second = await operations.CreateFolderAsync(Root, "새 폴더", CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first.DisplayPath), first.DisplayPath);
        Assert.True(Directory.Exists(second.DisplayPath), second.DisplayPath);
    }

    [Fact]
    public async Task Really_CopiesAFile()
    {
        var source = WriteFile("원본.txt");
        var destination = MakeFolder("대상");

        using var operations = new ShellFileOperations();

        await operations.CopyAsync([source], destination, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination.DisplayPath, "원본.txt")));
        Assert.True(File.Exists(source.DisplayPath));
    }

    [Fact]
    public async Task Really_MovesAFile()
    {
        var source = WriteFile("옮길것.txt");
        var destination = MakeFolder("대상");

        using var operations = new ShellFileOperations();

        await operations.MoveAsync([source], destination, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination.DisplayPath, "옮길것.txt")));
        Assert.False(File.Exists(source.DisplayPath));
    }

    [Fact]
    public async Task Really_RenamesAFile()
    {
        var source = WriteFile("옛이름.txt");

        using var operations = new ShellFileOperations();

        await operations.RenameAsync(source, "새이름.txt", CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(root, "새이름.txt")));
        Assert.False(File.Exists(source.DisplayPath));
    }

    // 항목을 여는 단계에서 걸린다 — shell 에 넘기기 전이라 대화상자가 뜨지 않는다.
    // 이것이 없으면 없는 원본이 shell 의 오류 대화상자로 가고 그것은 답을 기다린다.
    [Fact]
    public async Task Really_MissingSource_IsNotFound()
    {
        var destination = MakeFolder("대상");

        using var operations = new ShellFileOperations();

        var error = await Assert.ThrowsAsync<LocationAccessException>(
            async () => await operations.CopyAsync(
                [Parse(Path.Combine(root, "없는파일.txt"))],
                destination,
                CancellationToken.None));

        Assert.Equal(LocationErrorKind.NotFound, error.Kind);
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    /// <summary>요청을 기록만 하고 성공을 낸다.</summary>
    private static ShellFileOperations Recording(Action<ShellFileOperations.Request> record)
        => new(request =>
        {
            record(request);

            return new ShellFileOperations.Outcome(0, null);
        });

    private static ShellFileOperations Failing(int hresult)
        => new(_ => new ShellFileOperations.Outcome(hresult, null));

    private static Task InvokeAsync(IFileOperations operations, string operation) => operation switch
    {
        nameof(IFileOperations.CopyAsync) =>
            operations.CopyAsync([Location("a.txt")], Location("대상"), CancellationToken.None),
        nameof(IFileOperations.MoveAsync) =>
            operations.MoveAsync([Location("a.txt")], Location("대상"), CancellationToken.None),
        nameof(IFileOperations.DeleteAsync) =>
            operations.DeleteAsync([Location("a.txt")], CancellationToken.None),
        nameof(IFileOperations.RenameAsync) =>
            operations.RenameAsync(Location("a.txt"), "b.txt", CancellationToken.None),
        nameof(IFileOperations.CreateFolderAsync) =>
            operations.CreateFolderAsync(Location("부모"), "새 폴더", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "모르는 조작이다."),
    };

    /// <summary>shell 을 부르지 않는 테스트가 쓰는 가짜 경로.</summary>
    private static LocationId Location(string name) => Parse(Path.Combine(@"C:\Temp\flex-dir", name));

    /// <summary>실물 shell 이 손대도 되는 유일한 자리.</summary>
    private LocationId Root => Parse(root);

    private LocationId WriteFile(string name)
    {
        var path = Path.Combine(root, name);

        File.WriteAllText(path, "flex-dir 테스트");

        return Parse(path);
    }

    private LocationId MakeFolder(string name)
    {
        var path = Path.Combine(root, name);

        Directory.CreateDirectory(path);

        return Parse(path);
    }

    private static LocationId Parse(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _), path);

        return location;
    }
}
