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
