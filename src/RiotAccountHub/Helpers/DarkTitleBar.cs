using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 把視窗上方的標題列改成深色，跟深色主題搭配。
/// 使用 Windows 內建的 DWM 設定（Win10 20H1 以後支援深色；Win11 還可以設定顏色）。
/// 不支援的系統會自動忽略，不影響使用。
/// </summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var on = 1;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));

                // 標題列顏色 = 背景色 #0C0F14（Windows 用 BGR 順序：0x00BBGGRR）
                var color = 0x00140F0C;
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref color, sizeof(int));
            }
            catch
            {
                // 舊版 Windows 不支援，沒關係
            }
        };
    }
}
