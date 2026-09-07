using System.Security;

using FlexDir.Core.Tools;
using FlexDir.Shell.Tools;

using Xunit;

namespace FlexDir.Shell.Tests.Tools;

/// <summary>
/// <see cref="AppPathsToolCatalog"/> 의 탐지 규칙.
///
/// <para>
/// <b>레지스트리를 실물로 때리지 않는다.</b> 이 기계에 VS Code 가 있는지, Git 이
/// <c>%ProgramFiles%</c> 에 있는지는 기계마다 다르고 그것으로 채점하면 게이트가 기계마다
/// 갈린다. 그래서 조회 셋(레지스트리 · 파일 존재 · 환경 변수)을 <c>internal</c> 생성자로
/// 바꿔 끼운다 — <see cref="FlexDir.Shell.Presentation.ShellThumbnailSource"/> 가 쓰는 수와
/// 같다.
/// </para>
///
/// <para>
/// <b>실물 확인은 <c>cmd.exe</c> 하나뿐이다</b> (맨 아래). 모든 Windows 에 있으므로
/// 기계마다 갈리지 않는 유일한 후보다.
/// </para>
/// </summary>
public class AppPathsToolCatalogTests
{
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private const string LocalAppData = @"C:\Users\T\AppData\Local";
    private const string ProgramFiles = @"C:\Program Files";

    /// <summary>
    /// 실물에서는 <c>%ProgramW6432%</c> 가 <c>%ProgramFiles%</c> 와 같은 값이지만, 여기서는
    /// <b>둘째 후보로 넘어갔다는 것을 경로로 구분하기 위해</b> 일부러 다른 뿌리를 준다.
    /// </summary>
    private const string ProgramW6432 = @"C:\Program Files (64)";

    private const string ProgramFilesX86 = @"C:\Program Files (x86)";
    private const string SystemRoot = @"C:\Windows";

    private const string Editor = @"C:\Users\T\AppData\Local\Programs\Microsoft VS Code\Code.exe";
    private const string MachineEditor = @"C:\Program Files\Microsoft VS Code\Code.exe";

    private const string WindowsAppsTerminal = LocalAppData + @"\Microsoft\WindowsApps\wt.exe";
    private const string RegisteredTerminal = @"C:\Terminal\wt.exe";
    private const string RegisteredPowerShell = @"C:\Registered\pwsh.exe";
    private const string ProgramFilesPowerShell = ProgramFiles + @"\PowerShell\7\pwsh.exe";
    private const string SystemPowerShell = SystemRoot + @"\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string SystemCommandPrompt = SystemRoot + @"\System32\cmd.exe";
    private const string ProgramFilesGitBash = ProgramFiles + @"\Git\git-bash.exe";
    private const string ProgramW6432GitBash = ProgramW6432 + @"\Git\git-bash.exe";
    private const string ProgramFilesX86GitBash = ProgramFilesX86 + @"\Git\git-bash.exe";

    private static string CurrentUserKey(string executable) => $@"HKEY_CURRENT_USER\{AppPaths}\{executable}";

    private static string LocalMachineKey(string executable) => $@"HKEY_LOCAL_MACHINE\{AppPaths}\{executable}";

    // ── 에디터 ────────────────────────────────────────────────────────

