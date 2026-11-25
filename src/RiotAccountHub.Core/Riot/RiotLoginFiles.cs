using RiotAccountHub.Core.Snapshots;

namespace RiotAccountHub.Core.Riot;

/// <summary>
/// Riot Client 的登入狀態存在哪些檔案。
///
/// 這份清單是在使用者電腦上實際檢查、並對照 TcNo Account Switcher（4.0.9 版）
/// 的 Riot 設定後整理出來的。Riot 更新後如果改了檔名，只需要改這裡。
///
/// 位置都在 %LocalAppData%\Riot Games\Riot Client\ 底下。
/// 不存在的項目會自動略過，所以新舊版本的檔名可以同時列著。
/// </summary>
public static class RiotLoginFiles
{
    /// <summary>%LocalAppData%\Riot Games\Riot Client</summary>
    public static string DefaultLiveRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riot Games", "Riot Client");

    public static readonly IReadOnlyList<LoginItem> Items = new[]
    {
        // 主要的登入狀態：勾選「保持登入」後，登入用的 cookie 會存在這裡（目前版本的檔名）
        new LoginItem(@"Data\RiotGamesPrivateSettings.yaml", IsDirectory: false),
        // 舊版 / 其他版本用的檔名（TcNo 清單裡的名稱）
        new LoginItem(@"Data\RiotClientPrivateSettings.yaml", IsDirectory: false),
        // 舊版 Riot Client 的 cookie 資料夾
        new LoginItem(@"Data\Cookies", IsDirectory: true),
        // 工作階段資料夾
        new LoginItem(@"Data\Sessions", IsDirectory: true),
        // Riot Client 設定（TcNo 也會一起切換）
        new LoginItem(@"Config\RiotClientSettings.yaml", IsDirectory: false),
    };

    /// <summary>存放「保持登入」資料的檔案（依序檢查）</summary>
    private static readonly string[] PrivateSettingsFiles =
    {
        @"Data\RiotGamesPrivateSettings.yaml",
        @"Data\RiotClientPrivateSettings.yaml",
    };

    /// <summary>
    /// 檢查 Riot Client 關閉後，登入檔案裡有沒有「保持登入」的資料。
    /// </summary>
    public static PersistState DetectPersistedLogin(string liveRoot)
    {
        foreach (var rel in PrivateSettingsFiles)
        {
            var path = Path.Combine(liveRoot, rel);
            if (!File.Exists(path)) continue;

            string text;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return PersistState.Unknown;
            }

            var state = ParsePersistState(text);
            if (state == PersistState.Persisted) return state;
            if (state == PersistState.Unknown) return state;
        }
        // 沒有檔案，或檔案裡沒有保持登入的資料
        return PersistState.NotPersisted;
    }

    /// <summary>
    /// 從 YAML 文字判斷有沒有保持登入。
    ///
    /// 新格式（2026-11 在使用者電腦上看到的，Riot Client 已經不寫 riot-login 區塊了）：
    ///   有保持登入：     psl:
    ///                      authorization:
    ///                        riot-client:
    ///                          refresh_token: "（一長串）"   ← 下次開啟就用它自動登入
    ///
    /// 舊格式：
    ///   沒勾保持登入：   riot-login:
    ///                     persist: null
    ///   有勾保持登入：   riot-login:
    ///                     persist:
    ///                       （底下還有一層 session / cookies 等資料）
    /// </summary>
    internal static PersistState ParsePersistState(string yaml)
    {
        var lines = yaml.Replace("\r", "").Split('\n');

        // 新格式：psl 區塊裡有 refresh_token 就是有保持登入
        if (TopLevelBlock(lines, "psl:").Any(l => HasValue(l, "refresh_token:")))
        {
            return PersistState.Persisted;
        }

        var sectionIndex = Array.FindIndex(lines, l => l.TrimEnd() == "riot-login:");
        if (sectionIndex < 0)
        {
            // 完全沒有 riot-login 區塊：當作沒有登入資料
            return PersistState.NotPersisted;
        }

        for (var i = sectionIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (!char.IsWhiteSpace(line[0])) break; // 已經到下一個頂層區塊了

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("persist:", StringComparison.Ordinal)) continue;

            var value = trimmed["persist:".Length..].Trim();
            if (value is "null" or "~" or "{}") return PersistState.NotPersisted;
            if (value.Length > 0) return PersistState.Unknown;

            // persist: 後面是空的 → 看下一行是不是縮排更深（有子資料）
            var indent = Indent(line);
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim().Length == 0) continue;
                return Indent(lines[j]) > indent ? PersistState.Persisted : PersistState.NotPersisted;
            }
            return PersistState.NotPersisted;
        }

        return PersistState.Unknown;
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary>某個頂層區塊（例如 "psl:"）底下的所有行；沒有這個區塊就是空的</summary>
    private static IEnumerable<string> TopLevelBlock(string[] lines, string header)
    {
        var start = Array.FindIndex(lines, l => l.TrimEnd() == header);
        if (start < 0) yield break;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (!char.IsWhiteSpace(line[0])) yield break; // 到下一個頂層區塊了
            yield return line;
        }
    }

    /// <summary>這一行是不是「欄位: 有內容的值」（null、空字串、{} 都不算有內容）</summary>
    private static bool HasValue(string line, string key)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key, StringComparison.Ordinal)) return false;
        var value = trimmed[key.Length..].Trim();
        return value is not ("" or "null" or "~" or "\"\"" or "''" or "{}");
    }
}

/// <summary>保持登入的狀態</summary>
public enum PersistState
{
    /// <summary>有保持登入的資料</summary>
    Persisted,
    /// <summary>沒有保持登入的資料</summary>
    NotPersisted,
    /// <summary>無法判斷（檔案格式跟預期不同，可能是 Riot 更新了）</summary>
    Unknown,
}
