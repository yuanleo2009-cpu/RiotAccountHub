using RiotAccountHub.Core.Models;

namespace RiotAccountHub.Core.Riot;

/// <summary>判斷「Riot Client 目前登入的帳號」是不是清單裡的某個帳號</summary>
public static class AccountMatching
{
    /// <summary>
    /// 兩邊都有 puuid 就比 puuid（改過 Riot ID 也認得出來），
    /// 不然就比 Riot ID（不分大小寫）。
    /// </summary>
    public static bool IsSameAccount(this Account account, RiotIdInfo detected)
    {
        if (!string.IsNullOrEmpty(account.Puuid) && !string.IsNullOrEmpty(detected.Puuid))
        {
            return string.Equals(account.Puuid, detected.Puuid, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(account.RiotId, detected.RiotId, StringComparison.OrdinalIgnoreCase);
    }
}
