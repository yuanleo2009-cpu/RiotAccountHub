using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace RiotAccountHub.Core.Riot;

/// <summary>
/// 跟 Riot Client「程式本身」有關的操作：找安裝路徑、關閉程序、啟動。
///
/// 安全原則：
///   - 只會「結束」下面清單裡的程序，不會讀寫任何程序的記憶體。
///   - Vanguard(vgc / vgk / vgtray)絕對不碰。
/// </summary>
public sealed class RiotClientService
{
    /// <summary>
    /// 切換前要關閉的程序名稱（不含 .exe）。
    /// 依據：TcNo Account Switcher 的 Riot 設定 + 在使用者電腦上實際看到的新版程序名稱。
    /// </summary>
    public static readonly IReadOnlyList<string> ProcessesToClose = new[]
    {
        // 遊戲（先關遊戲，再關 Riot Client）
        "VALORANT-Win64-Shipping",
        "VALORANT",
        "LeagueClient",
        "LeagueClientUx",
        "LeagueClientUxRender",
        "LoR",
        // Riot Client 本身
        "Riot Client",          // 新版介面程序
        "RiotClientUx",         // 舊版介面程序
        "RiotClientUxRender",
        "RiotClientServices",   // 背景服務（登入狀態由它負責寫入）
        "RiotClientCrashHandler",
    };

    /// <summary>遊戲程序（切換前要先提醒使用者遊戲會被關閉）</summary>
    private static readonly string[] GameProcesses =
    {
        "VALORANT-Win64-Shipping", "VALORANT", "LeagueClient", "LoR",
    };

    /// <summary>
    /// 絕對不能碰的程序：Vanguard 反作弊。
    /// 這裡再檢查一次，就算以後有人不小心把它加進上面的清單，也不會被關掉。
    /// </summary>
    private static readonly HashSet<string> NeverTouch = new(StringComparer.OrdinalIgnoreCase)
    {
        "vgc", "vgk", "vgtray",
    };

    /// <summary>Riot 官方記錄安裝位置的檔案</summary>
    public static string InstallsJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");

    /// <summary>Riot Client 的預設安裝位置</summary>
    public const string DefaultClientPath = @"C:\Riot Games\Riot Client\RiotClientServices.exe";

    // ------------------------------------------------------------------
    // 找路徑
    // ------------------------------------------------------------------

    /// <summary>
    /// 找出 RiotClientServices.exe 的位置。
    /// 順序：使用者在設定裡指定的 → RiotClientInstalls.json → 預設安裝位置。
    /// 都找不到就回傳 null（介面會請使用者手動選擇）。
    /// </summary>
    public string? FindClientPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        try
        {
            if (File.Exists(InstallsJsonPath))
            {
                var fromJson = ReadPathFromInstallsJson(File.ReadAllText(InstallsJsonPath));
                if (fromJson is not null && File.Exists(fromJson)) return fromJson;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("讀取 RiotClientInstalls.json 失敗", ex);
        }

        return File.Exists(DefaultClientPath) ? DefaultClientPath : null;
    }

