using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using RiotAccountHub.Core.GameSettings;

namespace RiotAccountHub.Tests;

/// <summary>「同步遊戲設定」的測試：編碼、合併、設定檔與備份存放。全部在本機，不會連線。</summary>
public sealed class GameSettingsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // 結構與使用者電腦上實際讀到的 Ares.PlayerSettings 相同（值是假的、只留部分欄位）
    private const string SourceJson = """
        {
          "actionMappings": [ { "alt": false, "bindIndex": 0, "characterName": "", "cmd": false, "ctrl": false, "key": "Q", "name": "Activate_Ability1", "shift": false, "tapHoldType": "" } ],
          "axisMappings": [],
          "boolSettings": [ { "settingEnum": "EAresBoolSettingName::ShowCrosshair", "value": true } ],
          "floatSettings": [ { "settingEnum": "EAresFloatSettingName::MouseSensitivity", "value": 0.35 } ],
          "intSettings": [ { "settingEnum": "EAresIntSettingName::LastAcceptedCodeOfConductVersion", "value": 1 } ],
          "stringSettings": [
            { "settingEnum": "EAresStringSettingName::SavedCrosshairProfileData", "value": "SOURCE-CROSSHAIR" },
            { "settingEnum": "EAresStringSettingName::LastSeenSeasonalPopup", "value": "source-popup" }
          ],
          "roamingSetttingsVersion": 15,
          "settingsProfileData": [ { "presetIndex": 0, "profileName": "None" } ],
          "settingsProfiles": []
        }
        """;

    private const string TargetJson = """
        {
          "actionMappings": [],
          "axisMappings": [],
          "boolSettings": [],
          "floatSettings": [ { "settingEnum": "EAresFloatSettingName::MouseSensitivity", "value": 0.8 } ],
          "intSettings": [ { "settingEnum": "EAresIntSettingName::LastAcceptedCodeOfConductVersion", "value": 7 } ],
          "stringSettings": [ { "settingEnum": "EAresStringSettingName::LastSeenSeasonalPopup", "value": "target-popup" } ],
          "roamingSetttingsVersion": 15,
          "settingsProfileData": [],
          "settingsProfiles": []
        }
        """;

    // ---------------- 編碼 / 解碼 ----------------

    [Fact]
    public void 編碼後再解碼_內容完全一樣()
    {
        var encoded = PlayerSettingsCodec.Encode(SourceJson);
        Assert.Equal(SourceJson, PlayerSettingsCodec.Decode(encoded));
    }

    [Fact]
    public void 編碼格式是_base64_加_raw_deflate_跟_Riot_伺服器一樣()
    {
        var encoded = PlayerSettingsCodec.Encode(SourceJson);

        // 用最基本的 raw deflate 解壓，確認格式正確（不是 gzip、也不是 zlib）
        using var input = new MemoryStream(Convert.FromBase64String(encoded));
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        Assert.Equal(SourceJson, reader.ReadToEnd());
    }

    [Fact]
    public void 只接受看起來正常的遊戲設定()
    {
        Assert.True(PlayerSettingsCodec.LooksValid(SourceJson));
        Assert.False(PlayerSettingsCodec.LooksValid("{}"));
        Assert.False(PlayerSettingsCodec.LooksValid("不是 JSON"));
        Assert.False(PlayerSettingsCodec.LooksValid("""{ "floatSettings": "錯的型別", "actionMappings": [] }"""));
    }

    [Fact]
    public void 內容相同但排版不同_視為相同()
    {
        var compact = JsonNode.Parse(SourceJson)!.ToJsonString();
        Assert.True(PlayerSettingsCodec.AreEquivalent(SourceJson, compact));
        Assert.False(PlayerSettingsCodec.AreEquivalent(SourceJson, TargetJson));
    }

    [Fact]
    public void 項目順序不同但內容一樣_視為相同()
    {
        // 合併「自己和自己」只會改變順序，內容應該完全一樣
        var merged = PlayerSettingsCodec.MergeKeepingAccountState(SourceJson, SourceJson);
        Assert.True(PlayerSettingsCodec.AreEquivalent(SourceJson, merged));
    }

    // ---------------- 合併 ----------------

    [Fact]
    public void 套用時_玩家設定用來源的值()
    {
        var merged = JsonNode.Parse(PlayerSettingsCodec.MergeKeepingAccountState(SourceJson, TargetJson))!;

        Assert.Equal(0.35, Find(merged, "floatSettings", "MouseSensitivity")!.GetValue<double>());
        Assert.Equal("SOURCE-CROSSHAIR", Find(merged, "stringSettings", "SavedCrosshairProfileData")!.GetValue<string>());
        Assert.Equal("Q", merged["actionMappings"]![0]!["key"]!.GetValue<string>());
    }

    [Fact]
    public void 套用時_帳號自己的紀錄保留目標帳號的值()
    {
        var merged = JsonNode.Parse(PlayerSettingsCodec.MergeKeepingAccountState(SourceJson, TargetJson))!;

        Assert.Equal("target-popup", Find(merged, "stringSettings", "LastSeenSeasonalPopup")!.GetValue<string>());
        Assert.Equal(7, Find(merged, "intSettings", "LastAcceptedCodeOfConductVersion")!.GetValue<int>());

        // 不會重複出現
        Assert.Single(merged["stringSettings"]!.AsArray(), i => i!["settingEnum"]!.GetValue<string>().EndsWith("LastSeenSeasonalPopup"));
    }

    private static JsonNode? Find(JsonNode root, string section, string name) =>
        root[section]!.AsArray()
            .FirstOrDefault(i => i!["settingEnum"]!.GetValue<string>().EndsWith("::" + name))?["value"];

    // ---------------- 設定檔與備份 ----------------

    [Fact]
    public void 設定檔_存檔讀回刪除()
    {
        var store = new SettingsProfileStore(_temp.Root);
        var profile = new SettingsProfile { Name = "主號設定", SourceRiotId = "夜行者#TW1", SourcePuuid = "p1", SettingsJson = SourceJson };

        store.SaveProfile(profile);
        var loaded = Assert.Single(store.ListProfiles());
        Assert.Equal("主號設定", loaded.Name);
        Assert.True(PlayerSettingsCodec.AreEquivalent(SourceJson, loaded.SettingsJson));

        store.DeleteProfile(profile.Id);
        Assert.Empty(store.ListProfiles());
    }

    [Fact]
    public void 帳號設定_每個帳號一份_每次覆蓋同一份()
    {
        var store = new SettingsProfileStore(_temp.Root);
        const string accountA = "aaaa1111";
        Assert.Null(store.AccountProfile(accountA));

        store.SaveProfile(new SettingsProfile { Id = SettingsProfileStore.AccountProfileId(accountA), Name = "A 第一次", SettingsJson = SourceJson });
        store.SaveProfile(new SettingsProfile { Id = SettingsProfileStore.AccountProfileId(accountA), Name = "A 第二次", SettingsJson = SourceJson });
        store.SaveProfile(new SettingsProfile { Id = SettingsProfileStore.AccountProfileId("bbbb2222"), Name = "B", SettingsJson = SourceJson });

        Assert.Equal("A 第二次", store.AccountProfile(accountA)!.Name);
        Assert.Equal("B", store.AccountProfile("bbbb2222")!.Name);
        Assert.Equal(2, store.ListProfiles().Count);

        store.DeleteProfile(SettingsProfileStore.AccountProfileId(accountA));
        Assert.Null(store.AccountProfile(accountA));
    }

    [Fact]
    public void 設定檔裡沒有任何_token()
    {
        var store = new SettingsProfileStore(_temp.Root);
        store.SaveProfile(new SettingsProfile { Name = "x", SettingsJson = SourceJson });

        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(_temp.Root, "profiles"))[0]);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 備份_取得最新的一份_而且每個帳號最多留10份()
    {
        var store = new SettingsProfileStore(_temp.Root);
        for (var i = 1; i <= 12; i++)
        {
            store.SaveBackup("puuid-a", new SettingsProfile { Name = $"備份{i}", CreatedUtc = DateTime.UtcNow.AddMinutes(i), SettingsJson = SourceJson });
            Thread.Sleep(5); // 讓檔名時間不同
        }

        Assert.Equal("備份12", store.LatestBackup("puuid-a")!.Name);
        Assert.Equal(10, Directory.GetFiles(Path.Combine(_temp.Root, "backups", "puuid-a")).Length);
        Assert.Null(store.LatestBackup("puuid-b"));
        Assert.Null(store.LatestBackup(null));
    }

    [Fact]
    public void 奇怪的編號不會寫到別的資料夾()
    {
        var store = new SettingsProfileStore(_temp.Root);
        store.SaveBackup(@"..\..\evil", new SettingsProfile { Name = "x", SettingsJson = SourceJson });

        Assert.False(Directory.Exists(Path.Combine(_temp.Root, "..", "evil")));
        Assert.True(Directory.Exists(Path.Combine(_temp.Root, "backups", "evil")));
    }
}
