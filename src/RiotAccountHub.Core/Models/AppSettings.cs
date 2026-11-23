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
}
