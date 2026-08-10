using System.Windows;
using System.Windows.Controls;

using FlexDir.App.ViewModels;

namespace FlexDir.App.Views;

/// <summary>
/// 목록에서 트리로 끌어다 놓아 고정하는 attached behavior (docs/PRD-v2.md §10-2).
///
/// <para>
/// 싣는 쪽은 <see cref="DragDropInput"/> 이 이미 한다 — 목록은 선택된 경로를
/// <c>CF_HDROP</c> 으로 싣고 있으므로 받는 쪽만 있으면 된다. <b>탐색기에서 끌어 온 것도
/// 같은 형식이라 그대로 받는다.</b>
/// </para>
///
/// <para>
/// <b>효과는 <see cref="DragDropEffects.Link"/> 다.</b> 복사도 이동도 아니다 — 원본은
/// 그대로 있고 트리에 가리키는 것만 생긴다. 커서가 그 뜻을 보여줘야 사용자가 파일이
/// 옮겨진다고 오해하지 않는다.
/// </para>
///
/// <para>
/// <b>폴더인지 여기서 가리지 않는다.</b> 그 판정은 파일시스템에 물어야 하고
/// (CLAUDE.md §3) 드롭 핸들러는 UI 스레드다. 경로 문자열만 넘기고
/// <c>FolderTreeViewModel.AddFavoritesAsync</c> 가 가린다.
/// </para>
///
/// <para>
/// 판정 둘(<see cref="Effect"/>·<see cref="Paths"/>)만 채점한다. 모달 루프는 자동
/// 테스트가 밟을 수 없다 (CLAUDE.md §5).
/// </para>
/// </summary>
public static class FavoriteDropInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(FavoriteDropInput),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>받을 수 있으면 링크, 아니면 아무것도 아니다.</summary>
    internal static DragDropEffects Effect(bool hasFiles)
        => hasFiles ? DragDropEffects.Link : DragDropEffects.None;

    /// <summary>실린 경로들. 파일 목록이 아니면 빈 배열이다.</summary>
    internal static string[] Paths(IDataObject? data)
        => data?.GetData(DataFormats.FileDrop) as string[] ?? [];

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TreeView tree || args.NewValue is not true)
        {
            return;
        }

        tree.AllowDrop = true;

        // 켜는 것은 XAML 에서 한 번뿐이다 — 끄는 경로가 없으므로 해제 코드도 없다.
        tree.DragOver += OnDragOver;
        tree.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        args.Effects = Effect(args.Data.GetDataPresent(DataFormats.FileDrop));
        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        // 트리는 워크스페이스를 DataContext 로 갖는다 (창 전체가 하나다).
        if (sender is not TreeView { DataContext: WorkspaceViewModel { Tree: { } tree } })
        {
            return;
        }

        var paths = Paths(args.Data);

        if (paths.Length == 0)
        {
            return;
        }

        args.Handled = true;

        // 기다리지 않는다 — 드롭 핸들러가 돌아가야 원본 앱의 DoDragDrop 이 풀린다
        // (DragDropInput.OnDrop 과 같은 이유). 폴더 판정과 저장은 그 뒤에 이어진다.
        _ = tree.AddFavoritesAsync(paths);
    }
}
