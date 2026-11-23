using System.Globalization;

namespace RiotAccountHub.Core.Crosshairs;

/// <summary>
/// VALORANT 準星代碼解析器（例如 <c>0;P;c;5;h;0;0l;4;0o;2;0a;1;0f;0;1b;0</c>）。
///
/// 格式：以分號分隔；開頭是版本號（目前是 0），接著是區段標記與「鍵;值」配對：
///   P = 主準星（Primary）、A = 瞄準時（ADS）、S = 狙擊鏡（Sniper）
/// 代碼只會寫出「跟預設值不同」的欄位，沒寫的就是預設值。
///
/// 欄位意義的查證來源：
///   1. 使用者帳號的遊戲設定 SavedCrosshairProfileData（Riot 自己的欄位名稱與預設值）
///   2. 開源專案 salw2e/crosshair 的 codes.js（代碼鍵 ↔ 欄位）
/// 不認得或格式錯誤的欄位一律略過（記在 Warnings），絕不丟出例外。
/// </summary>
public static class CrosshairCode
{
    /// <summary>顏色預設值（c 的 0~7；8 = 自訂顏色，看 u）</summary>
    public static readonly IReadOnlyList<(string Hex, string Name)> PresetColors = new[]
    {
        ("#FFFFFF", "白色"),
        ("#00FF00", "綠色"),
        ("#7FFF00", "黃綠色"),
        ("#DFFF00", "綠黃色"),
        ("#FFFF00", "黃色"),
        ("#00FFFF", "青色"),
        ("#FF00FF", "粉紅色"),
        ("#FF0000", "紅色"),
    };

    /// <summary>
    /// 解析準星代碼。永遠回傳結果；IsValid = false 代表完全看不懂（例如不是準星代碼）。
    /// </summary>
    public static CrosshairParseResult Parse(string? code)
    {
        var result = new CrosshairParseResult();
        var text = (code ?? "").Trim();
        if (text.Length == 0)
        {
            result.Error = "代碼是空的。";
            return result;
        }

        var tokens = text.Split(';', StringSplitOptions.TrimEntries);
        // 結尾多一個分號是常見的，去掉空白的最後一格
        if (tokens.Length > 0 && tokens[^1].Length == 0) tokens = tokens[..^1];

        if (tokens.Length == 0 || !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            result.Error = "這不是 VALORANT 準星代碼（代碼應該以「0;」開頭，例如 0;P;c;5;...）。";
            return result;
        }
        result.Version = version;

        var section = 'G'; // G = 第一個區段標記之前的全域設定
        var i = 1;
        while (i < tokens.Length)
        {
            var token = tokens[i];
            if (token is "P" or "A" or "S")
            {
                section = token[0];
                if (section == 'P') result.HasPrimarySection = true;
                i++;
                continue;
            }

            if (i + 1 >= tokens.Length)
            {
                result.Warnings.Add($"「{token}」沒有值，已略過");
                break;
            }

            var key = token;
            var value = tokens[i + 1];
            i += 2;

            var applied = section switch
            {
                'P' => ApplyStyleKey(result.Primary, key, value),
                'A' => ApplyStyleKey(result.Ads, key, value),
                'S' => ApplySniperKey(result.Sniper, key, value),
                _ => ApplyGlobalKey(result, key, value),
            };
            if (!applied) result.Warnings.Add($"{section}:{key}={value}");
        }

        result.IsValid = true;
        return result;
    }

