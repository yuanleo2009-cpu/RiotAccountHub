using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RiotAccountHub.Core.Models;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 一張帳號卡片要顯示的內容。
/// 它包著一個 Account（資料），並把資料轉成畫面好用的文字。
/// </summary>
public sealed partial class AccountCardViewModel : ObservableObject
{
    public AccountCardViewModel(Account model, bool hasSnapshot)
    {
        Model = model;
        _hasSnapshot = hasSnapshot;
    }

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
    public string LastUsedText
    {
        get
        {
            if (Model.LastUsedUtc is not { } utc) return "尚未從這裡啟動過";
            var local = utc.ToLocalTime();
            var today = DateTime.Today;
            var when = local.Date == today ? $"今天 {local:HH:mm}"
                : local.Date == today.AddDays(-1) ? $"昨天 {local:HH:mm}"
                : local.ToString("yyyy/MM/dd HH:mm");
            return "上次使用：" + when;
        }
    }

    /// <summary>資料改變後呼叫，讓畫面全部重新讀取</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
