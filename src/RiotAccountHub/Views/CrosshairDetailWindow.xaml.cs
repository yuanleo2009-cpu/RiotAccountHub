using System.Windows;
using RiotAccountHub.Helpers;
using RiotAccountHub.ViewModels;

namespace RiotAccountHub.Views;

/// <summary>準星大圖預覽</summary>
public partial class CrosshairDetailWindow : Window
{
    private readonly CrosshairItemViewModel _item;
    private readonly CrosshairsViewModel _owner;

    public CrosshairDetailWindow(CrosshairItemViewModel item, CrosshairsViewModel owner)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _item = item;
        _owner = owner;

        NameText.Text = item.Name;
        SourceText.Text = item.SourceText;
        CodeBox.Text = item.Code;
        Preview.Code = item.Code;
        DeleteButton.Visibility = item.IsExample ? Visibility.Collapsed : Visibility.Visible;
        UpdateFavoriteText();

        // 背景跟著主視窗目前的選擇
        Preview.BackgroundMode = owner.BackgroundMode;
        (owner.BackgroundMode switch
        {
            CrosshairBackground.Light => BgLight,
            CrosshairBackground.Map => BgMap,
            _ => BgDark,
        }).IsChecked = true;
    }

    private void OnBackgroundChanged(object sender, RoutedEventArgs e)
    {
        if (Preview is null) return; // InitializeComponent 期間
        var mode = sender == BgLight ? CrosshairBackground.Light
            : sender == BgMap ? CrosshairBackground.Map
            : CrosshairBackground.Dark;
        Preview.BackgroundMode = mode;
        _owner.BackgroundMode = mode; // 網格上的預覽也一起換
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!_owner.TrySetClipboard(_item.Code)) return;
        CopiedText.Text = CrosshairsViewModel.CopiedHint;
        CopiedNotice.Visibility = Visibility.Visible;
        _owner.NoticeText = CrosshairsViewModel.CopiedHint;
    }

    private void OnFavorite(object sender, RoutedEventArgs e)
    {
        _owner.ToggleFavoriteCommand.Execute(_item);
        UpdateFavoriteText();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var countBefore = _owner.Items.Count;
        _owner.DeleteCommand.Execute(_item);
        if (_owner.Items.Count < countBefore) Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateFavoriteText() =>
        FavoriteButton.Content = _item.IsFavorite ? "★ 已收藏" : "☆ 收藏";
}
