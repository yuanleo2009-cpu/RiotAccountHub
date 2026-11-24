namespace RiotAccountHub.Core.Riot;

/// <summary>切換帳號後，Riot Client 有沒有自動登入</summary>
public enum AutoLoginResult
{
    /// <summary>登入了</summary>
    LoggedIn,
    /// <summary>Riot Client 已經開好，卻一直停在登入畫面：存起來的登入狀態失效了</summary>
    NeedsLogin,
    /// <summary>等不到結果（例如 Riot Client 一直沒開好，或被關掉了）</summary>
    Unknown,
}

/// <summary>
/// 判斷「Riot Client 是不是停在登入畫面」的規則。
/// 每查到一次登入狀態就告訴它一次；連續「沒登入」超過容許時間，才判定需要重新登入。
///
/// 為什麼要等一段時間：Riot Client 剛開好的那一兩秒本來就還沒登入，不能馬上當成失效。
/// 把規則獨立成一個小類別，是為了可以單元測試（不需要真的開 Riot Client）。
/// </summary>
public sealed class AutoLoginTracker
{
    private readonly TimeSpan _grace;
    private TimeSpan? _notLoggedInSince;

    /// <param name="grace">Riot Client 開好後，容許它「還沒登入」多久</param>
    public AutoLoginTracker(TimeSpan grace) => _grace = grace;

    /// <summary>
    /// 告訴它這一次查到的狀態。elapsed 是從開始等待到現在經過的時間。
    /// 回傳 null 代表還沒有結論，要繼續等。
    /// </summary>
    public AutoLoginResult? Observe(ApiLoginState state, TimeSpan elapsed)
    {
        switch (state)
        {
            case ApiLoginState.LoggedIn:
                return AutoLoginResult.LoggedIn;

            case ApiLoginState.NotLoggedIn:
                _notLoggedInSince ??= elapsed;
                return elapsed - _notLoggedInSince.Value >= _grace ? AutoLoginResult.NeedsLogin : null;

            default:
                // 連不上（Riot Client 還在開，或剛被關掉重開）：重新計時，避免誤判
                _notLoggedInSince = null;
                return null;
        }
    }
}
