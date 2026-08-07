using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Model;
using FlexDir.Shell.Enumeration;

using Xunit;

namespace FlexDir.Shell.Tests.Enumeration;

/// <summary>
/// 서버의 공유 목록 (docs/SHELL_NOTES.md §네트워크 · docs/PRD-v2.md §5 N-3).
/// <para>
/// <b>실물 서버에 의존하지 않는다.</b> 도달 가능한 NAS 가 있는 기계에서만 도는 테스트는
/// 다른 기계에서 조용히 빨간불이 된다. 여기서는 <b>기계와 무관한 규칙</b>만 고정하고,
/// 실물 목록은 사람이 본다 (.harness/manual-plan.md §v2 네트워크 · CLAUDE.md §5).
/// </para>
/// </summary>
public class NetworkShareSourceTests
{
    // ── 서버가 아닌 위치는 이 구현체의 일이 아니다 ────────────────
    // 라우팅이 잘못 붙었을 때 조용히 빈 목록을 내면 원인을 찾을 수 없다.

    [Fact]
    public async Task Enumerate_ForANonServerLocation_Throws()
    {
        var source = new NetworkShareSource();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(Folder(@"C:\Temp"), CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task TryGetItem_ForANonServerLocation_Throws()
    {
        var source = new NetworkShareSource();

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.TryGetItemAsync(Folder(@"\\server\share"), CancellationToken.None));
    }

    // ── 취소 ──────────────────────────────────────────────────────
    // 열거는 네트워크에 닿는다. 취소를 관측하지 않으면 폴더를 떠난 뒤에도 계속 돈다.

    [Fact]
    public async Task Enumerate_WithACancelledToken_Throws()
    {
        var source = new NetworkShareSource();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(Folder(@"\\server"), cancelled.Token))
            {
            }
        });
    }

    // ── 도달할 수 없는 서버 ───────────────────────────────────────
    // 이름 해석이 실패하는 것은 정상 상황이다 (오타·꺼진 서버). 예외 타입이 계약이다 —
    // 상태표시줄 문구가 여기서 갈린다.

    [Fact]
    public async Task Enumerate_ForAServerThatCannotBeReached_IsALocationAccessException()
    {
        var source = new NetworkShareSource();

        // RFC 2606 이 예약한 이름이라 어느 기계에서도 해석되지 않는다.
        var error = await Assert.ThrowsAsync<LocationAccessException>(async () =>
        {
            await foreach (var _ in source.EnumerateAsync(Folder(@"\\flex-dir-nowhere.invalid"), CancellationToken.None))
            {
            }
        });

        Assert.NotEqual(0, error.Win32Error);
    }

    // ── 오류 분류 (docs/PRD-v2.md §5 N-4) ─────────────────────────
    // 뭉뚱그리면 비밀번호를 고쳐야 하는지 주소를 고쳐야 하는지 알 수 없다.

    [Theory]
    [InlineData(5, LocationErrorKind.AccessDenied)]               // ERROR_ACCESS_DENIED
    [InlineData(1326, LocationErrorKind.AccessDenied)]            // ERROR_LOGON_FAILURE — 자격증명 거부
    [InlineData(53, LocationErrorKind.NotFound)]                  // ERROR_BAD_NETPATH
    [InlineData(67, LocationErrorKind.NotFound)]                  // ERROR_BAD_NET_NAME
    [InlineData(1219, LocationErrorKind.CredentialConflict)]      // ERROR_SESSION_CREDENTIAL_CONFLICT
    public void Translate_ClassifiesTheNetworkCodes(int win32, LocationErrorKind expected)
    {
        var error = NetworkShareSource.Translate(win32, Folder(@"\\10.10.10.23"));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(win32, error.Win32Error);
    }

    [Fact]
    public void Translate_1219_IsNotAccessDenied()
    {
        // 1219 는 비밀번호를 고쳐도, 주소를 고쳐도 안 된다. AccessDenied 로 접으면
        // 사용자가 손쓸 방법이 없다 — 기존 세션을 끊으라고 말해야 한다.
        var server = Folder(@"\\10.10.10.23");

        Assert.NotEqual(
            NetworkShareSource.Translate(5, server).Message,
            NetworkShareSource.Translate(1219, server).Message);

        Assert.Contains(@"net use \\10.10.10.23 /delete", NetworkShareSource.Translate(1219, server).Message);
    }

    // ── 항목의 모양 ───────────────────────────────────────────────

    [Fact]
    public void ShareItem_IsADirectoryWithoutSizeOrTime()
    {
        // 공유는 폴더처럼 다룬다 — 더블클릭으로 들어가고 정렬에서 위로 온다.
        // 크기와 수정 시각은 없다: 모른다는 것을 빈 칸으로 말한다 (TimestampFormatter).
        var item = NetworkShareSource.ShareItem(Folder(@"\\server"), "공유");

        Assert.True(item.IsDirectory);
        Assert.Equal("공유", item.Name);
        Assert.Equal(@"\\server\공유", item.Location.DisplayPath);
        Assert.Equal(0, item.Size);
        Assert.Equal(default, item.ModifiedUtc);
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out var error), $"파싱 실패: {error}");
        return location;
    }
}
