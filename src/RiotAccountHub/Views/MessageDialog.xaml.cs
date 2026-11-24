using System.Windows;
using System.Windows.Media;
using RiotAccountHub.Helpers;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

/// <summary>訊息視窗的種類（決定圖示和顏色）</summary>
public enum DialogKind
{
    Info,
    Question,
    Warning,
    Danger,
    Error,
}

public partial class MessageDialog : FluentWindow
{
    private MessageDialog(string title, string message, DialogKind kind, string primaryText, string? secondaryText)
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryBtn.Content = primaryText;

        if (secondaryText is null)
        {
            // 只有一個按鈕：它同時是「確定」也是「取消(Esc)」
            SecondaryBtn.Visibility = Visibility.Collapsed;
            PrimaryBtn.IsCancel = true;
            PrimaryBtn.IsDefault = true;
        }
        else
        {
            SecondaryBtn.Content = secondaryText;
            // 危險動作（例如刪除）不讓 Enter 直接確認，避免誤按
            PrimaryBtn.IsDefault = kind != DialogKind.Danger;
        }

        // 依種類設定圖示與顏色：一般是灰色，警告是黃色，危險 / 錯誤用紅色
        var (symbol, foreground, background) = kind switch
        {
            DialogKind.Info => (SymbolRegular.Info24, "Brush.TextSecondary", "Brush.Surface3"),
            DialogKind.Question => (SymbolRegular.QuestionCircle24, "Brush.TextSecondary", "Brush.Surface3"),
            DialogKind.Warning => (SymbolRegular.Warning24, "Brush.Warning", "Brush.WarningSoft"),
            DialogKind.Danger => (SymbolRegular.Delete24, "Brush.Accent", "Brush.AccentSoft"),
            _ => (SymbolRegular.ErrorCircle24, "Brush.Accent", "Brush.AccentSoft"),
        };
        IconSymbol.Symbol = symbol;
        IconSymbol.Foreground = (Brush)FindResource(foreground);
        IconBox.Background = (Brush)FindResource(background);
    }

    /// <summary>顯示訊息視窗。按主要按鈕回傳 true。</summary>
    /// <param name="onTop">保持在所有視窗的最上層（用在主視窗可能被別的程式蓋住的提醒）</param>
    public static bool Show(Window? owner, string title, string message, DialogKind kind, string primaryText, string? secondaryText, bool onTop = false)
    {
        var dialog = new MessageDialog(title, message, kind, primaryText, secondaryText) { Topmost = onTop };
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dialog.ShowDialog() == true;
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSecondary(object sender, RoutedEventArgs e) => DialogResult = false;
}
