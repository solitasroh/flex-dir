using System.Globalization;
using System.IO;

using FlexDir.App.Threading;
using FlexDir.App.ViewModels;

using FlexDir.Core.Usage;

using FlexDir.Host.Startup;

using FlexDir.Shell.Activation;
using FlexDir.Shell.Enumeration;
using FlexDir.Shell.Favorites;
using FlexDir.Shell.Operations;
using FlexDir.Shell.Presentation;
using FlexDir.Shell.Settings;
using FlexDir.Shell.Storage;
using FlexDir.Shell.Updates;
using FlexDir.Shell.Usage;
using FlexDir.Shell.ViewState;
using FlexDir.Shell.Watching;

namespace FlexDir.Host.Composition;

/// <summary>
/// 포트에 실물을 끼우는 유일한 자리. <c>FlexDir.Host</c> 만 <c>Core</c>·<c>Shell</c>·
/// <c>App</c> 셋을 다 알고, 그 지식을 이 클래스 하나에 가둔다 (docs/ARCHITECTURE.md §1).
///
/// <para>
/// <b>DI 컨테이너를 쓰지 않는다.</b> 조립 대상은 열두 개 남짓이고 그래프가 하나뿐이다.
/// 컨테이너를 들이면 (1) cold start 에 리플렉션 비용이 얹히고 — 그것이 ADR-003 이 상주
/// 프로세스를 고른 바로 그 비용이다 — (2) 수명 관리가 컨테이너의 규약으로 옮겨가는데,
/// 여기서 정말 어려운 수명은 <b>STA 스레드를 든 shell 구현체 여섯</b>이고 그것은 아래
/// <see cref="DisposeAsync"/> 가 손으로 다루는 편이 읽힌다.
/// </para>
///
/// <para>
/// <b>WPF <c>Application</c> 을 요구하지 않는다.</b> UI 스레드로 가는 통로는
/// <see cref="IUiDispatcher"/> 하나이고 그것을 호출자가 넘긴다. 화면 없이 조립이 서는지는
/// <c>tests/FlexDir.Host.Tests/Composition/AppCompositionTests.cs</c> 가 채점한다.
/// </para>
///
/// <para>
/// <b>포트 인스턴스는 두 페인이 나눠 쓴다.</b> 페인마다 만들면 STA 워커가 두 배가 되고
/// (<c>ShellTypeNameProvider</c> 는 확장자 캐시까지 두 벌이 된다) 얻는 것이 없다 —
/// 페인이 나눠 갖지 않는 것은 상태이지 포트가 아니다.
/// </para>
/// </summary>
public sealed class AppComposition : IAsyncDisposable
{
    /// <summary>
    /// STA 스레드를 든 구현체들. <b>누가 언제 닫는가</b> 가 이 배열의 존재 이유다 —
    /// 상주 프로세스에서는 창이 닫혀도 이것들이 살아 있어야 다음 창이 곧바로 뜬다
    /// (ADR-003). 닫는 시점은 완전 종료 하나뿐이고 그것이 <see cref="DisposeAsync"/> 다.
    /// </summary>
    private readonly IDisposable[] shellServices;

    private bool disposed;

    /// <summary>
    /// 릴리스가 올라가는 곳 (docs/PRD-v2.md §9). 저장소가 public 이라 토큰이 없다
    /// (사용자 결정 2026-08-07) — 배포물에 비밀값을 싣지 않는다.
    /// </summary>
    private const string UpdateFeed = "https://github.com/solitasroh/flex-dir";

    private AppComposition(WorkspaceViewModel workspace, IUsageLog usageLog, IDisposable[] shellServices)
    {
        Workspace = workspace;
        UsageLog = usageLog;
        this.shellServices = shellServices;
    }

