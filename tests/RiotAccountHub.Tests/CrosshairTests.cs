using RiotAccountHub.Core.Crosshairs;

namespace RiotAccountHub.Tests;

/// <summary>準星代碼解析與「我的準星」存檔的測試</summary>
public sealed class CrosshairTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void 只有版本號_全部用遊戲預設值()
    {
        var r = CrosshairCode.Parse("0;P");
        Assert.True(r.IsValid);
        var p = r.Primary;
        Assert.Equal("#FFFFFF", p.ColorHex);
        Assert.True(p.Outline);
        Assert.Equal(1, p.OutlineThickness);
        Assert.Equal(0.5, p.OutlineOpacity);
        Assert.False(p.CenterDot);
        Assert.Equal((2.0, 6.0, 3.0, 0.8), (p.Inner.Thickness, p.Inner.Length, p.Inner.Offset, p.Inner.Opacity));
        Assert.Equal((2.0, 2.0, 10.0, 0.35), (p.Outer.Thickness, p.Outer.Length, p.Outer.Offset, p.Outer.Opacity));
        Assert.True(p.Inner.IsVisible);
        Assert.True(p.Outer.IsVisible);
    }

    [Fact]
    public void 一般代碼_各欄位都讀對()
    {
        // 使用者需求文件裡的範例代碼
        var r = CrosshairCode.Parse("0;P;c;5;h;0;0l;4;0o;2;0a;1;0f;0;1b;0;S;c;1;s;0.7");
        Assert.True(r.IsValid);
        Assert.Empty(r.Warnings);
        var p = r.Primary;
        Assert.Equal("#00FFFF", p.ColorHex);
        Assert.False(p.Outline);
        Assert.Equal(4, p.Inner.Length);
        Assert.Equal(4, p.Inner.EffectiveVerticalLength);
        Assert.Equal(2, p.Inner.Offset);
        Assert.Equal(1, p.Inner.Opacity);
        Assert.False(p.Outer.Show);
        Assert.False(p.Outer.IsVisible);
        Assert.Equal(1, r.Sniper.ColorIndex);
        Assert.Equal(0.7, r.Sniper.CenterDotSize);
    }

    [Fact]
    public void 自訂顏色_與垂直長度()
    {
        var r = CrosshairCode.Parse("0;P;c;8;u;FF8800FF;0l;3;0v;7;0g;1");
        Assert.Equal("#FF8800", r.Primary.ColorHex);
        Assert.Equal(3, r.Primary.Inner.Length);
        Assert.Equal(7, r.Primary.Inner.EffectiveVerticalLength);
    }

    [Fact]
    public void 瞄準時區段_不會影響主準星()
    {
        var r = CrosshairCode.Parse("0;p;0;P;c;1;A;c;7;0l;10");
        Assert.False(r.UsePrimaryForAds);
        Assert.Equal("#00FF00", r.Primary.ColorHex);
        Assert.Equal(6, r.Primary.Inner.Length);
        Assert.Equal("#FF0000", r.Ads.ColorHex);
        Assert.Equal(10, r.Ads.Inner.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("P;c;5")]
    public void 不是準星代碼_回傳無效但不崩潰(string code)
    {
        var r = CrosshairCode.Parse(code);
        Assert.False(r.IsValid);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void 壞掉的欄位只略過_不影響其他欄位()
    {
        var r = CrosshairCode.Parse("0;P;0l;abc;zz;1;c;5;0o");
        Assert.True(r.IsValid);
        Assert.Equal(6, r.Primary.Inner.Length);      // abc 讀不懂 → 維持預設
        Assert.Equal("#00FFFF", r.Primary.ColorHex);  // 後面的欄位照樣讀
        Assert.Equal(3, r.Warnings.Count);            // 0l=abc、zz、結尾沒有值的 0o
    }

    [Fact]
    public void 超出範圍的數值會被限制()
    {
        var r = CrosshairCode.Parse("0;P;0l;999;0a;5;c;42;t;-3");
        Assert.Equal(20, r.Primary.Inner.Length);
        Assert.Equal(1, r.Primary.Inner.Opacity);
        Assert.Equal(0, r.Primary.OutlineThickness);
        Assert.Equal("#FF0000", r.Primary.ColorHex); // 42 → 限制成 8（自訂）但沒有 u → 用最後一個預設色
    }

    [Fact]
    public void 結尾多分號與空白都沒關係()
    {
        var r = CrosshairCode.Parse("  0;P;c;5;  ");
        Assert.True(r.IsValid);
        Assert.Equal("#00FFFF", r.Primary.ColorHex);
    }

    [Theory]
    [InlineData("0;P;c;5;h;0;0l;4", true)]
    [InlineData("0;P", true)]
    [InlineData("0", false)]
    [InlineData("一般的文字", false)]
    [InlineData("https://www.vcrdb.net/", false)]
    public void 判斷剪貼簿內容是不是準星代碼(string text, bool expected)
    {
        Assert.Equal(expected, CrosshairCode.LooksLikeCode(text));
    }

    [Fact]
    public void 範例樣式都是合法代碼()
    {
        foreach (var ex in CrosshairLibrary.Examples)
        {
            var r = CrosshairCode.Parse(ex.Code);
            Assert.True(r.IsValid, ex.Name);
            Assert.Empty(r.Warnings);
        }
    }

    [Fact]
    public void 我的準星_新增收藏刪除_而且排在範例前面()
    {
        var lib = new CrosshairLibrary(Path.Combine(_temp.Root, "crosshairs.json"));
        var mine = lib.Add(" 我的點 ", "0;P;d;1");
        lib.SetFavorite(mine.Id, true);
        lib.SetFavorite("ex-dot", true);

        var all = lib.LoadAll();
        Assert.Equal("我的點", all[0].Name);
        Assert.True(all[0].IsFavorite);
        Assert.False(all[0].IsExample);
        Assert.True(all.Single(c => c.Id == "ex-dot").IsFavorite);
        Assert.Equal(CrosshairLibrary.Examples.Count + 1, all.Count);

        lib.Delete(mine.Id);
        lib.Delete("ex-dot"); // 範例不能刪，不會出錯
        Assert.Equal(CrosshairLibrary.Examples.Count, lib.LoadAll().Count);
    }
}
