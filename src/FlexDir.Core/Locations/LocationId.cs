using System.Diagnostics.CodeAnalysis;

namespace FlexDir.Core.Locations;

/// <summary>
/// 위치의 종류. v1 은 <see cref="FileSystem"/> 만 만든다 (ADR-010).
/// </summary>
public enum LocationKind
{
    /// <summary>로컬 파일시스템 경로. v1 이 다루는 유일한 종류.</summary>
    FileSystem,

    /// <summary>v2 예약 — 알려진 폴더(ThisPC · Network 등). v1 은 만들지 않는다.</summary>
    ShellKnownFolder,

    /// <summary>v2 예약 — PIDL 로만 표현되는 shell 네임스페이스. v1 은 만들지 않는다.</summary>
    ShellNamespace,
}

/// <summary>
/// <see cref="LocationId.TryParse"/> 가 입력을 거부한 이유.
/// </summary>
public enum LocationParseError
{
    None,

    /// <summary>null · 빈 문자열 · 공백뿐.</summary>
    Empty,

    /// <summary>드라이브 루트로 시작하지 않아 프로세스의 현재 디렉터리 없이는 해소할 수 없다.</summary>
    RelativePath,

    /// <summary>경로 본문에 NTFS 대체 데이터 스트림 구분자(':')가 있다.</summary>
    AlternateDataStream,

    /// <summary>파일시스템이 허용하지 않는 문자가 있다.</summary>
    InvalidCharacter,

    /// <summary>
    /// <c>\\</c> 로 시작하는데 서버 이름이 없다. '잘못된 경로' 와 구분하는 이유는 사용자가
    /// 고칠 것이 다르기 때문이다 — 오타가 아니라 <b>덜 쓴 것</b>이다.
    /// </summary>
    NetworkPathIncomplete,
}

/// <summary>
/// 포트가 주고받는 위치 식별자. 순수 값 타입이며 파일시스템에 접근하지 않는다 —
/// 존재 여부 판정은 열거 계층의 일이다.
/// </summary>
public sealed class LocationId : IEquatable<LocationId>
{
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>Win32 확장 UNC 형식. <c>\\server\share</c> 의 내부 표현이 이것이다.</summary>
    private const string UncPrefix = @"\\?\UNC\";

