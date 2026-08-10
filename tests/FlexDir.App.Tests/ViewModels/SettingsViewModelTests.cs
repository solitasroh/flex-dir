using System.IO;
using System.Net.Http;

using FlexDir.App.Tests.Fakes;
using FlexDir.App.ViewModels;

using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.Updates;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 설정 창 (docs/PRD-v2.md §12). 세 항목뿐이다 — 정보 · 시작 폴더 · 숨김 파일 보기.
/// <para>
/// <b>저장은 조작마다 즉시 한다.</b> 전역 뷰 상태(창을 닫을 때)보다 이르다 — 즐겨찾기와
/// 같은 판단이고, 강제 종료 한 번에 방금 정한 것이 날아가면 안 된다.
/// </para>
/// </summary>
public class SettingsViewModelTests
{
    private const string StateDirectory = @"C:\Users\tester\AppData\Roaming\flex-dir";

    private readonly FakeSettingsStore store = new();
    private readonly FakeUpdateSource updates = new();
    private readonly InlineUiDispatcher dispatcher = new();

    private SettingsViewModel Create(string version = "0.3.1") => new(
        store,
        dispatcher,
        version,
        StateDirectory,
        new UpdateViewModel(updates, dispatcher));

    private static LocationId Loc(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    private async Task<SettingsViewModel> LoadedAsync(AppSettings? seed = null)
    {
        if (seed is not null)
        {
            store.Seed(seed);
        }

        var settings = Create();
        await settings.LoadAsync(CancellationToken.None);

        return settings;
    }

    // ── 정보 ──────────────────────────────────────────────────────

    [Fact]
    public void Version_IsWhateverTheHostHandsIn()
    {
        // 조립하는 쪽이 어셈블리에서 읽어 준다 (Host/Startup/ProductVersion). ViewModel 이
        // 진입 어셈블리를 직접 읽으면 테스트에서는 테스트 실행기의 버전이 나온다.
        Assert.Equal("0.3.1", Create("0.3.1").Version);
    }

    [Fact]
    public void StateFolderText_ShowsWhereTheStateLives()
    {
        Assert.Equal(StateDirectory, Create().StateFolderText);
    }

    [Fact]
    public void OpenStateFolder_AsksToNavigateThere()
    {
        // 우리가 파일 관리자다 — 상태 폴더도 활성 페인에서 연다. 탐색기를 띄우면 포트가
        // 하나 더 필요하고, 여기서 보고 싶은 것(settings.json·view-state.json)은 우리가
        // 이미 그릴 수 있는 것이다.
        var settings = Create();
        LocationId? asked = null;
        settings.NavigationRequested += (_, location) => asked = location;

        settings.OpenStateFolderCommand.Execute(null);

        Assert.Equal(Loc(StateDirectory), asked);
    }

    // ── 업데이트 수동 확인 ────────────────────────────────────────

    [Fact]
    public async Task CheckUpdate_WhenThereIsNothingNew_SaysSoOutLoud()
    {
        // 시작할 때의 자동 확인과 반대다 — 방금 누른 조작이라 아무 말도 하지 않으면
        // 눌렸는지조차 알 수 없다.
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.Checks);
        Assert.Contains("최신", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_WhenSomethingIsFound_NamesTheVersion()
    {
        updates.Available = new AvailableUpdate("0.4.0");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Contains("0.4.0", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_WhenTheFeedCannotBeReached_SaysItFailed()
    {
        updates.Failure = new HttpRequestException("피드에 못 닿는다");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Contains("확인하지 못했습니다", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUpdate_LeavesTheCheckingFlagDownWhenItIsOver()
    {
        // 버튼이 '확인 중…' 에 걸려 있으면 다시 누를 수 없다. 실패해도 내려와야 한다.
        updates.Failure = new HttpRequestException("피드에 못 닿는다");
        var settings = Create();

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.False(settings.IsCheckingUpdate);
    }

    [Fact]
    public async Task CheckUpdate_WithNoUpdateWiring_DoesNothingAndDoesNotThrow()
    {
        // 조립이 업데이트 없이 서는 경우가 있다 (WorkspaceViewModel 의 선택 인자와 같은 이유).
        var settings = new SettingsViewModel(store, dispatcher, "0.3.1", StateDirectory);

        await settings.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, updates.Checks);
    }

    // ── 시작 폴더 ─────────────────────────────────────────────────

    [Fact]
    public async Task Load_BringsBackWhatWasSaved()
    {
        var settings = await LoadedAsync(new AppSettings
        {
            StartMode = StartFolderMode.Fixed,
            StartFolder = Loc(@"C:\work"),
            ShowHiddenItems = true,
        });

        Assert.True(settings.StartsAtFixedFolder);
        Assert.False(settings.StartsAtLastFolder);
        Assert.Equal(@"C:\work", settings.StartFolderText);
        Assert.True(settings.ShowHiddenItems);
    }

    [Fact]
    public async Task Load_DoesNotWriteBackWhatItJustRead()
    {
        // 읽자마자 저장하면 앱을 켜기만 해도 파일이 다시 쓰인다 — 손으로 고쳐 둔 서식이
        // 매 실행마다 지워지고, 저장 실패가 시작 경로의 문제가 된다.
        await LoadedAsync(new AppSettings { ShowHiddenItems = true });

        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Load_WhenTheStoreThrows_LeavesTheDefaultsAndDoesNotThrow()
    {
        // 시작 경로가 이것을 지난다. 설정 하나 때문에 창이 안 뜨면 안 된다.
        store.LoadFailure = new IOException("파일이 잠겼다");
        var settings = Create();

        await settings.LoadAsync(CancellationToken.None);

        Assert.True(settings.StartsAtLastFolder);
        Assert.False(settings.ShowHiddenItems);
    }

    [Fact]
    public async Task ChoosingFixedMode_IsSavedImmediately()
    {
        var settings = await LoadedAsync();

        settings.StartsAtFixedFolder = true;
        await settings.SaveWork;

        Assert.Equal(StartFolderMode.Fixed, store.Current.StartMode);
    }

    [Fact]
    public async Task TypingAFolder_IsSavedImmediately()
    {
        var settings = await LoadedAsync();

        settings.StartFolderText = @"C:\work";
        await settings.SaveWork;

        Assert.Equal(Loc(@"C:\work"), store.Current.StartFolder);
        Assert.Null(settings.StartFolderError);
    }

    [Fact]
    public async Task TypingSomethingThatIsNotAPath_SaysWhyAndKeepsTheFolderUnset()
    {
        // 알아볼 수 없는 경로를 저장하면 다음 실행이 빈 페인으로 뜬다. 정하지 않은 것으로
        // 두면 시작 폴더 규칙이 마지막 폴더로 되돌아간다 (AppSettings.ResolveStartFolder).
        var settings = await LoadedAsync();

        settings.StartFolderText = "work";
        await settings.SaveWork;

        Assert.Equal("전체 경로를 입력하세요", settings.StartFolderError);
        Assert.Null(store.Current.StartFolder);
    }

    [Fact]
    public async Task ClearingTheFolder_IsNotAnError()
    {
        var settings = await LoadedAsync(new AppSettings { StartFolder = Loc(@"C:\work") });

        settings.StartFolderText = "   ";
        await settings.SaveWork;

        Assert.Null(settings.StartFolderError);
        Assert.Null(store.Current.StartFolder);
    }

    [Fact]
    public async Task UseCurrentFolder_FillsTheBoxAndSwitchesToFixedMode()
    {
        // 폴더를 채우기만 하고 모드를 그대로 두면 눌러도 아무 일이 없는 것처럼 보인다 —
        // 이 버튼을 누르는 이유가 '여기서 시작하겠다' 이기 때문이다.
        var settings = await LoadedAsync();

        settings.UseCurrentFolder(Loc(@"C:\work\project"));
        await settings.SaveWork;

        Assert.True(settings.StartsAtFixedFolder);
        Assert.Equal(@"C:\work\project", settings.StartFolderText);
        Assert.Equal(Loc(@"C:\work\project"), store.Current.StartFolder);
    }

    [Fact]
    public async Task UseCurrentFolder_WithNoFolderOpen_DoesNothing()
    {
        // 페인이 비어 있을 수 있다 (첫 실행에 폴백마저 없을 때).
        var settings = await LoadedAsync();

        settings.UseCurrentFolder(null);
        await settings.SaveWork;

        Assert.True(settings.StartsAtLastFolder);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Save_WhenTheStoreThrows_DoesNotThrowOutOfTheSetter()
    {
        // 세터는 바인딩이 부른다. 예외가 나가면 UI 스레드에서 잡을 사람이 없다.
        var settings = await LoadedAsync();
        store.SaveFailure = new IOException("디스크가 가득 찼다");

        settings.ShowHiddenItems = true;
        await settings.SaveWork;

        Assert.True(settings.ShowHiddenItems);   // 화면은 사용자가 고른 것을 그대로 둔다
    }

    // ── 숨김·시스템 파일 보기 ─────────────────────────────────────

    [Fact]
    public async Task ShowHiddenItems_IsSavedAndAnnounced()
    {
        // 목록과 트리가 지금 보고 있는 것을 곧바로 다시 걸러야 한다. 알리지 않으면 다음
        // 폴더로 옮길 때까지 화면이 옛 정책으로 남는다.
        var settings = await LoadedAsync();
        var announced = 0;
        settings.HiddenItemsChanged += (_, _) => announced++;

        settings.ShowHiddenItems = true;
        await settings.SaveWork;

        Assert.True(store.Current.ShowHiddenItems);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task ShowHiddenItems_SetToTheSameValue_ChangesNothing()
    {
        // 라디오·체크박스는 같은 값을 다시 밀어 넣는다. 그때마다 목록 둘과 트리를 다시
        // 읽으면 클릭 한 번에 저장소 호출이 쏟아진다.
        var settings = await LoadedAsync();
        var announced = 0;
        settings.HiddenItemsChanged += (_, _) => announced++;

        settings.ShowHiddenItems = false;
        await settings.SaveWork;

        Assert.Equal(0, announced);
        Assert.Equal(0, store.Saves);
    }

    // ── 여닫기 ────────────────────────────────────────────────────

    [Fact]
    public void Panel_StartsClosed()
    {
        Assert.False(Create().IsOpen);
    }

    [Fact]
    public void OpenAndClose_FlipThePanel()
    {
        var settings = Create();

        settings.OpenCommand.Execute(null);
        Assert.True(settings.IsOpen);

        settings.CloseCommand.Execute(null);
        Assert.False(settings.IsOpen);
    }

    [Fact]
    public void OpeningAgain_ClearsTheLastUpdateMessage()
    {
        // '최신 버전입니다' 가 어제 누른 결과로 떠 있으면 방금 확인한 것처럼 읽힌다.
        var settings = Create();
        settings.OpenCommand.Execute(null);
        settings.CheckUpdateCommand.Execute(null);
        settings.CloseCommand.Execute(null);

        settings.OpenCommand.Execute(null);

        Assert.Equal(string.Empty, settings.UpdateStatus);
    }

    [Fact]
    public void OpenStateFolder_ClosesThePanel()
    {
        // 오버레이가 페인을 덮고 있다. 열어 놓고 그 폴더를 보여 주면 아무것도 안 보인다.
        var settings = Create();
        settings.OpenCommand.Execute(null);

        settings.OpenStateFolderCommand.Execute(null);

        Assert.False(settings.IsOpen);
    }

    // ── 현재 값 ───────────────────────────────────────────────────

    [Fact]
    public async Task Current_IsWhatTheStartFolderRuleWillSee()
    {
        // WorkspaceViewModel.RestoreAsync 가 이 값으로 페인을 연다.
        var settings = await LoadedAsync();

        settings.StartsAtFixedFolder = true;
        settings.StartFolderText = @"C:\work";
        await settings.SaveWork;

        Assert.Equal(Loc(@"C:\work"), settings.Current.ResolveStartFolder(Loc(@"C:\last"), null));
    }
}