    /// <summary>
    /// 뷰 상태·사용 기록·계측이 쌓이는 곳 (docs/ARCHITECTURE.md §4).
    /// 읽는 것만으로 만들지 않는다 — 만들면 이 속성을 보기만 해도 흔적이 남는다.
    ///
    /// <para>
    /// <b><c>%LOCALAPPDATA%</c> 가 아니라 <c>%APPDATA%</c> 다</b> (2026-08-07에 옮겼다).
    /// Velopack 이 <c>%LOCALAPPDATA%\flex-dir</c> 에 설치하는데 그것이 예전 상태 폴더와
    /// <b>정확히 같은 경로</b>였다 — 첫 설치에서 <c>usage.log</c> 가 <c>current\</c>·
    /// <c>packages\</c>·<c>Update.exe</c> 와 한 폴더에 섞여 있는 것을 보고 알았다.
    /// </para>
    /// <para>
    /// 사용자 데이터를 설치기가 관리하는 폴더에 두지 않는다. 갱신·제거가 그것을 건드리면
    /// <b>쓴 날의 기록도 폴더별 뷰 설정도 통째로 사라진다</b>.
    /// </para>
    /// </summary>
    public static string DefaultStateDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "flex-dir");

    public WorkspaceViewModel Workspace { get; }

    public IUsageLog UsageLog { get; }

    /// <summary>정리 순서와 정리 여부를 테스트가 보는 자리. Host 밖으로 나가지 않는다.</summary>
    internal IReadOnlyList<IDisposable> ShellServices => shellServices;

    /// <param name="ownerWindow">
    /// shell 대화상자와 컨텍스트 메뉴의 소유 창을 내는 공급자 (<c>Startup/OwnerWindow</c>).
    /// <b>값이 아니라 함수다</b> — 창은 활성화가 만들므로 여기서는 아직 없다.
    /// </param>
    public static AppComposition Create(IUiDispatcher dispatcher, string stateDirectory, Func<nint> ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(ownerWindow);

        // 서버(\\server)는 폴더가 아니라 공유 목록이고 열거 기제가 완전히 다르다
        // (WNetEnumResource vs FileSystemEnumerator — docs/PRD-v2.md §5 N-3).
        // 한 몸에 섞지 않고 라우팅으로 가른다. 페인은 여전히 IFolderSource 하나만 안다.
        var folderSource = new RoutingFolderSource(new FileSystemFolderSource(), new NetworkShareSource());
        var folderWatcher = new FileSystemFolderWatcher();
        var viewStates = new JsonViewStateStore(stateDirectory);

        var typeNames = new ShellTypeNameProvider();
        var thumbnails = new ShellThumbnailSource();
        // shell 대화상자에 소유 창을 준다 — 컨텍스트 메뉴와 같은 결정이다 (사용자 결정
        // 2026-08-06). 없으면 진행률·충돌 대화상자가 별도 작업표시줄 항목으로 뜬다.
        var fileOperations = new ShellFileOperations(ownerWindow);
        var clipboard = new ShellClipboardBridge();
        var activator = new ShellItemActivator();
        var contextMenus = new ShellContextMenuProvider(ownerWindow);

        // STA 도 정리도 필요 없다 — COM 이 아니라 GetDiskFreeSpaceEx 다. 그래서 아래
        // 정리 목록에 들어가지 않는다. 드라이브 목록(mpr.dll)도 같은 자리다.
        var driveSpace = new FileSystemDriveSpace();
        var driveList = new SystemDriveList();

        // 이쪽은 COM 이라 STA 워커를 든다 — 아래 정리 목록에 들어간다.
        var networkPlaces = new ShellNetworkPlaceList();

        // 환경을 읽는 곳은 여기 한 곳이다. ViewModel 이 CultureInfo.CurrentCulture 를 직접
        // 읽으면 같은 목록에 다른 형식이 섞이고 테스트가 기계 설정에 따라 갈린다.
        var culture = CultureInfo.CurrentCulture;
        var timeZone = TimeZoneInfo.Local;

        PaneViewModel Pane() => new(
            folderSource,
            folderWatcher,
            typeNames,
            thumbnails,
            viewStates,
            fileOperations,
            clipboard,
            activator,
            dispatcher,
            culture,
            timeZone,
            contextMenus,
            driveSpace: driveSpace);

        // 새 버전 알림. 확인·받기는 여기서 시작하지 않는다 — 조립은 화면 없이 서야 하고
        // (위 §요약) 네트워크에 닿는 것은 Program 이 시작 뒤에 건다.
        var update = new UpdateViewModel(new VelopackUpdateSource(UpdateFeed), dispatcher);

        // 트리도 페인과 같은 열거 포트를 쓴다 (docs/PRD-v2.md §10) — 라우팅이 이미 서버와
        // 폴더를 가르므로 트리는 UNC 를 따로 알 필요가 없다. 창에 하나뿐이라 페인처럼
        // 두 벌 만들지 않는다.
        // 즐겨찾기는 뷰 상태와 다른 파일이다 — 그쪽이 깨져서 기본값으로 접히는 사건이
        // 사용자가 모아 둔 목록을 함께 지우면 안 된다 (docs/PRD-v2.md §10-2).
        var favorites = new JsonFavoriteStore(stateDirectory);

        var tree = new FolderTreeViewModel(driveList, networkPlaces, favorites, folderSource, dispatcher);

        // 설정도 즐겨찾기와 같은 이유로 뷰 상태와 다른 파일이다 (docs/PRD-v2.md §12) —
        // 뷰 상태는 캐시라 깨지면 기본값으로 접는데, 설정은 사용자의 의도라 그렇게 접히면
        // "숨김 파일을 보겠다" 가 조용히 뒤집힌다.
        //
        // 버전은 여기서 읽어 넘긴다. ViewModel 이 진입 어셈블리를 직접 읽으면 테스트에서는
        // 테스트 실행기의 버전이 나온다 (Startup/ProductVersion).
        var settings = new SettingsViewModel(
            new JsonSettingsStore(stateDirectory),
            dispatcher,
            ProductVersion.Current,
            stateDirectory,
            update);

        var workspace = new WorkspaceViewModel(Pane(), Pane(), viewStates, update, tree, settings);

        return new AppComposition(
            workspace,
            new FileUsageLog(stateDirectory),
            [typeNames, thumbnails, fileOperations, clipboard, activator, contextMenus, networkPlaces]);
    }

    /// <summary>
    /// 완전 종료. <b>페인을 먼저 접고 shell 구현체를 닫는다</b> — 순서를 뒤집으면 진행 중인
    /// 썸네일·유형 이름 요청이 닫힌 STA 큐에 들어가 관측되지 않는 예외가 된다.
    /// <para>
    /// 창이 닫힐 때 부르지 않는다. 상주 프로세스는 창 없이 살아 있고(ADR-003), 그때
    /// STA 워커까지 접으면 다음 창이 그 비용을 다시 낸다.
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        await Workspace.Left.DisposeAsync().ConfigureAwait(false);
        await Workspace.Right.DisposeAsync().ConfigureAwait(false);

        foreach (var service in shellServices)
        {
            service.Dispose();
        }
    }
}
