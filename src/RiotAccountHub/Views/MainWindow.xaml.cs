using RiotAccountHub.Helpers;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

/// <summary>
/// 主視窗（FluentWindow：WPF UI 的視窗，有 Mica 背景和自訂標題列）。
/// 依照 MVVM，這裡幾乎不寫程式：畫面上的按鈕都綁定到 MainViewModel 的 Command。
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);
    }
}
