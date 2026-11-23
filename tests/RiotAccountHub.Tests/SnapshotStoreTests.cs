using System.Text;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;

namespace RiotAccountHub.Tests;

/// <summary>
/// 快照儲存與加密的測試。
/// 每個測試都在暫存資料夾裡模擬一個假的「Riot Client 資料夾」，不會碰到真的檔案。
/// </summary>
public sealed class SnapshotStoreTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _live;     // 模擬 %LocalAppData%\Riot Games\Riot Client
    private readonly string _staging;  // 模擬 %AppData%\RiotAccountHub\staging
    private readonly SnapshotStore _store;

    // 跟正式程式用同一份清單
    private static readonly IReadOnlyList<LoginItem> Items = RiotLoginFiles.Items;

    public SnapshotStoreTests()
    {
        _live = _temp.Sub("live");
        _staging = _temp.Sub("staging");
        _store = new SnapshotStore(new DpapiProtector(), _staging)
        {
            RetryCount = 2,
            RetryDelay = TimeSpan.FromMilliseconds(10),
        };
    }

    public void Dispose() => _temp.Dispose();

    // ---------------- 小工具 ----------------

    private void WriteLive(string relative, string content)
    {
        var path = Path.Combine(_live, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string ReadLive(string relative) => File.ReadAllText(Path.Combine(_live, relative));

    private bool LiveExists(string relative) => File.Exists(Path.Combine(_live, relative));

    /// <summary>建立一組「帳號 A」的登入檔案</summary>
    private void CreateAccountA()
    {
        WriteLive(@"Data\RiotGamesPrivateSettings.yaml", "riot-login:\n    persist:\n        session: ACCOUNT-A");
        WriteLive(@"Data\Sessions\abc\heartbeat.json", "{\"a\":1}");
        WriteLive(@"Config\RiotClientSettings.yaml", "settings-A");
    }

    // ---------------- 測試 ----------------

    [Fact]
    public void 拍照後放回_檔案內容完全一樣()
    {
        CreateAccountA();
        var snapshot = _store.Capture(_live, Items);

        // 換成帳號 B 的檔案
        WriteLive(@"Data\RiotGamesPrivateSettings.yaml", "ACCOUNT-B");
        WriteLive(@"Config\RiotClientSettings.yaml", "settings-B");
        Directory.Delete(Path.Combine(_live, @"Data\Sessions"), recursive: true);

        _store.Apply(snapshot, _live, Items);

        Assert.Contains("ACCOUNT-A", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
        Assert.Equal("{\"a\":1}", ReadLive(@"Data\Sessions\abc\heartbeat.json"));
        Assert.Equal("settings-A", ReadLive(@"Config\RiotClientSettings.yaml"));
    }

    [Fact]
    public void 快照有加密_看不到原始內容()
    {
        CreateAccountA();
        var snapshot = _store.Capture(_live, Items);

        var asText = Encoding.UTF8.GetString(snapshot);
        Assert.DoesNotContain("ACCOUNT-A", asText);
        Assert.DoesNotContain("RiotGamesPrivateSettings", asText);
        // 也不是一般的 zip(zip 檔開頭是 "PK")
        Assert.False(snapshot[0] == (byte)'P' && snapshot[1] == (byte)'K');
    }

    [Fact]
    public void 快照只包含清單裡的檔案()
    {
        CreateAccountA();
        WriteLive(@"Data\其他不相關的檔案.txt", "不要備份我");
        WriteLive(@"Config\lockfile", "Riot Client:1:2:secret:https");

        var entries = _store.ListEntries(_store.Capture(_live, Items));

        Assert.Contains("Data/RiotGamesPrivateSettings.yaml", entries);
        Assert.Contains("Data/Sessions/abc/heartbeat.json", entries);
        Assert.Contains("Config/RiotClientSettings.yaml", entries);
        Assert.DoesNotContain(entries, e => e.Contains("其他不相關"));
        Assert.DoesNotContain(entries, e => e.Contains("lockfile"));
    }

    [Fact]
    public void 清空登入狀態_只刪清單裡的檔案()
    {
        CreateAccountA();
        WriteLive(@"Config\lockfile", "keep me");

        _store.Apply(null, _live, Items);

        Assert.False(LiveExists(@"Data\RiotGamesPrivateSettings.yaml"));
        Assert.False(Directory.Exists(Path.Combine(_live, @"Data\Sessions")));
        Assert.False(LiveExists(@"Config\RiotClientSettings.yaml"));
        Assert.True(LiveExists(@"Config\lockfile")); // 不在清單裡的檔案不能被刪
    }

    [Fact]
    public void 放回快照時_快照裡沒有的檔案會被移除()
    {
        // 快照拍的時候沒有 Cookies 資料夾
        CreateAccountA();
        var snapshot = _store.Capture(_live, Items);

        // 之後多了 Cookies（例如別的帳號留下的）
        WriteLive(@"Data\Cookies\cookie.bin", "別的帳號的 cookie");

        _store.Apply(snapshot, _live, Items);

        Assert.False(Directory.Exists(Path.Combine(_live, @"Data\Cookies")));
        Assert.Contains("ACCOUNT-A", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
    }

    [Fact]
    public void 存檔後讀回來_可以正常使用_而且沒有留下暫存檔()
    {
        CreateAccountA();
        var file = Path.Combine(_temp.Root, "snapshots", "a.bin");

        _store.SaveToFile(file, _store.Capture(_live, Items));
        Assert.False(File.Exists(file + ".tmp"));

        Directory.Delete(Path.Combine(_live, "Data"), recursive: true);
        _store.Apply(_store.LoadFromFile(file), _live, Items);

        Assert.Contains("ACCOUNT-A", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
    }

    [Fact]
    public void 快照檔不存在_顯示中文錯誤()
    {
        var ex = Assert.Throws<UserFacingException>(() => _store.LoadFromFile(Path.Combine(_temp.Root, "nope.bin")));
        Assert.Contains("重新登入", ex.Message);
    }

    [Fact]
    public void 用不同的金鑰無法解密_而且原本的檔案不會被動到()
    {
        CreateAccountA();
        var other = new SnapshotStore(new DpapiProtector(Encoding.UTF8.GetBytes("別的程式")), _staging);
        var snapshotFromOther = other.Capture(_live, Items);

        WriteLive(@"Data\RiotGamesPrivateSettings.yaml", "CURRENT");

        Assert.Throws<UserFacingException>(() => _store.Apply(snapshotFromOther, _live, Items));
        Assert.Equal("CURRENT", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
    }

    [Fact]
    public void 快照檔損毀_顯示錯誤而且原本的檔案不會被動到()
    {
        CreateAccountA();
        var broken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Throws<UserFacingException>(() => _store.Apply(broken, _live, Items));
        Assert.Contains("ACCOUNT-A", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
    }

    [Theory]
    [InlineData("cleared")] // 刪完舊檔案、還沒放新檔案時失敗
    [InlineData("placed")]  // 新檔案都放好了才失敗
    public void 切換到一半失敗_會自動還原成切換前的狀態(string failAt)
    {
        // 先準備帳號 B 的快照
        WriteLive(@"Data\RiotGamesPrivateSettings.yaml", "ACCOUNT-B");
        WriteLive(@"Data\Cookies\b.bin", "cookie-B");
        var snapshotB = _store.Capture(_live, Items);

        // 目前是帳號 A
        Directory.Delete(Path.Combine(_live, "Data"), recursive: true);
        CreateAccountA();

        _store.TestHook = step =>
        {
            if (step == failAt) throw new IOException("模擬檔案被鎖住");
        };

        var ex = Assert.Throws<UserFacingException>(() => _store.Apply(snapshotB, _live, Items));
        Assert.Contains("已經自動還原", ex.Message);

        // 一切都回到帳號 A，帳號 B 的檔案沒有殘留
        Assert.Contains("ACCOUNT-A", ReadLive(@"Data\RiotGamesPrivateSettings.yaml"));
        Assert.Equal("{\"a\":1}", ReadLive(@"Data\Sessions\abc\heartbeat.json"));
        Assert.Equal("settings-A", ReadLive(@"Config\RiotClientSettings.yaml"));
        Assert.False(Directory.Exists(Path.Combine(_live, @"Data\Cookies")));
    }

    [Fact]
    public void 檔案被鎖住時_切換失敗但不會修改任何檔案()
    {
        CreateAccountA();
        var snapshot = _store.Capture(_live, Items);
        WriteLive(@"Config\RiotClientSettings.yaml", "CURRENT-SETTINGS");

        // 用「完全不分享」的方式打開檔案，模擬 Riot Client 還沒關閉、檔案被鎖住
        var locked = Path.Combine(_live, @"Data\RiotGamesPrivateSettings.yaml");
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<UserFacingException>(() => _store.Apply(snapshot, _live, Items));
            Assert.Contains("沒有任何檔案被修改", ex.Message);
        }

        Assert.Equal("CURRENT-SETTINGS", ReadLive(@"Config\RiotClientSettings.yaml"));
        Assert.True(LiveExists(@"Data\Sessions\abc\heartbeat.json"));
    }

    [Fact]
    public void 成功切換後_暫存區會被清乾淨()
    {
        CreateAccountA();
        var snapshot = _store.Capture(_live, Items);

        _store.Apply(snapshot, _live, Items);

        Assert.Empty(Directory.GetDirectories(_staging));
    }

    [Fact]
    public void 沒有任何登入檔案時_也能拍照和放回()
    {
        var empty = _store.Capture(_live, Items);
        Assert.Empty(_store.ListEntries(empty));

        CreateAccountA();
        _store.Apply(empty, _live, Items); // 等於清空

        Assert.False(LiveExists(@"Data\RiotGamesPrivateSettings.yaml"));
    }
}