    /// <summary>粗略判斷一段文字是不是準星代碼（給「從剪貼簿自動帶入」用）</summary>
    public static bool LooksLikeCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return false;
        var t = text.Trim();
        return t.Contains(';') && char.IsDigit(t[0]) && Parse(t) is { IsValid: true, HasPrimarySection: true };
    }

    // ------------------------------------------------------------------

    private static bool ApplyGlobalKey(CrosshairParseResult r, string key, string value)
    {
        switch (key)
        {
            case "p": // 瞄準時沿用主準星
                if (!TryBool(value, out var p)) return false;
                r.UsePrimaryForAds = p;
                return true;
            default:
                return false; // 其他全域欄位（例如 s、c）不影響預覽，略過
        }
    }

    /// <summary>主準星 / 瞄準時準星的欄位</summary>
    private static bool ApplyStyleKey(CrosshairStyle s, string key, string value)
    {
        // 0 開頭 = 內線、1 開頭 = 外線
        if (key.Length == 2 && (key[0] == '0' || key[0] == '1'))
        {
            return ApplyLineKey(key[0] == '0' ? s.Inner : s.Outer, key[1], value);
        }

        switch (key)
        {
            case "c":
                if (!TryInt(value, out var c)) return false;
                s.ColorIndex = Math.Clamp(c, 0, 8);
                return true;
            case "u":
                if (!TryHexColor(value, out var hex)) return false;
                s.CustomColorHex = hex;
                return true;
            case "b": // 使用自訂顏色（新版代碼會寫 b;1 搭配 u）
                if (!TryBool(value, out var b)) return false;
                s.UseCustomColor = b;
                return true;
            case "h":
                return TryBool(value, out var h) && Set(() => s.Outline = h);
            case "t":
                return TryNumber(value, out var t) && Set(() => s.OutlineThickness = Math.Clamp(t, 0, 6));
            case "o":
                return TryNumber(value, out var o) && Set(() => s.OutlineOpacity = Math.Clamp(o, 0, 1));
            case "d":
                return TryBool(value, out var d) && Set(() => s.CenterDot = d);
            case "z":
                return TryNumber(value, out var z) && Set(() => s.CenterDotSize = Math.Clamp(z, 0, 6));
            case "a":
                return TryNumber(value, out var a) && Set(() => s.CenterDotOpacity = Math.Clamp(a, 0, 1));
            case "f": // 開火誤差時淡出、s 顯示觀戰對象準星、m 覆蓋開火誤差：不影響靜態預覽
            case "s":
            case "m":
                return true;
            default:
                return false;
        }
    }

    /// <summary>內線 / 外線的欄位（key 的第二個字）</summary>
    private static bool ApplyLineKey(CrosshairLines l, char key, string value)
    {
        switch (key)
        {
            case 'b':
                return TryBool(value, out var b) && Set(() => l.Show = b);
            case 't':
                return TryNumber(value, out var t) && Set(() => l.Thickness = Math.Clamp(t, 0, 10));
            case 'l':
                return TryNumber(value, out var len) && Set(() => l.Length = Math.Clamp(len, 0, 20));
            case 'v':
                return TryNumber(value, out var v) && Set(() => l.VerticalLength = Math.Clamp(v, 0, 20));
            case 'g':
                return TryBool(value, out var g) && Set(() => l.SeparateVertical = g);
            case 'o':
                return TryNumber(value, out var o) && Set(() => l.Offset = Math.Clamp(o, 0, 40));
            case 'a':
                return TryNumber(value, out var a) && Set(() => l.Opacity = Math.Clamp(a, 0, 1));
            case 'm': // 移動誤差、f 開火誤差、s/e 誤差倍率：只影響動態擴散，靜態預覽不用
            case 'f':
            case 's':
            case 'e':
                return true;
            default:
                return false;
        }
    }

    /// <summary>狙擊鏡區段（只取中心點相關欄位）</summary>
    private static bool ApplySniperKey(SniperStyle s, string key, string value)
    {
        switch (key)
        {
            case "c":
                return TryInt(value, out var c) && Set(() => s.ColorIndex = Math.Clamp(c, 0, 8));
            case "u":
                return TryHexColor(value, out var hex) && Set(() => s.CustomColorHex = hex);
            case "d":
                return TryBool(value, out var d) && Set(() => s.CenterDot = d);
            case "s":
                return TryNumber(value, out var size) && Set(() => s.CenterDotSize = Math.Clamp(size, 0, 4));
            case "o":
                return TryNumber(value, out var o) && Set(() => s.CenterDotOpacity = Math.Clamp(o, 0, 1));
            case "b":
                return TryBool(value, out var b) && Set(() => s.UseCustomColor = b);
            default:
                return false;
        }
    }

    // ---------------- 小工具 ----------------

    private static bool Set(Action action)
    {
        action();
        return true;
    }

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static bool TryInt(string value, out int number)
    {
        number = 0;
        if (!TryNumber(value, out var d)) return false;
        number = (int)Math.Round(d);
        return true;
    }

    private static bool TryBool(string value, out bool flag)
    {
        flag = false;
        if (!TryNumber(value, out var d)) return false;
        flag = d != 0;
        return true;
    }

    /// <summary>自訂顏色：RRGGBB 或 RRGGBBAA（前面可能有 #）</summary>
    private static bool TryHexColor(string value, out string hex)
    {
        hex = "";
        var v = value.TrimStart('#');
        if (v.Length is not (6 or 8) || !v.All(Uri.IsHexDigit)) return false;
        hex = "#" + v[..6].ToUpperInvariant();
        return true;
    }
}

