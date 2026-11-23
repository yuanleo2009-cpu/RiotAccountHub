using System.Text.Json;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Core.GameSettings;

/// <summary>
/// 一份存在本機的遊戲設定（使用者匯出的設定檔，或套用前自動建立的備份）。
/// 裡面只有遊戲設定（鍵位、靈敏度、準星…），沒有任何登入資料或 token。
/// </summary>
public sealed class SettingsProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>使用者取的名字，例如「主號設定」</summary>
    public string Name { get; set; } = "";

    /// <summary>從哪個帳號匯出的（只是顯示用）</summary>
    public string SourceRiotId { get; set; } = "";

    /// <summary>來源帳號的 puuid（用來找備份）</summary>
    public string? SourcePuuid { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>遊戲設定內容（解碼後的 JSON 文字）</summary>
    public string SettingsJson { get; set; } = "";
}

/// <summary>
/// 管理設定檔與備份的存放位置：
///   %AppData%\RiotAccountHub\game-settings\profiles\{編號}.json        使用者匯出的設定檔
///   %AppData%\RiotAccountHub\game-settings\backups\{puuid}\{時間}.json  套用前自動備份（每個帳號保留最近 10 份）
/// </summary>
public sealed class SettingsProfileStore
{
    private const int MaxBackupsPerAccount = 10;

    private readonly string _profilesDir;
    private readonly string _backupsDir;

    public SettingsProfileStore(string rootDir)
    {
        _profilesDir = Path.Combine(rootDir, "profiles");
        _backupsDir = Path.Combine(rootDir, "backups");
    }

    // ---------------- 設定檔 ----------------

    /// <summary>所有設定檔，新的在前面</summary>
    public List<SettingsProfile> ListProfiles() => ReadAll(_profilesDir);

    public void SaveProfile(SettingsProfile profile) =>
        JsonStore.Write(Path.Combine(_profilesDir, SafeName(profile.Id) + ".json"), profile);

    public void DeleteProfile(string id)
    {
        var path = Path.Combine(_profilesDir, SafeName(id) + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    // ---------------- 備份 ----------------

    /// <summary>儲存一份備份，並刪掉太舊的備份</summary>
    public void SaveBackup(string puuid, SettingsProfile backup)
    {
        var dir = Path.Combine(_backupsDir, SafeName(puuid));
        JsonStore.Write(Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json"), backup);

        foreach (var old in Directory.GetFiles(dir, "*.json").OrderByDescending(f => f).Skip(MaxBackupsPerAccount))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    /// <summary>這個帳號最新的一份備份；沒有就回傳 null</summary>
    public SettingsProfile? LatestBackup(string? puuid)
    {
        if (string.IsNullOrEmpty(puuid)) return null;
        return ReadAll(Path.Combine(_backupsDir, SafeName(puuid))).FirstOrDefault();
    }

    // ---------------- 內部 ----------------

    private static List<SettingsProfile> ReadAll(string dir)
    {
        var list = new List<SettingsProfile>();
        if (!Directory.Exists(dir)) return list;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var p = JsonStore.Read<SettingsProfile>(file);
                if (p is not null && PlayerSettingsCodec.LooksValid(p.SettingsJson)) list.Add(p);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                AppLog.Error("設定檔讀取失敗，已略過：" + Path.GetFileName(file), ex);
            }
        }
        return list.OrderByDescending(p => p.CreatedUtc).ToList();
    }

    /// <summary>只允許英數字與 - 當檔名，避免奇怪的路徑</summary>
    private static string SafeName(string value)
    {
        var clean = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        return clean.Length == 0 ? "unknown" : clean;
    }
}
