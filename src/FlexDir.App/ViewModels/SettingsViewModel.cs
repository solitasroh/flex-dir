using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FlexDir.App.Threading;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Settings;
using FlexDir.Core.Tools;

namespace FlexDir.App.ViewModels;

/// <summary>
/// 터미널 목록에 뜨는 것 하나. <see cref="Label"/> 은 사람이 읽는 이름이고
/// <see cref="Preset"/> 이 값이다 — 화면은 <c>DisplayMemberPath</c> 로 앞의 것만 본다.
/// <para>
/// <see cref="TerminalPreset"/> 을 그대로 목록에 실을 수 없어서 있다. 열거형을 실으면
/// 화면에 <c>WindowsTerminal</c> 이 그대로 뜨고, 그것을 고치려면 변환기가 하나 는다 —
/// 변환기는 <c>ExternalToolCommand.Label</c> 을 부르는 것 말고 할 일이 없다.
/// </para>
/// </summary>
public sealed record TerminalOption(TerminalPreset Preset, string Label);

/// <summary>
/// 설정 창 (docs/PRD-v2.md §12). 세 항목뿐이다 — 정보 · 시작 폴더 · 숨김 파일 보기.
///
/// <para>
/// <b>별도 창이 아니라 창 안의 오버레이다</b> (사용자 결정 2026-08-10). 상주 프로세스에서
/// 창 닫기는 숨기기이고 (ADR-003), 별도 <c>Window</c> 를 두면 "주 창이 숨을 때 설정 창은
/// 어떻게 되나" 가 그대로 새 결정이 된다. 여기서 <see cref="IsOpen"/> 하나만 내고 화면은
/// 그것을 본다 — ViewModel 은 WPF 창을 만지지 않는다 (docs/ARCHITECTURE.md §1).
/// </para>
///
/// <para>
/// <b>저장은 조작마다 즉시 한다.</b> 전역 뷰 상태(창을 닫을 때)보다 이르다 — 설정은 캐시가
/// 아니라 사용자의 의도라서, 강제 종료 한 번에 방금 정한 것이 날아가면 안 된다.
/// 즐겨찾기와 같은 판단이다 (docs/PRD-v2.md §10-2).
/// </para>
///
/// <para>
/// <b>페인도 트리도 모른다.</b> 현재 폴더는 <see cref="UseCurrentFolder"/> 로 받고, 상태
/// 폴더를 여는 것은 <see cref="NavigationRequested"/> 로 부탁한다 —
/// <c>WorkspaceViewModel</c> 이 잇는다. 트리가 같은 구도다.
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore store;
    private readonly IUiDispatcher dispatcher;
    private readonly ISystemThemeSource systemTheme;
    private readonly IExternalToolCatalog? externalTools;
    private readonly IExternalToolLauncher? toolLauncher;
    private readonly LocationId? stateFolder;

    /// <summary>
    /// 읽어 오는 중인가. <see cref="LoadAsync"/> 가 세터를 지나므로 이것이 없으면 앱을
    /// 켜기만 해도 방금 읽은 것을 그대로 다시 쓴다.
    /// </summary>
    private bool loading;

    private StartFolderMode startMode = AppSettings.Default.StartMode;
    private LocationId? startFolder;
    private string startFolderText = string.Empty;
    private string? startFolderError;
    private bool showHiddenItems = AppSettings.Default.ShowHiddenItems;

    private TerminalPreset terminalPreset = AppSettings.Default.TerminalPreset;
    private string? terminalExecutable = AppSettings.Default.TerminalExecutable;
    private string? terminalArguments = AppSettings.Default.TerminalArguments;
    private string? terminalTestResult;

    private ThemeMode themeMode = AppSettings.Default.Theme;

    /// <summary>
    /// OS 가 마지막으로 읽힌 값. <see cref="LoadAsync"/> 와 <see cref="RefreshSystemTheme"/>
    /// 가 채운다 — 매 바인딩마다 레지스트리를 다시 읽으면 <see cref="IsDarkMode"/> 를
    /// 조회하는 곳마다(색 토큰 열여섯 곳) 저장소 호출이 나간다.
    /// </summary>
    private bool systemIsDark;

    private bool isOpen;
    private bool isCheckingUpdate;
    private string updateStatus = string.Empty;

    private Task saveWork = Task.CompletedTask;

    /// <param name="version">
    /// 화면에 낼 버전. <b>조립하는 쪽이 넘긴다</b> (<c>Host/Startup/ProductVersion</c>) —
    /// 여기서 진입 어셈블리를 읽으면 테스트에서는 테스트 실행기의 버전이 나오고, 그러면
    /// "<c>Directory.Build.props</c> 의 값과 같은가" 를 채점할 수 없다.
    /// </param>
    /// <param name="stateDirectory">
    /// 뷰 상태·설정·기록이 쌓이는 폴더 (<c>%APPDATA%\flex-dir</c>). 문자열로 받아 여기서
    /// 판정한다 — 조립이 이것 하나 때문에 파싱 실패를 다루게 만들지 않는다.
    /// </param>
    /// <param name="themeSource">
    /// OS 가 라이트인지 다크인지 묻는 포트 (docs/PRD-v2.md §19). <b>선택이 아니다</b> — 항상
    /// 실물이 있다(<c>update</c> 와 다르다). 이 값이 <see cref="ThemeMode.System"/> 일 때만
    /// 쓰인다.
    /// </param>
    /// <param name="update">
    /// 새 버전 확인. <b>선택이다</b> — 없으면 '확인' 이 아무 일도 하지 않고 나머지는 그대로
    /// 돈다. <c>WorkspaceViewModel</c> 이 같은 자리에서 같은 이유로 선택을 쓴다.
    /// </param>
    /// <param name="externalTools">
    /// 터미널을 이 기계에서 찾는 포트. <b>선택이다</b> (<paramref name="update"/> 와 같은
    /// 자리) — 없으면 <see cref="TestTerminalCommand"/> 만 조용하고 목록과 저장은 그대로 돈다.
    /// </param>
    /// <param name="toolLauncher">
    /// 찾은 것을 띄우는 포트. <paramref name="externalTools"/> 와 짝이다 — 둘 중 하나만
    /// 있으면 <see cref="TestTerminalCommand"/> 는 아무 일도 하지 않는다.
    /// </param>
    public SettingsViewModel(
        ISettingsStore settingsStore,
        IUiDispatcher uiDispatcher,
        string version,
        string stateDirectory,
        ISystemThemeSource themeSource,
        UpdateViewModel? update = null,
        IExternalToolCatalog? externalTools = null,
        IExternalToolLauncher? toolLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(themeSource);

        store = settingsStore;
        dispatcher = uiDispatcher;
        systemTheme = themeSource;
        this.externalTools = externalTools;
        this.toolLauncher = toolLauncher;
        Version = version;
        StateFolderText = stateDirectory;
        Update = update;

        // 선언 순서 그대로 낸다. 손으로 적어 두면 프리셋이 하나 늘 때 목록에서 조용히 빠진다.
        TerminalOptions = [.. Enum.GetValues<TerminalPreset>()
            .Select(preset => new TerminalOption(preset, ExternalToolCommand.Label(preset)))];

        stateFolder = LocationId.TryParse(stateDirectory, out var parsed, out _) ? parsed : null;
    }

    /// <summary>상태 폴더를 열어 달라는 부탁. 어느 페인이 갈지는 워크스페이스가 정한다.</summary>
    public event EventHandler<LocationId>? NavigationRequested;

    /// <summary>
    /// <see cref="ShowHiddenItems"/> 가 <b>실제로</b> 바뀌었다. 목록 둘과 트리가 지금 보고
    /// 있는 것을 다시 걸러야 한다 — 알리지 않으면 다음 폴더로 옮길 때까지 옛 정책으로 남는다.
    /// </summary>
    public event EventHandler? HiddenItemsChanged;

    /// <summary>
    /// 터미널 선택이 바뀌었다. <c>WorkspaceViewModel</c> 이 열려 있는 페인들에 다시 민다 —
    /// <see cref="HiddenItemsChanged"/> 와 같은 자리이고 같은 이유다.
    /// <para>
    /// <b>프리셋·실행 파일·인자 셋 중 무엇이 바뀌어도 낸다.</b> 페인이 받는 것은
    /// <see cref="AppSettings.ResolveTerminal"/> 가 접은 값 하나이고, 그 값은 셋 모두에서
    /// 나온다 — 프리셋만 보면 사용자 지정 칸을 고치는 동안 아무 일도 일어나지 않는다.
    /// </para>
    /// </summary>
    public event EventHandler? TerminalChanged;

    /// <summary>새 버전 확인. 배선되지 않았으면 <see langword="null"/> 이다.</summary>
    public UpdateViewModel? Update { get; }

    /// <summary>설치된 버전. <c>Directory.Build.props</c> 의 <c>&lt;Version&gt;</c> 이 정본이다.</summary>
    public string Version { get; }

    /// <summary>상태 폴더의 경로. 표시용이며 사용자가 고치지 않는다.</summary>
    public string StateFolderText { get; }

    /// <summary>설정 패널이 떠 있는가. View 가 이것으로 오버레이를 낸다.</summary>
    public bool IsOpen
    {
        get => isOpen;
        private set => SetProperty(ref isOpen, value);
    }

    /// <summary>확인 중인가. 버튼을 잠그고 '확인 중…' 을 내는 값이다.</summary>
    public bool IsCheckingUpdate
    {
        get => isCheckingUpdate;
        private set => SetProperty(ref isCheckingUpdate, value);
    }

    /// <summary>마지막 확인이 남긴 말. 비어 있으면 아직 누르지 않은 것이다.</summary>
    public string UpdateStatus
    {
        get => updateStatus;
        private set => SetProperty(ref updateStatus, value);
    }

    /// <summary>
    /// 마지막 폴더에서 시작하는가. <see cref="StartsAtFixedFolder"/> 와 짝이다 —
    /// 라디오 버튼 둘이 각각 <c>bool</c> 에 붙는 편이 열거형 변환기를 두는 것보다 짧다.
    /// </summary>
    public bool StartsAtLastFolder
    {
        get => startMode == StartFolderMode.LastFolder;
        set
        {
            if (value)
            {
                StartMode = StartFolderMode.LastFolder;
            }
        }
    }

    /// <summary>정해 둔 폴더에서 시작하는가.</summary>
    public bool StartsAtFixedFolder
    {
        get => startMode == StartFolderMode.Fixed;
        set
        {
            if (value)
            {
                StartMode = StartFolderMode.Fixed;
            }
        }
    }

    private StartFolderMode StartMode
    {
        get => startMode;
        set
        {
            if (startMode == value)
            {
                return;
            }

            startMode = value;

            OnPropertyChanged(nameof(StartsAtLastFolder));
            OnPropertyChanged(nameof(StartsAtFixedFolder));

            Persist();
        }
    }

    /// <summary>
    /// 시작 폴더 상자의 글자. <b>정규화하지 않는다</b> — 고치는 중에 글자가 바뀌면 이어서
    /// 칠 자리를 잃는다. 알아본 경로는 따로 들고 있다 (<see cref="Current"/>).
    /// </summary>
    public string StartFolderText
    {
        get => startFolderText;
        set
        {
            var text = value ?? string.Empty;

            if (!SetProperty(ref startFolderText, text))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                // 비운 것은 오류가 아니라 '정하지 않음' 이다.
                startFolder = null;
                StartFolderError = null;
            }
            else if (LocationId.TryParse(text, out var parsed, out var error))
            {
                startFolder = parsed;
                StartFolderError = null;
            }
            else
            {
                // 알아볼 수 없는 것을 저장하면 다음 실행이 빈 페인으로 뜬다. 정하지 않은
                // 것으로 두면 시작 폴더 규칙이 마지막 폴더로 되돌아간다.
                startFolder = null;

                // 경로는 상자에 그대로 보이므로 문구에 다시 싣지 않는다.
                StartFolderError = PaneViewModel.DescribeParseError(error, address: null);
            }

            Persist();
        }
    }

    /// <summary>시작 폴더 입력이 거부된 사유. 없으면 <see langword="null"/> 이다.</summary>
    public string? StartFolderError
    {
        get => startFolderError;
        private set => SetProperty(ref startFolderError, value);
    }

    /// <summary>OS 설정을 따라가는가. <see cref="IsLightTheme"/>·<see cref="IsDarkTheme"/> 와 짝이다.</summary>
    public bool IsSystemTheme
    {
        get => themeMode == ThemeMode.System;
        set
        {
            if (value)
            {
                Theme = ThemeMode.System;
            }
        }
    }

    /// <summary>OS 와 무관하게 늘 라이트인가.</summary>
    public bool IsLightTheme
    {
        get => themeMode == ThemeMode.Light;
        set
        {
            if (value)
            {
                Theme = ThemeMode.Light;
            }
        }
    }

    /// <summary>OS 와 무관하게 늘 다크인가.</summary>
    public bool IsDarkTheme
    {
        get => themeMode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                Theme = ThemeMode.Dark;
            }
        }
    }

    private ThemeMode Theme
    {
        get => themeMode;
        set
        {
            if (themeMode == value)
            {
                return;
            }

            themeMode = value;

            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsDarkMode));

            Persist();
        }
    }

    /// <summary>
    /// 지금 화면을 다크로 그려야 하는가. <c>v:ThemeSync.IsDarkMode</c> 가 이것을 바인딩한다.
    /// </summary>
    public bool IsDarkMode => Current.ResolveIsDarkMode(systemIsDark);

    /// <summary>숨김·시스템 파일을 목록과 트리에 보이는가.</summary>
    public bool ShowHiddenItems
    {
        get => showHiddenItems;
        set
        {
            if (!SetProperty(ref showHiddenItems, value))
            {
                return;
            }

            Persist();

            if (!loading)
            {
                HiddenItemsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 화면에 뜨는 프리셋 여섯 (<see cref="Core.Tools.TerminalPreset.Custom"/> 포함).
    /// 순서는 열거형 선언 순서다.
    /// </summary>
    public IReadOnlyList<TerminalOption> TerminalOptions { get; }

    /// <summary>
    /// 지금 고른 것. <b>목록이 여섯이라 라디오가 아니라 <c>ComboBox</c> 다</b> — 테마는
    /// 셋이라 라디오가 맞았지만 여섯을 세로로 늘어놓으면 그 아래 §숨김 항목이 패널 밖으로
    /// 밀린다.
    /// <para>
    /// 값을 담지 않는다. <see cref="TerminalPreset"/> 이 정본이고 여기서는 그것을 목록의
    /// 항목으로 바꿔 낼 뿐이다 — 둘 다 담으면 어긋날 자리가 생긴다.
    /// </para>
    /// </summary>
    public TerminalOption SelectedTerminal
    {
        get => TerminalOptions.First(option => option.Preset == terminalPreset);
        set
        {
            if (value is not null)
            {
                TerminalPreset = value.Preset;
            }
        }
    }

    /// <summary>
    /// 사용자 지정을 골랐는가. 아래 상자 둘의 <c>IsEnabled</c> 가 이것이다 — 프리셋을 고른
    /// 동안 상자를 열어 두면, 고칠 수 있는데 아무 효과가 없는 칸이 된다
    /// (<see cref="StartFolderText"/> 와 같은 판단).
    /// </summary>
    public bool IsCustomTerminal => terminalPreset == Core.Tools.TerminalPreset.Custom;

    /// <summary>
    /// 터미널 버튼이 열 것 (docs/PRD-v2.md §20). 화면은 <see cref="SelectedTerminal"/> 로
    /// 붙는다 — <c>ComboBox</c> 가 목록의 항목을 돌려주므로 열거형은 안쪽에 남는다.
    /// </summary>
    internal TerminalPreset TerminalPreset
    {
        get => terminalPreset;
        set
        {
            if (terminalPreset == value)
            {
                return;
            }

            terminalPreset = value;

            OnPropertyChanged(nameof(SelectedTerminal));
            OnPropertyChanged(nameof(IsCustomTerminal));

            Persist();
            AnnounceTerminal();
        }
    }

    /// <summary>
    /// <see cref="Core.Tools.TerminalPreset.Custom"/> 일 때 띄울 실행 파일.
    /// <b>정규화하지 않는다</b> — <see cref="StartFolderText"/> 와 같은 이유로, 고치는 중에
    /// 글자가 바뀌면 이어서 칠 자리를 잃는다. 공백뿐인 것을 접는 것은 저장소가 한다
    /// (<c>JsonSettingsStore</c>).
    /// </summary>
    public string? TerminalExecutable
    {
        get => terminalExecutable;
        set
        {
            if (!SetProperty(ref terminalExecutable, value))
            {
                return;
            }

            Persist();
            AnnounceTerminal();
        }
    }

    /// <summary>
    /// 사용자 지정 터미널에 넘길 인자 한 줄.
    /// <see cref="ExternalToolCommand.PathToken"/> 이 현재 폴더로 바뀐다.
    /// </summary>
    public string? TerminalArguments
    {
        get => terminalArguments;
        set
        {
            if (!SetProperty(ref terminalArguments, value))
            {
                return;
            }

            Persist();
            AnnounceTerminal();
        }
    }

    /// <summary>
    /// <c>[실행해 보기]</c> 가 남긴 한 줄. 아직 누르지 않았으면 <see langword="null"/> 이고,
    /// 그때는 화면에서 자리까지 접힌다.
    /// </summary>
    public string? TerminalTestResult
    {
        get => terminalTestResult;
        private set => SetProperty(ref terminalTestResult, value);
    }

    /// <summary>
    /// 지금 정해져 있는 것. <c>WorkspaceViewModel.RestoreAsync</c> 가 이 값으로 페인을 연다.
    /// <para>
    /// <b>사용자 지정 칸의 공백은 여기서 접는다.</b> 지운 칸은 '아직 안 적었다' 이지 '빈
    /// 이름의 프로그램' 이 아니다 — 빈 문자열이 그대로 실리면 페인이 그것을 실행 파일로
    /// 알고 버튼을 켠다. 접는 자리가 세터가 아닌 이유는 <see cref="StartFolderText"/> 와
    /// 같다: 고치는 중에 글자가 바뀌면 이어서 칠 자리를 잃는다.
    /// </para>
    /// </summary>
    public AppSettings Current => new()
    {
        StartMode = startMode,
        StartFolder = startFolder,
        ShowHiddenItems = showHiddenItems,
        Theme = themeMode,
        TerminalPreset = terminalPreset,
        TerminalExecutable = NullIfBlank(terminalExecutable),
        TerminalArguments = NullIfBlank(terminalArguments),
    };

    /// <summary>진행 중인 저장. 테스트가 "남았는가" 를 보는 자리다.</summary>
    internal Task SaveWork => saveWork;

    /// <summary>
    /// 저장된 설정을 읽어 온다. <b>읽은 것을 그대로 다시 쓰지 않는다</b> — 앱을 켜기만 해도
    /// 파일이 새로 쓰이면 손으로 고쳐 둔 서식이 매 실행마다 지워진다.
    /// <para>
    /// <b>던지지 않는다.</b> 시작 경로가 이것을 지나므로 (<c>WorkspaceViewModel.RestoreAsync</c>)
    /// 설정 파일 하나 때문에 창이 안 뜨면 안 된다.
    /// </para>
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        AppSettings loaded;

        try
        {
            loaded = await store.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            loaded = AppSettings.Default;   // 저장된 설정은 못 읽어도 OS 테마는 마저 읽는다.
        }

        // 창이 뜨기 전에 한 번은 실물을 읽어야 한다 — 안 그러면 System 모드가 항상
        // "읽은 적 없음"(라이트) 로 보인다. 실패해도 던지지 않는다 (Update).
        var isDark = await ReadSystemThemeAsync(ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() =>
        {
            var before = (terminalPreset, terminalExecutable, terminalArguments);

            loading = true;

            try
            {
                StartMode = loaded.StartMode;
                StartFolderText = loaded.StartFolder?.DisplayPath ?? string.Empty;
                ShowHiddenItems = loaded.ShowHiddenItems;
                TerminalPreset = loaded.TerminalPreset;
                TerminalExecutable = loaded.TerminalExecutable;
                TerminalArguments = loaded.TerminalArguments;
                systemIsDark = isDark;
                Theme = loaded.Theme;

                // 세터가 텍스트에서 다시 파싱했다. 읽어 온 값을 정본으로 되돌린다 —
                // 저장된 경로는 이미 판정을 지난 것이다.
                startFolder = loaded.StartFolder;
            }
            finally
            {
                loading = false;
            }

            OnPropertyChanged(nameof(IsDarkMode));

            // 읽어 온 값도 알린다 — 세터들은 loading 중에 조용하고 (AnnounceTerminal),
            // 그러면 설정 창을 켜 두고 다시 읽는 경로에서 페인이 옛 프리셋으로 남는다.
            //
            // 셋을 따로 알리지 않는 이유는 중간 상태다: 프리셋만 바뀐 순간에 알리면 페인이
            // 아직 실행 파일이 비어 있는 사용자 지정으로 한 번 탐지를 돈다. 달라진 것이
            // 없으면 조용한 이유는 세터들과 같다 — 알림 하나가 페인 수만큼의 조회다.
            if (before != (terminalPreset, terminalExecutable, terminalArguments))
            {
                TerminalChanged?.Invoke(this, EventArgs.Empty);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>WM_SETTINGCHANGE</c>(<c>ImmersiveColorSet</c>) 를 받았을 때 부른다
    /// (<c>Views/SystemThemeWatcher</c>). <see cref="ThemeMode.System"/> 이 아니면 OS 값이
    /// 바뀌어도 화면은 그대로다 — 사용자가 고정으로 고른 것을 재조회가 뒤집으면 안 된다.
    /// </summary>
    public async Task RefreshSystemTheme(CancellationToken ct = default)
    {
        var isDark = await ReadSystemThemeAsync(ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() =>
        {
            if (systemIsDark == isDark)
            {
                return;
            }

            systemIsDark = isDark;
            OnPropertyChanged(nameof(IsDarkMode));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 터미널 선택이 바뀌었다고 알린다. <b>읽어 오는 중에는 조용하다</b> — 셋을 차례로
    /// 넣는 사이의 중간 상태가 그대로 나가면 페인이 헛되이 다시 탐지한다.
    /// <see cref="LoadAsync"/> 가 다 넣은 뒤에 한 번 낸다.
    /// </summary>
    private void AnnounceTerminal()
    {
        if (!loading)
        {
            TerminalChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>못 읽으면 라이트로 접는다 — 테마 하나 때문에 시작·재조회가 죽으면 안 된다.</summary>
    private async Task<bool> ReadSystemThemeAsync(CancellationToken ct)
    {
        try
        {
            return await systemTheme.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 활성 페인이 보고 있는 폴더를 시작 폴더로 삼는다. <b>모드도 함께 바꾼다</b> — 이
    /// 버튼을 누르는 이유가 '여기서 시작하겠다' 이고, 폴더만 채우면 눌러도 아무 일이 없는
    /// 것처럼 보인다.
    /// </summary>
    /// <param name="folder">활성 페인의 현재 폴더. 비어 있으면 아무 일도 하지 않는다.</param>
    public void UseCurrentFolder(LocationId? folder)
    {
        if (folder is null)
        {
            return;
        }

        StartFolderText = folder.DisplayPath;
        StartMode = StartFolderMode.Fixed;
    }

    [RelayCommand]
    private void Open()
    {
        // 지난번 확인 결과를 지운다. '최신 버전입니다' 가 어제 누른 결과로 떠 있으면 방금
        // 확인한 것처럼 읽힌다.
        UpdateStatus = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// 상태 폴더를 활성 페인에서 연다. <b>패널을 닫는다</b> — 오버레이가 페인을 덮고 있어서
    /// 열어 둔 채로 보여 주면 아무것도 보이지 않는다.
    /// </summary>
    [RelayCommand]
    private void OpenStateFolder()
    {
        if (stateFolder is not { } folder)
        {
            return;
        }

        IsOpen = false;
        NavigationRequested?.Invoke(this, folder);
    }

    /// <summary>
    /// 사용자가 직접 누른 새 버전 확인. <b>결과를 반드시 말한다</b> — 시작할 때의 자동
    /// 확인이 조용한 것과 반대다 (<see cref="UpdateViewModel.CheckNowAsync"/>).
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync(CancellationToken ct)
    {
        if (Update is not { } update)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            IsCheckingUpdate = true;
            UpdateStatus = "확인 중…";
        }).ConfigureAwait(false);

        var outcome = await update.CheckNowAsync(ct).ConfigureAwait(false);

        await dispatcher.InvokeAsync(() =>
        {
            UpdateStatus = outcome switch
            {
                UpdateCheckOutcome.UpToDate => "최신 버전입니다.",
                UpdateCheckOutcome.Downloaded => $"새 버전 {update.Version} 을 받았습니다 — 알림에서 '지금 설치' 를 누르세요.",
                _ => "확인하지 못했습니다. 잠시 뒤 다시 눌러 보세요.",
            };

            IsCheckingUpdate = false;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>[실행해 보기]</c> — 지금 화면이 가리키는 터미널을 <b>상태 폴더에서</b> 열어 본다.
    /// <para>
    /// <b>시험 폴더가 상태 폴더인 이유.</b> 설정 패널은 어느 페인의 폴더인지 모르고, 알려면
    /// 배관이 하나 는다 (<see cref="UseCurrentFolder"/> 처럼 워크스페이스가 넘겨 줘야 한다).
    /// 상태 폴더는 언제나 있고, 눌러 본 사람이 무엇이 열렸는지 바로 안다.
    /// </para>
    /// <para>
    /// <b>잠그지 않는다.</b> 프리셋을 고른 상태에서도 눌러 봐야 그것이 이 기계에 있는지 알
    /// 수 있고, 그것이 이 버튼의 존재 이유다.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task TestTerminalAsync(CancellationToken ct)
    {
        // 포트가 없는 조립에서는 아무 일도 하지 않는다 (Update 와 같은 자리).
        // 상태 폴더를 알아볼 수 없는 것은 있을 수 없지만, 그때도 조용히 물러난다.
        if (externalTools is null || toolLauncher is null || stateFolder is not { } folder)
        {
            return;
        }

        var choice = Current.ResolveTerminal();
        var label = ExternalToolCommand.Label(choice.Preset);

        // 옛 결과를 먼저 지운다. 남아 있으면 방금 누른 것의 답으로 읽힌다 — 고친 뒤 다시
        // 눌렀을 때 '찾을 수 없습니다' 가 그대로 떠 있으면 고친 것이 안 먹은 줄 안다.
        await dispatcher.InvokeAsync(() => TerminalTestResult = null).ConfigureAwait(false);

        string? executable;

        try
        {
            // Custom 은 탐지 대상이 아니다 (IExternalToolCatalog 계약) — 사용자가 적은 것은
            // 찾아 주는 것이 아니라 받는 것이다. 아직 안 적었으면 '없다' 로 접는다.
            executable = choice.Preset == TerminalPreset.Custom
                ? NullIfBlank(choice.Executable)
                : await externalTools.FindTerminalAsync(choice.Preset, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // 계약은 던지지 않는 것이지만, 새면 '이 기계에 없다' 와 같은 자리로 접는다.
            executable = null;
        }

        if (executable is null)
        {
            await ReportAsync(ExternalToolMessages.Describe(LocationErrorKind.NotFound, label))
                .ConfigureAwait(false);

            return;
        }

        LocationErrorKind result;

        try
        {
            result = await toolLauncher
                .LaunchAsync(
                    executable,
                    ExternalToolCommand.FormatArguments(ExternalToolCommand.Resolve(choice).Arguments, folder),
                    folder,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // 커맨드 밖으로 나간 예외는 잡을 사람이 없고 프로세스를 죽인다
            // (.harness/HANDOFF.md §규칙 10). 사용자에게는 열지 못한 것으로 보이면 된다.
            result = LocationErrorKind.Unknown;
        }

        // 성공도 말한다 — 페인의 버튼과 다른 자리다 (PaneViewModel.LaunchToolAsync 는 실패만
        // 낸다). 여기서는 사용자가 답을 보려고 누른 것이라, 아무 말이 없으면 눌리긴 한
        // 건지 알 수 없다.
        await ReportAsync(result == LocationErrorKind.None
                ? $"{label} 을(를) 열었습니다"
                : ExternalToolMessages.Describe(result, label))
            .ConfigureAwait(false);

        Task ReportAsync(string message)
            => dispatcher.InvokeAsync(() => TerminalTestResult = message);
    }

    /// <summary>공백뿐인 것은 '아직 안 적었다' 로 접는다 (<c>JsonSettingsStore</c> 와 같은 수).</summary>
    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// 지금 값을 파일에 남긴다. <b>기다리지 않는다</b> — 부르는 곳이 전부 바인딩 세터이고
    /// 저장은 저장소에 닿는다 (CLAUDE.md §3).
    /// <para>
    /// <b>실패는 삼킨다.</b> 세터 밖으로 나가면 UI 스레드에서 잡을 사람이 없다. 화면은
    /// 사용자가 고른 것을 그대로 두고, 다음 조작이 다시 시도한다.
    /// </para>
    /// </summary>
    private void Persist()
    {
        if (loading)
        {
            return;
        }

        var settings = Current;

        saveWork = SaveAsync(settings);

        async Task SaveAsync(AppSettings value)
        {
            try
            {
                await store.SaveAsync(value, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 위 §요약 참조.
            }
        }
    }
}