    [Fact]
    public async Task Editor_InCurrentUserOnly_IsFound()
    {
        var machine = new Machine();
        machine.Install(CurrentUserKey("code.exe"), Editor);

        Assert.Equal(Editor, await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Editor_InLocalMachineOnly_IsFound()
    {
        var machine = new Machine();
        machine.Install(LocalMachineKey("code.exe"), MachineEditor);

        Assert.Equal(MachineEditor, await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    /// <summary>
    /// 사용자 설치본(HKCU)이 이긴다. 순서를 뒤집으면 이 기계의 VS Code 를 못 찾는다 —
    /// HKLM 에 자국이 없기 때문이다 (실측 2026-08-24).
    /// </summary>
    [Fact]
    public async Task Editor_InBothHives_TakesTheCurrentUserOne()
    {
        var machine = new Machine();
        machine.Install(CurrentUserKey("code.exe"), Editor);
        machine.Install(LocalMachineKey("code.exe"), MachineEditor);

        Assert.Equal(Editor, await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    /// <summary>지운 프로그램의 App Paths 항목은 남는다. 파일이 없으면 없는 것이다.</summary>
    [Fact]
    public async Task Editor_InTheRegistryButMissingFromDisk_IsNotFound()
    {
        var machine = new Machine();
        machine.Registry[CurrentUserKey("code.exe")] = Editor;

        Assert.Null(await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    /// <summary>
    /// <b>탐지원은 App Paths 하나다.</b> <c>PATH</c> 폴백이 생기면 셸 래퍼가 잡혀,
    /// "App Paths 를 임시로 옮기면 버튼이 비활성이 된다" 는 사람 확인이 영영 실패한다.
    /// </summary>
    [Fact]
    public async Task Editor_NotInTheRegistry_IsNotFoundEvenWhenItSitsOnThePath()
    {
        var machine = new Machine();
        machine.Environment["PATH"] = @"C:\tools;C:\Program Files\Microsoft VS Code";
        machine.Files.Add(@"C:\tools\code.exe");
        machine.Files.Add(MachineEditor);

        Assert.Null(await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Editor_WithAQuotedRegistryValue_IsUnwrapped()
    {
        var machine = new Machine();
        machine.Registry[CurrentUserKey("code.exe")] = $"\"{Editor}\"";
        machine.Files.Add(Editor);

        Assert.Equal(Editor, await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    /// <summary>접근이 막힌 하이브는 "못 찾았다" 다 — 포트 계약 1 (실패는 던지지 않는다).</summary>
    [Fact]
    public async Task Editor_WhenTheRegistryThrows_IsNotFound()
    {
        var machine = new Machine { RegistryThrows = true };
        machine.Install(CurrentUserKey("code.exe"), Editor);

        Assert.Null(await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Editor_WhenTheFileCheckThrows_IsNotFound()
    {
        var machine = new Machine { FileCheckThrows = true };
        machine.Install(CurrentUserKey("code.exe"), Editor);

        Assert.Null(await machine.Catalog().FindEditorAsync(CancellationToken.None));
    }

    /// <summary>캐시하지 않는다는 증거 그 하나 — 조회가 부를 때마다 나간다 (포트 계약 2).</summary>
    [Fact]
    public async Task FindEditorAsync_CalledTwice_AsksTheRegistryAgain()
    {
        var machine = new Machine();
        machine.Install(CurrentUserKey("code.exe"), Editor);
        var catalog = machine.Catalog();

        await catalog.FindEditorAsync(CancellationToken.None);
        await catalog.FindEditorAsync(CancellationToken.None);

        Assert.Equal([CurrentUserKey("code.exe"), CurrentUserKey("code.exe")], machine.RegistryQueries);
    }

    /// <summary>증거 그 둘 — 그 사이에 지웠으면 다음 답이 달라야 한다.</summary>
    [Fact]
    public async Task FindEditorAsync_AfterTheEditorIsRemoved_IsNotFoundAnymore()
    {
        var machine = new Machine();
        machine.Install(CurrentUserKey("code.exe"), Editor);
        var catalog = machine.Catalog();

        Assert.Equal(Editor, await catalog.FindEditorAsync(CancellationToken.None));

        machine.Registry.Remove(CurrentUserKey("code.exe"));
        machine.Files.Remove(Editor);

        Assert.Null(await catalog.FindEditorAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FindEditorAsync_WithACancelledToken_Throws()
    {
        var machine = new Machine();
        machine.Install(CurrentUserKey("code.exe"), Editor);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await machine.Catalog().FindEditorAsync(cts.Token));
    }

    // ── 터미널 ────────────────────────────────────────────────────────

    /// <summary>
    /// 후보가 전부 있는 기계에서 프리셋마다 <b>첫 후보</b>가 나온다. 뒤 후보도 실제로
    /// 존재하므로, 순서가 뒤집히면 반드시 다른 경로가 나와 잡힌다.
    /// </summary>
    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal, WindowsAppsTerminal)]
    [InlineData(TerminalPreset.PowerShell7, RegisteredPowerShell)]
    [InlineData(TerminalPreset.WindowsPowerShell, SystemPowerShell)]
    [InlineData(TerminalPreset.CommandPrompt, SystemCommandPrompt)]
    [InlineData(TerminalPreset.GitBash, ProgramFilesGitBash)]
    public async Task Terminal_OnAFullyEquippedMachine_TakesTheFirstCandidate(TerminalPreset preset, string expected)
    {
        var machine = FullyEquipped();

        Assert.Equal(expected, await machine.Catalog().FindTerminalAsync(preset, CancellationToken.None));
    }

    [Fact]
    public async Task WindowsTerminal_MissingFromWindowsApps_FallsBackToAppPaths()
    {
        var machine = FullyEquipped();
        machine.Files.Remove(WindowsAppsTerminal);

        Assert.Equal(
            RegisteredTerminal,
            await machine.Catalog().FindTerminalAsync(TerminalPreset.WindowsTerminal, CancellationToken.None));
    }

    [Fact]
    public async Task PowerShell7_MissingFromTheRegistry_FallsBackToProgramFiles()
    {
        var machine = FullyEquipped();
        machine.Registry.Remove(CurrentUserKey("pwsh.exe"));
        machine.Registry.Remove(LocalMachineKey("pwsh.exe"));

        Assert.Equal(
            ProgramFilesPowerShell,
            await machine.Catalog().FindTerminalAsync(TerminalPreset.PowerShell7, CancellationToken.None));
    }

    /// <summary>레지스트리가 던져도 다음 후보로 넘어간다 — 삼키는 것이 흐름을 끊지 않는다.</summary>
    [Fact]
    public async Task PowerShell7_WhenTheRegistryThrows_FallsBackToProgramFiles()
    {
        var machine = FullyEquipped();
        machine.RegistryThrows = true;

        Assert.Equal(
            ProgramFilesPowerShell,
            await machine.Catalog().FindTerminalAsync(TerminalPreset.PowerShell7, CancellationToken.None));
    }

    [Fact]
    public async Task GitBash_MissingFromProgramFiles_FallsBackToProgramW6432()
    {
        var machine = FullyEquipped();
        machine.Files.Remove(ProgramFilesGitBash);

        Assert.Equal(
            ProgramW6432GitBash,
            await machine.Catalog().FindTerminalAsync(TerminalPreset.GitBash, CancellationToken.None));
    }

    [Fact]
    public async Task GitBash_MissingFromBothProgramFiles_FallsBackToTheX86One()
    {
        var machine = FullyEquipped();
        machine.Files.Remove(ProgramFilesGitBash);
        machine.Files.Remove(ProgramW6432GitBash);

        Assert.Equal(
            ProgramFilesX86GitBash,
            await machine.Catalog().FindTerminalAsync(TerminalPreset.GitBash, CancellationToken.None));
    }

    /// <summary>
    /// 없는 환경 변수는 <b>던지지 않고</b> 건너뛴다. <c>%ProgramFiles(x86)%</c> 는 32비트
    /// Windows 에 아예 없다.
    /// </summary>
    [Fact]
    public async Task GitBash_WithNoProgramFilesVariables_IsNotFound()
    {
        var machine = FullyEquipped();
        machine.Environment.Remove("ProgramFiles");
        machine.Environment.Remove("ProgramW6432");
        machine.Environment.Remove("ProgramFiles(x86)");

        Assert.Null(await machine.Catalog().FindTerminalAsync(TerminalPreset.GitBash, CancellationToken.None));
    }

    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal)]
    [InlineData(TerminalPreset.PowerShell7)]
    [InlineData(TerminalPreset.WindowsPowerShell)]
    [InlineData(TerminalPreset.CommandPrompt)]
    [InlineData(TerminalPreset.GitBash)]
    public async Task Terminal_OnABareMachine_IsNotFound(TerminalPreset preset)
    {
        // 레지스트리도 환경 변수도 비어 있다 — 없는 변수를 묻는 경로가 여기서 함께 돈다.
        var machine = new Machine();

        Assert.Null(await machine.Catalog().FindTerminalAsync(preset, CancellationToken.None));
    }

    /// <summary>
    /// 사용자가 적은 것은 탐지의 대상이 아니라 입력이다 (포트 계약). 찾으려 들지도 않으므로
    /// 조회 자체가 나가지 않는다.
    /// </summary>
    [Fact]
    public async Task Custom_IsNeverFound_EvenOnAFullyEquippedMachine()
    {
        var machine = FullyEquipped();

        Assert.Null(await machine.Catalog().FindTerminalAsync(TerminalPreset.Custom, CancellationToken.None));
        Assert.Empty(machine.RegistryQueries);
    }

    [Fact]
    public async Task FindTerminalAsync_CalledTwice_AsksTheRegistryAgain()
    {
        var machine = FullyEquipped();
        var catalog = machine.Catalog();

        await catalog.FindTerminalAsync(TerminalPreset.PowerShell7, CancellationToken.None);
        await catalog.FindTerminalAsync(TerminalPreset.PowerShell7, CancellationToken.None);

        Assert.Equal([CurrentUserKey("pwsh.exe"), CurrentUserKey("pwsh.exe")], machine.RegistryQueries);
    }

    [Fact]
    public async Task FindTerminalAsync_WithACancelledToken_Throws()
    {
        var machine = FullyEquipped();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await machine.Catalog().FindTerminalAsync(TerminalPreset.CommandPrompt, cts.Token));
    }

    // ── 실물 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 인자 없는 공개 생성자가 실제로 조회를 세운다는 확인. <c>cmd.exe</c> 만 쓰는 이유는
    /// 위 클래스 주석 참조 — <c>code.exe</c>·<c>wt.exe</c>·<c>git-bash.exe</c> 는 기계마다
    /// 있고 없다.
    /// </summary>
    [Fact]
    public async Task FindTerminalAsync_OnThisMachine_FindsCommandPrompt()
    {
        var found = await new AppPathsToolCatalog()
            .FindTerminalAsync(TerminalPreset.CommandPrompt, CancellationToken.None);

        Assert.NotNull(found);
        Assert.EndsWith("cmd.exe", found, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(found));
    }

    /// <summary>표의 후보가 전부 있는 기계. 첫 후보 판정과 폴백 판정이 둘 다 여기서 출발한다.</summary>
    private static Machine FullyEquipped()
    {
        var machine = new Machine
        {
            Environment =
            {
                ["LOCALAPPDATA"] = LocalAppData,
                ["ProgramFiles"] = ProgramFiles,
                ["ProgramW6432"] = ProgramW6432,
                ["ProgramFiles(x86)"] = ProgramFilesX86,
                ["SystemRoot"] = SystemRoot,
            },
        };

        machine.Install(CurrentUserKey("wt.exe"), RegisteredTerminal);
        machine.Install(CurrentUserKey("pwsh.exe"), RegisteredPowerShell);

        machine.Files.Add(WindowsAppsTerminal);
        machine.Files.Add(ProgramFilesPowerShell);
        machine.Files.Add(SystemPowerShell);
        machine.Files.Add(SystemCommandPrompt);
        machine.Files.Add(ProgramFilesGitBash);
        machine.Files.Add(ProgramW6432GitBash);
        machine.Files.Add(ProgramFilesX86GitBash);

        return machine;
    }

    /// <summary>가짜 기계 하나 — 레지스트리 · 디스크 · 환경 변수.</summary>
    private sealed class Machine
    {
        public Dictionary<string, string?> Registry { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>나간 레지스트리 조회. 순서와 횟수가 곧 탐지 순서와 캐시 여부다.</summary>
        public List<string> RegistryQueries { get; } = [];

        public bool RegistryThrows { get; set; }

        public bool FileCheckThrows { get; set; }

        public AppPathsToolCatalog Catalog() => new(ReadRegistry, Exists, ReadEnvironment);

        /// <summary>레지스트리에 적고 파일도 놓는다 — 실제로 설치된 상태.</summary>
        public void Install(string key, string path)
        {
            Registry[key] = path;
            Files.Add(path);
        }

        private string? ReadRegistry(string key)
        {
            RegistryQueries.Add(key);

            if (RegistryThrows)
            {
                throw new SecurityException("이 하이브를 읽을 수 없다.");
            }

            return Registry.GetValueOrDefault(key);
        }

        private bool Exists(string path)
        {
            if (FileCheckThrows)
            {
                throw new IOException("경로를 확인할 수 없다.");
            }

            return Files.Contains(path);
        }

        private string? ReadEnvironment(string variable) => Environment.GetValueOrDefault(variable);
    }
}
