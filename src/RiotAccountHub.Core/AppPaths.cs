namespace RiotAccountHub.Core;

/// <summary>
/// 集中管理「本程式自己的資料」要放在哪裡。
/// 預設位置是 %AppData%\RiotAccountHub\,
/// 單元測試時可以傳入一個暫存資料夾，避免動到真正的資料。
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string dataRoot)
    {
        DataRoot = dataRoot;
    }

    /// <summary>預設路徑：%AppData%\RiotAccountHub</summary>
    public static AppPaths CreateDefault() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RiotAccountHub"));

    /// <summary>資料根目錄</summary>
    public string DataRoot { get; }

    /// <summary>帳號清單（只有 Riot ID、備註等，不含任何密碼或登入資料）</summary>
    public string AccountsFile => Path.Combine(DataRoot, "accounts.json");

    /// <summary>程式設定</summary>
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");

    /// <summary>每個帳號的加密快照都放在這個資料夾</summary>
    public string SnapshotsDir => Path.Combine(DataRoot, "snapshots");

    /// <summary>第一次使用前，使用者「原本」的登入狀態（加密）</summary>
    public string OriginalBackupFile => Path.Combine(DataRoot, "original-backup.bin");

    /// <summary>切換檔案時用的暫存區（切換成功後會自動清掉）</summary>
    public string StagingDir => Path.Combine(DataRoot, "staging");

    /// <summary>記錄檔資料夾（只記錄操作步驟，不會記錄任何登入資料）</summary>
    public string LogsDir => Path.Combine(DataRoot, "logs");

    /// <summary>遊戲設定檔與套用前備份（只有遊戲設定，沒有登入資料）</summary>
    public string GameSettingsDir => Path.Combine(DataRoot, "game-settings");

    /// <summary>使用者自己加的準星與收藏</summary>
    public string CrosshairsFile => Path.Combine(DataRoot, "crosshairs.json");

    /// <summary>內建瀏覽器（WebView2）的資料夾</summary>
    public string WebViewDataDir => Path.Combine(DataRoot, "webview2");

    /// <summary>卡面圖片的快取資料夾（下載一次就存在這裡）</summary>
    public string CardsDir => Path.Combine(DataRoot, "cards");

    /// <summary>某個帳號的快照檔案路徑</summary>
    public string SnapshotFile(string accountId) => Path.Combine(SnapshotsDir, SafeFileName(accountId) + ".bin");

    /// <summary>
    /// 帳號編號只留英數字、- 和 _ 才拿來當檔名。
    /// 正常的編號本來就只有英數字；這是防止帳號清單檔被改成奇怪的內容時，
    /// 讀寫或刪除到快照資料夾以外的檔案。
    /// </summary>
    private static string SafeFileName(string? id)
    {
        var clean = new string((id ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length == 0 ? "unknown" : clean;
    }

    /// <summary>確保資料夾都存在</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(SnapshotsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
