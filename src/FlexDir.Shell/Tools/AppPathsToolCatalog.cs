using FlexDir.Core.Tools;

using Microsoft.Win32;

namespace FlexDir.Shell.Tools;

/// <summary>
/// 이 기계에 설치된 외부 도구를 <c>App Paths</c> 레지스트리와 고정 후보 경로로 찾는
/// <see cref="IExternalToolCatalog"/> 구현체.
///
/// <para>
/// <b>COM 이 아니다</b> — <see cref="Settings.RegistrySystemThemeSource"/> ·
/// <see cref="Locations.KnownFolderList"/> 와 같은 자리다. STA 워커도 정리도 필요 없어
/// <see cref="IDisposable"/> 을 구현하지 않고 <c>AppComposition</c> 의 정리 목록에도
/// 들어가지 않는다.
/// </para>
///
/// <para>
/// <b>그래도 UI 스레드에서 부를 수 없다</b> (CLAUDE.md §3). 레지스트리와 파일 존재 확인이고,
/// <c>%ProgramFiles%</c> 가 리디렉션된 기계에서는 얼마나 걸릴지 모른다. 그래서 전부
/// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> 안에서 한다.
/// </para>
///
/// <para>
/// <b>프로세스를 띄우지 않는다.</b> 여기는 "있는가" 만 답한다 — 실행은
/// <see cref="IExternalToolLauncher"/> 의 일이다.
/// </para>
///
/// <para><b>탐지 순서</b> (앞의 것이 이기고, 파일이 실제로 있을 때만 낸다):</para>
/// <list type="table">
///   <item><term>에디터 (<c>code.exe</c>)</term>
///     <description>App Paths HKCU → HKLM. <b>그 둘뿐이다</b></description></item>
///   <item><term><see cref="TerminalPreset.WindowsTerminal"/></term>
///     <description><c>%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe</c> → App Paths HKCU → HKLM</description></item>
///   <item><term><see cref="TerminalPreset.PowerShell7"/></term>
///     <description>App Paths HKCU → HKLM → <c>%ProgramFiles%\PowerShell\7\pwsh.exe</c></description></item>
///   <item><term><see cref="TerminalPreset.WindowsPowerShell"/></term>
///     <description><c>%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe</c></description></item>
///   <item><term><see cref="TerminalPreset.CommandPrompt"/></term>
///     <description><c>%SystemRoot%\System32\cmd.exe</c></description></item>
///   <item><term><see cref="TerminalPreset.GitBash"/></term>
///     <description><c>%ProgramFiles%</c> → <c>%ProgramW6432%</c> → <c>%ProgramFiles(x86)%</c> 의
///     <c>\Git\git-bash.exe</c>. <b>App Paths 에 없다</b> (실측 2026-08-24)</description></item>
///   <item><term><see cref="TerminalPreset.Custom"/></term>
///     <description><b>언제나 <see langword="null"/></b> — 사용자가 적은 것은 탐지 대상이 아니다</description></item>
/// </list>
///
/// <para>
/// <b>에디터는 <c>PATH</c> 를 보지 않는다.</b> 탐지원이 하나여야 App Paths 항목을 치웠을 때
/// 버튼이 실제로 비활성이 된다 — <c>PATH</c> 폴백이 있으면 셸 래퍼(<c>code</c>)가 계속
/// 잡혀 그 확인이 영영 성립하지 않는다.
/// </para>
/// </summary>
public sealed class AppPathsToolCatalog : IExternalToolCatalog
{
    private const string AppPathsSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private const string CurrentUserHive = "HKEY_CURRENT_USER";
    private const string LocalMachineHive = "HKEY_LOCAL_MACHINE";

    private const string EditorExecutable = "code.exe";
    private const string WindowsTerminalExecutable = "wt.exe";
    private const string PowerShell7Executable = "pwsh.exe";

    private readonly Func<string, string?> appPath;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, string?> environmentVariable;

    public AppPathsToolCatalog()
        : this(ReadAppPath, File.Exists, Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>
    /// 조회 셋을 바꿔 끼운다 (<see cref="Presentation.ShellThumbnailSource"/> 와 같은 수).
    /// 실물 레지스트리는 기계마다 다른 답을 내므로 탐지 순서를 그것으로 채점할 수 없다 —
    /// 이 기계에 VS Code 가 있는지, Git 이 어느 <c>Program Files</c> 에 있는지가 게이트를
    /// 가르면 안 된다.
    /// </summary>
    /// <param name="appPath">레지스트리 키 전체 경로를 받아 그 키의 기본값을 낸다. 없으면 <c>null</c>.</param>
    /// <param name="fileExists">그 경로에 파일이 실제로 있는가.</param>
    /// <param name="environmentVariable">환경 변수 값. 없으면 <c>null</c>.</param>
    internal AppPathsToolCatalog(
        Func<string, string?> appPath,
        Func<string, bool> fileExists,
        Func<string, string?> environmentVariable)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(environmentVariable);

        this.appPath = appPath;
        this.fileExists = fileExists;
        this.environmentVariable = environmentVariable;
    }

