using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 從視窗底部滑出來的小提示（WPF UI 的 Snackbar），幾秒後自己消失。
/// 視窗裡要放一個 ui:SnackbarPresenter，再把它傳進來。
/// </summary>
public static class Toast
{
    private static readonly TimeSpan ShowTime = TimeSpan.FromSeconds(4);

    /// <summary>成功的提示（綠色勾勾）</summary>
    public static void Success(SnackbarPresenter presenter, string title, string message)
    {
        var app = Application.Current;
        var snackbar = new Snackbar(presenter)
        {
            Title = title,
            Content = message,
            Appearance = ControlAppearance.Secondary,
            Icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24)
            {
                Filled = true,
                FontSize = 24,
                Foreground = app?.TryFindResource("Brush.Success") as Brush,
            },
            Timeout = ShowTime,
            IsCloseButtonEnabled = true,
        };
        if (app?.TryFindResource("Brush.Surface3") is Brush background) snackbar.Background = background;
        if (app?.TryFindResource("Brush.StrokeStrong") is Brush border) snackbar.BorderBrush = border;
        if (app?.TryFindResource("Brush.Text") is Brush text)
        {
            snackbar.Foreground = text;
            snackbar.ContentForeground = app.TryFindResource("Brush.TextSecondary") as Brush ?? text;
        }

        // 新的提示直接取代還在顯示的舊提示，不用排隊等
        snackbar.Show(immediately: true);
    }
}
