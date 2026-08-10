using System.Reflection;

using FlexDir.Host.Startup;

using Xunit;

namespace FlexDir.Host.Tests.Startup;

/// <summary>
/// 설정 창이 내는 버전 (docs/PRD-v2.md §12).
/// <para>
/// <b>정본은 <c>Directory.Build.props</c> 의 <c>&lt;Version&gt;</c> 이다</b> — 자동 업데이트가
/// "설치된 버전과 피드의 최신 버전을 견준다" 로 서 있으므로, 화면에 다른 값이 나오면 사용자가
/// 릴리스 목록과 대조할 수 없다.
/// </para>
/// </summary>
public class ProductVersionTests
{
    /// <summary>버전 문자열만 든 가짜 어셈블리 속성. 실제 어셈블리를 만들지 않고 규칙만 잰다.</summary>
    private static string Read(string? informational, string? assemblyVersion = "1.2.3.4")
        => ProductVersion.Describe(informational, assemblyVersion);

    [Fact]
    public void PlainVersion_IsUsedAsIs()
    {
        Assert.Equal("0.3.1", Read("0.3.1"));
    }

    [Fact]
    public void BuildMetadata_IsCutOff()
    {
        // SourceLink 를 켜면 InformationalVersion 에 커밋 해시가 붙는다
        // (0.3.1+7f2c9a1...). 사용자가 릴리스 목록과 대조하는 값이라 그것까지 내면
        // 같은 버전인데 달라 보인다.
        Assert.Equal("0.3.1", Read("0.3.1+7f2c9a1e0b5d4c3a2b1908f7e6d5c4b3a2910fed"));
    }

    [Fact]
    public void PrereleaseTag_IsKept()
    {
        // 빌드 메타데이터('+')와 달리 프리릴리스('-')는 버전의 일부다 — semver 가 그것으로
        // 순서를 정하고 vpk 도 패키지 이름에 그대로 쓴다.
        Assert.Equal("0.4.0-rc.1", Read("0.4.0-rc.1"));
    }

    [Fact]
    public void WithoutAnInformationalVersion_FallsBackToTheAssemblyVersion()
    {
        // 없을 수 있는 값이다. 빈 문자열을 내면 설정 창에 버전 자리가 비어 보인다.
        Assert.Equal("1.2.3.4", Read(null));
        Assert.Equal("1.2.3.4", Read("   "));
    }

    [Fact]
    public void WithNeither_SaysItDoesNotKnow()
    {
        // 던지지 않는다 — 조립 경로가 이것을 지나므로 여기서 던지면 앱이 안 뜬다.
        Assert.Equal("알 수 없음", Read(null, null));
    }

    [Fact]
    public void Current_ReadsThisAssembly_AndIsNotEmpty()
    {
        // 실제 경로도 한 번 지난다. 값 자체는 빌드마다 다르므로 모양만 본다.
        Assert.False(string.IsNullOrWhiteSpace(ProductVersion.Current));
        Assert.DoesNotContain('+', ProductVersion.Current);
    }

    [Fact]
    public void Current_MatchesWhatTheAssemblyCarries()
    {
        // Directory.Build.props 가 이 어셈블리에 실어 준 값과 같아야 한다 — 그 파일이
        // 정본이라는 진술이 여기서 한 번 채점된다.
        var carried = typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(ProductVersion.Describe(carried, null), ProductVersion.Current);
    }
}
