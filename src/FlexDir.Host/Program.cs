using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using FlexDir.App.Threading;

using FlexDir.Host.Composition;
using FlexDir.Host.Diagnostics;
using FlexDir.Host.SingleInstance;
using FlexDir.Host.Startup;

namespace FlexDir.Host;

// 명시적 Main 이다. App.xaml(ApplicationDefinition)로 가지 않은 이유는 둘이다.
//
// 1. 두 번째 실행은 WPF 초기화 비용을 내기 전에 끝나야 한다. ApplicationDefinition 이
//    만들어 주는 Main 은 곧바로 Application 을 세우므로 그 판정을 앞에 둘 자리가 없다.
//    상주 프로세스를 고른 이유가 그 비용이다 (ADR-003).
// 2. 조립을 넣을 곳이 App.xaml.cs 밖에 없어진다. 거기는 InitializeComponent() 만 두는
//    자리이고 (CLAUDE.md §2), 전작의 god object 가 자란 자리가 그것이다.
//
// 이 파일은 TDD 가드의 검사 대상이 아니다. 그러므로 판단은 여기 두지 않는다 —
// 아래가 하는 일은 순서를 정하는 것뿐이고, 판단은 전부 채점되는 클래스 안에 있다.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var gate = SingleInstanceGate.Acquire(SingleInstanceGate.DefaultName);

        // 두 번째 실행은 인자를 넘기고 끝난다 (ADR-003 · ARCHITECTURE §6).
        if (!gate.IsPrimary)
        {
            return gate
                .SendAsync(args, SingleInstanceGate.DefaultConnectTimeout, CancellationToken.None)
                .GetAwaiter().GetResult()
                ? 0
                : 1;
        }

        return RunResident(gate, args);
    }

    private static int RunResident(SingleInstanceGate gate, string[] args)
    {
        var application = new Application
        {
            // 창을 닫아도 프로세스는 남는다 (ADR-003). 완전 종료는 명시적 Shutdown 뿐이다.
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        var state = AppComposition.DefaultStateDirectory;
        var composition = AppComposition.Create(
            new WpfUiDispatcher(Dispatcher.CurrentDispatcher), state);

        var router = new ActivationRouter(composition.Workspace, composition.UsageLog, TimeProvider.System);
        using var lifetime = new CancellationTokenSource();

        // 실행 자체가 첫 활성화다. 기다리지 않는다 — 메시지 펌프가 아직 돌지 않았고,
        // 여기서 기다리면 조립이 UI 스레드에서 열거를 기다리게 된다.
        _ = router.ActivateAsync(args, lifetime.Token);
        _ = router.RunAsync(gate.ActivationsAsync(lifetime.Token), lifetime.Token);

        // cold start: 프로세스가 만들어진 순간부터 조립이 끝난 순간까지 (docs/PRD.md §5).
        // 재는 도구를 따로 만들지 않는다 (ARCHITECTURE §7) — 값은 perf.log 에 남는다.
        _ = new PerformanceLog(state).RecordAsync(
            MeasurementPoint.ColdStart,
            DateTime.Now - Process.GetCurrentProcess().StartTime,
            DateTimeOffset.Now,
            lifetime.Token);

        // 창은 phase B 에서 붙는다. 지금은 창 없이 상주하며 활성화 요청만 받는다.
        var code = application.Run();

        lifetime.Cancel();

        // 여기까지 와야 STA 워커가 닫힌다. 창이 닫히는 것과는 다른 사건이다.
        composition.DisposeAsync().AsTask().GetAwaiter().GetResult();

        return code;
    }
}
