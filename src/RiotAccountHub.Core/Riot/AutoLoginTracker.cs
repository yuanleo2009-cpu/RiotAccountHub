namespace RiotAccountHub.Core.Riot;

/// <summary>切換帳號後，Riot Client 有沒有自動登入</summary>
public enum AutoLoginResult
{
    /// <summary>登入了</summary>
    LoggedIn,
    /// <summary>Riot Client 已經開好，卻沒有自動登入：存起來的登入狀態失效了</summary>
    NeedsLogin,
    /// <summary>等不到結果（例如 Riot Client 一直沒開好，或被關掉了）</summary>
    Unknown,
}

/// <summary>每次問 Riot Client「現在登入了嗎」得到的訊號</summary>
public enum AutoLoginSignal
{
    /// <summary>已經登入</summary>
    LoggedIn,
    /// <summary>Riot Client 的登入流程走到「等使用者登入」了：這次確定不會自動登入（最明確的訊號）</summary>
    LoginRequired,
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
/// 訊號越明確，需要連續看到的時間就越短：
///   - 登入流程走到「等使用者登入」：最明確，連續看到兩次（不到一秒）就判定
///   - 停在登入畫面：也算明確，連續看到幾秒就判定
///   - 只是「還沒登入」：比較模糊，要等久一點
/// 把規則獨立成一個小類別，是為了可以單元測試（不需要真的開 Riot Client）。
/// </summary>
public sealed class AutoLoginTracker
{
    private readonly TimeSpan _loginRequiredGrace;
    private readonly TimeSpan _loginPageGrace;
    private readonly TimeSpan _notLoggedInGrace;
    private TimeSpan? _loginRequiredSince;
    private TimeSpan? _loginPageSince;
    private TimeSpan? _notLoggedInSince;

    /// <param name="loginRequiredGrace">連續「等使用者登入」多久，就判定需要重新登入</param>
    /// <param name="loginPageGrace">連續「停在登入畫面」多久，就判定需要重新登入</param>
    /// <param name="notLoggedInGrace">連續「還沒登入」多久，就判定需要重新登入</param>
    public AutoLoginTracker(TimeSpan loginRequiredGrace, TimeSpan loginPageGrace, TimeSpan notLoggedInGrace)
    {
        _loginRequiredGrace = loginRequiredGrace;
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

            case AutoLoginSignal.LoginRequired:
                _loginRequiredSince ??= elapsed;
                _loginPageSince ??= elapsed;
                _notLoggedInSince ??= elapsed;
                break;

            case AutoLoginSignal.AtLoginPage:
                _loginRequiredSince = null;
                _loginPageSince ??= elapsed;
                _notLoggedInSince ??= elapsed;
                break;

            case AutoLoginSignal.NotLoggedIn:
                _loginRequiredSince = null;
                _loginPageSince = null;
                _notLoggedInSince ??= elapsed;
                break;

            default:
                // 連不上（Riot Client 還在開，或剛被關掉重開）：全部重新計時，避免誤判
                _loginRequiredSince = null;
                _loginPageSince = null;
                _notLoggedInSince = null;
                return null;
        }

        var longEnough =
            (_loginRequiredSince is { } required && elapsed - required >= _loginRequiredGrace) ||
            (_loginPageSince is { } page && elapsed - page >= _loginPageGrace) ||
            (_notLoggedInSince is { } since && elapsed - since >= _notLoggedInGrace);
        return longEnough ? AutoLoginResult.NeedsLogin : null;
    }
}
