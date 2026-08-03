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
    /// UNC·네트워크 위치. v1 범위 밖이라 거부하지만 '잘못된 경로' 와는 구분한다 —
    /// v2 가 이 분기를 살려 쓴다 (docs/SHELL_NOTES.md 함정 4).
    /// </summary>
    NetworkPathNotSupported,
}

/// <summary>
/// 포트가 주고받는 위치 식별자. 순수 값 타입이며 파일시스템에 접근하지 않는다 —
/// 존재 여부 판정은 열거 계층의 일이다.
/// </summary>
public sealed class LocationId : IEquatable<LocationId>
{
    private const string ExtendedPrefix = @"\\?\";

    private LocationId(LocationKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public LocationKind Kind { get; }

    /// <summary>정규화된 내부 표현. 로컬은 항상 <c>\\?\</c> 확장 접두사로 시작한다.</summary>
    public string Value { get; }

    /// <summary>사용자에게 보여줄 형태. <c>\\?\</c> 접두사를 제거한 것.</summary>
    public string DisplayPath => Value[ExtendedPrefix.Length..];

    /// <summary>마지막 구성요소. 드라이브 루트는 <c>C:\</c> 를 낸다.</summary>
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

        // 확장 접두사를 뗀 본문. 항상 드라이브 루트로 시작해야 한다.
        string body;

        // 함정 1: UNC 판정을 구분자 정규화보다 먼저 한다. '/' 를 먼저 '\' 로 바꾸면
        // //server/share 에 드라이브 문자가 없어 상대 경로로 오분류된다.
        if (TryStripExtendedPrefix(raw, out var stripped))
        {
            // 함정 3: \\?\C:\ 는 UNC 가 아니다 — 확장 접두사가 붙은 로컬 경로다.
            // UNC 는 접두사 뒤에 UNC 요소가 붙은 \\?\UNC\server\share 쪽이다.
            if (IsUncElement(stripped))
            {
                error = LocationParseError.NetworkPathNotSupported;
                return false;
            }

            body = stripped;
        }
        else if (raw.Length > 1 && IsSeparator(raw[0]) && IsSeparator(raw[1]))
        {
            // 함정 4: \\server (공유 없는 서버) 도 탐색기에서는 공유 목록 폴더다.
            // v1 은 거부하지만 InvalidCharacter·RelativePath 로 뭉개지 않는다.
            error = LocationParseError.NetworkPathNotSupported;
            return false;
        }
        else
        {
            body = raw;
        }

        body = body.Replace('/', '\\');

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

    public bool TryGetParent([NotNullWhen(true)] out LocationId? parent)
    {
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
