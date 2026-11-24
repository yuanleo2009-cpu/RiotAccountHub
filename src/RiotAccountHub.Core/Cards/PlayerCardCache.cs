using System.Collections.Concurrent;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Core.Cards;

/// <summary>
/// 下載並快取 VALORANT 的卡面圖片（頭像用的正方形小圖、卡片背景用的長條圖）。
///
/// 圖片來源：valorant-api.com（玩家社群架設的網站，不是 Riot 官方）
///   https://media.valorant-api.com/playercards/{卡面編號}/smallart.png  正方形
///   https://media.valorant-api.com/playercards/{卡面編號}/wideart.png   長條形
///
/// 隱私：送出去的只有「卡面編號」，跟帳號、Riot ID、登入資料完全無關。
/// 每張卡面只下載一次，之後都從 %AppData%\RiotAccountHub\cards\ 讀取。
/// </summary>
public sealed class PlayerCardCache
{
    private const string BaseUrl = "https://media.valorant-api.com/playercards/";
    private const int MaxImageBytes = 5 * 1024 * 1024; // 正常圖片不到 100 KB，超過 5 MB 一定有問題

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string _dir;

    // 同一張卡面同時只下載一次
    private readonly ConcurrentDictionary<string, Task<bool>> _inFlight = new();

    public PlayerCardCache(string cacheDir)
    {
        _dir = cacheDir;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RiotAccountHub/1.0");
        return http;
    }

    /// <summary>卡面編號一定是 GUID 格式；不是的話一律拒絕（避免奇怪的路徑或網址）</summary>
    public static bool IsValidId(string? id) => Guid.TryParse(id, out _);

    /// <summary>正方形小圖（頭像）的本機路徑；還沒下載就回傳 null</summary>
    public string? GetSmallArt(string? id) => Existing(id, "small");

    /// <summary>長條圖（卡片背景）的本機路徑；還沒下載就回傳 null</summary>
    public string? GetWideArt(string? id) => Existing(id, "wide");

    private string? Existing(string? id, string kind)
    {
        if (!IsValidId(id)) return null;
        var path = PathFor(id!, kind);
        return File.Exists(path) ? path : null;
    }

    private string PathFor(string id, string kind) =>
        Path.Combine(_dir, $"{Guid.Parse(id):D}-{kind}.png");

    /// <summary>
    /// 確保這張卡面的兩張圖都已經下載好。
    /// 已經有了就直接回傳 true；下載失敗回傳 false（不會丟出錯誤，介面會改用文字頭像）。
    /// </summary>
    public Task<bool> EnsureAsync(string? id)
    {
        if (!IsValidId(id)) return Task.FromResult(false);
        var key = Guid.Parse(id!).ToString("D");
        if (GetSmallArt(key) is not null && GetWideArt(key) is not null) return Task.FromResult(true);

        return _inFlight.GetOrAdd(key, k => DownloadBothAsync(k).ContinueWith(t =>
        {
            _inFlight.TryRemove(k, out _);
            return t.IsCompletedSuccessfully && t.Result;
        }, TaskScheduler.Default));
    }

    private async Task<bool> DownloadBothAsync(string id)
    {
        var small = await DownloadAsync(id, "smallart.png", PathFor(id, "small"));
        var wide = await DownloadAsync(id, "wideart.png", PathFor(id, "wide"));
        return small && wide;
    }

    private static async Task<bool> DownloadAsync(string id, string fileName, string destination)
    {
        if (File.Exists(destination)) return true;
        try
        {
            using var resp = await Http.GetAsync(BaseUrl + id + "/" + fileName, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                AppLog.Info($"卡面圖片下載失敗（{(int)resp.StatusCode}）：{fileName}");
                return false;
            }
            if (resp.Content.Headers.ContentLength > MaxImageBytes) return false;

            // 邊下載邊檢查大小：超過上限就馬上停，不會把一個超大的檔案整個讀進記憶體
            byte[] bytes;
            await using (var stream = await resp.Content.ReadAsStreamAsync())
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxImageBytes) break;
                }
                bytes = buffer.ToArray();
            }

            if (bytes.Length > MaxImageBytes || !LooksLikePng(bytes))
            {
                AppLog.Info("卡面圖片格式不正確，已略過：" + fileName);
                return false;
            }

            AtomicFile.WriteAllBytes(destination, bytes);
            AppLog.Info("已下載卡面圖片：" + fileName);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            AppLog.Info($"卡面圖片下載失敗：{fileName}({ex.GetType().Name})");
            return false;
        }
    }

    /// <summary>檢查是不是 PNG 圖片（PNG 檔開頭固定是 89 50 4E 47 0D 0A 1A 0A）</summary>
    internal static bool LooksLikePng(byte[] bytes) =>
        bytes.Length > 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
}
