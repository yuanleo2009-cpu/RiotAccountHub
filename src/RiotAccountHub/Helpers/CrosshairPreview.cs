using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RiotAccountHub.Core.Crosshairs;

namespace RiotAccountHub.Helpers;

/// <summary>預覽背景</summary>
public enum CrosshairBackground
{
    Dark,
    Light,
    Map,
}

/// <summary>
/// 畫出準星預覽的元件。
///
/// 做法：把代碼解析成 CrosshairStyle，再用「1080p 下的像素」為單位畫出：
///   內線 / 外線：上下左右四條，間距 offset、長度 length、粗細 thickness、透明度 opacity
///   中心點：邊長 size 的正方形
///   外框：每個部分外面再包一圈 outline thickness 的黑框
/// 最後整體放大 PixelScale 倍，並對齊到整數像素，看起來才會銳利。
/// 代碼看不懂時只顯示「?」，不會崩潰。
/// </summary>
public sealed class CrosshairPreview : FrameworkElement
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(CrosshairPreview),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PixelScaleProperty = DependencyProperty.Register(
        nameof(PixelScale), typeof(double), typeof(CrosshairPreview),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundModeProperty = DependencyProperty.Register(
        nameof(BackgroundMode), typeof(CrosshairBackground), typeof(CrosshairPreview),
        new FrameworkPropertyMetadata(CrosshairBackground.Dark, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>準星代碼</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>放大倍數（遊戲裡 1 像素畫成幾個像素）</summary>
    public double PixelScale
    {
        get => (double)GetValue(PixelScaleProperty);
        set => SetValue(PixelScaleProperty, value);
    }

    public CrosshairBackground BackgroundMode
    {
        get => (CrosshairBackground)GetValue(BackgroundModeProperty);
        set => SetValue(BackgroundModeProperty, value);
    }

    public CrosshairPreview()
    {
        // 關掉反鋸齒，準星的像素邊緣才會跟遊戲一樣銳利
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
        ClipToBounds = true;
    }

    private static readonly Brush DarkBg = Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x17, 0x1C)));
    private static readonly Brush LightBg = Frozen(new SolidColorBrush(Color.FromRgb(0xE9, 0xE5, 0xDB)));
    private static readonly Brush MapBg = Frozen(new LinearGradientBrush(
        Color.FromRgb(0x6E, 0x75, 0x7D), Color.FromRgb(0xA4, 0xA9, 0xAE), new Point(0, 0), new Point(1, 1)));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(BackgroundMode switch
        {
            CrosshairBackground.Light => LightBg,
            CrosshairBackground.Map => MapBg,
            _ => DarkBg,
        }, null, new Rect(0, 0, w, h));

        CrosshairParseResult parsed;
        try
        {
            parsed = CrosshairCode.Parse(Code);
        }
        catch
        {
            parsed = new CrosshairParseResult(); // 保險：解析器本身不會丟錯，但預覽絕不能讓程式崩潰
        }

        if (!parsed.IsValid)
        {
            DrawQuestionMark(dc, w, h);
            return;
        }

        var style = parsed.Primary;
        var scale = Math.Max(1, PixelScale);
        var cx = Math.Round(w / 2);
        var cy = Math.Round(h / 2);

        // 收集要畫的長方形（單位：遊戲像素，相對於中心）
        var parts = new List<(Rect Rect, double Opacity)>();
        AddLines(parts, style.Inner);
        AddLines(parts, style.Outer);
        if (style.CenterDot && style.CenterDotSize > 0 && style.CenterDotOpacity > 0)
        {
            var z = style.CenterDotSize;
            parts.Add((new Rect(-z / 2, -z / 2, z, z), style.CenterDotOpacity));
        }

        // 1. 先畫外框（在每個部分外面多 t 像素）
        if (style.Outline && style.OutlineThickness > 0 && style.OutlineOpacity > 0)
        {
            var t = style.OutlineThickness;
            var outline = new SolidColorBrush(Color.FromArgb(ToByte(style.OutlineOpacity), 0, 0, 0));
            outline.Freeze();
            foreach (var (rect, _) in parts)
            {
                var r = rect;
                r.Inflate(t, t);
                dc.DrawRectangle(outline, null, ToScreen(r, cx, cy, scale));
            }
        }

        // 2. 再畫本體
        var color = ParseColor(style.ColorHex);
        foreach (var (rect, opacity) in parts)
        {
            var brush = new SolidColorBrush(Color.FromArgb(ToByte(opacity), color.R, color.G, color.B));
            brush.Freeze();
            dc.DrawRectangle(brush, null, ToScreen(rect, cx, cy, scale));
        }
    }

    /// <summary>加入一組（內或外）線：上、下、左、右</summary>
    private static void AddLines(List<(Rect, double)> parts, CrosshairLines lines)
    {
        if (!lines.IsVisible) return;
        var t = lines.Thickness;
        var o = lines.Offset;
        var len = lines.Length;
        var v = lines.EffectiveVerticalLength;

        if (len > 0)
        {
            parts.Add((new Rect(o, -t / 2, len, t), lines.Opacity));        // 右
            parts.Add((new Rect(-o - len, -t / 2, len, t), lines.Opacity)); // 左
        }
        if (v > 0)
        {
            parts.Add((new Rect(-t / 2, o, t, v), lines.Opacity));          // 下
            parts.Add((new Rect(-t / 2, -o - v, t, v), lines.Opacity));     // 上
        }
    }

    /// <summary>遊戲像素 → 螢幕座標，並對齊整數像素</summary>
    private static Rect ToScreen(Rect r, double cx, double cy, double scale)
    {
        var x1 = Math.Round(cx + r.Left * scale);
        var y1 = Math.Round(cy + r.Top * scale);
        var x2 = Math.Round(cx + r.Right * scale);
        var y2 = Math.Round(cy + r.Bottom * scale);
        return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
    }

    private static byte ToByte(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.White;
        }
    }

    private void DrawQuestionMark(DrawingContext dc, double w, double h)
    {
        var text = new FormattedText("?", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Math.Min(w, h) * 0.4, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
    }
}
