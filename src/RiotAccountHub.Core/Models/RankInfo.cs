namespace RiotAccountHub.Core.Models;

/// <summary>
/// 帳號「最後一次看到」的段位。
/// 資料來源是 Riot Client 在本機提供的狀態資料（VALORANT 執行中才有），不會連到外部網路。
/// </summary>
public sealed class RankInfo
{
    /// <summary>
    /// 段位編號（Riot 的 competitiveTier）：
    ///   0 = 未分級、3~5 = 鐵牌 1~3、6~8 = 銅牌、9~11 = 銀牌、12~14 = 金牌、
    ///   15~17 = 白金、18~20 = 鑽石、21~23 = 超凡入聖、24~26 = 神話、27 = 輻能戰魂
    /// </summary>
    public int CompetitiveTier { get; set; }

    /// <summary>排行榜名次（只有高段位才有；沒有就是 null）</summary>
    public int? LeaderboardPosition { get; set; }

    /// <summary>帳號等級</summary>
    public int? AccountLevel { get; set; }

    /// <summary>這筆資料是什麼時候讀到的（UTC）</summary>
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>段位編號 → 中文名稱與顏色</summary>
public static class RankNames
{
    // 每個大段位有 3 個小段，從編號 3 開始
    private static readonly string[] Divisions =
    {
        "鐵牌", "銅牌", "銀牌", "金牌", "白金", "鑽石", "超凡入聖", "神話",
    };

    // 自己挑的顏色（不使用官方段位圖示）
    private static readonly string[] DivisionColors =
    {
        "#9AA0A6", // 鐵牌
        "#C08457", // 銅牌
        "#C9D1DA", // 銀牌
        "#E6BE55", // 金牌
        "#4CC3CF", // 白金
        "#B58CF5", // 鑽石
        "#4CCB8F", // 超凡入聖
        "#E0607E", // 神話
    };

    public const int Radiant = 27;

    /// <summary>例如 24 → 「神話 1」</summary>
    public static string ToDisplay(int tier) => tier switch
    {
        < 3 => "未分級",
        Radiant => "輻能戰魂",
        > Radiant => $"段位 {tier}", // Riot 以後如果新增段位，至少還看得到編號
        _ => $"{Divisions[(tier - 3) / 3]} {(tier - 3) % 3 + 1}",
    };

    /// <summary>段位的顯示顏色（#RRGGBB）</summary>
    public static string ColorHex(int tier) => tier switch
    {
        < 3 => "#6E7684",
        >= Radiant => "#FFD978",
        _ => DivisionColors[(tier - 3) / 3],
    };
}
