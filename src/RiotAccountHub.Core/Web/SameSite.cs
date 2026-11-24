namespace RiotAccountHub.Core.Web;

/// <summary>
/// 判斷「一個網址是不是屬於某個網站」（內建瀏覽器用來決定：
/// 網站想開新視窗時，同一個網站就留在程式裡開，其他網站交給預設瀏覽器）。
/// </summary>
public static class SameSite
{
    /// <summary>
    /// host 是不是 siteHost 這個網站，或它底下的子網域。開頭的 www. 不算。
    ///   tracker.gg、api.tracker.gg  → 是 tracker.gg 的
    ///   eviltracker.gg              → 不是（只是名字結尾剛好一樣，是別的網站）
    /// </summary>
    public static bool IsSameSite(string? host, string? siteHost)
    {
        var site = Normalize(siteHost);
        var candidate = Normalize(host);
        if (site.Length == 0 || candidate.Length == 0) return false;
        return candidate == site || candidate.EndsWith("." + site, StringComparison.Ordinal);
    }

    private static string Normalize(string? host)
    {
        var text = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        return text.StartsWith("www.", StringComparison.Ordinal) ? text[4..] : text;
    }
}
