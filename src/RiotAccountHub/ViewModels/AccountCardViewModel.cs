using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.Models;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 一張帳號卡片要顯示的內容。
/// 它包著一個 Account（資料），並把資料轉成畫面好用的文字。
/// </summary>
public sealed partial class AccountCardViewModel : ObservableObject
{
    private readonly PlayerCardCache? _cards;

    public AccountCardViewModel(Account model, bool hasSnapshot, PlayerCardCache? cards = null)
    {
        Model = model;
        _hasSnapshot = hasSnapshot;
        _cards = cards;
    }

    // ---------------- 卡面圖片 ----------------

    /// <summary>頭像圖片（卡面正方形小圖）的本機路徑；還沒有就是 null，畫面會改顯示文字頭像</summary>
    public string? AvatarImagePath => _cards?.GetSmallArt(Model.PlayerCardId);

    /// <summary>卡片背景圖片（卡面長條圖）的本機路徑</summary>
    public string? BannerImagePath => _cards?.GetWideArt(Model.PlayerCardId);

    public bool HasAvatarImage => AvatarImagePath is not null;

    public bool HasBannerImage => BannerImagePath is not null;

    /// <summary>真正的帳號資料</summary>
    public Account Model { get; }

    /// <summary>這個帳號有沒有登入快照（沒有的話卡片會提示要重新登入）</summary>
    [ObservableProperty]
    private bool _hasSnapshot;

    public string GameName => Model.GameName;

    /// <summary>"#TAG"；沒有 Tag 就是空字串</summary>
    public string TagText => string.IsNullOrWhiteSpace(Model.TagLine) ? "" : "#" + Model.TagLine;

    public string RiotId => Model.RiotId;

    public string LoginMethodText => Model.LoginMethod.ToDisplay();

    public string Note => Model.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);

    /// <summary>頭像上的第一個字（支援中文、emoji）</summary>
    public string Initial
    {
        get
        {
            var name = Model.GameName.Trim();
            if (name.Length == 0) return "?";
            return StringInfo.GetNextTextElement(name, 0).ToUpperInvariant();
        }
    }

    /// <summary>「上次使用：今天 14:30」這樣的文字</summary>
    public string LastUsedText =>
        Model.LastUsedUtc is { } utc ? "上次使用：" + FormatWhen(utc) : "尚未從這裡啟動過";

    // ---------------- 段位 ----------------

    /// <summary>有沒有段位資料</summary>
    public bool HasRank => Model.Rank is not null;

    /// <summary>例如「神話 1」；沒資料就是「段位未知」</summary>
    public string RankText => Model.Rank is { } r ? RankNames.ToDisplay(r.CompetitiveTier) : "段位未知";

    /// <summary>段位顏色（#RRGGBB），WPF 會自動把它轉成畫刷</summary>
    public string RankColor => Model.Rank is { } r ? RankNames.ColorHex(r.CompetitiveTier) : "#6E7684";

    /// <summary>排行榜名次，例如「#6373」；沒有就是空字串</summary>
    public string LeaderboardText => Model.Rank?.LeaderboardPosition is { } p ? $"#{p}" : "";

    public bool HasLeaderboard => Model.Rank?.LeaderboardPosition is not null;

    /// <summary>滑鼠移到段位上顯示的說明</summary>
    public string RankToolTip
    {
        get
        {
            if (Model.Rank is not { } r) return "用這個啟動器開一次遊戲後，就會自動顯示段位";
            var lines = new List<string> { $"段位：{RankNames.ToDisplay(r.CompetitiveTier)}" };
            if (r.LeaderboardPosition is { } p) lines.Add($"排行榜：第 {p} 名");
            if (r.AccountLevel is { } lv) lines.Add($"帳號等級：{lv}");
            lines.Add($"更新時間：{FormatWhen(r.UpdatedUtc)}");
            lines.Add("（每次用啟動器玩這個帳號時會自動更新）");
            return string.Join("\n", lines);
        }
    }

    /// <summary>把時間變成「今天 14:30」、「昨天 09:10」或日期</summary>
    private static string FormatWhen(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTime.Today;
        return local.Date == today ? $"今天 {local:HH:mm}"
            : local.Date == today.AddDays(-1) ? $"昨天 {local:HH:mm}"
            : local.ToString("yyyy/MM/dd HH:mm");
    }

    /// <summary>資料改變後呼叫，讓畫面全部重新讀取</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
