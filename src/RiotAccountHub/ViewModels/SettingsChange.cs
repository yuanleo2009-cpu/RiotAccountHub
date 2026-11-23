using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 遊戲設定變更的紀錄：哪個帳號、套用了哪份設定（還原時是 null）、
/// 有沒有已經自動重開遊戲（有的話，下次啟動就不用再提示「設定已套用」）。
/// </summary>
public sealed record SettingsChange(RiotIdInfo Account, string? ProfileName, bool Restored, bool Relaunched = false);
