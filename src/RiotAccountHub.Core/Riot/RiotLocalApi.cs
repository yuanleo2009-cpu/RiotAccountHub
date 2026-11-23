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
                if (!p.TryGetProperty("puuid", out var id) || !string.Equals(id.GetString(), puuid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.TryGetProperty("product", out var product) || product.GetString() != "valorant") continue;
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
            return new PresenceRank(tier.Value, position > 0 ? position : null, ReadInt("accountLevel"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

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
    private async Task<(HttpStatusCode? Status, string? Body)> GetAsync(string path, CancellationToken ct)
    {
        var lockfile = ReadLockfile();
        if (lockfile is null) return (null, null);

        // Riot Client 用的是自己簽的憑證，所以只對 127.0.0.1 這一個本機位址放行
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is { Host: "127.0.0.1" },
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };

        var url = $"{lockfile.Protocol}://127.0.0.1:{lockfile.Port}{path}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + lockfile.Password)));

        try
        {
            using var resp = await http.SendAsync(req, ct);
            // 用 UTF-8 讀取，中文的遊戲名稱才不會變亂碼
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return (resp.StatusCode, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
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

/// <summary>從 presence 讀到的段位資料</summary>
public sealed record PresenceRank(int CompetitiveTier, int? LeaderboardPosition, int? AccountLevel);

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
