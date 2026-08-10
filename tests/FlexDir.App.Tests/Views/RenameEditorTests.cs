using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlexDir.App.ViewModels;
using FlexDir.App.Views;

using FlexDir.Core.Locations;
using FlexDir.Core.Model;

using Xunit;

namespace FlexDir.App.Tests.Views;

/// <summary>
/// 이름변경 인라인 편집기 (phase B-4 · docs/DESIGN.md §9-1).
/// <para>
/// 채점하는 것은 판정 셋이다 — 키가 뜻하는 것, 편집기를 열 때의 초기 선택 범위, 편집이
/// 닫힌 뒤 키보드를 돌려줄 목록. 포커스 이동과 이벤트 훅은 사람이 확인한다 (CLAUDE.md §5).
/// </para>
/// </summary>
public class RenameEditorTests
{
    private static LocationId Loc(string path) => LocationId.TryParse(path, out var id, out _)
        ? id
        : throw new InvalidOperationException(path);

    // ── 키가 뜻하는 것 (docs/DESIGN.md §9-1) ──────────────────────

    [Theory]
    [InlineData(Key.Return, RenameKey.Commit)]
    [InlineData(Key.Escape, RenameKey.Cancel)]
    [InlineData(Key.Down, RenameKey.Stay)]
    [InlineData(Key.Tab, RenameKey.Stay)]        // 편집 중에는 페인 전환도 새지 않는다
    [InlineData(Key.F2, RenameKey.Stay)]
    [InlineData(Key.A, RenameKey.Stay)]
    [InlineData(Key.System, RenameKey.Stay)]     // Alt 조합. Alt+D 가 주소줄로 가면 안 된다
    public void Choose_MapsTheKeyToWhatItMeans(Key key, RenameKey expected)
    {
        Assert.Equal(expected, RenameEditor.Choose(key));
    }

    // ── 무엇을 삼키는가 (B-4 실물이 잡은 자리) ────────────────────

