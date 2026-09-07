using System.Windows;
using System.Windows.Input;

using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 페인 간·페인 밖 드래그앤드롭 (phase B-4 · docs/DESIGN.md §9-1).
/// <para>
/// <c>FlexDir.Shell</c> 을 쓰지 않는다 — WPF <c>DataObject</c> 가 <c>CF_HDROP</c> 마샬링을
/// 대신하므로 포트를 늘리지 않는다. 채점하는 것은 판정 셋이다: 수정키가 뜻하는 효과,
/// 놓인 자리가 뜻하는 폴더, 드래그가 시작됐는가. <c>DoDragDrop</c> 은 모달 루프라
/// 자동 테스트가 밟을 수 없다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class DragDropInputTests
{
    // ── 수정키 → 효과 (docs/DESIGN.md §9-1) ───────────────────────

    [Theory]
    // 기본이 복사인 것은 탐색기와 다르다 — 2분할에서 드래그는 일상 조작이라 같은 손동작이
    // 대상에 따라 원본을 지우기도 안 지우기도 하면 사고가 난다
    [InlineData(ModifierKeys.None, DragDropEffects.Copy)]
    [InlineData(ModifierKeys.Shift, DragDropEffects.Move)]
    [InlineData(ModifierKeys.Control, DragDropEffects.Copy)]
    // 바로가기는 v1 범위 밖이다 — 아무 일도 하지 않는다
    [InlineData(ModifierKeys.Alt, DragDropEffects.None)]
    // Shift 가 이긴다 (클릭 판정과 같은 규칙 — ListInput.Choose)
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, DragDropEffects.Move)]
    // Alt 가 섞이면 무엇을 뜻하는지 정해진 바가 없다. 아무 일도 하지 않는 쪽이 안전하다
    [InlineData(ModifierKeys.Shift | ModifierKeys.Alt, DragDropEffects.None)]
    public void Effect_MapsModifiersToWhatHappens(ModifierKeys modifiers, DragDropEffects expected)
    {
        Assert.Equal(expected, DragDropInput.Effect(modifiers));
    }

    [Theory]
    [InlineData(DragDropEffects.Move, true)]
    [InlineData(DragDropEffects.Copy, false)]
    public void IsMove_ReadsTheEffectBack(DragDropEffects effect, bool expected)
    {
        Assert.Equal(expected, DragDropInput.IsMove(effect));
    }

    // ── 놓인 자리 → 폴더 (docs/DESIGN.md §9-1) ────────────────────

    [Fact]
    public void TargetFolder_OnAFolder_IsThatFolder()
    {
        var pictures = Folder(@"C:\Temp\Pictures");

        Assert.Equal(pictures, DragDropInput.TargetFolder(Item("Pictures", isDirectory: true), Folder(@"C:\Temp")));
    }

    [Fact]
    public void TargetFolder_OnAFile_IsThePaneFolder()
    {
        // 파일 위에 놓는 것은 겨냥이 어긋난 것이다 — 페인이 보고 있는 폴더로 본다.
        var current = Folder(@"C:\Temp");

        Assert.Equal(current, DragDropInput.TargetFolder(Item("a.txt", isDirectory: false), current));
    }

    [Fact]
    public void TargetFolder_OnEmptySpace_IsThePaneFolder()
    {
        var current = Folder(@"C:\Temp");

        Assert.Equal(current, DragDropInput.TargetFolder(null, current));
    }

    [Fact]
    public void TargetFolder_WithoutAPaneFolder_IsNothing()
    {
        // 아직 아무것도 열지 않은 페인이다.
        Assert.Null(DragDropInput.TargetFolder(null, null));
    }

    // ── 드래그가 시작됐는가 ───────────────────────────────────────

    [Fact]
    public void HasLeftTheStartingPoint_OnlyBeyondTheSystemThreshold()
    {
        // 원점은 (0,0) 이다 — 임계값이 DPI 배율로 나뉘어 3.2 같은 값이 되면 이진수로
        // 표현되지 않아 (100 + 3.2) - 100 이 3.2 를 2.8e-15 만큼 넘는다. 그러면 '정확히
        // 임계값' 이어야 할 점이 임계값 밖이 되어 배율 125% 인 기계에서만 실패한다.
        var origin = new Point(0, 0);
        var dx = SystemParameters.MinimumHorizontalDragDistance;
        var dy = SystemParameters.MinimumVerticalDragDistance;

        Assert.False(DragDropInput.HasLeftTheStartingPoint(origin, origin));
        Assert.False(DragDropInput.HasLeftTheStartingPoint(origin, new Point(dx, dy)));
        Assert.True(DragDropInput.HasLeftTheStartingPoint(origin, new Point(dx + 1, 0)));
        Assert.True(DragDropInput.HasLeftTheStartingPoint(origin, new Point(0, -dy - 1)));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void IsFileDragOrigin_RequiresAnItemOutsideTheRenameEditor(
        bool hasItem,
        bool isEditing,
        bool expected)
    {
        Assert.Equal(expected, DragDropInput.IsFileDragOrigin(hasItem, isEditing));
    }

    // ── 무엇을 싣는가 ─────────────────────────────────────────────

    [Fact]
    public void Payload_CarriesTheDisplayPathOfEverySelectedName()
    {
        // 탐색기의 파서는 \\?\ 확장 접두사를 모른다 — ShellClipboardBridge 와 같은 규칙이다.
        var selection = new PaneSelection();
        selection.ReplaceWith(["a.txt", "b.txt"]);

        var paths = DragDropInput.Payload(selection, Folder(@"C:\Temp"));

        Assert.Equal([@"C:\Temp\a.txt", @"C:\Temp\b.txt"], [.. paths.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void Payload_WithNothingSelected_IsEmpty()
    {
        Assert.Empty(DragDropInput.Payload(new PaneSelection(), Folder(@"C:\Temp")));
    }

    [Fact]
    public void Payload_WithoutAFolder_IsEmpty()
    {
        var selection = new PaneSelection();
        selection.ReplaceWith(["a.txt"]);

        Assert.Empty(DragDropInput.Payload(selection, null));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Enabled_RoundTrips()
    {
        var element = new DependencyObject();

        Assert.False(DragDropInput.GetEnabled(element));

        DragDropInput.SetEnabled(element, true);

        Assert.True(DragDropInput.GetEnabled(element));
    }

    private static LocationId Folder(string path)
    {
        Assert.True(LocationId.TryParse(path, out var location, out _));

        return location;
    }

    private static FileItemViewModel Item(string name, bool isDirectory)
    {
        var folder = Folder(@"C:\Temp");

        return new FileItemViewModel(
            new FileItem(
                name,
                folder.Combine(name),
                1024,
                DateTimeOffset.UnixEpoch,
                isDirectory ? FileItemFlags.Directory : FileItemFlags.None),
            string.Empty,
            string.Empty,
            string.Empty);
    }
}
