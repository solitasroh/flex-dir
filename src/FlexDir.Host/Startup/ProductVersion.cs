using System.Reflection;

namespace FlexDir.Host.Startup;

/// <summary>
/// 화면에 낼 제품 버전 (docs/PRD-v2.md §12 설정 창 §정보).
///
/// <para>
/// <b>정본은 <c>Directory.Build.props</c> 의 <c>&lt;Version&gt;</c> 이다.</b> 자동 업데이트가
/// "설치된 버전과 피드의 최신 버전을 견준다" 하나로 도므로 (<c>scripts/pack.ps1</c>),
/// 설정 창이 다른 값을 내면 사용자가 릴리스 목록과 대조할 수 없다. 그래서 상수를 두지 않고
/// <b>빌드가 어셈블리에 실어 준 값</b>을 읽는다 — 두 곳에 적으면 반드시 갈린다.
/// </para>
///
/// <para>
/// <b>App 이 아니라 Host 가 읽는다.</b> ViewModel 이 진입 어셈블리를 직접 읽으면 테스트에서는
/// 테스트 실행기의 버전이 나온다 (<c>SettingsViewModel</c> 의 <c>version</c> 인자).
/// </para>
/// </summary>
internal static class ProductVersion
{
    /// <summary>버전을 알 수 없을 때. 빈 문자열을 내면 설정 창의 버전 자리가 비어 보인다.</summary>
    private const string Unknown = "알 수 없음";

    /// <summary>이 어셈블리가 실어 온 버전. 조립이 <c>SettingsViewModel</c> 에 넘긴다.</summary>
    public static string Current { get; } = Describe(
        typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        null);

    /// <summary>
    /// 표시할 문자열을 고른다. 어셈블리를 읽는 것과 나누어 두는 이유는 <b>규칙만 채점하기</b>
    /// 위해서다 — 실제 값은 빌드마다 다르다.
    /// </summary>
    /// <param name="informationalVersion">
    /// <c>AssemblyInformationalVersion</c>. SourceLink 가 켜져 있으면 <c>+커밋해시</c> 가
    /// 붙는다.
    /// </param>
    /// <param name="assemblyVersion">
    /// <c>AssemblyVersion</c>. 위가 없을 때만 쓴다.
    /// </param>
    public static string Describe(string? informationalVersion, string? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            // '+' 뒤는 semver 의 빌드 메타데이터다. 순서에 관여하지 않고 사용자가 대조할
            // 값도 아니다 — 프리릴리스('-')는 반대로 버전의 일부라 그대로 둔다.
            var metadata = informationalVersion.IndexOf('+');

            return metadata < 0 ? informationalVersion : informationalVersion[..metadata];
        }

        return string.IsNullOrWhiteSpace(assemblyVersion) ? Unknown : assemblyVersion;
    }
}
