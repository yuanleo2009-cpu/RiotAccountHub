using System.Windows;
using RiotAccountHub.Helpers;
using RiotAccountHub.ViewModels;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

/// <summary>準星大圖預覽</summary>
public partial class CrosshairDetailWindow : FluentWindow
{
    private readonly CrosshairItemViewModel _item;
    private readonly CrosshairsViewModel _owner;

    public CrosshairDetailWindow(CrosshairItemViewModel item, CrosshairsViewModel owner)
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);
        _item = item;
        _owner = owner;

        Title = "準星預覽 · " + item.Name;
        NameText.Text = item.Name;
        SourceText.Text = item.SourceText;
        CodeBox.Text = item.Code;
        Preview.Code = item.Code;
        DeleteButton.Visibility = item.IsExample ? Visibility.Collapsed : Visibility.Visible;
        UpdateFavoriteButton();

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

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!await _owner.TrySetClipboardAsync(_item.Code)) return;
        Toast.Success(Snackbars, CrosshairsViewModel.CopiedTitle, CrosshairsViewModel.CopiedHint);
    }

    private void OnFavorite(object sender, RoutedEventArgs e)
    {
        _owner.ToggleFavoriteCommand.Execute(_item);
        UpdateFavoriteButton();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var countBefore = _owner.Items.Count;
        _owner.DeleteCommand.Execute(_item);
        if (_owner.Items.Count < countBefore) Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>收藏按鈕：已收藏是紅色實心星星</summary>
    private void UpdateFavoriteButton()
    {
        FavoriteButton.Content = _item.IsFavorite ? "已收藏" : "收藏";
        FavoriteButton.Icon = new SymbolIcon(SymbolRegular.Star20) { Filled = _item.IsFavorite };
        if (_item.IsFavorite)
        {
            FavoriteButton.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Accent");
        }
        else
        {
            FavoriteButton.ClearValue(ForegroundProperty);
        }
    }
}