/// <summary>解析結果</summary>
public sealed class CrosshairParseResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public int Version { get; set; }
    public bool HasPrimarySection { get; set; }
    public bool UsePrimaryForAds { get; set; } = true;
    public CrosshairStyle Primary { get; } = new();
    public CrosshairStyle Ads { get; } = new();
    public SniperStyle Sniper { get; } = new();

    /// <summary>看不懂而略過的欄位（除錯用）</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// 一組準星外觀。預設值與遊戲的預設準星相同
/// （來源：遊戲設定 SavedCrosshairProfileData 裡的預設欄位值）。
/// 單位是 1080p 下的像素。
/// </summary>
public sealed class CrosshairStyle
{
    public int ColorIndex { get; set; }
    public bool UseCustomColor { get; set; }
    public string? CustomColorHex { get; set; }

    public bool Outline { get; set; } = true;
    public double OutlineThickness { get; set; } = 1;
    public double OutlineOpacity { get; set; } = 0.5;

    public bool CenterDot { get; set; }
    public double CenterDotSize { get; set; } = 2;
    public double CenterDotOpacity { get; set; } = 1;

    public CrosshairLines Inner { get; } = new() { Thickness = 2, Length = 6, VerticalLength = 6, Offset = 3, Opacity = 0.8 };
    public CrosshairLines Outer { get; } = new() { Thickness = 2, Length = 2, VerticalLength = 2, Offset = 10, Opacity = 0.35 };

    /// <summary>實際顏色（#RRGGBB）。c=8 或指定自訂顏色時用 u</summary>
    public string ColorHex =>
        (ColorIndex == 8 || UseCustomColor) && CustomColorHex is not null
            ? CustomColorHex
            : CrosshairCode.PresetColors[Math.Clamp(ColorIndex, 0, 7)].Hex;
}

/// <summary>內線或外線</summary>
public sealed class CrosshairLines
{
    public bool Show { get; set; } = true;
    public double Thickness { get; set; }
    public double Length { get; set; }
    public double VerticalLength { get; set; }
    /// <summary>垂直線使用獨立長度（0g / 1g）</summary>
    public bool SeparateVertical { get; set; }
    public double Offset { get; set; }
    public double Opacity { get; set; }

    /// <summary>實際的垂直長度</summary>
    public double EffectiveVerticalLength => SeparateVertical ? VerticalLength : Length;

    /// <summary>是否真的看得到（顯示、而且有長度、粗細、不透明度）</summary>
    public bool IsVisible => Show && Thickness > 0 && (Length > 0 || EffectiveVerticalLength > 0) && Opacity > 0;
}

/// <summary>狙擊鏡中心點</summary>
public sealed class SniperStyle
{
    public int ColorIndex { get; set; } = 7;
    public bool UseCustomColor { get; set; }
    public string? CustomColorHex { get; set; }
    public bool CenterDot { get; set; } = true;
    public double CenterDotSize { get; set; } = 1;
    public double CenterDotOpacity { get; set; } = 0.75;
}
