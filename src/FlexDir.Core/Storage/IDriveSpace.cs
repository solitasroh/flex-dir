using FlexDir.Core.Locations;

namespace FlexDir.Core.Storage;

/// <summary>
/// 어떤 위치가 올라앉은 볼륨의 용량. 바이트 단위이며 표시 형식은 호출자가 정한다
/// (<c>StatusSummary.ForFreeSpace</c>).
/// <para>
/// <see cref="TotalBytes"/> 를 함께 두는 이유: 상태표시줄은 여유만 쓰지만, 남은 비율로
/// 경고를 주는 것은 값이 둘 다 있어야 한다. 한 번 묻는 김에 같이 오는 값이라
/// (<c>GetDiskFreeSpaceEx</c> 가 한 호출에 셋을 낸다) 나중에 다시 물으러 가지 않는다.
/// </para>
/// </summary>
public readonly record struct DriveSpace(long FreeBytes, long TotalBytes);

/// <summary>
/// 위치가 속한 볼륨의 여유 용량을 재는 포트 (docs/DESIGN.md §1 상태표시줄 · 목업 .free).
/// <para>
/// <b>왜 포트인가</b>: 이 조회는 저장소에 닿는다. 네트워크 경로와 클라우드 자리표시자에서는
/// 초 단위로 블로킹하므로 UI 스레드에서 부를 수 없고 (CLAUDE.md §3), 구현이 Core 안에
/// 있으면 ViewModel 테스트가 실제 드라이브를 요구하게 된다.
/// </para>
/// </summary>
public interface IDriveSpace
{
    /// <summary>
    /// 위치가 속한 볼륨의 용량. <b>알 수 없으면 <c>null</c> 이다</b> — 사라진 드라이브,
    /// 권한 없는 볼륨, 응답하지 않는 네트워크 경로가 전부 여기로 온다.
    /// <para>
    /// <c>null</c> 과 <c>DriveSpace(0, 0)</c> 은 다르다. 0 을 내면 상태표시줄이
    /// "여유 공간 0 B" 를 띄우고, 그것은 디스크가 꽉 찼다는 뜻이 된다.
    /// </para>
    /// <para>
    /// <b>실패는 던지지 않는다.</b> 여유 용량은 곁다리 정보이고, 못 읽는다고 폴더를 못 여는
    /// 것은 아니다. 취소만 예외로 나온다.
    /// </para>
    /// </summary>
    ValueTask<DriveSpace?> MeasureAsync(LocationId location, CancellationToken ct);
}