    private LocationId(LocationKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public LocationKind Kind { get; }

    /// <summary>
    /// 정규화된 내부 표현. 로컬은 <c>\\?\C:\…</c>, UNC 는 <c>\\?\UNC\server\share\…</c> 다.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 네트워크 경로인가. 파일시스템 경로인 것은 같다 (<see cref="Kind"/> 도 같다) —
    /// SMB 리다이렉터를 지날 뿐이다. 다른 것은 <b>얼마나 걸릴지 모른다</b> 는 점이고,
    /// 그래서 호출자가 시한·취소를 다르게 잡아야 한다 (CLAUDE.md §3).
    /// </summary>
    public bool IsNetwork => Value.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 서버 자체인가 (<c>\\server</c>). 여기에 있는 것은 폴더가 아니라 <b>공유</b>이고
    /// 열거 방식이 완전히 다르다 — 디렉터리 열거가 아니라 <c>WNetEnumResource</c> 다
    /// (docs/PRD-v2.md §5 N-3 · docs/SHELL_NOTES.md §네트워크). 라우팅이 이것을 본다.
    /// </summary>
    public bool IsNetworkServer => IsNetwork && !Value[UncPrefix.Length..].Contains('\\');

    /// <summary>
    /// 사용자에게 보여줄 형태. 로컬은 <c>C:\…</c>, UNC 는 <c>\\server\share\…</c> 다 —
    /// 내부 표현의 <c>UNC\</c> 를 그대로 보여주면 주소줄에 되붙여 넣을 수 없다.
    /// </summary>
    public string DisplayPath
        => IsNetwork ? @"\\" + Value[UncPrefix.Length..] : Value[ExtendedPrefix.Length..];

    /// <summary>
    /// 마지막 구성요소. 루트는 자기 표시형을 낸다 — 드라이브는 <c>C:\</c>, 서버는
    /// <c>\\server</c>. 주소줄이 이것을 칸 이름으로 쓴다 (<see cref="PathSegments"/>).
    /// </summary>
    public string Name
    {
        get
        {
            var display = DisplayPath;

            // 후행 구분자가 남는 것은 드라이브 루트뿐이다 (정규화 규칙 6).
            if (display.EndsWith('\\'))
            {
                return display;
            }

            // 서버만 있는 UNC 는 앞의 \\ 말고 구분자가 없다 — 잘라내면 이름이 사라진다.
            if (IsNetwork && !Value[UncPrefix.Length..].Contains('\\'))
            {
                return display;
            }

            return display[(display.LastIndexOf('\\') + 1)..];
        }
    }

    public static bool TryParse(string? input, [NotNullWhen(true)] out LocationId? location, out LocationParseError error)
    {
        location = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = LocationParseError.Empty;
            return false;
        }

        var raw = input.Trim();

        // 확장 접두사를 뗀 본문. 드라이브 루트이거나 UNC 본문(server\share…)이다.
        string body;

        // 함정 1: UNC 판정을 구분자 정규화보다 먼저 한다. '/' 를 먼저 '\' 로 바꾸면
        // //server/share 에 드라이브 문자가 없어 상대 경로로 오분류된다.
        var isUnc = false;

        if (TryStripExtendedPrefix(raw, out var stripped))
        {
            // 함정 3: \\?\C:\ 는 UNC 가 아니다 — 확장 접두사가 붙은 로컬 경로다.
            // UNC 는 접두사 뒤에 UNC 요소가 붙은 \\?\UNC\server\share 쪽이다.
            if (IsUncElement(stripped))
            {
                isUnc = true;
                body = stripped.Length > 3 ? stripped[4..] : string.Empty;
            }
            else
            {
                body = stripped;
            }
        }
        else if (raw.Length > 1 && IsSeparator(raw[0]) && IsSeparator(raw[1]))
        {
            // 함정 4: \\server (공유 없는 서버) 도 탐색기에서는 공유 목록 폴더다.
            isUnc = true;
            body = raw[2..];
        }
        else
        {
            body = raw;
        }

        body = body.Replace('/', '\\');

        if (isUnc)
        {
            return TryParseUnc(body, out location, out error);
        }

        // 드라이브 루트 여부. "C:" · "C:docs" 는 드라이브 상대 경로이므로 여기서 걸린다.
        // Path.GetFullPath 에 맡기지 않는 이유가 이것이다 — 그쪽은 프로세스의 현재
        // 디렉터리를 읽어 상대 경로를 조용히 절대 경로로 바꾼다.
        if (body.Length < 3 || !char.IsAsciiLetter(body[0]) || body[1] != ':' || body[2] != '\\')
        {
            error = LocationParseError.RelativePath;
            return false;
        }

        // 함정 2: 드라이브 문자 뒤의 ':' 는 합법이므로 스캔에서 제외한다.
        var charError = ClassifyChars(body[2..]);
        if (charError != LocationParseError.None)
        {
            error = charError;
            return false;
        }

        var segments = new List<string>();
        foreach (var segment in body[3..].Split('\\'))
        {
            // 규칙 6: 중복·후행 구분자는 빈 요소로 나타난다.
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // 규칙 5: 루트를 넘어가는 '..' 는 루트에서 멈춘다.
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        // body[..3] 은 "C:\" — 드라이브 루트는 구분자를 유지한다 (규칙 6).
        location = new LocationId(LocationKind.FileSystem, ExtendedPrefix + body[..3] + string.Join('\\', segments));
        error = LocationParseError.None;
        return true;
    }

    /// <summary>
    /// UNC 본문(<c>server\share\…</c>)을 정규화한다. 드라이브 루트 대신 <b>서버가 루트</b>인
    /// 것 말고는 규칙이 같다 — '..' 는 서버에서 멈추고, 중복·후행 구분자는 사라진다.
    /// </summary>
    private static bool TryParseUnc(string body, out LocationId? location, out LocationParseError error)
    {
        location = null;

        var parts = new List<string>();

        foreach (var segment in body.Split('\\'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // 규칙 5: 서버를 넘어가는 '..' 는 서버에서 멈춘다. parts[0] 이 서버다.
                if (parts.Count > 1)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        if (parts.Count == 0)
        {
            // "\\" 만 온 경우다. 오타가 아니라 덜 쓴 것이므로 RelativePath 로 뭉개지 않는다.
            error = LocationParseError.NetworkPathIncomplete;
            return false;
        }

        var charError = ClassifyChars(string.Join('\\', parts));
        if (charError != LocationParseError.None)
        {
            error = charError;
            return false;
        }

        location = new LocationId(LocationKind.FileSystem, UncPrefix + string.Join('\\', parts));
        error = LocationParseError.None;
        return true;
    }

    public bool TryGetParent([NotNullWhen(true)] out LocationId? parent)
    {
        if (IsNetwork)
        {
            var unc = Value[UncPrefix.Length..];
            var lastSeparator = unc.LastIndexOf('\\');

            // 서버가 루트다. 그 위는 "네트워크" 노드인데 셸 네임스페이스라 여기 담기지 않는다.
            if (lastSeparator < 0)
            {
                parent = null;
                return false;
            }

            parent = new LocationId(Kind, UncPrefix + unc[..lastSeparator]);
            return true;
        }

        var display = DisplayPath;

        if (display.EndsWith('\\'))
        {
            parent = null;
            return false;
        }

        var separator = display.LastIndexOf('\\');

        // "C:\Temp" 의 부모는 드라이브 루트이므로 구분자를 남긴다.
        var parentDisplay = separator == 2 ? display[..3] : display[..separator];

        parent = new LocationId(Kind, ExtendedPrefix + parentDisplay);
        return true;
    }

    /// <summary>
    /// 하위 이름을 붙인다. 구분자·상대경로 요소·파일시스템이 거부하는 문자가 섞이면
    /// <see cref="ArgumentException"/>. 그대로 붙이면 <see cref="TryParse"/> 가 되돌려
    /// 읽을 수 없는 <see cref="Value"/> 가 만들어진다.
    /// </summary>
    public LocationId Combine(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
        {
            throw new ArgumentException("하위 이름이 비어 있다.", nameof(childName));
        }

        if (childName.Contains('\\') || childName.Contains('/'))
        {
            throw new ArgumentException($"하위 이름에 경로 구분자가 있다: '{childName}'", nameof(childName));
        }

        if (childName is "." or "..")
        {
            throw new ArgumentException($"하위 이름이 상대경로 요소다: '{childName}'", nameof(childName));
        }

        if (ClassifyChars(childName) != LocationParseError.None)
        {
            throw new ArgumentException($"하위 이름에 쓸 수 없는 문자가 있다: '{childName}'", nameof(childName));
        }

        var separator = Value.EndsWith('\\') ? string.Empty : "\\";
        return new LocationId(Kind, Value + separator + childName);
    }

    // 규칙 7: Windows 파일시스템은 대소문자를 구분하지 않으므로
    // \\?\C:\Temp 와 \\?\c:\temp 는 같은 위치다.
    public bool Equals(LocationId? other)
        => other is not null
           && Kind == other.Kind
           && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as LocationId);

    public override int GetHashCode()
        => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Value));