    public async ValueTask<string?> FindEditorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() => FirstPresent(EditorCandidates(), ct), ct).ConfigureAwait(false);
    }

    public async ValueTask<string?> FindTerminalAsync(TerminalPreset preset, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() => FirstPresent(TerminalCandidates(preset), ct), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 앞에서부터 실제로 있는 첫 후보. 하나도 없으면 <see langword="null"/> 이며 그것은
    /// 오류가 아니다 (포트 계약 1).
    /// </summary>
    private string? FirstPresent(IReadOnlyList<Func<string?>> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            // 후보마다 본다 — 각각이 레지스트리와 저장소에 닿고, 셋까지 늘어난다.
            ct.ThrowIfCancellationRequested();

            if (Present(candidate) is { } path)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 후보 하나를 실제 경로로 굳힌다. 값이 없거나 · 파일이 없거나 · 조회가 던지면
    /// <see langword="null"/> 이다.
    /// <para>
    /// <b>실패를 삼키는 자리가 여기인 것에 이유가 있다.</b> 하이브 하나가 막혀 있어도
    /// (<c>SecurityException</c>) 다음 후보로 넘어가야 한다 — 안쪽 조회에서 삼키면 그
    /// 구분을 못 하고, 바깥에서 삼키면 남은 후보가 통째로 날아간다.
    /// </para>
    /// </summary>
    private string? Present(Func<string?> candidate)
    {
        try
        {
            var raw = candidate();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var path = Unquote(raw);

            return path.Length != 0 && fileExists(path) ? path : null;
        }
        catch (Exception)
        {
            // 외부 도구를 못 찾는 것이 앱을 못 쓰게 하면 안 된다 (KnownFolderList 와 같은
            // 판단). 취소는 여기로 올 수 없다 — 후보 조회에 토큰을 주지 않는다.
            return null;
        }
    }

    /// <summary>
    /// App Paths 값은 따옴표에 싸여 있을 수 있다 (<c>"C:\...\Code.exe"</c>). 벗기지 않으면
    /// 파일 존재 확인이 언제나 실패한다.
    /// </summary>
    private static string Unquote(string value) => value.Trim().Trim('"').Trim();

    /// <summary>
    /// App Paths 후보 하나. 키 전체 경로를 만드는 자리가 여기뿐이라 하이브 순서가 한눈에
    /// 보인다.
    /// </summary>
    private Func<string?> FromAppPaths(string hive, string executable)
        => () => appPath($@"{hive}\{AppPathsSubKey}\{executable}");

    /// <summary>
    /// 환경 변수 아래의 고정 경로 후보. <b>변수가 없으면 그 후보를 건너뛴다</b> —
    /// <c>%ProgramFiles(x86)%</c> 는 32비트 Windows 에 아예 없다.
    /// </summary>
    private Func<string?> Below(string variable, string relativePath)
        => () => environmentVariable(variable) is { Length: > 0 } root
            ? Path.Combine(root, relativePath)
            : null;

    /// <summary>
    /// <b>HKCU 가 먼저다.</b> VS Code 의 사용자 설치본은 HKLM 에 자국을 남기지 않으므로
    /// 순서를 뒤집으면 그것을 못 찾는다 (이 기계에서 실측 · 2026-08-24).
    /// </summary>
    private IReadOnlyList<Func<string?>> EditorCandidates() =>
    [
        FromAppPaths(CurrentUserHive, EditorExecutable),
        FromAppPaths(LocalMachineHive, EditorExecutable),
    ];

    private IReadOnlyList<Func<string?>> TerminalCandidates(TerminalPreset preset) => preset switch
    {
        // Store 앱 실행 별칭이 먼저다 — wt.exe 는 그쪽에 서고 App Paths 는 부수적이다.
        TerminalPreset.WindowsTerminal =>
        [
            Below("LOCALAPPDATA", @"Microsoft\WindowsApps\wt.exe"),
            FromAppPaths(CurrentUserHive, WindowsTerminalExecutable),
            FromAppPaths(LocalMachineHive, WindowsTerminalExecutable),
        ],

        // pwsh 는 설치 위치를 고를 수 있어 레지스트리가 먼저다. 기본 위치는 폴백이다.
        TerminalPreset.PowerShell7 =>
        [
            FromAppPaths(CurrentUserHive, PowerShell7Executable),
            FromAppPaths(LocalMachineHive, PowerShell7Executable),
            Below("ProgramFiles", @"PowerShell\7\pwsh.exe"),
        ],

        // OS 구성 요소 둘은 자리가 정해져 있다 — 레지스트리를 물을 이유가 없다.
        TerminalPreset.WindowsPowerShell =>
        [
            Below("SystemRoot", @"System32\WindowsPowerShell\v1.0\powershell.exe"),
        ],

        TerminalPreset.CommandPrompt =>
        [
            Below("SystemRoot", @"System32\cmd.exe"),
        ],

        // git-bash.exe 는 App Paths 에 자국을 남기지 않는다 (실측). 32비트 설치본까지 본다.
        TerminalPreset.GitBash =>
        [
            Below("ProgramFiles", @"Git\git-bash.exe"),
            Below("ProgramW6432", @"Git\git-bash.exe"),
            Below("ProgramFiles(x86)", @"Git\git-bash.exe"),
        ],

        // 사용자가 적은 경로는 탐지의 대상이 아니라 입력이다 (포트 계약). 조회조차 나가지 않는다.
        TerminalPreset.Custom => [],

        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "알 수 없는 프리셋이다."),
    };

    /// <summary>
    /// App Paths 키의 <b>기본값</b>(이름 없는 값)이 실행 파일 전체 경로다. 키가 없으면
    /// <see cref="Registry.GetValue(string, string, object)"/> 가 <see langword="null"/> 을 낸다.
    /// </summary>
    private static string? ReadAppPath(string key) => Registry.GetValue(key, string.Empty, null) as string;
}
