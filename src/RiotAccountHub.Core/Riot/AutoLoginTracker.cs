namespace RiotAccountHub.Core.Riot;

/// <summary>切換帳號後，Riot Client 有沒有自動登入</summary>
public enum AutoLoginResult
{
    /// <summary>登入了</summary>
    LoggedIn,
    /// <summary>Riot Client 已經開好，卻停在登入畫面：存起來的登入狀態失效了</summary>
    NeedsLogin,
    /// <summary>等不到結果（例如 Riot Client 一直沒開好，或被關掉了）</summary>
    Unknown,
}

/// <summary>每次問 Riot Client「現在登入了嗎」得到的訊號</summary>
public enum AutoLoginSignal
{
    /// <summary>已經登入</summary>
    LoggedIn,
    /// <summary>Riot Client 正停在登入畫面，等使用者輸入帳號</summary>
    AtLoginPage,
    /// <summary>Riot Client 開著，但還沒登入（可能還在啟動）</summary>
    NotLoggedIn,
    /// <summary>連不上（Riot Client 還在開，或已經關掉）</summary>
    Unreachable,
}

/// <summary>
/// 判斷「Riot Client 是不是登出的狀態」的規則。
/// 每問到一次訊號就告訴它一次，它會回答：登入了 / 需要重新登入 / 還要再等。
///
/// 為什麼不能一看到「沒登入」就下結論：Riot Client 剛開好的那幾秒本來就還沒登入。
///   - 看到「停在登入畫面」：這是很明確的訊號，連續看到一小段時間就判定
///   - 只是「還沒登入」：訊號比較模糊，要等久一點
/// 把規則獨立成一個小類別，是為了可以單元測試（不需要真的開 Riot Client）。
/// </summary>
public sealed class AutoLoginTracker
{
    private readonly TimeSpan _loginPageGrace;
    private readonly TimeSpan _notLoggedInGrace;
    private TimeSpan? _loginPageSince;
    private TimeSpan? _notLoggedInSince;

    /// <param name="loginPageGrace">連續「停在登入畫面」多久，就判定需要重新登入</param>
    /// <param name="notLoggedInGrace">連續「還沒登入」多久，就判定需要重新登入</param>
    public AutoLoginTracker(TimeSpan loginPageGrace, TimeSpan notLoggedInGrace)
    {
        _loginPageGrace = loginPageGrace;
        _notLoggedInGrace = notLoggedInGrace;
    }

    /// <summary>
    /// 告訴它這一次的訊號。elapsed 是從開始等待到現在經過的時間。
    /// 回傳 null 代表還沒有結論，要繼續等。
    /// </summary>
    public AutoLoginResult? Observe(AutoLoginSignal signal, TimeSpan elapsed)
    {
        switch (signal)
        {
            case AutoLoginSignal.LoggedIn:
                return AutoLoginResult.LoggedIn;

            case AutoLoginSignal.AtLoginPage:
                _loginPageSince ??= elapsed;
                _notLoggedInSince ??= elapsed;
                break;

            case AutoLoginSignal.NotLoggedIn:
                _loginPageSince = null;
                _notLoggedInSince ??= elapsed;
                break;

            default:
                // 連不上（Riot Client 還在開，或剛被關掉重開）：全部重新計時，避免誤判
                _loginPageSince = null;
                _notLoggedInSince = null;
                return null;
        }

        var atLoginPageLongEnough = _loginPageSince is { } page && elapsed - page >= _loginPageGrace;
        var notLoggedInLongEnough = _notLoggedInSince is { } since && elapsed - since >= _notLoggedInGrace;
        return atLoginPageLongEnough || notLoggedInLongEnough ? AutoLoginResult.NeedsLogin : null;
    }
}
