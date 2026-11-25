using System.Reflection;
using System.Text.Json;

namespace RiotAccountHub.Core.Updates;

/// <summary>
/// 「最新版」的說明檔（update.json）：每次發布新版時，跟更新包一起放在 GitHub Releases。
/// 程式讀它來知道有沒有新版本、更新包在哪裡、檔案應該長怎樣（大小和 SHA-256 指紋）。
///
/// 範例：
/// {
///   "version": "1.0.4",
///   "date": "2026-11-25",
///   "notes": ["新功能：程式裡就能更新", "修正：……"],
///   "url": "https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/download/v1.0.4/RiotAccountHub-update-v1.0.4.zip",
///   "sha256": "（64 個 0-9a-f）",
///   "size": 62417072,
///   "page": "https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/tag/v1.0.4"
/// }
/// </summary>
public sealed record UpdateManifest(
    Version Version,
    string Date,
    IReadOnlyList<string> Notes,
    Uri PackageUrl,
    string Sha256,
    long Size,
    Uri? Page)
{
    /// <summary>更新包最大可以多大（防止被塞一個超大的檔案）</summary>
    public const long MaxPackageBytes = 300L * 1024 * 1024;

    /// <summary>
    /// 解讀 update.json；內容不合理就回傳 null（不會丟出錯誤）。
    /// 更新包只接受 https 的 GitHub 網址；allowLocalTest 是測試用，額外接受本機（127.0.0.1）的 http 網址。
    /// </summary>
    public static UpdateManifest? Parse(string? json, bool allowLocalTest = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = AppVersion.Parse(Text(root, "version"));
            if (version is null) return null;

            if (!Uri.TryCreate(Text(root, "url"), UriKind.Absolute, out var url) || !IsAllowedPackageUrl(url, allowLocalTest)) return null;

            var sha = Text(root, "sha256")?.Trim().ToLowerInvariant();
            if (sha is null || sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return null;

            if (!root.TryGetProperty("size", out var sizeEl) || !sizeEl.TryGetInt64(out var size) || size <= 0 || size > MaxPackageBytes) return null;

            var notes = new List<string>();
            if (root.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in notesEl.EnumerateArray())
                {
                    if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) && notes.Count < 12)
                    {
                        notes.Add(n.GetString()!.Trim());
                    }
                }
            }

            Uri? page = Uri.TryCreate(Text(root, "page"), UriKind.Absolute, out var p) && p.Scheme == Uri.UriSchemeHttps ? p : null;
            return new UpdateManifest(version, Text(root, "date") ?? "", notes, url, sha, size, page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>更新包的網址只能是 GitHub（https）；測試時另外允許本機</summary>
    public static bool IsAllowedPackageUrl(Uri url, bool allowLocalTest) =>
        (url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) ||
        (allowLocalTest && url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}

/// <summary>版本號的小工具：統一成「主.次.修」三段來比較</summary>
public static class AppVersion
{
    /// <summary>解讀 "1.0.4"、"v1.0.4"、"1.0.4+abc123"、"1.0.4.0" 這類寫法；看不懂就回傳 null</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var cut = t.IndexOfAny(new[] { '+', '-', ' ' });
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    /// <summary>只留前三段（1.0.4.0 → 1.0.4），沒有的補 0</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>這個程式自己的版本</summary>
    public static Version Current { get; } =
        Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>給人看的寫法：1.0.4</summary>
    public static string Display(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
}