    private static bool IsSeparator(char c) => c is '\\' or '/';

    /// <summary><c>\\?\</c> 접두사를 뗀다. 구분자는 '/' 도 받는다.</summary>
    private static bool TryStripExtendedPrefix(string raw, out string body)
    {
        if (raw.Length >= 4 && IsSeparator(raw[0]) && IsSeparator(raw[1]) && raw[2] == '?' && IsSeparator(raw[3]))
        {
            body = raw[4..];
            return true;
        }

        body = string.Empty;
        return false;
    }

    /// <summary>확장 접두사 뒤가 UNC 요소로 시작하는지.</summary>
    private static bool IsUncElement(string body)
        => body.Equals("UNC", StringComparison.OrdinalIgnoreCase)
           || (body.Length > 3 && body.StartsWith("UNC", StringComparison.OrdinalIgnoreCase) && IsSeparator(body[3]));

    /// <summary>경로 본문에서 쓸 수 없는 문자를 분류한다. 문제가 없으면 None.</summary>
    private static LocationParseError ClassifyChars(string text)
    {
        foreach (var c in text)
        {
            if (c == ':')
            {
                return LocationParseError.AlternateDataStream;
            }

            if (c is '<' or '>' or '"' or '|' or '?' or '*' || c < ' ')
            {
                return LocationParseError.InvalidCharacter;
            }
        }

        return LocationParseError.None;
    }
}
