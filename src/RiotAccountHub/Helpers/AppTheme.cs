using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 程式的主題設定：深色 + 唯一的強調色（紅色 #FF4655）。
/// 顏色、字號、圓角、間距的定義都在 Themes/Tokens.xaml。
/// </summary>
public static class AppTheme
{
    /// <summary>強調色。要跟 Tokens.xaml 的 Color.Accent 一樣。</summary>
    public static readonly Color Accent = Color.FromRgb(0xFF, 0x46, 0x55);

    /// <summary>Mica（半透明的桌布霧面背景）需要 Windows 11（組建 22000 以上）</summary>
    public static bool SupportsMica => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>程式啟動時呼叫一次</summary>
    public static void Apply()
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, updateAccent: false);

        // 讓 WPF UI 自己的控制項（開關、進度圈…）也用同一個紅色
        ApplicationAccentColorManager.Apply(Accent, Accent, Accent, Accent);

        // 紅色上面的字和圖示一律用白色（WPF UI 預設會算成黑色）
        var resources = Application.Current.Resources;
        var white = new SolidColorBrush(Colors.White);
        white.Freeze();
        resources["TextOnAccentFillColorPrimary"] = Colors.White;
        resources["TextOnAccentFillColorSecondary"] = Colors.White;
        resources["TextOnAccentFillColorPrimaryBrush"] = white;
        resources["TextOnAccentFillColorSecondaryBrush"] = white;
    }

    /// <summary>
    /// 每個視窗建立時呼叫：不支援 Mica 的系統（Windows 10）改用純深色背景。
    /// </summary>
    public static void PrepareWindow(FluentWindow window)
    {
        if (!SupportsMica)
        {
            window.WindowBackdropType = WindowBackdropType.None;
        }
    }
}
