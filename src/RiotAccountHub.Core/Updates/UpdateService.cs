using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace RiotAccountHub.Core.Updates;

/// <summary>
/// 程式內更新：到 GitHub 看有沒有新版本，下載更新包並確認檔案沒有被改過。
///
/// 安全設計：
///   - 只會連到 GitHub（https）。update.json 和更新包都在這個專案自己的 GitHub Releases 裡
///   - 更新包下載完要跟 update.json 寫的大小、SHA-256 指紋完全一樣，不一樣就丟掉
///   - 解開後的程式檔，產品名稱必須是 Riot Account Hub、版本必須跟 update.json 一樣
///   - 不會送出任何帳號資料；GitHub 只會看到「有人在下載這個檔案」
/// </summary>
public sealed class UpdateService
{
    /// <summary>永遠指向「最新版」的 update.json（GitHub 會自動轉到最新的 Release）</summary>
    public static readonly Uri DefaultManifestUrl =
        new("https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/latest/download/update.json");

    /// <summary>最新版的下載頁面（沒辦法自動更新時，打開這裡讓使用者手動下載）</summary>
    public const string LatestReleasePage = "https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/latest";

    /// <summary>更新包裡的程式檔名</summary>
    public const string ExeName = "RiotAccountHub.exe";

    private static readonly HttpClient Http = CreateClient();

    private readonly Uri _manifestUrl;
    private readonly bool _allowLocalTest;

    /// <param name="manifestUrl">
    /// 只有測試會傳：換成本機（127.0.0.1）的 update.json。其他網址一律不接受，維持用 GitHub。
    /// </param>
    public UpdateService(Uri? manifestUrl = null)
    {
        _allowLocalTest = manifestUrl is { IsLoopback: true } && manifestUrl.Scheme == Uri.UriSchemeHttp;
        _manifestUrl = _allowLocalTest ? manifestUrl! : DefaultManifestUrl;
    }

    /// <summary>實際使用的 update.json 網址</summary>
    internal Uri ManifestUrl => _manifestUrl;

    /// <summary>測試時換掉「讀程式檔版本」的方法（單元測試用的不是真的程式檔）</summary>
    internal Func<string, Version?> ReadExeVersion { get; set; } = InstalledCopies.ReadVersion;

    /// <summary>
    /// 讀取最新版的 update.json。連不上或內容不對就回傳 null（不會丟出錯誤）。
    /// </summary>
    public async Task<UpdateManifest?> FetchLatestAsync(CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var resp = await Http.GetAsync(_manifestUrl, limit.Token);
            if (!resp.IsSuccessStatusCode)
            {
                AppLog.Info($"檢查更新：GitHub 回應 {(int)resp.StatusCode}");
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(limit.Token);
            var manifest = UpdateManifest.Parse(json, _allowLocalTest);
            if (manifest is null) AppLog.Info("檢查更新：update.json 的內容看不懂，略過");
            return manifest;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            AppLog.Info($"檢查更新：連不上（{ex.GetType().Name}）");
            return null;
        }
    }

    /// <summary>
    /// 下載更新包、確認大小和 SHA-256 指紋、解開，回傳新版程式檔的位置。
    /// 任何一步不對都會丟出 UserFacingException（中文說明），而且不會動到目前的程式。
    /// </summary>
    /// <param name="workDir">放下載檔的資料夾（每次都會先清空）</param>
    public async Task<string> DownloadAsync(UpdateManifest manifest, string workDir,
        IProgress<SwitchStep>? steps = null, IProgress<double>? percent = null, CancellationToken ct = default)
    {
        if (!UpdateManifest.IsAllowedPackageUrl(manifest.PackageUrl, _allowLocalTest))
        {
            throw new UserFacingException("更新包的位置不對", "更新包不在這個程式的 GitHub 上，為了安全沒有下載。");
        }

        ResetFolder(workDir);
        var part = Path.Combine(workDir, "package.zip.part");
        var zipPath = Path.Combine(workDir, "package.zip");

        steps?.Report(SwitchStep.Downloading);
        try
        {
            using var resp = await Http.GetAsync(manifest.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is { } announced && announced != manifest.Size)
            {
                throw new UserFacingException("更新包不對", "GitHub 上的更新包大小跟說明檔不一樣，為了安全沒有更新。請稍後再試，或到下載頁面手動下載。");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await resp.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                var lastReported = -1;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > manifest.Size)
                    {
                        throw new UserFacingException("更新包不對", "下載到的檔案比說明檔寫的還大，為了安全沒有更新。");
                    }
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);

                    var pct = (int)(total * 100 / manifest.Size);
                    if (pct != lastReported) { lastReported = pct; percent?.Report(total / (double)manifest.Size); }
                }
                if (total != manifest.Size)
                {
                    throw new UserFacingException("更新包下載不完整", "下載到一半中斷了。請確認網路後再試一次。");
                }
            }

            steps?.Report(SwitchStep.Verifying);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (actual != manifest.Sha256)
            {
                throw new UserFacingException("更新包檢查沒通過", "下載到的檔案跟 GitHub 上公布的指紋（SHA-256）不一樣，可能下載時壞掉了。為了安全沒有更新，請再試一次。");
            }
            File.Move(part, zipPath);
        }
        catch (HttpRequestException ex)
        {
            throw new UserFacingException("下載更新失敗", "連不上 GitHub，或下載到一半中斷了。請確認網路後再試一次。", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new UserFacingException("下載更新失敗", "下載太久沒有完成（網路可能太慢或斷線了）。請稍後再試一次。", ex);
        }

        // 解開：只取出 RiotAccountHub.exe
        var exePath = Path.Combine(workDir, ExeName);
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length <= 0 || entry.Length > 2 * UpdateManifest.MaxPackageBytes)
            {
                throw new UserFacingException("更新包不對", "更新包裡找不到程式檔，沒有更新。");
            }
            entry.ExtractToFile(exePath, overwrite: true);
        }
        File.Delete(zipPath);

        // 確認解開的程式真的是 Riot Account Hub、而且是說明檔寫的那個版本
        var version = ReadExeVersion(exePath);
        if (version is null || AppVersion.Normalize(version) != AppVersion.Normalize(manifest.Version))
        {
            throw new UserFacingException("更新包不對", "更新包裡的程式版本跟說明檔不一樣，為了安全沒有更新。");
        }
        return exePath;
    }

    /// <summary>清空（或建立）下載用的資料夾</summary>
    public static void ResetFolder(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Directory.CreateDirectory(dir);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RiotAccountHub", AppVersion.Display(AppVersion.Current)));
        return client;
    }
}
