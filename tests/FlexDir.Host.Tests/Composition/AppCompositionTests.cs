using System.IO;
using System.Windows;

using FlexDir.App.Tests.Fakes;

using FlexDir.Core.Locations;

using FlexDir.Host.Composition;

using FlexDir.Shell.Presentation;

using Xunit;

namespace FlexDir.Host.Tests.Composition;

/// <summary>
/// 조립이 <b>화면 없이</b> 끝나는가. 이 파일이 phase C 의 자동 채점이다 — 이것이 없으면
/// "창 없이 실행된다" 는 주장을 사람이 눈으로 보는 수밖에 없고, 그것은 자가 채점이다.
/// <para>
/// 여기서 만들어지는 것은 <b>실물</b>이다 (<c>FileSystemFolderSource</c>·
/// <c>ShellTypeNameProvider</c>·<c>ShellThumbnailSource</c> …). 그래서 실제 폴더를 한 번
/// 열어 본다 — 조립이 서기만 하고 아무것도 못 하는 상태를 통과시키지 않으려는 것이다.
/// </para>
/// <para>
/// 저장 위치는 전부 <see cref="Path.GetTempPath"/> 아래다. 실제
/// <c>%LOCALAPPDATA%\flex-dir\</c> 를 건드리면 도그푸딩 게이트가 자기 테스트 실행을
/// 사용으로 센다.
/// </para>
/// </summary>
public class AppCompositionTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "flex-dir-host-tests", Guid.NewGuid().ToString("N"));

    private readonly InlineUiDispatcher dispatcher = new();

    public AppCompositionTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 정리 실패로 테스트를 실패로 만들지 않는다.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Create_WithoutAWpfApplication_BuildsTheWorkspace()
    {
        // Application.Current 가 null 인 채로 여기까지 온다는 것이 이 단정문의 핵심이다.
        Assert.Null(Application.Current);

        await using var composition = AppComposition.Create(dispatcher, State());

        Assert.NotNull(composition.Workspace);
        Assert.NotNull(composition.UsageLog);
        Assert.NotSame(composition.Workspace.Left, composition.Workspace.Right);
        Assert.Null(Application.Current);
    }

    [Fact]
    public async Task Create_OpensARealFolder()
    {
        // 조립이 서기만 하고 아무것도 못 하는 상태를 통과시키지 않는다. 이 경로는 실물
        // 열거와 실물 유형 이름 조회(STA 워커)를 지난다.
        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(folder, "b.txt"), "b");

        await using var composition = AppComposition.Create(dispatcher, State());

        await composition.Workspace.Left.NavigateAsync(Loc(folder));

        Assert.Equal(["a.txt", "b.txt"], composition.Workspace.Left.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Create_TheTwoPanesAreIndependent()
    {
        // 2분할이 있는 이유가 이것이다 (docs/PRD.md §4). 포트 인스턴스를 공유하는 것과
        // 상태를 공유하는 것은 다른 일이다.
        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "a");

        await using var composition = AppComposition.Create(dispatcher, State());

        await composition.Workspace.Left.NavigateAsync(Loc(folder));

        Assert.Single(composition.Workspace.Left.Items);
        Assert.Empty(composition.Workspace.Right.Items);
        Assert.Null(composition.Workspace.Right.CurrentLocation);
    }

    [Fact]
    public async Task Create_KeepsEveryFileItWritesUnderTheGivenDirectory()
    {
        // 이 경로가 인자로 들어오지 않으면 테스트가 실제 %LOCALAPPDATA% 를 건드린다.
        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);

        var state = State();
        await using var composition = AppComposition.Create(dispatcher, state);

        await composition.Workspace.Left.NavigateAsync(Loc(folder));
        await composition.Workspace.Left.ChangeViewModeCommand.ExecuteAsync(
            Core.ViewState.ViewMode.LargeIcons);

        Assert.NotEmpty(Directory.GetFiles(state));
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheShellImplementations()
    {
        // 창을 닫아도 프로세스는 산다 (ADR-003). shell 구현체 안의 STA 스레드를 닫는 것은
        // 창이 아니라 이 조립이고, 그 시점은 완전 종료다.
        var composition = AppComposition.Create(dispatcher, State());
        var thumbnails = composition.ShellServices.OfType<ShellThumbnailSource>().Single();

        await composition.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => { _ = thumbnails.GetTypeIconAsync("txt", isDirectory: false, 16, default); });
    }

    [Fact]
    public async Task DisposeAsync_DisposesEveryShellImplementationThatHoldsAnStaThread()
    {
        // 다섯이다 — HANDOFF 가 넷이라고 적은 것은 ShellTypeNameProvider 를 빠뜨린 것이다.
        var composition = AppComposition.Create(dispatcher, State());
        var shell = composition.ShellServices.ToList();

        await composition.DisposeAsync();

        Assert.Equal(5, shell.Count);
        Assert.All(shell, service => Assert.True(IsClosed(service), service.GetType().Name));
    }

    [Fact]
    public async Task DisposeAsync_Twice_IsFine()
    {
        // 완전 종료 경로가 겹칠 수 있다. 두 번째가 던지면 종료가 예외로 끝난다.
        var composition = AppComposition.Create(dispatcher, State());

        await composition.DisposeAsync();
        await composition.DisposeAsync();
    }

    [Fact]
    public void DefaultStateDirectory_IsUnderLocalAppData()
    {
        // docs/ARCHITECTURE.md §4. 만들지는 않는다 — 이 속성을 읽는 것만으로 디렉터리가
        // 생기면 테스트가 실제 저장 위치를 건드린다.
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flex-dir");

        Assert.Equal(expected, AppComposition.DefaultStateDirectory);
    }

    [Fact]
    public void Create_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => AppComposition.Create(null!, State()));
        Assert.Throws<ArgumentNullException>(() => AppComposition.Create(dispatcher, null!));
        Assert.Throws<ArgumentException>(() => AppComposition.Create(dispatcher, "  "));
    }

    /// <summary>이 테스트만의 저장 위치. 테스트마다 갈라 서로의 뷰 상태를 읽지 않게 한다.</summary>
    private string State() => Path.Combine(root, "state");

    /// <summary>
    /// 닫힌 STA 큐는 새 작업을 받지 않는다. 포트 계약이 제각각이라 구현체 타입으로 가른다 —
    /// 여기서 재는 것은 "정말 Dispose 됐는가" 하나다.
    /// </summary>
    private static bool IsClosed(IDisposable service)
    {
        try
        {
            switch (service)
            {
                case ShellThumbnailSource thumbnails:
                    _ = thumbnails.GetTypeIconAsync("txt", isDirectory: false, 16, default);
                    break;

                case ShellTypeNameProvider typeNames:
                    _ = typeNames.GetTypeNameAsync("flexdir-probe", isDirectory: false, default);
                    break;

                case Shell.Activation.ShellItemActivator activator:
                    _ = activator.ActivateAsync(Loc(Path.GetTempPath()), default);
                    break;

                case Shell.Operations.ShellFileOperations operations:
                    _ = operations.DeleteAsync([Loc(Path.GetTempPath())], default);
                    break;

                case Shell.Operations.ShellClipboardBridge clipboard:
                    clipboard.SetCopy([Loc(Path.GetTempPath())]);
                    break;

                default:
                    return false;
            }
        }
        catch (ObjectDisposedException)
        {
            return true;
        }

        return false;
    }

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }
}
