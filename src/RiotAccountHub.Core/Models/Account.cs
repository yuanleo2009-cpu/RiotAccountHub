using System.Text.Json.Serialization;

namespace RiotAccountHub.Core.Models;

/// <summary>
/// 一個 Riot 帳號的「基本資料」。
/// 這裡只有顯示用的資訊（Riot ID、備註、時間），完全沒有密碼。
/// 真正的登入狀態另外加密存在 snapshots\{Id}.bin。
/// </summary>
public sealed class Account
{
    /// <summary>程式內部用的編號，也是快照檔名</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>遊戲名稱（Riot ID 的 # 前面）</summary>
    public string GameName { get; set; } = "";

    /// <summary>Tag（Riot ID 的 # 後面，不含 #）</summary>
    public string TagLine { get; set; } = "";

    /// <summary>
    /// Riot 給這個帳號的固定編號（自動抓到時才有）。
    /// 改 Riot ID 也不會變，用來確認「重新登入的是不是同一個帳號」。不是密碼。
    /// </summary>
    public string? Puuid { get; set; }

    /// <summary>這個帳號在遊戲裡裝備的卡面編號（用來顯示頭像與卡片背景）</summary>
    public string? PlayerCardId { get; set; }

    /// <summary>最近一次套用到這個帳號的遊戲設定檔名稱（還原後會清掉）</summary>
    public string? AppliedSettingsName { get; set; }

    /// <summary>套用遊戲設定的時間（UTC）</summary>
    public DateTime? AppliedSettingsUtc { get; set; }

    /// <summary>下次按「啟動」時要顯示的提示，例如「已套用遊戲設定「主號設定」」。顯示一次後清除。</summary>
    public string? PendingLaunchNotice { get; set; }

    /// <summary>最後一次看到的段位（還沒用啟動器開過遊戲就是 null）</summary>
    public RankInfo? Rank { get; set; }

    /// <summary>登入方式（只是標籤，方便使用者辨認）</summary>
    public LoginMethod LoginMethod { get; set; } = LoginMethod.RiotAccount;

    /// <summary>自訂備註，例如「主號」、「小號」</summary>
    public string Note { get; set; } = "";

    /// <summary>建立時間(UTC)</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>上次從本程式啟動的時間(UTC)；從沒用過就是 null</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>快照最後更新的時間(UTC)</summary>
    public DateTime? SnapshotUpdatedUtc { get; set; }

    /// <summary>完整的 Riot ID，例如 Player#TW1</summary>
    [JsonIgnore]
    public string RiotId => string.IsNullOrWhiteSpace(TagLine) ? GameName : $"{GameName}#{TagLine}";
}
