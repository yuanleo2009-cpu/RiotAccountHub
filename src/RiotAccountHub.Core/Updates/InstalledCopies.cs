using System.Diagnostics;

namespace RiotAccountHub.Core.Updates;

/// <summary>電腦上另一份 Riot Account Hub（路徑、版本、正在執行的話它的程序編號）</summary>
public sealed record InstalledCopy(string Path, Version Version, int? RunningPid);

/// <summary>
/// 找出電腦上「別的位置」的舊版 Riot Account Hub。
///
/// 用途：舊版（v1.0.3 以前）沒有「程式內更新」。使用者下載更新包、打開新版時，
/// 新版會找到舊的那一份，問要不要直接把它換成新版。這樣捷徑、工作列釘選、開機自動開啟都照常能用，
/// 不用重新設定。
///
/// 找的地方：
///   1. 正在執行中的 Riot Account Hub
///   2. 「開機時自動開啟」登記的程式路徑
///   3. install.ps1 的預設安裝位置（%LocalAppData%\Programs\RiotAccountHub）
/// </summary>
public static class InstalledCopies
{
    public const string ProductName = "Riot Account Hub";

    /// <summary>install.ps1 的預設安裝位置</summary>
    public static string DefaultInstallPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RiotAccountHub", "RiotAccountHub.exe");

    /// <summary>實際到電腦上找（會讀程序清單和檔案版本）</summary>
    public static InstalledCopy? FindOlder(string selfPath, Version selfVersion, string? autostartPath)
    {
        var candidates = new List<(string Path, int? Pid)>();
        foreach (var p in Process.GetProcessesByName("RiotAccountHub"))
        {
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                try
                {
                    if (p.MainModule?.FileName is { } file) candidates.Add((file, p.Id));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // 讀不到路徑（例如剛好結束了）：略過
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(autostartPath)) candidates.Add((autostartPath, null));
        candidates.Add((DefaultInstallPath, null));

        return ChooseOlder(candidates.Select(c => (c.Path, ReadVersion(c.Path), c.Pid)), selfPath, selfVersion);
    }

    /// <summary>
    /// 從候選裡挑出要更新的那一份：不是自己、版本比自己舊、而且確實是 Riot Account Hub。
    /// 正在執行的優先（使用者正在用的那一份）。
    /// </summary>
    public static InstalledCopy? ChooseOlder(IEnumerable<(string Path, Version? Version, int? Pid)> candidates, string selfPath, Version selfVersion)
    {
        var self = Normalize(selfPath);
        InstalledCopy? best = null;
        foreach (var (path, version, pid) in candidates)
        {
            if (version is null || string.IsNullOrWhiteSpace(path)) continue;
            if (string.Equals(Normalize(path), self, StringComparison.OrdinalIgnoreCase)) continue;
            if (AppVersion.Normalize(version) >= AppVersion.Normalize(selfVersion)) continue;

            var copy = new InstalledCopy(System.IO.Path.GetFullPath(path), AppVersion.Normalize(version), pid);
            if (best is null || (best.RunningPid is null && pid is not null)) best = copy;
        }
        return best;
    }

    /// <summary>讀程式檔的版本；不存在、不是 Riot Account Hub 就回傳 null</summary>
    public static Version? ReadVersion(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.Equals(info.ProductName, ProductName, StringComparison.Ordinal)) return null;
            return AppVersion.Parse(info.ProductVersion) ?? AppVersion.Parse(info.FileVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path).TrimEnd('\\'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
