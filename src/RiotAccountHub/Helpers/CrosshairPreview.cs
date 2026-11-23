using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        new FrameworkPropertyMetadata(CrosshairBackground.Map, FrameworkPropertyMetadataOptions.AffectsRender));

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
    /// <summary>模擬遊戲裡的水泥牆面（程式自己產生的材質，不使用任何網站或遊戲圖片）</summary>
    private static readonly Lazy<Brush> MapBg = new(CreateWallBrush);

    /// <summary>
    /// 產生一張 256×256、可以無縫重複的米灰色牆面材質：
    ///   底色 + 大片的明暗斑駁 + 細小顆粒 + 幾條淡淡的裂紋。
    /// 用固定的亂數種子，所以每次畫出來都一樣。
    /// </summary>
    private static Brush CreateWallBrush()
    {
        const int size = 256;
        var rng = new Random(20261123);

        // 1. 低頻斑駁：在 8×8 的格子上隨機取值，再平滑內插（可無縫重複）
        const int grid = 8;
        var cells = new double[grid, grid];
        for (var y = 0; y < grid; y++)
            for (var x = 0; x < grid; x++)
                cells[x, y] = rng.NextDouble();

        double Blotch(int px, int py)
        {
            var gx = px / (double)size * grid;
            var gy = py / (double)size * grid;
            int x0 = (int)gx % grid, y0 = (int)gy % grid;
            int x1 = (x0 + 1) % grid, y1 = (y0 + 1) % grid;
            double fx = gx - Math.Floor(gx), fy = gy - Math.Floor(gy);
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var top = cells[x0, y0] + (cells[x1, y0] - cells[x0, y0]) * fx;
            var bottom = cells[x0, y1] + (cells[x1, y1] - cells[x0, y1]) * fx;
            return top + (bottom - top) * fy;
        }

        // 2. 逐像素上色：米灰底色 + 斑駁 + 顆粒
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var shade = (Blotch(x, y) - 0.5) * 22 + (rng.NextDouble() - 0.5) * 9;
                var i = (y * size + x) * 4;
                pixels[i + 0] = ClampByte(118 + shade); // B
                pixels[i + 1] = ClampByte(130 + shade); // G
                pixels[i + 2] = ClampByte(137 + shade); // R
                pixels[i + 3] = 255;
            }
        }
        var baseBitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);

        // 3. 疊上幾條淡淡的裂紋（隨機彎曲的細線）
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseBitmap, new Rect(0, 0, size, size));
            var dark = new Pen(new SolidColorBrush(Color.FromArgb(70, 60, 56, 50)), 1.2);
            var light = new Pen(new SolidColorBrush(Color.FromArgb(55, 210, 205, 195)), 1);
            for (var c = 0; c < 7; c++)
            {
                var p = new Point(rng.Next(size), rng.Next(size));
                var angle = rng.NextDouble() * Math.PI * 2;
                var segments = rng.Next(3, 7);
                for (var s = 0; s < segments; s++)
                {
                    angle += (rng.NextDouble() - 0.5) * 1.2;
                    var len = 6 + rng.NextDouble() * 12;
                    var next = new Point(p.X + Math.Cos(angle) * len, p.Y + Math.Sin(angle) * len);
                    dc.DrawLine(dark, p, next);
                    dc.DrawLine(light, new Point(p.X + 1, p.Y + 1), new Point(next.X + 1, next.Y + 1));
                    p = next;
                }
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private static byte ClampByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

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
            CrosshairBackground.Map => MapBg.Value,
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
