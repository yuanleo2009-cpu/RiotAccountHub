using RiotAccountHub.Core.Web;

namespace RiotAccountHub.Tests;

/// <summary>內建瀏覽器的廣告封鎖測試</summary>
public sealed class AdBlockerTests
{
    [Theory]
    // 在 VCRDB 實際看到的廣告網址
    [InlineData("https://s.nitropay.com/ads-1234.js")]
    [InlineData("https://securepubads.g.doubleclick.net/tag/js/gpt.js")]
    [InlineData("https://ad.doubleclick.net/ddm/trackimp")]
    [InlineData("https://c.amazon-adsystem.com/aax2/apstag.js")]
    [InlineData("https://gum.criteo.com/sync")]
    [InlineData("https://ib.adnxs.com/getuid")]
    [InlineData("https://p.ad.gt/api/v1/p")]
    [InlineData("https://connect.facebook.net/en_US/fbevents.js")]
    [InlineData("https://NITROPAY.COM/x")]
    public void 廣告網址會被封鎖(string url)
    {
        Assert.True(AdBlocker.IsBlocked(url));
    }

    [Theory]
    // 網站本身與正常資源不能被擋
    [InlineData("https://www.vcrdb.net/")]
    [InlineData("https://www.vcrdb.net/crosshair/85")]
    [InlineData("https://vcrdb.net/_next/static/chunks/main.js")]
    [InlineData("https://fonts.googleapis.com/css2?family=Inter")]
    [InlineData("https://www.google.com/")]
    [InlineData("https://notdoubleclick.net/")]  // 只是名字很像，不是子網域
    [InlineData("https://doubleclick.net.example.com/")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData(null)]
    public void 正常網址不會被封鎖(string? url)
    {
        Assert.False(AdBlocker.IsBlocked(url));
    }

    [Fact]
    public void 隱藏廣告的腳本是合法的字串()
    {
        var script = AdBlocker.HideAdsScript;
        Assert.Contains("rah-adblock", script);
        Assert.Contains("#ad-anchor", script);
        Assert.DoesNotContain("\n", script);
    }
}
