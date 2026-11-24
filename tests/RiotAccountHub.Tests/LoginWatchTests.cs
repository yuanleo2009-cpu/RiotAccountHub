using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Tests;

/// <summary>
/// 「切換帳號後，Riot Client 有沒有自動登入」的判斷規則。
/// 在遊戲或 Riot Client 裡按過「登出」的帳號，存起來的登入會失效，切換後會停在登入畫面。
/// </summary>
public sealed class LoginWatchTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void 一查到已登入_馬上回報登入成功()
    {
        var tracker = new AutoLoginTracker(Grace);

        Assert.Null(tracker.Observe(ApiLoginState.Unreachable, S(0))); // Riot Client 還在開
        Assert.Equal(AutoLoginResult.LoggedIn, tracker.Observe(ApiLoginState.LoggedIn, S(10)));
    }

    [Fact]
    public void 剛開好時短暫還沒登入_不算失效()
    {
        var tracker = new AutoLoginTracker(Grace);

        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(10)));
        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(20))); // 才過 10 秒，繼續等
        Assert.Equal(AutoLoginResult.LoggedIn, tracker.Observe(ApiLoginState.LoggedIn, S(22)));
    }

    [Fact]
    public void 一直停在登入畫面_判定需要重新登入()
    {
        var tracker = new AutoLoginTracker(Grace);

        Assert.Null(tracker.Observe(ApiLoginState.Unreachable, S(0)));
        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(12))); // 從這裡開始算
        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(26))); // 14 秒，還沒到
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(ApiLoginState.NotLoggedIn, S(27)));
    }

    [Fact]
    public void 中途連不上_重新計時避免誤判()
    {
        var tracker = new AutoLoginTracker(Grace);

        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(0)));
        Assert.Null(tracker.Observe(ApiLoginState.Unreachable, S(10))); // 例如 Riot Client 被重新啟動
        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(16))); // 重新開始算
        Assert.Null(tracker.Observe(ApiLoginState.NotLoggedIn, S(30))); // 才 14 秒
        Assert.Equal(AutoLoginResult.NeedsLogin, tracker.Observe(ApiLoginState.NotLoggedIn, S(31)));
    }

    [Fact]
    public void Riot_Client一直沒開好_不會誤判成需要登入()
    {
        var tracker = new AutoLoginTracker(Grace);

        for (var t = 0; t <= 90; t += 2)
        {
            Assert.Null(tracker.Observe(ApiLoginState.Unreachable, S(t)));
        }
    }
}
