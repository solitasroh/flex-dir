using FlexDir.Core.Errors;

namespace FlexDir.Core.Tools;

/// <summary>
/// 외부 도구 실행 실패를 상태표시줄 한 줄로 접는다 (docs/UI_GUIDE.md §상태 표현).
/// <para>
/// <see cref="LocationErrorMessages"/> 를 재사용하지 않는다 — 그쪽 문구는 <b>폴더</b>를
/// 못 여는 사건을 말한다("경로를 찾을 수 없습니다"). 여기서 그것을 내면 사용자는 자기
/// 폴더가 사라진 줄 알고, 손쓸 것을 엉뚱한 데서 찾는다.
/// </para>
/// </summary>
public static class ExternalToolMessages
{
    /// <summary>
    /// 실행 실패를 사람이 읽는 한 줄로. <paramref name="toolLabel"/> 은
    /// <c>VS Code</c>·<c>Windows Terminal</c> 처럼 <b>무엇을 열려다 실패했는지</b>다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> 가 <see cref="LocationErrorKind.None"/> 일 때.
    /// 성공을 실패 문구로 만드는 호출은 버그이고, 조용히 넘기면 화면에 헛말이 뜬다.
    /// </exception>
    public static string Describe(LocationErrorKind kind, string toolLabel)
        => kind switch
        {
            LocationErrorKind.NotFound => $"{toolLabel} 을(를) 찾을 수 없습니다",
            LocationErrorKind.AccessDenied => $"{toolLabel} 을(를) 실행할 권한이 없습니다",

            // 나머지 넷은 사용자가 손쓸 것이 같다 — 다시 눌러 보는 것뿐이라 하나로 접는다.
            LocationErrorKind.DeviceNotReady
                or LocationErrorKind.Sharing
                or LocationErrorKind.CredentialConflict
                or LocationErrorKind.Unknown => $"{toolLabel} 을(를) 열지 못했습니다",

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "오류가 아닌 것을 설명할 수 없다."),
        };
}
