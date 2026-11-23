using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Core.GameSettings;

/// <summary>
/// 「同步遊戲設定」的完整流程：匯出、套用（先自動備份）、還原。
/// 所有動作都只針對「Riot Client 目前登入的帳號」。
/// </summary>
public sealed class SettingsSync
{
    private readonly RiotLocalApi _api;
    private readonly RiotClientService _riot;
    private readonly PlayerSettingsClient _client;

    public SettingsSync(RiotLocalApi api, RiotClientService riot, PlayerSettingsClient client, SettingsProfileStore store)
    {
        _api = api;
        _riot = riot;
        _client = client;
        Store = store;
    }

    public SettingsProfileStore Store { get; }

    public bool IsValorantRunning() => _riot.IsValorantRunning();

    /// <summary>
    /// 套用 / 還原後重新開啟 VALORANT（套用前遊戲是開著的才會用到）。
    /// Riot Client 一直開著、帳號也登入著，所以直接請它「開始遊戲」就好。
    /// 剛關掉遊戲時 Riot Client 可能還沒反應過來，被拒絕的話每 2 秒再試，最多約 20 秒。
    /// 回傳是否成功送出。
    /// </summary>
    public async Task<bool> RelaunchGameAsync()
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            if (_riot.IsValorantRunning()) return true;

