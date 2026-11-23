using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Core.GameSettings;

/// <summary>
/// 向 Riot 的「玩家偏好設定」伺服器讀寫 VALORANT 遊戲設定（Ares.PlayerSettings）。
///
/// 在使用者電腦上實際驗證過（2026-11）：
///   GET https://player-preferences-usw2.pp.sgp.pvp.net/playerPref/v3/getPreference/Ares.PlayerSettings
///       標頭：Authorization: Bearer {accessToken}、X-Riot-Entitlements-JWT: {token}
///       回應：{ "type": "Ares.PlayerSettings", "data": "...", "modified": 數字 }
///   （舊網址 playerpreferences.riotgames.com 已經無法連線）
/// 寫入用同一台主機的 PUT /playerPref/v3/savePreference，內容是 { "type": ..., "data": ... }。
///
/// 安全：
///   - 只連到上面這一台 Riot 主機，使用正常的 HTTPS 憑證檢查
///   - token 只在這次請求中使用，不存檔、不寫進記錄檔
///   - 不碰遊戲記憶體、不碰 Vanguard
/// </summary>
public sealed class PlayerSettingsClient
{
    private const string Host = "https://player-preferences-usw2.pp.sgp.pvp.net";
    private const string SettingsType = "Ares.PlayerSettings";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>讀取帳號目前的遊戲設定，回傳解碼後的 JSON 文字</summary>
    public async Task<string> DownloadAsync(EntitlementTokens tokens, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Host}/playerPref/v3/getPreference/{SettingsType}");
        AddAuth(req, tokens);

        var body = await SendAsync(req, "讀取", ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
            {
                throw new UserFacingException("讀取遊戲設定失敗", "Riot 伺服器回傳的資料裡沒有遊戲設定。\n如果這個帳號從來沒進過遊戲，請先開一次 VALORANT 再試。");
            }
            var json = PlayerSettingsCodec.Decode(data.GetString()!);
            if (!PlayerSettingsCodec.LooksValid(json))
            {
                throw new UserFacingException("讀取遊戲設定失敗", "讀到的遊戲設定格式跟預期不同，可能是 Riot 更新了格式。為了安全，這次不做任何動作。");
            }
            return json;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            throw new UserFacingException("讀取遊戲設定失敗", "Riot 伺服器回傳的資料無法解讀，可能是 Riot 更新了格式。為了安全，這次不做任何動作。", ex);
        }
    }

    /// <summary>
    /// 把 JSON 設定寫入帳號，寫完會再讀一次確認內容真的一樣。
    /// 確認失敗會丟出錯誤（呼叫的人負責還原備份）。
    /// </summary>
    public async Task UploadAsync(EntitlementTokens tokens, string json, CancellationToken ct = default)
    {
        if (!PlayerSettingsCodec.LooksValid(json))
        {
            throw new UserFacingException("設定檔內容不正確", "這個設定檔的內容看起來不是正常的遊戲設定，為了安全，不會寫入帳號。");
        }

        var payload = JsonSerializer.Serialize(new { type = SettingsType, data = PlayerSettingsCodec.Encode(json) });
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{Host}/playerPref/v3/savePreference")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        AddAuth(req, tokens);
        await SendAsync(req, "寫入", ct);

        // 再讀一次，確認伺服器上的設定真的變成我們寫的內容
        var after = await DownloadAsync(tokens, ct);
        if (!PlayerSettingsCodec.AreEquivalent(after, json))
        {
            throw new UserFacingException("寫入遊戲設定失敗", "寫入後再讀一次，發現 Riot 伺服器上的設定跟預期不一樣。");
        }
    }

    private static void AddAuth(HttpRequestMessage req, EntitlementTokens tokens)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        req.Headers.Add("X-Riot-Entitlements-JWT", tokens.EntitlementsJwt);
    }

    /// <summary>送出請求，把各種失敗轉成中文錯誤訊息（不會把 token 寫進訊息或記錄檔）</summary>
    private static async Task<string> SendAsync(HttpRequestMessage req, string action, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLog.Info($"遊戲設定{action}：無法連線（{ex.GetType().Name}）");
            throw new UserFacingException("無法連線到 Riot", $"{action}遊戲設定時連不上 Riot 的伺服器。\n請確認網路正常後再試一次。", ex);
        }

        using (resp)
        {
            AppLog.Info($"遊戲設定{action}：HTTP {(int)resp.StatusCode}");
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new UserFacingException("登入已過期",
                    "Riot Client 的登入憑證已經過期。\n請關掉 Riot Client，再用啟動器重新開一次這個帳號後再試。");
            }
            if (!resp.IsSuccessStatusCode)
            {
                throw new UserFacingException($"{action}遊戲設定失敗",
                    $"Riot 伺服器回應錯誤（HTTP {(int)resp.StatusCode}）。\n可能是 Riot 暫時有問題，或改了設定的存取方式。請稍後再試。");
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }
}