    [Theory]
    // 글자를 만드는 키는 절대 삼키면 안 된다. KeyDown 을 Handled 로 표시하면 WPF 가 그
    // 키에서 TextInput 을 만들지 않아 <b>타이핑이 통째로 죽는다</b> — 캐럿은 살아 있고
    // 글자만 안 들어간다.
    [InlineData(Key.A, ModifierKeys.None, false)]
    [InlineData(Key.D1, ModifierKeys.None, false)]
    [InlineData(Key.Space, ModifierKeys.None, false)]
    [InlineData(Key.OemPeriod, ModifierKeys.None, false)]
    [InlineData(Key.ImeProcessed, ModifierKeys.None, false)]  // 한글 조합 중
    // 목록으로 새면 안 되는 키들
    [InlineData(Key.Return, ModifierKeys.None, true)]
    [InlineData(Key.Escape, ModifierKeys.None, true)]
    [InlineData(Key.Down, ModifierKeys.None, true)]
    [InlineData(Key.PageDown, ModifierKeys.None, true)]
    [InlineData(Key.Tab, ModifierKeys.None, true)]           // 페인 전환도 새지 않는다
    [InlineData(Key.F2, ModifierKeys.None, true)]
    [InlineData(Key.F5, ModifierKeys.None, true)]
    // Delete 는 창이 휴지통에 걸어 두었다 — 편집 중에 새면 고르던 파일이 사라진다
    [InlineData(Key.Delete, ModifierKeys.None, true)]
    // Ctrl+Space 는 선택 토글이다. 맨 Space 는 글자다
    [InlineData(Key.Space, ModifierKeys.Control, true)]
    public void Swallows_BlocksTheShortcutsButNeverTheTyping(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, RenameEditor.Swallows(key, modifiers));
    }

    // ── 초기 선택 범위 — 파일은 확장자 제외, 폴더는 전체 (탐색기와 같다) ──

    [Theory]
    [InlineData("report.txt", 0, 6)]
    [InlineData("a.tar.gz", 0, 5)]              // 마지막 점만 확장자다
    [InlineData("noextension", 0, 11)]          // 점이 없으면 전체
    [InlineData(".gitignore", 0, 10)]           // 앞점 이름은 확장자가 아니라 이름이다
    [InlineData("trailing.", 0, 8)]
    [InlineData("", 0, 0)]
    public void InitialSelection_ForAFile_LeavesTheExtensionOut(string name, int start, int length)
    {
        Assert.Equal((start, length), RenameEditor.InitialSelection(name, isDirectory: false));
    }

    [Theory]
    [InlineData("Documents")]
    [InlineData("folder.v2")]                   // 폴더의 점은 확장자가 아니다
    public void InitialSelection_ForAFolder_IsTheWholeName(string name)
    {
        Assert.Equal((0, name.Length), RenameEditor.InitialSelection(name, isDirectory: true));
    }

    // ── 편집이 닫히면 키보드를 목록·트리로 돌려준다 ────────────────

    [Fact]
    public void OwnerContainer_FindsTheListTheEditorSitsIn()
    {
        // 편집기는 합성 행 안에 있을 수 있다 (ADR-016) — 중첩된 ItemsControl 에서
        // 멈추면 포커스가 Focusable=False 인 행 컨테이너로 간다.
        OnSta(() =>
        {
            var box = new TextBox();
            var row = new ItemsControl { Focusable = false };
            var list = new ListBox();

            row.Items.Add(box);
            list.Items.Add(row);
            list.Measure(new Size(400, 400));

            Assert.Same(list, RenameEditor.OwnerContainer(box));
        });
    }

    [Fact]
    public void OwnerContainer_FindsTheTreeToo()
    {
        // 즐겨찾기 이름도 같은 편집기로 고친다 (docs/PRD-v2.md §10-2).
        OnSta(() =>
        {
            var box = new TextBox();
            var item = new TreeViewItem { Header = box };
            var tree = new TreeView();

            tree.Items.Add(item);
            tree.Measure(new Size(400, 400));

            Assert.Same(tree, RenameEditor.OwnerContainer(box));
        });
    }

    [Fact]
    public void OwnerContainer_OutsideBoth_IsNothing()
    {
        OnSta(() => Assert.Null(RenameEditor.OwnerContainer(new TextBox())));
    }

    // ── 무엇의 이름을 고치고 있나 ─────────────────────────────────

    [Fact]
    public void Subject_ForAListItem_IsItsName()
    {
        var item = new FileItemViewModel(
            new FileItem("report.txt", Loc(@"C:\Temp\report.txt"), 10, DateTimeOffset.UnixEpoch, FileItemFlags.None),
            "10 KB",
            "텍스트 문서",
            "2026-08-10");

        Assert.Equal(("report.txt", false), RenameEditor.Subject(item));
    }

    [Fact]
    public void Subject_ForAFavorite_IsTheEditingLabel()
    {
        // 표시 이름이 아니라 편집용 글자를 싣는다 — 확정할 때까지 트리의 이름은 그대로다.
        var node = new TreeNodeViewModel(Loc(@"C:\work"), "작업", isFavorite: true);
        node.EditingLabel = "펌웨어";

        // 즐겨찾기는 늘 폴더라 전체가 선택된다.
        Assert.Equal(("펌웨어", true), RenameEditor.Subject(node));
    }

    [Fact]
    public void Subject_OfSomethingElse_IsNothing()
    {
        Assert.Null(RenameEditor.Subject(null));
        Assert.Null(RenameEditor.Subject("문자열"));
    }

    // ── attached property 왕복 ────────────────────────────────────

    [Fact]
    public void Wiring_RoundTrips()
    {
        var element = new DependencyObject();
        var command = new NoopCommand();

        RenameEditor.SetEnabled(element, true);
        RenameEditor.SetCommitCommand(element, command);
        RenameEditor.SetCancelCommand(element, command);

        Assert.True(RenameEditor.GetEnabled(element));
        Assert.Same(command, RenameEditor.GetCommitCommand(element));
        Assert.Same(command, RenameEditor.GetCancelCommand(element));
    }

    [Fact]
    public void Wiring_DefaultsToOff()
    {
        var element = new DependencyObject();

        Assert.False(RenameEditor.GetEnabled(element));
        Assert.Null(RenameEditor.GetCommitCommand(element));
        Assert.Null(RenameEditor.GetCancelCommand(element));
    }

    /// <summary>
    /// WPF 요소는 STA 에서만 만들어지고 xunit 은 스레드풀(MTA)에서 돈다
    /// (<c>ListInputTests.OnSta</c> 와 같다).
    /// </summary>
    private static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }
        })
        {
            IsBackground = true,
            Name = "flex-dir test sta",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA 스레드가 끝나지 않았다.");

        failure?.Throw();
    }

    private sealed class NoopCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
