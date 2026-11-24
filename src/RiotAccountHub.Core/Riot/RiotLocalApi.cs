using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RiotAccountHub.Core.Riot;

/// <summary>
/// Riot Client 執行時，會在本機(127.0.0.1)開一個只有自己電腦連得到的 API。
/// 連線資訊寫在 %LocalAppData%\Riot Games\Riot Client\Config\lockfile,
/// 格式是：名稱:PID:port:密碼:協定（例如 Riot Client:17416:64971:xxxx:https）
///
/// 本程式只用 GET 讀取資訊，不會用它做任何修改。
///
/// 在使用者電腦上實際驗證過的端點：
///   GET /rso-auth/v1/session            已登入 → 200（含 persistLogin 欄位）；沒登入 → 404
///   GET /rso-auth/v1/authorization      已登入 → 200；沒登入 → 404
///   GET /player-account/aliases/v1/active  已登入 → 200（含 game_name、tag_line）
/// </summary>
public sealed class RiotLocalApi
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// 全程式共用一個連線工具。
    /// （以前每問一次就新建一個，每幾秒問一次的話很浪費時間和系統資源。）
    ///   - Riot Client 用的是自己簽的憑證，所以只對 127.0.0.1 這一個本機位址放行
    ///   - 不走系統的 Proxy：這些請求只會送到自己電腦，連線密碼不該經過任何中間站
    ///   - 每次請求各自計時（看 SendAsync），所以這裡不設總時間限制
    /// </summary>
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, _) =>
            request.RequestUri is { Host: "127.0.0.1" },
        UseProxy = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly string _liveRoot;

    public RiotLocalApi(string liveRoot)
    {
        _liveRoot = liveRoot;
    }

    /// <summary>lockfile 的內容</summary>
    public sealed record LockfileInfo(string Name, int Pid, int Port, string Password, string Protocol);

    /// <summary>讀取 lockfile;Riot Client 沒在執行時回傳 null</summary>
    public LockfileInfo? ReadLockfile()
    {
        var path = Path.Combine(_liveRoot, "Config", "lockfile");
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return ParseLockfile(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>解析 lockfile 文字（名稱:PID:port:密碼:協定）</summary>
    public static LockfileInfo? ParseLockfile(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length != 5) return null;
        if (!int.TryParse(parts[1], out var pid) || !int.TryParse(parts[2], out var port)) return null;
        // 檢查內容合理才用：port 要在正常範圍、協定只能是 https 或 http（不然組出來的網址會出錯）
        if (port is < 1 or > 65535) return null;
        if (parts[4] is not ("https" or "http")) return null;
        return new LockfileInfo(parts[0], pid, port, parts[3], parts[4]);
    }

    /// <summary>
    /// 查詢目前的登入狀態。
    /// 這個函式不會丟出錯誤：連不上就回傳 Unreachable，讓呼叫的人自己決定怎麼辦。
    /// </summary>
    public async Task<ApiLoginStatus> GetLoginStatusAsync(CancellationToken ct = default)
    {
        var (status, json) = await GetAsync("/rso-auth/v1/session", ct);
        if (status == HttpStatusCode.NotFound) return ApiLoginStatus.NotLoggedIn;
        if (status != HttpStatusCode.OK || json is null) return ApiLoginStatus.Unreachable;

        bool? persist = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("persistLogin", out var p) &&
                p.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                persist = p.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // 格式不對就當作不知道
        }
        return ApiLoginStatus.LoggedIn(persist);
    }

    /// <summary>
    /// 取得目前登入帳號的 Riot ID（遊戲名稱 + Tag）。
    /// 取不到（沒登入、Riot Client 還在載入、Riot 改了 API）就回傳 null，不會丟出錯誤。
    ///
    /// 在使用者電腦上實際驗證過的端點：
    ///   GET /chat/v1/session                  → game_name、game_tag、puuid
    ///   GET /player-account/aliases/v1/active → game_name、tag_line（備用）
    /// </summary>
    public async Task<RiotIdInfo?> GetRiotIdAsync(CancellationToken ct = default)
    {
        // 第一優先：聊天服務的工作階段，資料最完整（有 puuid）
        var (status, json) = await GetAsync("/chat/v1/session", ct);
        var fromChat = status == HttpStatusCode.OK && json is not null ? ParseChatSession(json) : null;
        if (fromChat is not null) return fromChat;

        // 備用：帳號別名
        (status, json) = await GetAsync("/player-account/aliases/v1/active", ct);
        return status == HttpStatusCode.OK && json is not null ? ParseActiveAlias(json) : null;
    }

    /// <summary>
    /// 讀取目前登入帳號的段位（VALORANT 要在執行中）。讀不到就回傳 null，不會丟出錯誤。
    ///
    /// 在使用者電腦上實際驗證過（VALORANT release-13.10）：
    ///   GET /chat/v4/presences → presences[] 裡找 puuid 是自己、product 是 "valorant" 的那筆，
    ///   它的 private 欄位是 base64 編碼的 JSON，裡面有：
    ///     playerPresenceData.competitiveTier       段位編號（例如 24 = 神話 1）
    ///     playerPresenceData.leaderboardPosition   排行榜名次
    ///     playerPresenceData.accountLevel          帳號等級
    ///     playerPresenceData.playerCardId          裝備中的卡面編號
    /// </summary>
    public async Task<CurrentRank?> GetCurrentRankAsync(CancellationToken ct = default)
    {
        var me = await GetRiotIdAsync(ct);
        if (me?.Puuid is null) return null;

        var (status, json) = await GetAsync("/chat/v4/presences", ct);
        if (status != HttpStatusCode.OK || json is null) return null;

        var rank = ParseOwnRank(json, me.Puuid);
        return rank is null ? null : new CurrentRank(me, rank);
    }

    /// <summary>從 /chat/v4/presences 的回應裡找出自己的段位</summary>
    internal static PresenceRank? ParseOwnRank(string presencesJson, string puuid)
    {
        try
        {
            using var doc = JsonDocument.Parse(presencesJson);
            if (!doc.RootElement.TryGetProperty("presences", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var p in list.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                // 先確認欄位真的是文字才讀（不是文字時直接讀會出錯）
                if (!p.TryGetProperty("puuid", out var id) || id.ValueKind != JsonValueKind.String ||
                    !string.Equals(id.GetString(), puuid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.String ||
                    product.GetString() != "valorant") continue;
                if (!p.TryGetProperty("private", out var priv) || priv.ValueKind != JsonValueKind.String) continue;

                var rank = ParsePresencePrivate(priv.GetString()!);
                if (rank is not null) return rank;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>解碼 presence 的 private 欄位（base64 → JSON），取出段位</summary>
    internal static PresenceRank? ParsePresencePrivate(string base64)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // 目前的格式：資料在 playerPresenceData 底下；舊版格式直接放在最外層
            var data = root.TryGetProperty("playerPresenceData", out var ppd) && ppd.ValueKind == JsonValueKind.Object ? ppd : root;

            int? ReadInt(string key) =>
                data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

            var tier = ReadInt("competitiveTier");
            if (tier is null) return null;

            var position = ReadInt("leaderboardPosition");
            var cardId = data.TryGetProperty("playerCardId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return new PresenceRank(tier.Value, position > 0 ? position : null, ReadInt("accountLevel"), cardId);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 向本機 Riot Client 取得呼叫 Riot 伺服器用的 token（遊戲設定同步要用）。
    /// 已驗證：GET /entitlements/v1/token → accessToken、token、subject(puuid)。
    /// token 只放在記憶體裡用完就丟，絕對不會存檔或寫進記錄檔。
    /// 沒開 Riot Client 或沒登入時回傳 null。
    /// </summary>
    public async Task<EntitlementTokens?> GetEntitlementsAsync(CancellationToken ct = default)
    {
        var (status, json) = await GetAsync("/entitlements/v1/token", ct);
        if (status != HttpStatusCode.OK || json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Read(string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var access = Read("accessToken");
            var entitlement = Read("token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(entitlement)) return null;
            return new EntitlementTokens(access, entitlement, Read("subject"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Riot Client 是否已經登入完成（GET /rso-auth/v1/authorization 回 200）</summary>
    public async Task<bool> IsLoggedInAsync(CancellationToken ct = default)
    {
        var (status, _) = await GetAsync("/rso-auth/v1/authorization", ct);
        return status == HttpStatusCode.OK;
    }

    /// <summary>
    /// 請 Riot Client 開始 VALORANT，效果等於在 Riot 介面按「開始遊戲」。
    ///
    /// 為什麼需要：Riot 用伺服器端的功能開關（autoLaunch.disabled = true）關掉了
    /// 「--launch-product 參數自動開遊戲」，所以只傳參數的話，還要在 Riot 介面再按一次「開始遊戲」。
    ///
    /// 已在使用者電腦上驗證：POST /product-launcher/v1/products/valorant/patchlines/live
    /// 回 200（內容是遊戲工作階段編號），VALORANT 隨即啟動。
    /// </summary>
    public async Task<LaunchRequestResult> LaunchValorantAsync(CancellationToken ct = default)
    {
        // Riot Client 有時要準備一陣子才回應，所以等久一點（20 秒），避免還沒回應就以為失敗而重送
        var (status, _) = await SendAsync(HttpMethod.Post, "/product-launcher/v1/products/valorant/patchlines/live", ct, TimeSpan.FromSeconds(20));
        return status switch
        {
            HttpStatusCode.OK => LaunchRequestResult.Accepted,
            null => LaunchRequestResult.NoResponse,
            _ => LaunchRequestResult.Rejected,
        };
    }

    /// <summary>Riot Client 有沒有在執行（看 lockfile 存不存在）</summary>
    public bool IsClientRunning() => ReadLockfile() is not null;

    /// <summary>解析 /chat/v1/session 的回應</summary>
    internal static RiotIdInfo? ParseChatSession(string json) =>
        ParseRiotId(json, "game_name", "game_tag", "puuid");

    /// <summary>解析 /player-account/aliases/v1/active 的回應</summary>
    internal static RiotIdInfo? ParseActiveAlias(string json) =>
        ParseRiotId(json, "game_name", "tag_line", puuidKey: null);

    private static RiotIdInfo? ParseRiotId(string json, string nameKey, string tagKey, string? puuidKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            string? Read(string? key) =>
                key is not null &&
                doc.RootElement.TryGetProperty(key, out var v) &&
                v.ValueKind == JsonValueKind.String
                    ? v.GetString()?.Trim()
                    : null;

            var name = Read(nameKey);
            var tag = Read(tagKey);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(tag)) return null;

            var puuid = Read(puuidKey);
            return new RiotIdInfo(name, tag, string.IsNullOrEmpty(puuid) ? null : puuid);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>送出 GET 請求。回傳 （狀態碼， 內容）；連不上時狀態碼是 null。</summary>
    private Task<(HttpStatusCode? Status, string? Body)> GetAsync(string path, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, path, ct);

    /// <summary>送出請求到本機 Riot Client。回傳 （狀態碼， 內容）；連不上時狀態碼是 null。</summary>
    private async Task<(HttpStatusCode? Status, string? Body)> SendAsync(HttpMethod method, string path, CancellationToken ct, TimeSpan? timeout = null)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) return (null, null);

        var url = $"{lockfile.Protocol}://127.0.0.1:{lockfile.Port}{path}";
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + lockfile.Password)));

        // 這一次請求最多等多久（等太久就放棄，當作連不上）
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            using var resp = await Http.SendAsync(req, limit.Token);
            // 用 UTF-8 讀取，中文的遊戲名稱才不會變亂碼
            var bytes = await resp.Content.ReadAsByteArrayAsync(limit.Token);
            return (resp.StatusCode, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            AppLog.Info($"本機 API 連線失敗：{path}({ex.GetType().Name})");
            return (null, null);
        }
    }
}

/// <summary>
/// 從 Riot Client 讀到的帳號身分。
/// Puuid 是 Riot 給每個帳號的固定編號（改 Riot ID 也不會變），
/// 用來判斷「是不是同一個帳號」。它不是密碼，不能拿來登入。
/// </summary>
public sealed record RiotIdInfo(string GameName, string TagLine, string? Puuid)
{
    public string RiotId => $"{GameName}#{TagLine}";
}

/// <summary>
/// 呼叫 Riot 伺服器用的 token。這是敏感資料：只在記憶體中短暫使用，不存檔、不寫記錄。
/// ToString 故意不顯示內容，避免不小心被印出來。
/// </summary>
public sealed record EntitlementTokens(string AccessToken, string EntitlementsJwt, string? Puuid)
{
    public override string ToString() => "EntitlementTokens（內容已隱藏）";
}

/// <summary>「開始遊戲」請求的結果</summary>
public enum LaunchRequestResult
{
    /// <summary>Riot Client 接受了（回 200）</summary>
    Accepted,
    /// <summary>Riot Client 回應錯誤（例如還沒準備好）</summary>
    Rejected,
    /// <summary>沒有回應或連不上（請求可能已經送到，不要再重送）</summary>
    NoResponse,
}

/// <summary>從 presence 讀到的段位資料</summary>
public sealed record PresenceRank(int CompetitiveTier, int? LeaderboardPosition, int? AccountLevel, string? PlayerCardId = null);

/// <summary>目前登入的是誰 + 他的段位</summary>
public sealed record CurrentRank(RiotIdInfo Who, PresenceRank Rank);

/// <summary>本機 API 回報的登入狀態</summary>
public sealed record ApiLoginStatus(ApiLoginState State, bool? PersistLogin)
{
    public static readonly ApiLoginStatus NotLoggedIn = new(ApiLoginState.NotLoggedIn, null);
    public static readonly ApiLoginStatus Unreachable = new(ApiLoginState.Unreachable, null);
    public static ApiLoginStatus LoggedIn(bool? persist) => new(ApiLoginState.LoggedIn, persist);
}

public enum ApiLoginState
{
    /// <summary>已登入</summary>
    LoggedIn,
    /// <summary>Riot Client 在執行，但還沒登入</summary>
    NotLoggedIn,
    /// <summary>連不上本機 API（Riot Client 沒開，或 Riot 改了 API）</summary>
    Unreachable,
}
