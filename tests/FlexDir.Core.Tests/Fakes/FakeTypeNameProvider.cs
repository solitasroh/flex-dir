using FlexDir.Core.Presentation;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="ITypeNameProvider"/> 의 fake. shell 을 부르지 않고 확장자를 대문자로 되돌린다.
/// <para>
/// <b>캐시하지 않는다.</b> 확장자마다 한 번만 조회해 캐시하는 것은 실제 구현체(수동 Shell
/// phase)의 일이고, 여기서 캐시하면 "호출자가 확장자당 한 번만 묻는가" 를 검증할 수단이
/// 사라진다 — <see cref="Calls"/> 는 들어온 호출을 전부 기록한다.
/// </para>
/// </summary>
public sealed class FakeTypeNameProvider : ITypeNameProvider
{
    private readonly List<(string Extension, bool IsDirectory)> calls = [];

    /// <summary>들어온 호출을 순서대로 기록한다.</summary>
    public IReadOnlyList<(string Extension, bool IsDirectory)> Calls => calls;

    public ValueTask<string> GetTypeNameAsync(string extension, bool isDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extension);

        // 항목마다 관측한다. 실제 구현체도 그래야 하므로 fake 가 먼저 계약을 강제한다.
        ct.ThrowIfCancellationRequested();

        calls.Add((extension, isDirectory));

        return ValueTask.FromResult(Describe(extension, isDirectory));
    }

    /// <summary>같은 인자로 들어온 호출 횟수.</summary>
    public int CountFor(string extension, bool isDirectory)
        => calls.Count(call => call.Extension == extension && call.IsDirectory == isDirectory);

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
