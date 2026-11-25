using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Tests;

/// <summary>
/// Riot 相關小工具的測試：保持登入判斷、安裝路徑、lockfile、程序清單。
/// 測試資料是照著使用者電腦上實際看到的格式寫的。
/// </summary>
public sealed class RiotHelpersTests
{
    [Fact]
    public void 保持登入_沒勾選時是_null()
    {
        // 實際在使用者電腦上看到的格式（值已用假資料取代）
        const string yaml = """
            psl:
                authorization:
                    riot-client: null
            riot-login:
                persist: null
            rso-authenticator:
                tdid:
                    domain: "riotgames.com"
                    value: "FAKE"
            """;
        Assert.Equal(PersistState.NotPersisted, RiotLoginFiles.ParsePersistState(yaml));
    }

    [Fact]
    public void 保持登入_有勾選時底下有資料()
    {
        const string yaml = """
            riot-login:
                persist:
                    region: "AP"
                    session:
                        cookies:
                            -   domain: "auth.riotgames.com"
                                name: "ssid"
                                value: "FAKE"
            rso-authenticator:
                tdid:
                    value: "FAKE"
            """;
        Assert.Equal(PersistState.Persisted, RiotLoginFiles.ParsePersistState(yaml));
    }

    [Fact]
    public void 保持登入_新格式有_refresh_token_時算有勾()
    {
        // 2026-11 在使用者電腦上看到的格式（值都換成假的）：已經沒有 riot-login 區塊
        const string yaml = """
            psl:
                authorization:
                    riot-client:
                        claims: []
                        id_token: "FAKE"
                        is_dpop_bound: false
                        refresh_token: "FAKE-REFRESH"
                        refresh_token_write_count: 3
                        scopes:
                        - "openid"
            rso-authenticator:
                tdid:
                    persistent: true
                    value: "FAKE"
            """;
        Assert.Equal(PersistState.Persisted, RiotLoginFiles.ParsePersistState(yaml));
    }

    [Theory]
    [InlineData("psl:\n    authorization: {}\nrso-authenticator:\n    tdid: {}\n")]
    [InlineData("psl:\n    authorization:\n        riot-client:\n            refresh_token: null\n")]
    [InlineData("psl:\n    authorization:\n        riot-client:\n            refresh_token: \"\"\n")]
    public void 保持登入_新格式沒有_refresh_token_時算沒勾(string yaml)
    {
        Assert.Equal(PersistState.NotPersisted, RiotLoginFiles.ParsePersistState(yaml));
    }

    [Fact]
    public void 保持登入_refresh_token_不在_psl_區塊裡不算()
    {
        // 只認 psl 區塊裡的，別的地方出現同名欄位不算
        Assert.Equal(PersistState.NotPersisted, RiotLoginFiles.ParsePersistState("other:\n    refresh_token: \"FAKE\"\n"));
    }

    [Fact]
    public void 保持登入_沒有_riot_login_區塊()
    {
        Assert.Equal(PersistState.NotPersisted, RiotLoginFiles.ParsePersistState("rso-authenticator:\n    tdid: {}\n"));
    }

    [Fact]
    public void 保持登入_格式看不懂時回傳_Unknown()
    {
        Assert.Equal(PersistState.Unknown, RiotLoginFiles.ParsePersistState("riot-login:\n    persist: something-new\n"));
        Assert.Equal(PersistState.Unknown, RiotLoginFiles.ParsePersistState("riot-login:\n    other: 1\n"));
    }

    [Fact]
    public void 保持登入_Windows_換行也能判斷()
    {
        Assert.Equal(PersistState.Persisted, RiotLoginFiles.ParsePersistState("riot-login:\r\n    persist:\r\n        session: x\r\n"));
    }

    [Fact]
    public void 安裝路徑_從_RiotClientInstalls_json_讀取()
    {
        // 使用者電腦上的實際內容
        const string json = """
            {
                "associated_client": { "F:/Riot Games/VALORANT/live/": "C:/Riot Games/Riot Client/RiotClientServices.exe" },
                "patchlines": { "KeystoneFoundationLiveWin": "C:/Riot Games/Riot Client/RiotClientServices.exe" },
                "rc_default": "C:/Riot Games/Riot Client/RiotClientServices.exe",
                "rc_live": "C:/Riot Games/Riot Client/RiotClientServices.exe"
            }
            """;
        Assert.Equal(@"C:\Riot Games\Riot Client\RiotClientServices.exe", RiotClientService.ReadPathFromInstallsJson(json));
    }

