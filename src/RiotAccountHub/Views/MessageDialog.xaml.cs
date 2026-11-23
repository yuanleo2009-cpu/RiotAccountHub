using System.Windows;
using System.Windows.Media;
using RiotAccountHub.Helpers;

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

public partial class MessageDialog : Window
{
    private MessageDialog(string title, string message, DialogKind kind, string primaryText, string? secondaryText)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (secondaryText is null)
        {
            // 只有一個按鈕：它同時是「確定」也是「取消(Esc)」
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
            PrimaryButton.IsDefault = true;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
            // 危險動作（例如刪除）不讓 Enter 直接確認，避免誤按
            PrimaryButton.IsDefault = kind != DialogKind.Danger;
        }

        // 依種類設定圖示（Segoe 圖示字型的字碼）與顏色
        var (glyph, brushKey) = kind switch
        {
            DialogKind.Info => ("", "TextSecondaryBrush"),
            DialogKind.Question => ("", "TextSecondaryBrush"),
            DialogKind.Warning => ("", "WarningBrush"),
            DialogKind.Danger => ("", "AccentBrush"),
            _ => ("", "AccentBrush"),
        };
        var brush = (Brush)FindResource(brushKey);
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = brush;
        IconBg.Background = (Brush)FindResource("Surface2Brush");

        if (kind == DialogKind.Warning)
        {
            PrimaryButton.Style = (Style)FindResource("SecondaryButton");
        }
    }

    /// <summary>顯示訊息視窗。按主要按鈕回傳 true。</summary>
    public static bool Show(Window? owner, string title, string message, DialogKind kind, string primaryText, string? secondaryText)
    {
        var dialog = new MessageDialog(title, message, kind, primaryText, secondaryText);
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
