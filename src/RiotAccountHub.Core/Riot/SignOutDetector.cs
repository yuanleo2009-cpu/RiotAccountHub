namespace RiotAccountHub.Core.Riot;

/// <summary>
/// 發現「使用者在 Riot Client 或遊戲裡按了登出」。
///
/// 登出會讓 Riot 把那個帳號存起來的登入作廢，所以程式要記下來，下次按「啟動」時馬上提醒，
/// 而不是開了 Riot Client 才發現停在登入畫面。
///
/// 判斷方式：程式每隔幾秒看一次 Riot Client。
///   - 看到帳號 X 已登入：記住 X
///   - 不久之後（20 秒內），Riot Client 還開著、卻停在登入畫面：X 剛剛被登出了
/// Riot Client 被關掉（連不上）不算登出；超過 20 秒沒看到 X 也不再推論，避免誤判。
/// 程式自己在切換帳號、新增帳號的時候不要餵資料給它（呼叫 Reset）。
/// </summary>
public sealed class SignOutDetector
{
    /// <summary>最後一次看到帳號已登入之後，多久之內看到登入畫面才算是「這個帳號被登出」</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private (string AccountId, DateTime Utc)? _lastSignedIn;

    /// <summary>最近有沒有看到某個帳號登入（有的話，值得去確認一下是不是停在登入畫面）</summary>
    public bool IsWatching(DateTime utcNow) => _lastSignedIn is { } last && utcNow - last.Utc <= Window;

    /// <summary>看到某個帳號已經登入（認不出是哪個帳號就傳 null）</summary>
    public void SawSignedIn(string? accountId, DateTime utcNow) =>
        _lastSignedIn = accountId is null ? null : (accountId, utcNow);

    /// <summary>
    /// 看到沒有任何帳號登入。atSignInPage = Riot Client 開著、停在登入畫面。
    /// 回傳剛剛被登出的帳號；不能確定就回傳 null。
    /// </summary>
    public string? SawNotSignedIn(bool atSignInPage, DateTime utcNow)
    {
        if (_lastSignedIn is not { } last) return null;
        if (utcNow - last.Utc > Window)
        {
            _lastSignedIn = null; // 太久以前的事了，不能拿來推論
            return null;
        }
        if (!atSignInPage) return null; // 例如 Riot Client 被關掉了：不算登出，繼續觀察

        _lastSignedIn = null;
        return last.AccountId;
    }

    /// <summary>程式自己開始切換 / 登入流程時呼叫：之前看到的都不算了</summary>
    public void Reset() => _lastSignedIn = null;
}
