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
}
