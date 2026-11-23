using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 把「圖片檔案路徑」轉成畫面可以顯示的圖片。
/// 用 OnLoad 一次讀進記憶體，這樣圖片檔不會被鎖住；同一個路徑只讀一次。
/// 路徑是 null 或讀取失敗就回傳 null（畫面上就不顯示圖片）。
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Cache[path] = image;
            return image;
        }
        catch (Exception)
        {
            return null; // 圖片壞掉就不顯示，改用文字頭像
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
