using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Tests;

/// <summary>
/// 「切換帳號後，Riot Client 有沒有自動登入」的判斷。
/// 在遊戲或 Riot Client 裡按過「登出」的帳號，存起來的登入會被 Riot 作廢，切換後會停在登入畫面。
/// </summary>
public sealed class LoginWatchTests
{
    private static readonly TimeSpan LoginPageGrace = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan NotLoggedInGrace = TimeSpan.FromSeconds(20);

    private static AutoLoginTracker NewTracker() => new(LoginPageGrace, NotLoggedInGrace);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    // ---------------- 判斷規則 ----------------

    [Fact]
    public void 一查到已登入_馬上回報登入成功()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.Unreachable, S(0))); // Riot Client 還在開
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(1)));
        Assert.Equal(AutoLoginResult.LoggedIn, tracker.Observe(AutoLoginSignal.LoggedIn, S(4)));
    }

    [Fact]
    public void 停在登入畫面_幾秒內就判定需要重新登入()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.Unreachable, S(0)));
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(1)));
        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(3)));  // 從這裡開始算
        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(10))); // 7 秒，還沒到
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(AutoLoginSignal.AtLoginPage, S(11)));
    }

    [Fact]
    public void 登入畫面只閃一下就登入了_不算失效()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(1)));
        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(3)));
        Assert.Equal(AutoLoginResult.LoggedIn, tracker.Observe(AutoLoginSignal.LoggedIn, S(4)));
    }

    [Fact]
    public void 只是一直沒登入_要等比較久才判定()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(2)));  // 從這裡開始算
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(12))); // 超過 8 秒也還不算，因為沒看到登入畫面
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(21)));
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(AutoLoginSignal.NotLoggedIn, S(22)));
    }

    [Fact]
    public void 登入畫面被關掉後_還是會在時間到時判定()
    {
        // 使用者把 Riot 的登入視窗關掉：訊號從「停在登入畫面」變成「沒登入」
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(2)));
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(6)));
        Assert.Null(tracker.Observe(AutoLoginSignal.NotLoggedIn, S(21)));
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(AutoLoginSignal.NotLoggedIn, S(22)));
    }

    [Fact]
    public void 中途連不上_重新計時避免誤判()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(0)));
        Assert.Null(tracker.Observe(AutoLoginSignal.Unreachable, S(6)));  // 例如 Riot Client 自己重新啟動
        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(8)));  // 重新開始算
        Assert.Null(tracker.Observe(AutoLoginSignal.AtLoginPage, S(15))); // 才 7 秒
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(AutoLoginSignal.AtLoginPage, S(16)));
    }

    [Fact]
    public void Riot_Client一直沒開好_不會誤判成需要登入()
    {
        var tracker = NewTracker();

        for (var t = 0; t <= 90; t++)
        {
            Assert.Null(tracker.Observe(AutoLoginSignal.Unreachable, S(t)));
        }
    }

    // ---------------- 解讀 Riot Client 回報的登入狀態 ----------------

    // 這是 Riot Client 停在登入畫面時，實際回報的內容（2026-11-24 在真的 Riot Client 上看到的欄位）
    private const string AtLoginPageJson =
        "{\"authenticationType\":\"None\",\"country\":\"\",\"error\":\"\",\"multifactor\":null," +
        "\"persistLogin\":false,\"securityProfile\":\"\",\"type\":\"needs_credentials\"}";

    [Fact]
    public void 停在登入畫面時_不算已登入()
    {
        // 以前只看「有沒有回 200」，會把登入畫面誤當成「已登入、沒勾保持登入」
        Assert.Equal(ApiLoginState.NotLoggedIn, RiotLocalApi.ParseLoginStatus(AtLoginPageJson).State);
        Assert.True(RiotLocalApi.IsAskingForCredentials(AtLoginPageJson));
    }

    [Theory]
    [InlineData("needs_password")]
    [InlineData("needs_multifactor_verification")]
    [InlineData("error")]
    public void 登入到一半或出錯時_不算已登入(string type)
    {
        var json = "{\"type\":\"" + type + "\",\"persistLogin\":true}";

        Assert.Equal(ApiLoginState.NotLoggedIn, RiotLocalApi.ParseLoginStatus(json).State);
        Assert.False(RiotLocalApi.IsAskingForCredentials(json)); // 只有 needs_credentials 才是「停在登入畫面」
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 登入完成時_算已登入_並讀出有沒有勾保持登入(bool persist)
    {
        var json = "{\"type\":\"authenticated\",\"persistLogin\":" + (persist ? "true" : "false") + "}";

        var status = RiotLocalApi.ParseLoginStatus(json);

        Assert.Equal(ApiLoginState.LoggedIn, status.State);
        Assert.Equal(persist, status.PersistLogin);
        Assert.False(RiotLocalApi.IsAskingForCredentials(json));
    }

    [Theory]
    [InlineData("{\"persistLogin\":true}")] // 沒有 type 欄位（Riot 改了格式）：維持舊的判斷
    [InlineData("[]")]
    [InlineData("{ 壞掉的內容")]
    public void 看不懂的格式_維持舊的判斷當成已登入_也不會出錯(string json)
    {
        Assert.Equal(ApiLoginState.LoggedIn, RiotLocalApi.ParseLoginStatus(json).State);
        Assert.False(RiotLocalApi.IsAskingForCredentials(json));
    }
}
