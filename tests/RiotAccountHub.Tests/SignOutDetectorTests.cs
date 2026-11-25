using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Tests;

/// <summary>發現「使用者在 Riot Client 或遊戲裡按了登出」的規則，以及「登入已失效」的記號會存起來</summary>
public sealed class SignOutDetectorTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void 剛剛還登入_接著停在登入畫面_判定這個帳號被登出()
    {
        var d = new SignOutDetector();
        d.SawSignedIn("acc-1", At(0));

        Assert.True(d.IsWatching(At(5)));
        Assert.Equal("acc-1", d.SawNotSignedIn(atSignInPage: true, At(5)));
        Assert.False(d.IsWatching(At(6))); // 判定過一次就不再重複
    }

    [Fact]
    public void Riot_Client被關掉_不算登出()
    {
        var d = new SignOutDetector();
        d.SawSignedIn("acc-1", At(0));

        Assert.Null(d.SawNotSignedIn(atSignInPage: false, At(5)));  // 連不上 / 沒有登入畫面
        Assert.Null(d.SawNotSignedIn(atSignInPage: false, At(10)));
    }

    [Fact]
    public void 太久以前看到的登入_不拿來判斷()
    {
        var d = new SignOutDetector();
        d.SawSignedIn("acc-1", At(0));

        Assert.False(d.IsWatching(At(25)));
        Assert.Null(d.SawNotSignedIn(atSignInPage: true, At(25)));
    }

    [Fact]
    public void 程式自己的流程開始時重設_不會把切換造成的登入畫面算成登出()
    {
        var d = new SignOutDetector();
        d.SawSignedIn("acc-1", At(0));

        d.Reset(); // 例如使用者按了另一個帳號的「啟動」
        Assert.Null(d.SawNotSignedIn(atSignInPage: true, At(5)));
    }

    [Fact]
    public void 認不出來的帳號登入著_不判斷()
    {
        var d = new SignOutDetector();
        d.SawSignedIn("acc-1", At(0));
        d.SawSignedIn(null, At(3)); // 換成一個不在清單裡的帳號

        Assert.Null(d.SawNotSignedIn(atSignInPage: true, At(5)));
    }

    [Fact]
    public void 登入已失效的記號_會跟著帳號清單存起來()
    {
        var paths = new RiotAccountHub.Core.AppPaths(_temp.Root);
        paths.EnsureCreated();
        var repo = new AccountRepository(paths);
        repo.Save(new List<Account>
        {
            new() { GameName = "甲", TagLine = "TW1", NeedsRelogin = true },
            new() { GameName = "乙", TagLine = "TW2" },
        });

        var loaded = repo.Load();

        Assert.True(loaded[0].NeedsRelogin);
        Assert.False(loaded[1].NeedsRelogin);
    }

    [Fact]
    public void 舊版的帳號清單沒有這個欄位_讀起來是正常()
    {
        var paths = new RiotAccountHub.Core.AppPaths(_temp.Root);
        paths.EnsureCreated();
        File.WriteAllText(paths.AccountsFile, "[{\"Id\":\"abc\",\"GameName\":\"甲\",\"TagLine\":\"TW1\"}]");

        Assert.False(new AccountRepository(paths).Load()[0].NeedsRelogin);
    }
}
