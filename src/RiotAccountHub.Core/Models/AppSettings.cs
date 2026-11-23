namespace RiotAccountHub.Core.Models;

/// <summary>
/// 程式設定。會存成 %AppData%\RiotAccountHub\settings.json。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 使用者手動指定的 RiotClientServices.exe 路徑。
    /// 是 null 的話，程式會自動從 RiotClientInstalls.json 找。
    /// </summary>
    public string? RiotClientPath { get; set; }

    /// <summary>
    /// 按「啟動」後，自動在 Riot Client 按「開始遊戲」（不用再按第二次）。
    /// 預設關閉，由使用者在主畫面下方開啟。
    /// </summary>
    public bool AutoStartGame { get; set; }

    /// <summary>
    /// 主帳號的編號（Account.Id）。其他帳號的卡片上會出現「套用主帳號設定」按鈕。
    /// 沒設定就是 null。
    /// </summary>
    public string? MainAccountId { get; set; }

    /// <summary>
    /// 「複製設定」複製的是哪個帳號（Account.Id）的設定。
    /// 其他帳號的卡片上會出現「套用設定」。關掉程式再打開還會保留。
    /// </summary>
    public string? CopiedSettingsAccountId { get; set; }
}
