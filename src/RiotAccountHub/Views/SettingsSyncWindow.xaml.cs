using System.Windows;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

/// <summary>「同步遊戲設定」視窗。按鈕都綁定到 SettingsSyncViewModel 的 Command。</summary>
public partial class SettingsSyncWindow : Window
{
    public SettingsSyncWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }
}
