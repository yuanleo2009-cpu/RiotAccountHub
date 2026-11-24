using System.Text;
using System.Text.Json.Nodes;
using RiotAccountHub.Core;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;
using RiotAccountHub.Core.Storage;
using RiotAccountHub.Core.Web;

namespace RiotAccountHub.Tests;

/// <summary>
/// 程式碼審查後補上的測試：每一個對應一個修正過的問題，確保之後不會又壞掉。
/// </summary>
public sealed class ReviewFixTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly AppPaths _paths;

    public ReviewFixTests()
    {
        _paths = new AppPaths(_temp.Root);
        _paths.EnsureCreated();
    }

    public void Dispose() => _temp.Dispose();

    // ---------------- 帳號清單 ----------------

    [Fact]
    public void 帳號清單暫時讀不到時_丟出錯誤而不是當成空清單()
    {
        var repo = new AccountRepository(_paths) { RetryCount = 2, RetryDelay = TimeSpan.FromMilliseconds(10) };
        repo.Save(new List<Account> { new() { GameName = "主號", TagLine = "TW1" } });

        // 模擬檔案被其他程式（防毒、雲端同步）佔用
        using (new FileStream(_paths.AccountsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<UserFacingException>(() => repo.Load());
            Assert.Equal("無法讀取帳號清單", ex.Title);
        }

        // 檔案沒有被動過：放開之後照樣讀得到
        Assert.Single(repo.Load());
        Assert.Null(repo.LoadWarning);
    }

    [Fact]
    public void 帳號清單被手動改壞欄位時_會補好不會出錯()
    {
        File.WriteAllText(_paths.AccountsFile,
            "[ null, { \"Id\": \"\", \"GameName\": null, \"TagLine\": null, \"Note\": null }, { \"Id\": \"abc123\", \"GameName\": \"玩家\", \"TagLine\": \"TW1\" } ]",
            Encoding.UTF8);

        var loaded = new AccountRepository(_paths).Load();

        Assert.Equal(2, loaded.Count); // 空的那一項被拿掉
        Assert.Equal("", loaded[0].GameName);
        Assert.Equal("", loaded[0].TagLine);
        Assert.Equal("", loaded[0].Note);
        Assert.False(string.IsNullOrWhiteSpace(loaded[0].Id)); // 沒有編號的補上新編號
        Assert.Equal("abc123", loaded[1].Id);                   // 正常的項目不變
        Assert.Equal("玩家#TW1", loaded[1].RiotId);
    }

    // ---------------- 快照檔案的位置 ----------------

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData(@"C:\Windows\system32\x")]
    [InlineData("a/b/../../c")]
    public void 快照檔名_奇怪的帳號編號也只會落在快照資料夾裡(string id)
    {
        var path = Path.GetFullPath(_paths.SnapshotFile(id));
        Assert.Equal(Path.GetFullPath(_paths.SnapshotsDir), Path.GetDirectoryName(path));
    }

    [Fact]
    public void 快照檔名_正常的帳號編號不變()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.Equal(Path.Combine(_paths.SnapshotsDir, id + ".bin"), _paths.SnapshotFile(id));
    }

    // ---------------- 暫存區清理 ----------------

    [Fact]
    public void 啟動時清掉沒清乾淨的暫存區_但保留有記號的資料夾()
    {
        var staging = _temp.Sub("staging");
        var store = new SnapshotStore(new DpapiProtector(), staging);

        // 上次切換到一半當機留下來的（裡面是還沒加密的登入檔案）
        var stale = Path.Combine(staging, "20261101-120000-aaaaaaaa", "incoming", "Data");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "RiotGamesPrivateSettings.yaml"), "leftover");

        // 自動還原失敗時特意保留、給使用者救回檔案用的
        var kept = Path.Combine(staging, "20261102-120000-bbbbbbbb");
        Directory.CreateDirectory(Path.Combine(kept, "rollback"));
        File.WriteAllText(Path.Combine(kept, "KEEP.txt"), "keep");

        Assert.Equal(1, store.CleanupStaleStaging());

        Assert.False(Directory.Exists(Path.Combine(staging, "20261101-120000-aaaaaaaa")));
        Assert.True(Directory.Exists(Path.Combine(kept, "rollback")));
        Assert.Equal(0, store.CleanupStaleStaging()); // 再跑一次沒有東西可清
    }

    [Fact]
    public void 暫存區不存在時_清理不會出錯()
    {
        var store = new SnapshotStore(new DpapiProtector(), Path.Combine(_temp.Root, "not-there"));
        Assert.Equal(0, store.CleanupStaleStaging());
    }

    // ---------------- Riot Client 本機 API ----------------

    [Theory]
    [InlineData("Riot Client:17416:64971:FAKEPASSWORD:ftp")]      // 協定不對
    [InlineData("Riot Client:17416:0:FAKEPASSWORD:https")]        // port 不合理
    [InlineData("Riot Client:17416:70000:FAKEPASSWORD:https")]
    [InlineData("Riot Client:17416:64971:FAKEPASSWORD:https://x")] // 多出來的內容
    public void Lockfile內容不合理時_不採用(string text)
    {
        Assert.Null(RiotLocalApi.ParseLockfile(text));
    }

    [Fact]
    public void 段位資料的欄位不是文字時_略過不會出錯()
    {
        const string json = "{\"presences\":[{\"puuid\":123,\"product\":\"valorant\",\"private\":\"x\"}," +
                            "{\"puuid\":\"me\",\"product\":7,\"private\":\"x\"}]}";
        Assert.Null(RiotLocalApi.ParseOwnRank(json, "me"));
    }

    // ---------------- 遊戲設定 ----------------

    [Fact]
    public void 合併設定_遇到形狀奇怪的項目不會出錯()
    {
        const string source = "{\"actionMappings\":[],\"floatSettings\":[1,\"x\",null,{\"settingEnum\":5}," +
                              "{\"settingEnum\":\"EAresFloatSettingName::MouseSensitivity\",\"value\":0.3}]}";
        const string target = "{\"actionMappings\":[],\"floatSettings\":[null,{\"settingEnum\":\"X::LastSeenNews\",\"value\":9}]}";

        var merged = JsonNode.Parse(PlayerSettingsCodec.MergeKeepingAccountState(source, target))!;
        var floats = merged["floatSettings"]!.AsArray();

        Assert.Equal(6, floats.Count); // 原本 5 項都留著，加上目標帳號的 1 筆紀錄
        Assert.Contains(floats, f => f is JsonObject o && o["settingEnum"]?.ToString() == "X::LastSeenNews");
    }

    [Fact]
    public void 設定內容是空的_不算正常的設定()
    {
        Assert.False(PlayerSettingsCodec.LooksValid(null));
        Assert.False(PlayerSettingsCodec.LooksValid(""));
    }

    [Fact]
    public void 找最新的備份_壞掉的檔案會跳過()
    {
        const string settings = "{\"actionMappings\":[],\"floatSettings\":[]}";
        var store = new SettingsProfileStore(_temp.Sub("game-settings"));
        store.SaveBackup("puuid-a", new SettingsProfile { Name = "好的備份", SettingsJson = settings });

        // 之後多了一份檔名比較新、但內容壞掉的檔案
        var dir = Path.Combine(_temp.Root, "game-settings", "backups", "puuid-a");
        File.WriteAllText(Path.Combine(dir, "99991231-235959-999.json"), "{ 壞掉了");

        Assert.Equal("好的備份", store.LatestBackup("puuid-a")!.Name);
    }

    // ---------------- 內建瀏覽器 ----------------

    [Theory]
    [InlineData("tracker.gg", "tracker.gg", true)]
    [InlineData("api.tracker.gg", "tracker.gg", true)]
    [InlineData("WWW.VCRDB.NET", "www.vcrdb.net", true)]
    [InlineData("vcrdb.net", "www.vcrdb.net", true)]
    [InlineData("eviltracker.gg", "tracker.gg", false)]   // 只是結尾剛好一樣，是別的網站
    [InlineData("tracker.gg.evil.com", "tracker.gg", false)]
    [InlineData("", "tracker.gg", false)]
    [InlineData("tracker.gg", "", false)]
    public void 同一個網站的判斷(string host, string site, bool expected)
    {
        Assert.Equal(expected, SameSite.IsSameSite(host, site));
    }

    [Theory]
    [InlineData("doubleclick.net", true)]
    [InlineData("securepubads.g.doubleclick.net", true)]
    [InlineData("DoubleClick.NET.", true)]
    [InlineData("notdoubleclick.net", false)] // 名字結尾一樣但不是子網域
    [InlineData("net", false)]
    [InlineData("", false)]
    public void 擋廣告的網域比對(string host, bool expected)
    {
        Assert.Equal(expected, AdBlocker.IsBlockedHost(host));
    }
}
