using FlexDir.Core.Locations;
using FlexDir.Core.Tools;

using Xunit;

namespace FlexDir.Core.Tests.Tools;

/// <summary>
/// 외부 도구 버튼 둘(VS Code · 터미널)이 쓰는 순수 판정. 레지스트리도 프로세스도
/// 여기 없다 — 탐지는 <c>FlexDir.Shell</c> 의 카탈로그, 실행은 그쪽 실행기다.
/// </summary>
public class ExternalToolCommandTests
{
    private static LocationId Parse(string input)
    {
        Assert.True(LocationId.TryParse(input, out var location, out _));
        return location;
    }

    // ── CanRunAt ────────────────────────────────────────────────────

    [Fact]
    public void CanRunAt_Null_IsFalse()
    {
        Assert.False(ExternalToolCommand.CanRunAt(null));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users\X")]
    public void CanRunAt_LocalFolder_IsTrue(string path)
    {
        Assert.True(ExternalToolCommand.CanRunAt(Parse(path)));
    }

    /// <summary>
    /// 서버 루트에는 폴더가 아니라 <b>공유</b>가 있다 — 작업 디렉터리로 세울 수 없다.
    /// </summary>
    [Fact]
    public void CanRunAt_NetworkServerRoot_IsFalse()
    {
        Assert.False(ExternalToolCommand.CanRunAt(Parse(@"\\10.10.10.23")));
    }

    /// <summary>
    /// UNC 전체를 막으면 NAS 에서 두 버튼이 영영 죽는다. 막는 것은 서버 루트뿐이다.
    /// </summary>
    [Theory]
    [InlineData(@"\\10.10.10.23\home")]
    [InlineData(@"\\10.10.10.23\home\sub")]
    public void CanRunAt_NetworkShare_IsTrue(string path)
    {
        Assert.True(ExternalToolCommand.CanRunAt(Parse(path)));
    }

    // ── FormatArguments ─────────────────────────────────────────────

    /// <summary>
    /// 따옴표는 틀에 이미 적혀 있다. 여기서 또 붙이면 <c>-d ""C:\Temp""</c> 가 된다.
    /// </summary>
    [Fact]
    public void FormatArguments_QuotesComeFromTemplateOnly()
    {
        Assert.Equal(
            @"-d ""C:\Temp""",
            ExternalToolCommand.FormatArguments(@"-d ""{path}""", Parse(@"C:\Temp")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FormatArguments_BlankTemplate_IsEmpty(string? template)
    {
        Assert.Equal(string.Empty, ExternalToolCommand.FormatArguments(template, Parse(@"C:\Temp")));
    }

    [Fact]
    public void FormatArguments_ReplacesEveryToken()
    {
        Assert.Equal(
            @"--a C:\Temp --b C:\Temp",
            ExternalToolCommand.FormatArguments("--a {path} --b {path}", Parse(@"C:\Temp")));
    }

    /// <summary>설정 화면이 안내하는 계약은 <c>{path}</c> 하나다.</summary>
    [Fact]
    public void FormatArguments_IsCaseSensitive()
    {
        Assert.Equal("{PATH}", ExternalToolCommand.FormatArguments("{PATH}", Parse(@"C:\Temp")));
    }

    /// <summary>작업 디렉터리만으로 여는 프리셋이 있으므로 토큰 없는 틀도 그대로 낸다.</summary>
    [Fact]
    public void FormatArguments_WithoutToken_IsUnchanged()
    {
        Assert.Equal("-NoLogo", ExternalToolCommand.FormatArguments("-NoLogo", Parse(@"C:\Temp")));
    }

    /// <summary>내부 표현의 <c>\\?\UNC\</c> 가 명령줄로 새면 그 인자는 열리지 않는다.</summary>
    [Fact]
    public void FormatArguments_NetworkPath_UsesDisplayForm()
    {
        Assert.Equal(
            @"\\10.10.10.23\home",
            ExternalToolCommand.FormatArguments("{path}", Parse(@"\\10.10.10.23\home")));
    }

    // ── Resolve ─────────────────────────────────────────────────────

    /// <summary>
    /// 이 표는 실측이다 (2026-08-24). 인자가 빈 셋은 일부러 그렇다 — 실행기가
    /// 작업 디렉터리를 언제나 그 폴더로 세운다. <c>wt</c>·<c>pwsh</c> 만 그것을
    /// 물려받지 않아 명시 인자를 받는다.
    /// </summary>
    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal, "wt.exe", @"-d ""{path}""")]
    [InlineData(TerminalPreset.PowerShell7, "pwsh.exe", @"-NoLogo -WorkingDirectory ""{path}""")]
    [InlineData(TerminalPreset.WindowsPowerShell, "powershell.exe", "")]
    [InlineData(TerminalPreset.CommandPrompt, "cmd.exe", "")]
    [InlineData(TerminalPreset.GitBash, "git-bash.exe", "")]
    public void Resolve_Preset(TerminalPreset preset, string executable, string arguments)
    {
        Assert.Equal((executable, arguments), ExternalToolCommand.Resolve(new TerminalChoice(preset)));
    }

    [Fact]
    public void Resolve_Custom_PassesThrough()
    {
        Assert.Equal(
            ("wezterm-gui.exe", @"start --cwd ""{path}"""),
            ExternalToolCommand.Resolve(
                new TerminalChoice(TerminalPreset.Custom, "wezterm-gui.exe", @"start --cwd ""{path}""")));
    }

    /// <summary>덜 적은 사용자 지정은 버튼이 비활성일 뿐이다 — 던질 일이 아니다.</summary>
    [Fact]
    public void Resolve_CustomWithoutValues_IsEmptyPair()
    {
        Assert.Equal((string.Empty, string.Empty), ExternalToolCommand.Resolve(new TerminalChoice(TerminalPreset.Custom)));
    }

    // ── Label ───────────────────────────────────────────────────────

    /// <summary>프리셋을 늘리고 라벨을 빠뜨리면 여기서 잡힌다.</summary>
    [Fact]
    public void Label_AnswersEveryPreset()
    {
        foreach (var preset in Enum.GetValues<TerminalPreset>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ExternalToolCommand.Label(preset)));
        }
    }

    [Theory]
    [InlineData(TerminalPreset.WindowsTerminal, "Windows Terminal")]
    [InlineData(TerminalPreset.PowerShell7, "PowerShell 7")]
    [InlineData(TerminalPreset.WindowsPowerShell, "Windows PowerShell")]
    [InlineData(TerminalPreset.CommandPrompt, "명령 프롬프트")]
    [InlineData(TerminalPreset.GitBash, "Git Bash")]
    [InlineData(TerminalPreset.Custom, "사용자 지정")]
    public void Label_Wording(TerminalPreset preset, string expected)
    {
        Assert.Equal(expected, ExternalToolCommand.Label(preset));
    }
}