    [Fact]
    public void 安裝路徑_格式錯誤時回傳_null()
    {
        Assert.Null(RiotClientService.ReadPathFromInstallsJson("這不是 JSON"));
        Assert.Null(RiotClientService.ReadPathFromInstallsJson("{}"));
    }

    [Fact]
    public void Lockfile_解析()
    {
        var info = RiotLocalApi.ParseLockfile("Riot Client:17416:64971:FAKEPASSWORD:https");
        Assert.NotNull(info);
        Assert.Equal(17416, info!.Pid);
        Assert.Equal(64971, info.Port);
        Assert.Equal("FAKEPASSWORD", info.Password);
        Assert.Equal("https", info.Protocol);

        Assert.Null(RiotLocalApi.ParseLockfile("壞掉的內容"));
    }

    [Fact]
    public void 關閉清單裡絕對沒有_Vanguard()
    {
        foreach (var name in new[] { "vgc", "vgk", "vgtray" })
        {
            Assert.DoesNotContain(RiotClientService.ProcessesToClose, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RiotID_從聊天工作階段讀取()
    {
        // 欄位名稱與使用者電腦上 /chat/v1/session 實際回傳的一樣（值是假的）
        const string json = """
            { "federated": false, "game_name": "夜行者", "game_tag": "TW1", "loaded": true,
              "name": "夜行者", "pid": "fake@ap1.pvp.net", "puuid": "11111111-2222-3333-4444-555555555555",
              "region": "ap1", "resource": "RC-1", "state": "connected" }
            """;
        var info = RiotLocalApi.ParseChatSession(json);
        Assert.NotNull(info);
        Assert.Equal("夜行者#TW1", info!.RiotId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", info.Puuid);
    }

    [Fact]
    public void RiotID_從帳號別名讀取()
    {
        // 欄位名稱與 /player-account/aliases/v1/active 實際回傳的一樣
        const string json = """{ "active": true, "created_datetime": 0, "game_name": "SilentAim", "summoner": false, "tag_line": "0420" }""";
        var info = RiotLocalApi.ParseActiveAlias(json);
        Assert.NotNull(info);
        Assert.Equal("SilentAim#0420", info!.RiotId);
        Assert.Null(info.Puuid);
    }

    [Fact]
    public void RiotID_資料不完整時回傳_null_讓使用者手動輸入()
    {
        Assert.Null(RiotLocalApi.ParseChatSession("""{ "game_name": "", "game_tag": "", "loaded": false }"""));
        Assert.Null(RiotLocalApi.ParseChatSession("""{ "game_name": "OnlyName" }"""));
        Assert.Null(RiotLocalApi.ParseActiveAlias("不是 JSON"));
        Assert.Null(RiotLocalApi.ParseActiveAlias("[]"));
    }

    // ---------------- 段位 ----------------

    private static string ToBase64(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

    [Fact]
    public void 段位_從目前格式的_presence_讀取()
    {
        // 結構與使用者電腦上 VALORANT 13.10 實際的 private 資料相同（只留相關欄位，值是假的）
        var priv = ToBase64("""
            { "isValid": true, "partySize": 1,
              "playerPresenceData": { "accountLevel": 195, "competitiveTier": 24, "leaderboardPosition": 6373, "platformOverride": "PC" },
              "matchPresenceData": { "sessionLoopState": "MENUS", "queueId": "unrated" } }
            """);
        var rank = RiotLocalApi.ParsePresencePrivate(priv);
        Assert.NotNull(rank);
        Assert.Equal(24, rank!.CompetitiveTier);
        Assert.Equal(6373, rank.LeaderboardPosition);
        Assert.Equal(195, rank.AccountLevel);
    }

    [Fact]
    public void 段位_舊格式直接放在最外層也讀得到_名次為0時當作沒有()
    {
        var rank = RiotLocalApi.ParsePresencePrivate(ToBase64("""{ "competitiveTier": 12, "leaderboardPosition": 0, "accountLevel": 40 }"""));
        Assert.Equal(12, rank!.CompetitiveTier);
        Assert.Null(rank.LeaderboardPosition);
    }

    [Fact]
    public void 段位_只取自己的_VALORANT_presence()
    {
        var mine = ToBase64("""{ "playerPresenceData": { "competitiveTier": 18 } }""");
        var friend = ToBase64("""{ "playerPresenceData": { "competitiveTier": 27 } }""");
        var json = $$"""
            { "presences": [
                { "puuid": "friend", "product": "valorant", "private": "{{friend}}" },
                { "puuid": "me", "product": "league_of_legends", "private": "{{friend}}" },
                { "puuid": "me", "product": "valorant", "private": "{{mine}}" }
            ] }
            """;
        Assert.Equal(18, RiotLocalApi.ParseOwnRank(json, "me")!.CompetitiveTier);
        Assert.Null(RiotLocalApi.ParseOwnRank(json, "nobody"));
    }

    [Fact]
    public void 段位_資料壞掉時回傳_null()
    {
        Assert.Null(RiotLocalApi.ParsePresencePrivate("這不是base64!!"));
        Assert.Null(RiotLocalApi.ParsePresencePrivate(ToBase64("""{ "playerPresenceData": { } }""")));
        Assert.Null(RiotLocalApi.ParseOwnRank("不是 JSON", "me"));
    }

    [Theory]
    [InlineData(0, "未分級")]
    [InlineData(3, "鐵牌 1")]
    [InlineData(8, "銅牌 3")]
    [InlineData(12, "金牌 1")]
    [InlineData(20, "鑽石 3")]
    [InlineData(21, "超凡入聖 1")]
    [InlineData(24, "神話 1")]
    [InlineData(26, "神話 3")]
    [InlineData(27, "輻能戰魂")]
    [InlineData(30, "段位 30")]
    public void 段位名稱對照(int tier, string expected)
    {
        Assert.Equal(expected, RiotAccountHub.Core.Models.RankNames.ToDisplay(tier));
    }

    // ---------------- 卡面 ----------------

    [Fact]
    public void 卡面編號_從_presence_讀取()
    {
        var rank = RiotLocalApi.ParsePresencePrivate(ToBase64("""
            { "playerPresenceData": { "competitiveTier": 24, "playerCardId": "65cbcfd7-4eeb-d935-ee1b-889c669a9db1" } }
            """));
        Assert.Equal("65cbcfd7-4eeb-d935-ee1b-889c669a9db1", rank!.PlayerCardId);
    }

    [Theory]
    [InlineData("65cbcfd7-4eeb-d935-ee1b-889c669a9db1", true)]
    [InlineData("../../Windows/system32", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 卡面編號_只接受_GUID_格式(string? id, bool expected)
    {
        Assert.Equal(expected, RiotAccountHub.Core.Cards.PlayerCardCache.IsValidId(id));
    }

    [Fact]
    public void 卡面圖片_只接受_PNG()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
        Assert.True(RiotAccountHub.Core.Cards.PlayerCardCache.LooksLikePng(png));
        Assert.False(RiotAccountHub.Core.Cards.PlayerCardCache.LooksLikePng(System.Text.Encoding.UTF8.GetBytes("<html>error</html>")));
    }

    // ---------------- 判斷是不是同一個帳號（快速啟動用） ----------------

    [Fact]
    public void 同一個帳號_有_puuid_時比對_puuid_改名也認得出來()
    {
        var account = new RiotAccountHub.Core.Models.Account { GameName = "舊名字", TagLine = "TW1", Puuid = "AAA" };
        Assert.True(account.IsSameAccount(new RiotIdInfo("新名字", "0001", "aaa")));
        Assert.False(account.IsSameAccount(new RiotIdInfo("舊名字", "TW1", "BBB")));
    }

    [Fact]
    public void 同一個帳號_沒有_puuid_時比對_RiotID_不分大小寫()
    {
        var account = new RiotAccountHub.Core.Models.Account { GameName = "SilentAim", TagLine = "Tw1" };
        Assert.True(account.IsSameAccount(new RiotIdInfo("silentaim", "TW1", "CCC")));
        Assert.False(account.IsSameAccount(new RiotIdInfo("Other", "TW1", null)));
    }
}
