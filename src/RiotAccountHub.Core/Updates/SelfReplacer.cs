namespace RiotAccountHub.Core.Updates;

/// <summary>
/// 把程式檔（RiotAccountHub.exe）換成新版本。
///
/// Windows 不能覆蓋「正在執行中」的程式檔，但可以把它改名。所以做法是：
///   1. 舊的 RiotAccountHub.exe 改名成 RiotAccountHub.exe.old（程式照樣在跑）
///   2. 把新版本複製成 RiotAccountHub.exe
///   3. 第 2 步失敗的話，把 .old 改回原本的名字，不會留下壞掉的程式
/// 下次程式啟動時（舊的已經關掉了）再把 .old 刪掉。
/// </summary>
public static class SelfReplacer
{
    /// <summary>把 targetExe 換成 newExe 的內容（targetExe 正在執行也可以）</summary>
    public static void Replace(string newExe, string targetExe)
    {
        if (!File.Exists(newExe)) throw new FileNotFoundException("找不到新版本的程式檔", newExe);

        var backup = targetExe + ".old";
        TryDelete(backup);
        if (File.Exists(backup)) backup = $"{targetExe}.old-{DateTime.Now:yyyyMMddHHmmss}"; // 上一次的還被佔用著

        var hadTarget = File.Exists(targetExe);
        if (hadTarget) File.Move(targetExe, backup);
        try
        {
            // 用「讀出來再寫進去」的方式複製：不會把下載檔附帶的「來自網際網路」記號一起複製過去
            using var source = new FileStream(newExe, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var target = new FileStream(targetExe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
        catch
        {
            // 放不進去：把舊的放回原位，程式維持原本的版本
            TryDelete(targetExe);
            if (hadTarget && !File.Exists(targetExe)) File.Move(backup, targetExe);
            throw;
        }
    }

    /// <summary>刪掉上次更新留下的 .old 檔（還被佔用的就先留著，下次再刪）。回傳刪掉幾個。</summary>
    public static int CleanupBackups(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null || !Directory.Exists(dir)) return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, Path.GetFileName(exePath) + ".old*"))
        {
            if (TryDelete(file)) count++;
        }
        return count;
    }

    /// <summary>這個資料夾能不能寫入（例如放在 Program Files 就不行，那就沒辦法自動更新）</summary>
    public static bool CanWriteTo(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
        var probe = Path.Combine(directory, $".rah-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // 還在使用中，下次再刪
        }
    }
}
