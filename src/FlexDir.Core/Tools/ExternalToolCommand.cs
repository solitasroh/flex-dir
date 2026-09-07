using FlexDir.Core.Locations;

namespace FlexDir.Core.Tools;

/// <summary>
/// 터미널 프리셋. <see cref="Custom"/> 은 사용자가 실행 파일과 인자를 직접 적은 것이다.
/// </summary>
public enum TerminalPreset
{
    WindowsTerminal,
    PowerShell7,
    WindowsPowerShell,
    CommandPrompt,
    GitBash,
    Custom,
}

/// <summary>
/// 어느 터미널을 어떻게 열 것인가. <paramref name="Executable"/>·<paramref name="Arguments"/> 는
/// <see cref="TerminalPreset.Custom"/> 일 때만 쓰인다.
/// </summary>
public sealed record TerminalChoice(
    TerminalPreset Preset,
    string? Executable = null,
    string? Arguments = null);

/// <summary>
/// 외부 도구 버튼 둘(VS Code · 터미널)의 순수 판정. <b>실행하지 않는다</b> —
/// 실제 위치를 찾는 것과 프로세스를 띄우는 것은 <c>FlexDir.Shell</c> 의 일이다
/// (CLAUDE.md §1 — Core 는 파일시스템에 닿지 않는다).
/// </summary>
public static class ExternalToolCommand
{
    /// <summary>인자 틀이 폴더 경로를 부르는 이름. <b>계약은 이 하나뿐이다.</b></summary>
    public const string PathToken = "{path}";

    /// <summary>
    /// 이 폴더에서 외부 도구를 실행할 수 있는가.
    /// <para>
    /// 판정이 <see cref="LocationId.IsNetworkServer"/> 인 것에 이유가 있다.
    /// <c>\\server</c> 에 있는 것은 폴더가 아니라 <b>공유</b>라 작업 디렉터리로 세울 수
    /// 없지만, <c>\\server\share</c> 아래는 정상적인 작업 디렉터리다 —
    /// <see cref="LocationId.IsNetwork"/> 로 막으면 NAS 에서 두 버튼이 영영 죽는다.
    /// </para>
    /// </summary>
    public static bool CanRunAt(LocationId? folder)
        => folder is not null && !folder.IsNetworkServer;

    /// <summary>
    /// 인자 틀의 <see cref="PathToken"/> 을 폴더의 표시형 경로로 바꾼다.
    /// <para>
    /// <b>따옴표를 붙이지 않는다.</b> 필요하면 틀 쪽에 이미 적혀 있고(<c>-d "{path}"</c>),
    /// 여기서 또 붙이면 <c>-d ""C:\Temp""</c> 가 된다. 경로는
    /// <see cref="LocationId.Value"/> 가 아니라 <see cref="LocationId.DisplayPath"/> 다 —
    /// 내부 표현의 <c>\\?\UNC\</c> 가 명령줄로 새면 그 인자는 열리지 않는다.
    /// </para>
    /// </summary>
    public static string FormatArguments(string? template, LocationId folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        // 대소문자를 구분한다. 설정 화면이 {path} 하나만 안내하고 그 하나만 계약이다 —
        // {PATH} 까지 받아 주면 어느 표기가 되는지 사용자가 시험해 봐야 한다.
        return template.Replace(PathToken, folder.DisplayPath, StringComparison.Ordinal);
    }

    /// <summary>
    /// 프리셋이 쓰는 실행 파일 이름과 기본 인자 틀. <see cref="TerminalPreset.Custom"/> 은
    /// <paramref name="choice"/> 의 값을 그대로 낸다 (덜 적었으면 빈 문자열 — 그것은
    /// 버튼이 비활성일 뿐이라 던질 일이 아니다).
    /// <para>
    /// <b>인자가 빈 셋은 일부러 그렇다.</b> 실행기가 <c>WorkingDirectory</c> 를 언제나 그
    /// 폴더로 세우므로 자기 작업 디렉터리에서 뜨는 프로그램은 인자가 필요 없다.
    /// <c>wt.exe</c>·<c>pwsh.exe</c> 만 예외로, 둘은 작업 디렉터리를 물려받지 않고 자기
    /// 기본 프로필의 시작 폴더로 간다 (이 기계에서 실측 · 2026-08-24).
    /// </para>
    /// <para>
    /// <b><c>Executable</c> 은 경로가 아니라 파일 이름이다.</b> 실제 위치를 찾는 것은
    /// <c>FlexDir.Shell</c> 의 카탈로그다.
    /// </para>
    /// </summary>
    public static (string Executable, string Arguments) Resolve(TerminalChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        return choice.Preset switch
        {
            TerminalPreset.WindowsTerminal => ("wt.exe", $"-d \"{PathToken}\""),
            TerminalPreset.PowerShell7 => ("pwsh.exe", $"-NoLogo -WorkingDirectory \"{PathToken}\""),
            TerminalPreset.WindowsPowerShell => ("powershell.exe", string.Empty),
            TerminalPreset.CommandPrompt => ("cmd.exe", string.Empty),
            TerminalPreset.GitBash => ("git-bash.exe", string.Empty),
            TerminalPreset.Custom => (choice.Executable ?? string.Empty, choice.Arguments ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice.Preset, "알 수 없는 프리셋이다."),
        };
    }

    /// <summary>프리셋의 사람이 읽는 이름 (설정 창의 목록과 상태 문구가 쓴다).</summary>
    public static string Label(TerminalPreset preset)
        => preset switch
        {
            TerminalPreset.WindowsTerminal => "Windows Terminal",
            TerminalPreset.PowerShell7 => "PowerShell 7",
            TerminalPreset.WindowsPowerShell => "Windows PowerShell",
            TerminalPreset.CommandPrompt => "명령 프롬프트",
            TerminalPreset.GitBash => "Git Bash",
            TerminalPreset.Custom => "사용자 지정",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "알 수 없는 프리셋이다."),
        };
}
