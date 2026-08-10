using FlexDir.Core.Settings;

namespace FlexDir.Core.Tests.Fakes;

/// <summary>
/// <see cref="ISettingsStore"/> 의 fake. 저장한 것을 그대로 돌려준다.
/// </summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    private AppSettings saved = AppSettings.Default;

    /// <summary>저장 횟수. 조작마다 남기는지(=창을 강제 종료해도 남는지)를 여기서 본다.</summary>
    public int Saves { get; private set; }

    /// <summary>다음 저장에서 던질 예외. 실패해도 화면이 흔들리지 않는지 재는 데 쓴다.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>다음 읽기에서 던질 예외. 첫 읽기 실패가 창을 막지 않는지 재는 데 쓴다.</summary>
    public Exception? LoadFailure { get; set; }

    public AppSettings Current => saved;

    /// <summary>테스트가 "이미 저장돼 있던 설정" 을 만들어 두는 자리. 저장 횟수를 늘리지 않는다.</summary>
    public void Seed(AppSettings settings) => saved = settings;

    public ValueTask<AppSettings> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (LoadFailure is { } failure)
        {
            throw failure;
        }

        return ValueTask.FromResult(saved);
    }

    public ValueTask SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        if (SaveFailure is { } failure)
        {
            throw failure;
        }

        Saves++;
        saved = settings;

        return ValueTask.CompletedTask;
    }
}
