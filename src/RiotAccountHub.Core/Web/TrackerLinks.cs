namespace RiotAccountHub.Core.Web;

/// <summary>
/// Tracker.gg（第三方戰績網站）的網址。
/// 本程式只是用內建瀏覽器「打開」這個網頁，不會讀取或抓取網頁上的資料，也不需要任何登入憑證。
/// </summary>
public static class TrackerLinks
{
    private const string ProfileBase = "https://tracker.gg/valorant/profile/riot/";

    /// <summary>
    /// 某個 Riot ID 的戰績頁，例如「夜行者#TW1」→
    /// https://tracker.gg/valorant/profile/riot/%E5%A4%9C%E8%A1%8C%E8%80%85%23TW1/overview
    /// 名稱或 Tag 是空的就回傳 null（沒辦法查）。
    /// </summary>
    public static string? ProfileUrl(string? gameName, string? tagLine)
    {
        var name = (gameName ?? "").Trim();
        var tag = (tagLine ?? "").Trim().TrimStart('#').Trim();
        if (name.Length == 0 || tag.Length == 0) return null;

        // 名稱裡的空白、中文、# 都要轉成網址可以用的格式
        return ProfileBase + Uri.EscapeDataString($"{name}#{tag}") + "/overview";
    }
}
