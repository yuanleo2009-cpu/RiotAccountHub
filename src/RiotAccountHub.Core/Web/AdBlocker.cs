namespace RiotAccountHub.Core.Web;

/// <summary>
/// 程式內瀏覽器的廣告封鎖（跟一般瀏覽器的擋廣告外掛原理一樣）：
///   1. 拒絕廣告 / 廣告追蹤網域的請求 → 廣告根本不會載入
///   2. 用一段 CSS 把網頁上空出來的廣告欄位藏起來 → 不會留下空白
/// 不會修改網站本身的內容或功能。
///
/// 清單來源：在 VCRDB 實際載入時觀察到的廣告網域（NitroPay 聯播網與它串接的廣告商），
/// 再加上幾個常見的大型廣告網路。
/// </summary>
public static class AdBlocker
{
    /// <summary>封鎖的網域（包含它們的所有子網域）</summary>
    public static readonly IReadOnlyList<string> BlockedDomains = new[]
    {
        // NitroPay（VCRDB 使用的廣告聯播網）
        "nitropay.com", "nitrocnct.com", "nit.ro",
        // Google 廣告
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com",
        // 其他廣告商 / 競價平台
        "amazon-adsystem.com", "criteo.com", "criteo.net", "adnxs.com", "rubiconproject.com",
        "pubmatic.com", "openx.net", "33across.com", "adsrvr.org", "casalemedia.com", "indexww.com",
        "3lift.com", "sharethrough.com", "smartadserver.com", "media.net", "adform.net", "teads.tv",
        "gumgum.com", "lijit.com", "sovrn.com", "yieldmo.com", "connatix.com", "taboola.com", "outbrain.com",
        "fastclick.net",
        // 廣告驗證、身分辨識與追蹤（廣告用）
        "ad-delivery.net", "confiant-integrations.net", "p7cloud.net", "scalibur.io", "id5-sync.com",
        "btloader.com", "cpx.to", "hadronid.net", "rlcdn.com", "tapad.com", "crwdcntrl.net", "ad.gt",
        "rqtrk.eu", "dns-finder.com", "adsafeprotected.com", "moatads.com", "quantserve.com",
        "scorecardresearch.com", "connect.facebook.net",
    };

    /// <summary>藏起廣告欄位的 CSS（VCRDB 的廣告容器，以及常見的廣告元素）</summary>
    public const string HideAdsCss =
        "#ad-topbar, #ad-anchor, #ad-anchor-close, #ad-video-float, [id^='ad-'], " +
        "[id*='siderail' i], [class*='siderail' i], [class*='nitro-' i], .na-anchor-bottom, " +
        "ins.adsbygoogle, [id^='google_ads'], iframe[src*='doubleclick'], iframe[src*='nitropay'] " +
        "{ display: none !important; }";

    /// <summary>在每個網頁一開始就注入上面那段 CSS 的腳本</summary>
    public static string HideAdsScript =>
        "(() => {" +
        "  const add = () => {" +
        "    if (document.getElementById('rah-adblock')) return;" +
        "    const s = document.createElement('style');" +
        "    s.id = 'rah-adblock';" +
        $"    s.textContent = {System.Text.Json.JsonSerializer.Serialize(HideAdsCss)};" +
        "    (document.head || document.documentElement).appendChild(s);" +
        "  };" +
        "  add();" +
        "  document.addEventListener('DOMContentLoaded', add);" +
        "})();";

    /// <summary>這個網址要不要封鎖</summary>
    public static bool IsBlocked(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsBlockedHost(uri.Host);
    }

    /// <summary>網域本身或它的子網域在清單裡就封鎖（例如 securepubads.g.doubleclick.net）</summary>
    public static bool IsBlockedHost(string host)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        foreach (var domain in BlockedDomains)
        {
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
