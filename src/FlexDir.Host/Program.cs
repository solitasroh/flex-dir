using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

// WinForms(트레이 아이콘)가 암시적 global using 으로 들어와 WPF 쪽 이름과 겹친다.
using Application = System.Windows.Application;

using FlexDir.App.Threading;
using FlexDir.App.Views;

using FlexDir.Host.Composition;
using FlexDir.Host.Diagnostics;
using FlexDir.Host.SingleInstance;
using FlexDir.Host.Startup;

using Velopack;

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
        // 무엇보다 먼저다. 설치·갱신·제거 때 Windows 가 이 exe 를 특별한 인자로 부르는데,
        // 그 호출은 창을 띄우지 않고 끝나야 한다 — 뒤로 밀면 설치 중에 앱이 뜨고 뮤텍스가
        // 잡힌다. 설치되지 않은 실행에서는 아무 일도 하지 않고 지난다 (docs/PRD-v2.md §9).
        VelopackApp.Build().Run();

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
        var uiDispatcher = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        // shell 대화상자·컨텍스트 메뉴의 소유 창. 조립 시점에는 창이 없으므로 공급자를 넘긴다.
        var ownerWindow = new OwnerWindow();
        var composition = AppComposition.Create(uiDispatcher, state, ownerWindow.Source);

        // 창은 활성화가 보여준다 (ActivationRouter) — 여기서는 만들기만 한다.
        // 상주 규약(닫기 = 숨기기 · 배치 복원)은 ResidentWindow 가 건다.
        var window = new MainWindow { DataContext = composition.Workspace };
        ResidentWindow.Attach(window, composition.Workspace);
        ownerWindow.Track(window);

        // 계측이 쌓이는 파일은 하나다 (perf.log). 지점마다 만들면 같은 곳을 두 번 연다.
        var perf = new PerformanceLog(state);

        // UI 스레드 예외 하나가 상주 프로세스와 트레이 아이콘까지 날리던 자리다
        // (docs/PRD-v2.md §14 · 2026-08-10 의 XAML 크래시). 상주 앱이라 값이 다르다 —
        // 창 하나짜리 앱이면 죽는 것이 정직하지만 여기서는 트레이까지 사라진다.
        //
        // 삼킬지는 CrashNoticeViewModel 이 정한다. 판정이 먼저인 것은 그 결과를 줄에 적기
        // 때문이고, 기록이 동기라서 순서가 유실을 만들지 않는다 — 포기(fatal)면 이 핸들러가
        // 끝나는 대로 프로세스가 죽으므로 비동기였다면 정확히 그 줄을 잃는다 (ErrorLog).
        var errors = new ErrorLog(state);

        application.DispatcherUnhandledException += (_, unhandled) =>
        {
            var recovered = composition.Workspace.Crash.Report(unhandled.Exception);

            errors.Record(unhandled.Exception, recovered, DateTimeOffset.Now);

            unhandled.Handled = recovered;
        };

        var presenter = new WindowPresenter(
            _ => uiDispatcher.InvokeAsync(() =>
            {
                window.Show();
                window.Activate();
            }),
            perf,
            TimeProvider.System);

        var router = new ActivationRouter(
            composition.Workspace,
            composition.UsageLog,
            TimeProvider.System,
            presenter.PresentAsync);

        // 폴더 전환 → 첫 항목 도착 (FirstItem). 페인 밖에서 관찰한다 — perf.log 는 Host 의
        // 것이고, App 이 그것을 알면 참조 방향이 뒤집힌다.
        using var leftMeter = new FirstItemMeter(composition.Workspace.Left, perf, TimeProvider.System);
        using var rightMeter = new FirstItemMeter(composition.Workspace.Right, perf, TimeProvider.System);

        using var lifetime = new CancellationTokenSource();

        // 실행 자체가 첫 활성화다. 기다리지 않는다 — 메시지 펌프가 아직 돌지 않았고,
        // 여기서 기다리면 조립이 UI 스레드에서 열거를 기다리게 된다.
        // 시작은 마지막 폴더 복원 → 활성화 순서다. 폴백은 사용자 프로필이다 (phase B-2 결정).
        _ = router.StartAsync(
            args,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            lifetime.Token);
        _ = router.RunAsync(gate.ActivationsAsync(lifetime.Token), lifetime.Token);

        // 새 버전 확인. 기다리지 않는다 — 네트워크에 닿으므로 시작을 붙잡으면 cold start 가
        // 피드 왕복만큼 늘어난다 (ADR-003 이 상주를 고른 바로 그 비용이다). 실패는 조용하고
        // (UpdateViewModel) 설치되지 않은 실행에서는 아무 일도 일어나지 않는다.
        if (composition.Workspace.Update is { } update)
        {
            _ = update.CheckAsync(lifetime.Token);
        }

        // 상주 중임이 보이는 곳이자 유일한 완전 종료 조작이다 (TrayMenu · 사용자 결정).
        // WinForms 타입은 전체 이름으로 쓴다 — using 을 더하면 WPF Application 과 부딪힌다.
        // 아이콘은 이 스레드의 것이라 메뉴 이벤트도 이 스레드(UI)로 온다.
        Task OpenWindowAsync() => router.ActivateAsync([], lifetime.Token);

        using var trayIcon = ProductIcon.LoadTray();

        using var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "flex-dir",
            ContextMenuStrip = TrayMenu.Create(
                () => _ = OpenWindowAsync(),
                () => application.Shutdown()),
            Visible = true,
        };
        tray.DoubleClick += (_, _) => _ = OpenWindowAsync();

        // cold start: 프로세스가 만들어진 순간부터 조립이 끝난 순간까지 (docs/PRD.md §5).
        // 재는 도구를 따로 만들지 않는다 (ARCHITECTURE §7) — 값은 perf.log 에 남는다.
        _ = perf.RecordAsync(
            MeasurementPoint.ColdStart,
            DateTime.Now - Process.GetCurrentProcess().StartTime,
            DateTimeOffset.Now,
            lifetime.Token);

        // 첫 활성화가 위에서 창 표시를 큐에 넣었다 — 펌프가 돌기 시작하면 창이 뜬다.
        var code = application.Run();

        lifetime.Cancel();

        // 진행 중인 계측 기록을 마저 쓴다 — 종료 직전의 폴더 전환이 마지막 수치다.
        Task.WhenAll(leftMeter.Recording, rightMeter.Recording).GetAwaiter().GetResult();

        // 마지막 폴더·스플리터·창 배치를 남긴다 — 다음 실행의 시작 상태다. 페인을 접기 전이다.
        composition.Workspace.PersistAsync(CancellationToken.None).GetAwaiter().GetResult();

        // 여기까지 와야 STA 워커가 닫힌다. 창이 닫히는 것과는 다른 사건이다.
        composition.DisposeAsync().AsTask().GetAwaiter().GetResult();

        return code;
    }
}
