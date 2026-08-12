using FlexDir.Core.Settings;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="ISystemThemeSource"/> 의 fake. <see cref="IsDarkMode"/> 를 그대로 낸다 —
/// ViewModel 테스트가 이 기계의 실제 OS 테마 설정에 기대지 않게 하는 자리다.
/// </summary>
public sealed class FakeSystemThemeSource : ISystemThemeSource
{
    public bool IsDarkMode { get; set; }

    /// <summary>몇 번 물었는지. 캐시하고 끝내는지, 다시 묻는지를 여기서 본다.</summary>
    public int Asked { get; private set; }

    public ValueTask<bool> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Asked++;

        return ValueTask.FromResult(IsDarkMode);
    }
}