    /// <summary>
    /// 從 RiotClientInstalls.json 的內容取出 Riot Client 路徑。
    /// 實際檔案內容範例：
    ///   { "rc_default": "C:/Riot Games/Riot Client/RiotClientServices.exe",
    ///     "rc_live":    "C:/Riot Games/Riot Client/RiotClientServices.exe", ... }
    /// </summary>
    public static string? ReadPathFromInstallsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var key in new[] { "rc_default", "rc_live" })
            {
                if (doc.RootElement.TryGetProperty(key, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return Path.GetFullPath(value.GetString()!.Replace('/', '\\'));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            AppLog.Error("RiotClientInstalls.json 格式無法解析", ex);
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 偵測與關閉
    // ------------------------------------------------------------------

    /// <summary>有沒有任何 Riot 相關程序正在執行</summary>
    public bool IsAnyRiotProcessRunning() => ProcessesToClose.Any(IsRunning);

    /// <summary>有沒有遊戲正在執行(VALORANT / LoL / LoR)</summary>
    public bool IsGameRunning() => GameProcesses.Any(IsRunning);

    /// <summary>VALORANT 遊戲本體有沒有在執行（段位資料只有這時候讀得到）</summary>
    public bool IsValorantRunning() => IsRunning("VALORANT-Win64-Shipping");

    /// <summary>
    /// 只關閉 VALORANT 遊戲（Riot Client 保持開著），用在套用遊戲設定前。
    /// Vanguard 不在這個清單裡，不會被關閉。
    /// </summary>
    public async Task CloseValorantAsync()
    {
        string[] names = { "VALORANT-Win64-Shipping", "VALORANT" };
        if (!names.Any(IsRunning)) return;

        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        AppLog.Info($"結束程序 {name}（套用遊戲設定前）");
                        p.Kill(entireProcessTree: false);
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception ex) { AppLog.Error($"無法結束 {name}", ex); }
                }
            }
        }

        var sw = Stopwatch.StartNew();
        while (names.Any(IsRunning))
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(15))
            {
                throw new UserFacingException("無法關閉 VALORANT",
                    "套用設定前需要先關閉遊戲，但 VALORANT 沒有成功關閉。\n請手動關閉遊戲後再試一次。\n\n沒有任何設定被修改。");
            }
            await Task.Delay(100);
        }
    }

    /// <summary>把 VALORANT 的視窗叫到最前面（例如被縮小了）。只是切換視窗，不會碰遊戲本身。</summary>
    public void BringValorantToFront()
    {
        foreach (var p in Process.GetProcessesByName("VALORANT-Win64-Shipping"))
        {
            using (p)
            {
                var hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;
                if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                SetForegroundWindow(hwnd);
                return;
            }
        }
    }

    private const int SwRestore = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private static bool IsRunning(string name)
    {
        var list = Process.GetProcessesByName(name);
        foreach (var p in list) p.Dispose();
        return list.Length > 0;
    }

    /// <summary>
    /// 關閉所有 Riot Client 與遊戲程序，並等到它們真的結束為止。
    /// 超過 timeout 還沒結束就丟出錯誤（這時候不會動任何檔案）。
    /// </summary>
    public async Task CloseAllAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        // 本來就沒有在執行：不用關、也不用等
        if (!IsAnyRiotProcessRunning()) return;

        var accessDenied = new HashSet<string>();

        foreach (var name in ProcessesToClose)
        {
            if (NeverTouch.Contains(name)) continue; // 保險：Vanguard 一律跳過

            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        AppLog.Info($"結束程序 {name} (PID {p.Id})");
                        p.Kill(entireProcessTree: false);
                    }
                    catch (InvalidOperationException)
                    {
                        // 程序已經自己結束了，沒關係
                    }
                    catch (Win32Exception ex)
                    {
                        AppLog.Error($"無法結束 {name}", ex);
                        accessDenied.Add(name);
                    }
                }
            }
        }

        // 等所有程序真的消失
        var sw = Stopwatch.StartNew();
        while (IsAnyRiotProcessRunning())
        {
            if (sw.Elapsed > timeout)
            {
                var still = ProcessesToClose.Where(IsRunning).ToList();
                var hint = accessDenied.Count > 0
                    ? "\n\n可能原因：Riot Client 是用「系統管理員身分」執行的，本程式沒有權限關閉它。請手動關閉 Riot Client，或用系統管理員身分執行本程式。"
                    : "\n\n請手動關閉 Riot Client（包含右下角系統匣的圖示）後再試一次。";
                throw new UserFacingException(
                    "無法關閉 Riot Client",
                    "以下程序沒有成功關閉：\n" + string.Join("、", still) + hint +
                    "\n\n沒有任何登入檔案被修改。");
            }
            await Task.Delay(100, ct); // 每 0.1 秒檢查一次
        }

        // 程序結束後，給 Windows 一點點時間釋放檔案。
        // 不用等太久：之後讀寫檔案時如果還被鎖住，SnapshotStore 會自動重試。
        await Task.Delay(200, ct);
        AppLog.Info($"Riot Client 已關閉（{sw.ElapsedMilliseconds} ms）");
    }

    // ------------------------------------------------------------------
    // 啟動
    // ------------------------------------------------------------------

    /// <summary>只開啟 Riot Client（用於新增帳號、重新登入）</summary>
    public void StartClient(string clientPath) => Start(clientPath, "");

    /// <summary>開啟 Riot Client 並直接啟動 VALORANT</summary>
    public void StartValorant(string clientPath) =>
        Start(clientPath, "--launch-product=valorant --launch-patchline=live");

    private static void Start(string clientPath, string arguments)
    {
        if (!File.Exists(clientPath))
        {
            throw new UserFacingException("找不到 Riot Client", $"找不到這個檔案：\n{clientPath}\n\n請在下方「Riot Client 路徑」重新選擇 RiotClientServices.exe。");
        }

        try
        {
            AppLog.Info($"啟動 Riot Client:{arguments}");
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = clientPath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(clientPath)!,
                UseShellExecute = true,
            });
        }
        catch (Win32Exception ex)
        {
            throw new UserFacingException("無法啟動 Riot Client", "啟動 Riot Client 失敗。\n\n" + FriendlyError.ReasonLine(ex), ex);
        }
    }
}
