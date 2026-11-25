using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.Models;
using Wpf.Ui.Controls;

namespace RiotAccountHub.ViewModels;

/// <summary>帳號目前的狀態</summary>
public enum AccountActivity
{
    /// <summary>沒有在用</summary>
    None,
    /// <summary>剛按了啟動，Riot Client / 遊戲還在開啟中</summary>
    Launching,
    /// <summary>Riot Client 登入的是這個帳號，但遊戲還沒開</summary>
    LoggedIn,
    /// <summary>VALORANT 正在用這個帳號執行</summary>
    InGame,
}

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
    [NotifyPropertyChangedFor(nameof(ShowLoginWarning), nameof(LoginWarningText))]
    private bool _hasSnapshot;

    /// <summary>卡片上要不要顯示黃色的「請按重新登入」提示</summary>
    public bool ShowLoginWarning => !HasSnapshot || Model.NeedsRelogin;

    /// <summary>黃色提示的文字：沒有快照，或是存起來的登入已經失效</summary>
    public string LoginWarningText => !HasSnapshot
        ? "沒有登入狀態，請按「重新登入」"
        : "登入已失效，請按「重新登入」";

    public string GameName => Model.GameName;

    /// <summary>"#TAG"；沒有 Tag 就是空字串</summary>
    public string TagText => string.IsNullOrWhiteSpace(Model.TagLine) ? "" : "#" + Model.TagLine;

    public string RiotId => Model.RiotId;

    public string LoginMethodText => Model.LoginMethod.ToDisplay();

    /// <summary>
    /// 登入方式標籤上的小圖示（用通用圖示，不使用各家公司的商標）：
    /// Google = 地球、Facebook = 人群、Apple = 蘋果、帳密 = 鑰匙
    /// </summary>
    public SymbolRegular LoginMethodIcon => LoginMethodIcons.For(Model.LoginMethod);

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
        Model.LastUsedUtc is { } utc ? "上次使用：" + TimeText.FormatWhen(utc) : "尚未從這裡啟動過";

    // ---------------- 目前狀態 ----------------

    /// <summary>這個帳號現在的狀態：啟動中 / 遊戲中 / 已登入 / 沒有在用</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivity))]
    [NotifyPropertyChangedFor(nameof(IsLaunching))]
    [NotifyPropertyChangedFor(nameof(ActivityText))]
    [NotifyPropertyChangedFor(nameof(ActivityToolTip))]
    [NotifyPropertyChangedFor(nameof(IsInGame))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsNotRunning))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonText))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonToolTip))]
    private AccountActivity _activity = AccountActivity.None;

    /// <summary>這個帳號是不是「使用中」（卡片左側的紅線會亮著）</summary>
    public bool HasActivity => Activity != AccountActivity.None;

    /// <summary>正在啟動（狀態標籤上的小圓點會閃爍）</summary>
    public bool IsLaunching => Activity == AccountActivity.Launching;

    /// <summary>VALORANT 正在用這個帳號執行</summary>
    public bool IsInGame => Activity == AccountActivity.InGame;

    /// <summary>「啟動中」或「遊戲中」：啟動按鈕會從「啟動」換成目前的狀態</summary>
    public bool IsRunning => Activity is AccountActivity.Launching or AccountActivity.InGame;

    public bool IsNotRunning => !IsRunning;

    /// <summary>啟動按鈕上的字：平常是「啟動」，啟動後跟著狀態變成「啟動中」→「遊戲中」</summary>
    public string LaunchButtonText => Activity switch
    {
        AccountActivity.Launching => "啟動中",
        AccountActivity.InGame => "遊戲中",
        _ => "啟動",
    };

    /// <summary>滑鼠移到啟動按鈕上的說明（平常不需要說明）</summary>
    public string? LaunchButtonToolTip => Activity switch
    {
        AccountActivity.Launching => "正在開啟 Riot Client 和遊戲，請稍等",
        AccountActivity.InGame => "這個帳號正在遊戲中（再按一下可以關閉遊戲，會先問你）",
        _ => null,
    };

    public string ActivityText => Activity switch
    {
        AccountActivity.Launching => "啟動中",
        AccountActivity.InGame => "遊戲中",
        AccountActivity.LoggedIn => "已登入",
        _ => "",
    };

    public string ActivityToolTip => Activity switch
    {
        AccountActivity.Launching => "已經送出啟動，正在等 Riot Client 和遊戲開啟",
        AccountActivity.InGame => "VALORANT 正在用這個帳號執行",
        AccountActivity.LoggedIn => "Riot Client 目前登入的是這個帳號（遊戲還沒開）",
        _ => "",
    };

    // ---------------- 主帳號 ----------------

    /// <summary>這張卡片是不是主帳號</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainToggleText))]
    [NotifyPropertyChangedFor(nameof(MainToggleToolTip))]
    private bool _isMain;

    /// <summary>主帳號星星按鈕的說明文字</summary>
    public string MainToggleToolTip => IsMain
        ? "這是主帳號（按一下取消）"
        : "設為主帳號：它的遊戲設定可以一鍵套用到其他帳號";

    /// <summary>要不要在「啟動」旁邊顯示「套用主帳號設定」（有主帳號、而且自己不是主帳號）</summary>
    [ObservableProperty]
    private bool _canApplyMain;

    /// <summary>卡片下方按鈕的文字</summary>
    public string MainToggleText => IsMain ? "取消主帳號" : "設為主帳號";

    // ---------------- 複製 / 套用 / 還原 ----------------

    /// <summary>目前「已複製」的就是這個帳號的設定</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyButtonText))]
    private bool _isCopySource;

    /// <summary>「複製設定」按鈕上的字</summary>
    public string CopyButtonText => IsCopySource ? "已複製" : "複製設定";

    /// <summary>有複製別的帳號的設定 → 這張卡片顯示「套用設定」</summary>
    [ObservableProperty]
    private bool _canApplyCopied;

    /// <summary>這個帳號有「套用前的備份」→ 顯示「還原設定」</summary>
    [ObservableProperty]
    private bool _hasSettingsBackup;

    // ---------------- 遊戲設定 ----------------

    /// <summary>有沒有套用過遊戲設定檔</summary>
    public bool HasAppliedSettings => !string.IsNullOrEmpty(Model.AppliedSettingsName);

    /// <summary>卡片上的標籤，例如「設定：主號設定」</summary>
    public string AppliedSettingsText => $"設定：{Model.AppliedSettingsName}";

    /// <summary>還沒進遊戲讓設定生效（套用後還沒用啟動器開過）</summary>
    public bool HasPendingNotice => !string.IsNullOrEmpty(Model.PendingLaunchNotice);

    public string AppliedSettingsToolTip =>
        $"已套用遊戲設定「{Model.AppliedSettingsName}」" +
        (Model.AppliedSettingsUtc is { } t ? $"\n套用時間：{TimeText.FormatWhen(t)}" : "") +
        (HasPendingNotice ? "\n下次按「啟動」進遊戲後生效" : "");

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
            lines.Add($"更新時間：{TimeText.FormatWhen(r.UpdatedUtc)}");
            lines.Add("（每次用啟動器玩這個帳號時會自動更新）");
            return string.Join("\n", lines);
        }
    }

    /// <summary>資料改變後呼叫，讓畫面全部重新讀取</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
