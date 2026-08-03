using FlexDir.Core.Tests.Fakes;
using FlexDir.Core.ViewState;

namespace FlexDir.Core.Tests.ViewState;

/// <summary>
/// 포트 계약을 fake 로 한 번 통과시킨다. 파일명은 소스 인터페이스에 맞춘 것이고
/// 검증 내용은 전부 <see cref="ViewStateStoreContract"/> 에 있다 —
/// <c>FlexDir.Shell</c> 의 구현체 테스트도 같은 기반 클래스를 상속한다.
/// </summary>
public class InMemoryViewStateStoreTests : ViewStateStoreContract
{
    protected override IViewStateStore CreateStore() => new InMemoryViewStateStore();
}
