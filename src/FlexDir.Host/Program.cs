namespace FlexDir.Host;

// Application 진입점과 DI 조립은 수동 phase 에서 채운다 (ADR-009: UI·Shell 은 자율 실행 대상이 아니다).
// 지금은 솔루션이 빌드·테스트되는지 확인하기 위한 최소 진입점이다.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
    }
}
