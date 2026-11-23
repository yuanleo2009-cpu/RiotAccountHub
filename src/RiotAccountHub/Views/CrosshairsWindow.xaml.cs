using System.Windows;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

/// <summary>「熱門準星」視窗。按鈕都綁定到 CrosshairsViewModel。</summary>
public partial class CrosshairsWindow : Window
{
    public CrosshairsWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }
}