            var result = await _api.LaunchValorantAsync();
            if (result == LaunchRequestResult.Accepted)
            {
                AppLog.Info("套用設定後已重新開啟 VALORANT");
                return true;
            }
            if (result == LaunchRequestResult.NoResponse)
            {
                // 可能已經送到，只是沒回應；不要重送，避免開兩次
                AppLog.Info("套用設定後重新開啟 VALORANT：Riot Client 沒有回應，不再重送");
                return false;
            }
            await Task.Delay(2000);
        }
        AppLog.Info("套用設定後重新開啟 VALORANT：Riot Client 一直拒絕，請手動開始遊戲");
        return false;
    }

    /// <summary>
    /// 目前登入的是哪個帳號。沒開 Riot Client 或沒登入時，丟出容易懂的中文訊息。
    /// </summary>
    public async Task<RiotIdInfo> GetCurrentAccountAsync()
    {
        if (!_api.IsClientRunning())
        {
            throw new UserFacingException("Riot Client 沒有開啟",
                "要先打開 Riot Client 並登入，才能讀取或套用遊戲設定。\n\n" +
                "做法：在主畫面對想操作的帳號按「啟動」，等 Riot Client 或遊戲打開後，再回來這裡。");
        }

        var who = await _api.GetRiotIdAsync();
        if (who?.Puuid is null)
        {
            throw new UserFacingException("Riot Client 還沒登入",
                "Riot Client 有開，但還沒登入任何帳號（或還在載入中）。\n\n" +
                "請等 Riot Client 完成登入、看到主畫面後，再按「重新整理」。");
        }
        return who;
    }

    /// <summary>取得帳號與 token（token 只在這個流程中使用，用完就丟）</summary>
    private async Task<(RiotIdInfo Who, EntitlementTokens Tokens)> RequireLoginAsync(string? expectedPuuid)
    {
        var who = await GetCurrentAccountAsync();
        if (expectedPuuid is not null && !string.Equals(who.Puuid, expectedPuuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException("登入的帳號變了",
                $"Riot Client 現在登入的是「{who.RiotId}」，跟剛剛確認的帳號不一樣。\n為了安全，這次沒有做任何修改。請重新確認後再試。");
        }

        var tokens = await _api.GetEntitlementsAsync();
        if (tokens is null)
        {
            throw new UserFacingException("Riot Client 還沒登入完成",
                "暫時拿不到登入資訊（Riot Client 可能還在載入）。\n請等幾秒後再試一次。");
        }
        return (who, tokens);
    }

    /// <summary>
    /// 自動記下「主帳號設定」：讀取目前登入帳號（必須是主帳號）的設定，
    /// 存成固定編號的設定檔（每次覆蓋同一份）。
    /// </summary>
    public async Task<SettingsProfile> CaptureMainAsync(string mainPuuid)
    {
        var (who, tokens) = await RequireLoginAsync(mainPuuid);
        var json = await _client.DownloadAsync(tokens);

        var profile = new SettingsProfile
        {
            Id = SettingsProfileStore.MainProfileId,
            Name = $"主帳號設定（{who.RiotId}）",
            SourceRiotId = who.RiotId,
            SourcePuuid = who.Puuid,
            SettingsJson = json,
        };
        Store.SaveProfile(profile);
        AppLog.Info("已自動記下主帳號設定");
        return profile;
    }

    /// <summary>匯出：讀取目前帳號的設定，存成本機設定檔</summary>
    public async Task<SettingsProfile> ExportAsync(string name, string expectedPuuid)
    {
        var (who, tokens) = await RequireLoginAsync(expectedPuuid);
        var json = await _client.DownloadAsync(tokens);

        var profile = new SettingsProfile
        {
            Name = name,
            SourceRiotId = who.RiotId,
            SourcePuuid = who.Puuid,
            SettingsJson = json,
        };
        Store.SaveProfile(profile);
        AppLog.Info("已匯出遊戲設定檔");
        return profile;
    }

    /// <summary>
    /// 套用：把設定檔寫入目前登入的帳號。
    ///   1. 確認帳號沒換人
    ///   2. 關閉 VALORANT（遊戲開著的話，之後會把舊設定蓋回去）
    ///   3. 備份目標帳號目前的設定
    ///   4. 寫入（保留目標帳號自己的「看過的公告」等紀錄），並讀回確認
    ///   5. 失敗就自動寫回備份
    /// </summary>
    public async Task ApplyAsync(SettingsProfile profile, string expectedPuuid)
    {
        var (who, tokens) = await RequireLoginAsync(expectedPuuid);

        await _riot.CloseValorantAsync();

        var current = await _client.DownloadAsync(tokens);
        Store.SaveBackup(who.Puuid!, new SettingsProfile
        {
            Name = $"套用「{profile.Name}」前的備份",
            SourceRiotId = who.RiotId,
            SourcePuuid = who.Puuid,
            SettingsJson = current,
        });

        var merged = PlayerSettingsCodec.MergeKeepingAccountState(profile.SettingsJson, current);
        try
        {
            await _client.UploadAsync(tokens, merged);
            AppLog.Info("已套用遊戲設定檔");
        }
        catch (Exception ex)
        {
            AppLog.Error("套用遊戲設定失敗，嘗試自動還原", ex);
            try
            {
                await _client.UploadAsync(tokens, current);
            }
            catch (Exception restoreEx)
            {
                AppLog.Error("自動還原遊戲設定也失敗", restoreEx);
                throw new UserFacingException("套用失敗，而且自動還原也失敗",
                    "寫入遊戲設定時發生錯誤，自動改回原本設定也失敗了。\n\n" +
                    "別擔心，原本的設定已經備份在本機。等網路正常後，按「還原套用前的設定」就能改回去。\n\n" +
                    "詳細資訊：" + ex.Message, ex);
            }

            var (title, message) = ex is UserFacingException ufe ? (ufe.Title, ufe.Message) : ("套用失敗", ex.Message);
            throw new UserFacingException(title, message + "\n\n帳號原本的設定已經自動改回去了。", ex);
        }
    }

    /// <summary>還原：把目前帳號最近一次「套用前的備份」寫回去</summary>
    public async Task<SettingsProfile> RestoreAsync(string expectedPuuid)
    {
        var (who, tokens) = await RequireLoginAsync(expectedPuuid);
        var backup = Store.LatestBackup(who.Puuid)
            ?? throw new UserFacingException("沒有可以還原的備份", $"「{who.RiotId}」還沒有套用過設定檔，所以沒有備份可以還原。");

        await _riot.CloseValorantAsync();
        await _client.UploadAsync(tokens, backup.SettingsJson);
        AppLog.Info("已還原遊戲設定備份");
        return backup;
    }
}
