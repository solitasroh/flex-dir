using System.IO;
using System.Windows;

// WinForms(트레이 아이콘)가 암시적 global using 으로 들어와 WPF 쪽 이름과 겹친다.
using Application = System.Windows.Application;

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

        await using var composition = AppComposition.Create(dispatcher, State(), () => 0);

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

        await using var composition = AppComposition.Create(dispatcher, State(), () => 0);

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

        await using var composition = AppComposition.Create(dispatcher, State(), () => 0);

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
        await using var composition = AppComposition.Create(dispatcher, state, () => 0);

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
        //
        // 물어보는 것은 조회 둘뿐이다. 활성화·파일 조작·클립보드로 확인하면 <b>정리가
        // 안 된 경우에</b> 프로그램이 뜨고 휴지통에 항목이 남고 사용자의 클립보드가
        // 덮인다 — 게이트가 돌 때마다 일어나서는 안 되는 일이다 (.harness/HANDOFF.md §규칙 5).
        // 나머지 셋이 같은 배열에 들어 있다는 것은 아래 테스트가 본다.
        var composition = AppComposition.Create(dispatcher, State(), () => 0);
        var thumbnails = composition.ShellServices.OfType<ShellThumbnailSource>().Single();
        var typeNames = composition.ShellServices.OfType<ShellTypeNameProvider>().Single();

        await composition.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => { _ = thumbnails.GetTypeIconAsync("txt", isDirectory: false, 16, default); });
        Assert.Throws<ObjectDisposedException>(
            () => { _ = typeNames.GetTypeNameAsync("flexdir-probe", isDirectory: false, default); });
    }

    [Fact]
    public async Task Create_OwnsEveryShellImplementationThatHoldsAnStaThread()
    {
        // 여섯이다 — HANDOFF 가 한동안 넷이라고 적었고 빠진 것은 ShellTypeNameProvider 였다.
        // B-4 가 ShellContextMenuProvider 를 더했다. 이 배열에 없는 구현체는 아무도 닫지
        // 않고 STA 스레드가 프로세스에 남는다.
        await using var composition = AppComposition.Create(dispatcher, State(), () => 0);

        Assert.Equal(
            [
                typeof(ShellTypeNameProvider),
                typeof(ShellThumbnailSource),
                typeof(Shell.Operations.ShellFileOperations),
                typeof(Shell.Operations.ShellClipboardBridge),
                typeof(Shell.Activation.ShellItemActivator),
                typeof(Shell.Operations.ShellContextMenuProvider),
            ],
            composition.ShellServices.Select(service => service.GetType()));
    }

    [Fact]
    public async Task DisposeAsync_Twice_IsFine()
    {
        // 완전 종료 경로가 겹칠 수 있다. 두 번째가 던지면 종료가 예외로 끝난다.
        var composition = AppComposition.Create(dispatcher, State(), () => 0);

        await composition.DisposeAsync();
        await composition.DisposeAsync();
    }

    [Fact]
    public void DefaultStateDirectory_IsUnderRoamingAppData()
    {
        // docs/ARCHITECTURE.md §4. 만들지는 않는다 — 이 속성을 읽는 것만으로 디렉터리가
        // 생기면 테스트가 실제 저장 위치를 건드린다.
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "flex-dir");

        Assert.Equal(expected, AppComposition.DefaultStateDirectory);
    }

    [Fact]
    public void DefaultStateDirectory_IsNotTheInstallDirectory()
    {
        // Velopack 은 %LOCALAPPDATA%\<packId> 에 설치한다 — packId 가 'flex-dir' 이므로
        // 예전 상태 폴더와 <b>정확히 같은 경로</b>였다 (2026-08-07 실물에서 usage.log 가
        // current\·packages\·Update.exe 와 한 폴더에 섞여 있었다).
        //
        // 사용자 데이터를 설치기가 관리하는 폴더에 두지 않는다. usage.log 는 도그푸딩
        // 게이트(ADR-007)의 입력이라, 갱신·제거가 그것을 건드리면 게이트가 기억을 잃는다.
        var install = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flex-dir");

        Assert.NotEqual(install, AppComposition.DefaultStateDirectory);
    }

    [Fact]
    public void Create_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => AppComposition.Create(null!, State(), () => 0));
        Assert.Throws<ArgumentNullException>(() => AppComposition.Create(dispatcher, null!, () => 0));
        Assert.Throws<ArgumentException>(() => AppComposition.Create(dispatcher, "  ", () => 0));
    }

    /// <summary>이 테스트만의 저장 위치. 테스트마다 갈라 서로의 뷰 상태를 읽지 않게 한다.</summary>
    private string State() => Path.Combine(root, "state");

    private static LocationId Loc(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");

        return location;
    }
}
