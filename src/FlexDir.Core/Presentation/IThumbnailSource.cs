using FlexDir.Core.Locations;

namespace FlexDir.Core.Presentation;

/// <summary>
/// 위에서 아래로 채워진 BGRA32 픽셀. Stride 는 <c>Width * 4</c> 다.
/// <para>
/// 인코딩된 스트림이 아닌 이유: shell 은 <c>HBITMAP</c> 을 준다. PNG 로 인코딩해 넘기면
/// App 이 다시 디코딩해야 하고 96×96 이미지에 왕복 비용을 두 번 낸다. BGRA 는 한 번
/// 복사하면 <c>WriteableBitmap</c> 에 그대로 들어간다. 대가는 항목당 약 36KB(96×96×4)이므로
/// <b>보이는 범위 밖의 썸네일은 반드시 해제해야 한다.</b>
/// </para>
/// <para>
/// <c>ImageSource</c> 를 쓰지 않는다 — <c>FlexDir.Core</c> 는 WPF 를 모른다 (CLAUDE.md §1).
/// 변환은 View 계층의 일이다.
/// </para>
/// </summary>
public sealed record ThumbnailBitmap(int Width, int Height, byte[] Pixels)
{
    /// <summary>
    /// 길이는 <c>Width * Height * 4</c> 여야 한다. 어긋난 버퍼는 만드는 자리에서 걸러야
    /// 원인이 남는다 — <c>WriteableBitmap</c> 에 넣는 순간에 터지면 어느 항목의 것인지 모른다.
    /// </summary>
    public byte[] Pixels { get; } = Validated(Width, Height, Pixels);

    private static byte[] Validated(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var expected = (long)width * height * 4;

        return pixels.Length == expected
            ? pixels
            : throw new ArgumentException(
                $"BGRA32 픽셀 길이가 맞지 않는다. {width}×{height} 는 {expected} 바이트여야 하는데 {pixels.Length} 바이트다.",
                nameof(pixels));
    }
}

/// <summary>
/// 목록에 그릴 그림을 내는 포트. 구현체는 shell 캐시를 재사용하며
/// <c>FlexDir.Shell</c> 에 둔다 — 우리가 썸네일 캐시를 따로 만들지 않는다
/// (docs/UI_GUIDE.md §금지 목록. 중복 캐시는 어긋나고, 어긋난 썸네일은 원인 추적이 어렵다).
/// <para>
/// 비동기이고 취소 가능한 이유: shell 조회는 동기 블로킹이고 네트워크·클라우드 항목에서
/// 초 단위로 멈춘다 (docs/SHELL_NOTES.md §아이콘 함정 1). UI 스레드 밖에서 끝나야 한다
/// (CLAUDE.md §3).
/// </para>
/// <para>
/// 실패를 예외가 아니라 <c>null</c> 로 내는 이유: 썸네일이 없는 것은 정상이다. 형식
/// 아이콘으로 대체하고 재시도하지 않는다 (docs/PRD.md §4). 예외로 만들면 호출자가
/// 항목마다 잡아야 한다.
/// </para>
/// </summary>
public interface IThumbnailSource
{
    /// <summary>
    /// 항목의 실제 썸네일. 없거나 실패하면 <c>null</c> 이다.
    /// <para>
    /// 호출자는 <c>FileItem.IsContentAccessRisky</c> 항목에 이것을 부르지 않는다 —
    /// 내용을 건드리면 클라우드 다운로드가 트리거된다 (docs/SHELL_NOTES.md §열거 함정 3).
    /// </para>
    /// </summary>
    ValueTask<ThumbnailBitmap?> GetThumbnailAsync(LocationId item, int requestedSize, CancellationToken ct);

    /// <summary>
    /// 확장자에 대응하는 형식 아이콘. <paramref name="extension"/> 은 점을 포함하지 않는
    /// 소문자이고 (<c>FileItem.Extension</c>), 빈 문자열은 확장자 없음이다. 디렉터리도 빈
    /// 문자열로 오므로 <paramref name="isDirectory"/> 로 갈라야 폴더 아이콘과 확장자 없는
    /// 파일의 아이콘이 구분된다.
    /// <para>
    /// <b>파일마다 부르지 않는다.</b> 확장자마다 한 번이 대용량 폴더에서 가장 큰 승리였다
    /// (docs/SHELL_NOTES.md §아이콘). 캐시는 호출자가 들고 있다.
    /// </para>
    /// <para>
    /// 실제 구현에서는 <c>ITypeNameProvider.GetTypeNameAsync</c> 와 같은
    /// <c>SHGetFileInfoW</c> 호출로 함께 얻을 수 있다. 그 최적화는 수동 Shell phase 의 일이다.
    /// </para>
    /// </summary>
    ValueTask<ThumbnailBitmap?> GetTypeIconAsync(
        string extension,
        bool isDirectory,
        int requestedSize,
        CancellationToken ct);

    /// <summary>
    /// 그 위치의 <b>아이콘</b>. 썸네일이 아니라 shell 이 그 항목에 붙여 둔 그림이다.
    /// 없거나 실패하면 <c>null</c> 이다.
    /// <para>
    /// <see cref="GetThumbnailAsync"/> 로는 안 된다 — 그쪽은 내용의 <b>미리보기</b>라
    /// 실제 썸네일이 없으면 일부러 <c>null</c> 을 낸다 (그 계약이 있어야 호출자가 형식
    /// 아이콘으로 대체할지 정할 수 있다). 폴더에는 썸네일이 없으므로 항상 <c>null</c> 이다.
    /// 이쪽은 <b>아이콘</b>이라 거의 언제나 있다.
    /// </para>
    /// <para>
    /// <see cref="GetTypeIconAsync"/> 로도 안 된다 — 그쪽은 확장자마다 하나라 캐시가 잘
    /// 듣지만, 모든 폴더가 같은 일반 폴더 아이콘을 받는다. 이쪽은 <b>경로마다</b> 다를 수
    /// 있고(알려진 폴더 · 사용자 지정 아이콘 · <c>desktop.ini</c>) 캐시 키가 경로다.
    /// <b>그래서 목록의 항목마다 부르면 안 된다</b> — 대용량 폴더에서 확장자마다 한 번이
    /// 가장 큰 승리였던 것(docs/SHELL_NOTES.md §아이콘)이 무너진다. 부르는 쪽은
    /// <b>개수가 정해진 곳</b>이다.
    /// </para>
    /// <para>
    /// 실패는 던지지 않는다 — <c>null</c> 이다. 아이콘이 없다고 폴더를 못 여는 사건이
    /// 되면 안 된다. 취소만 예외로 나온다 — 이 포트의 다른 둘과 같다.
    /// </para>
    /// <para>
    /// UI 스레드에서 부르지 않는다 (CLAUDE.md §3). shell 조회는 동기 블로킹이고
    /// 네트워크·클라우드 항목에서 초 단위로 멈춘다.
    /// </para>
    /// <para>
    /// 크기는 요청일 뿐 보장이 아니다 — shell 이 가진 가장 가까운 크기가 온다.
    /// <paramref name="requestedSize"/> 는 양수여야 한다 (다른 둘과 같은 검증).
    /// </para>
    /// </summary>
    ValueTask<ThumbnailBitmap?> GetItemIconAsync(LocationId item, int requestedSize, CancellationToken ct);
}
