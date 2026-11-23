using System.Windows;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

/// <summary>
/// 主視窗。依照 MVVM，這裡幾乎不寫程式：畫面上的按鈕都綁定到 MainViewModel 的 Command。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }
}
