using RiotAccountHub.Core.Web;

namespace RiotAccountHub.Tests;

/// <summary>Tracker.gg 戰績頁網址的測試</summary>
public sealed class TrackerLinksTests
{
    [Fact]
    public void 一般名稱_井字號要轉成百分比格式()
    {
        Assert.Equal("https://tracker.gg/valorant/profile/riot/SilentAim%230420/overview",
            TrackerLinks.ProfileUrl("SilentAim", "0420"));
    }

    [Fact]
    public void 中文和空白也能正確轉換()
    {
        Assert.Equal("https://tracker.gg/valorant/profile/riot/%E5%A4%9C%E8%A1%8C%20%E8%80%85%23TW1/overview",
            TrackerLinks.ProfileUrl(" 夜行 者 ", "#TW1"));
    }

    [Theory]
    [InlineData("", "TW1")]
    [InlineData("Name", "")]
    [InlineData(null, "TW1")]
    [InlineData("Name", "  # ")]
    public void 名稱或Tag是空的就沒有網址(string? name, string? tag)
    {
        Assert.Null(TrackerLinks.ProfileUrl(name, tag));
    }
}
