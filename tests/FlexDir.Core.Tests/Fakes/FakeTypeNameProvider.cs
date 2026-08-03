using FlexDir.Core.Presentation;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="ITypeNameProvider"/> 의 fake. shell 을 부르지 않고 확장자를 대문자로 되돌린다.
/// <para>
/// <b>캐시하지 않는다.</b> 확장자마다 한 번만 조회해 캐시하는 것은 실제 구현체(수동 Shell
/// phase)의 일이고, 여기서 캐시하면 "호출자가 확장자당 한 번만 묻는가" 를 검증할 수단이
/// 사라진다 — <see cref="Calls"/> 는 들어온 호출을 전부 기록한다.
/// </para>
/// <para>
/// 호출자가 <b>두 경로에서 동시에</b> 묻는다 (열거와 감시 갱신이 각자 줄을 만든다). 그래서
/// 기록은 잠금으로 보호한다 — 단정은 조용해진 뒤에 읽는다
/// (<c>FakeThumbnailSource</c> 와 같은 이유).
/// </para>
/// </summary>
public sealed class FakeTypeNameProvider : ITypeNameProvider
{
    private readonly Lock gate = new();
    private readonly List<(string Extension, bool IsDirectory)> calls = [];

    /// <summary>들어온 호출을 순서대로 기록한다.</summary>
    public IReadOnlyList<(string Extension, bool IsDirectory)> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    /// <summary>
    /// 이 확장자는 빈 문자열을 낸다 (예외가 아니다). 계약대로 실패하는 구현체를 흉내낸다 —
    /// shell 이 유형 이름을 못 읽는 것은 정상이다 (<see cref="ITypeNameProvider"/>).
    /// </summary>
    public HashSet<string> UnknownExtensions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 조회가 던질 예외. null 이면 던지지 않는다.
    /// <para>
    /// <b>계약 위반을 주입하는 knob 이다.</b> 실패는 빈 문자열이어야 하지만 구현체는 COM 위에
    /// 서므로 예외가 나올 수 있고, 그때 호출자가 폴더를 통째로 잃지 않는지 재려면 그 상황을
    /// 만들 수 있어야 한다 (<c>FakeThumbnailSource.TypeIconFailure</c> 와 같은 이유).
    /// </para>
    /// </summary>
    public Exception? Failure { get; set; }

    public ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);

        // 항목마다 관측한다. 실제 구현체도 그래야 하므로 fake 가 먼저 계약을 강제한다.
        ct.ThrowIfCancellationRequested();

        // 실패해도 기록은 먼저 남긴다 — "불렸는데 실패했다" 와 "아예 안 불렸다" 는 다른 사건이다.
        lock (gate)
        {
            calls.Add((extension, isDirectory));
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        // 디렉터리는 확장자를 보지 않는다. 빈 문자열로 갈라면 확장자 없는 파일까지 함께 걸린다.
        if (!isDirectory && UnknownExtensions.Contains(extension))
        {
            return ValueTask.FromResult(string.Empty);
        }

        return ValueTask.FromResult(Describe(extension, isDirectory));
    }

    /// <summary>같은 인자로 들어온 호출 횟수.</summary>
    public int CountFor(string extension, bool isDirectory)
        => Calls.Count(call => call.Extension == extension && call.IsDirectory == isDirectory);

    /// <summary>디렉터리는 <c>DIR</c>, 확장자 없는 파일은 <c>FILE</c>, 그 외는 대문자 확장자.</summary>
    public static string Describe(string extension, bool isDirectory)
    {
        if (isDirectory)
        {
            return "DIR";
        }

        return extension.Length == 0 ? "FILE" : extension.ToUpperInvariant();
    }
}
