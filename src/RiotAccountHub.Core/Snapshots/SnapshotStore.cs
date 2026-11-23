using System.IO.Compression;
using System.Security.Cryptography;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Core.Snapshots;

/// <summary>
/// 快照的「拍照」、「加密存檔」與「放回原位」。
///
/// 快照格式：
///   1. 把登入相關的檔案打包成一個 zip（在記憶體裡，不會落地）
///   2. 前面加上 4 個字的檔頭 "RAH1"（用來確認解密後的內容是正確的）
///   3. 用 DPAPI 加密，最後才存成 .bin 檔
///
/// 放回原位(Apply)時，為了不讓檔案停在一半的狀態，步驟是：
///   1. 先把快照解密、解壓到暫存區（這一步失敗，Riot 的檔案完全沒被動過）
///   2. 把目前的檔案複製一份到暫存區當「還原備份」（這一步失敗，也還沒動到任何檔案）
///   3. 刪除目前的檔案
///   4. 把快照的檔案複製過去
///   第 3、4 步任何一步失敗，就用第 2 步的備份自動還原。
/// </summary>
public sealed class SnapshotStore
{
    private static readonly byte[] Magic = "RAH1"u8.ToArray();

    private readonly IDataProtector _protector;
    private readonly string _stagingRoot;

    public SnapshotStore(IDataProtector protector, string stagingRoot)
    {
        _protector = protector;
        _stagingRoot = stagingRoot;
    }

    /// <summary>檔案被鎖住時要重試幾次</summary>
    internal int RetryCount { get; set; } = 10;

    /// <summary>每次重試之間等多久</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 只給單元測試用：在特定步驟故意丟出錯誤，測試「自動還原」是否正常。
    /// 步驟名稱：extracted、backed-up、cleared、placed
    /// </summary>
    internal Action<string>? TestHook { get; set; }

    // ------------------------------------------------------------------
    // 1. 拍照：把目前的登入檔案打包並加密
    // ------------------------------------------------------------------

    /// <summary>
    /// 把 liveRoot 底下的登入檔案打包並加密，回傳加密後的資料。
    /// 不存在的檔案會自動略過。
    /// </summary>
    public byte[] Capture(string liveRoot, IReadOnlyList<LoginItem> items)
    {
        byte[] zipBytes;
        try
        {
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var rel in EnumerateItemFiles(liveRoot, items))
                {
                    AddFile(zip, Path.Combine(liveRoot, rel), rel.Replace('\\', '/'));
                }
            }
            zipBytes = zipStream.ToArray();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            throw new UserFacingException(
                "無法讀取登入檔案",
                "讀取 Riot Client 的登入檔案時失敗，可能是 Riot Client 還沒完全關閉，或檔案被其他程式（例如防毒軟體）鎖住。\n\n" +
                "沒有任何檔案被修改。請稍後再試一次。\n\n" + FriendlyError.ReasonLine(ex), ex);
        }

        var plain = new byte[Magic.Length + zipBytes.Length];
        Magic.CopyTo(plain, 0);
        zipBytes.CopyTo(plain, Magic.Length);

        try
        {
            return _protector.Protect(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain); // 用完就把記憶體裡的明文清掉
        }
    }

    private void AddFile(ZipArchive zip, string fullPath, string entryName)
    {
        var bytes = ReadWithRetry(fullPath);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var es = entry.Open();
        es.Write(bytes, 0, bytes.Length);
    }

    // ------------------------------------------------------------------
    // 2. 存檔 / 讀檔
    // ------------------------------------------------------------------

    /// <summary>把加密後的快照安全地寫到檔案（不會寫到一半）</summary>
    public void SaveToFile(string path, byte[] encrypted) => AtomicFile.WriteAllBytes(path, encrypted);

    /// <summary>讀取快照檔案</summary>
    public byte[] LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new UserFacingException(
                "找不到這個帳號的快照",
                "這個帳號的登入快照檔案不見了。\n請對這個帳號按「重新登入」，重新儲存一次登入狀態。");
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>列出快照裡有哪些檔案（除錯與測試用，不會顯示檔案內容）</summary>
    public IReadOnlyList<string> ListEntries(byte[] encrypted)
    {
        var zipBytes = Decrypt(encrypted);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName).ToList();
    }

    // ------------------------------------------------------------------
    // 3. 放回原位（或清空）
    // ------------------------------------------------------------------

    /// <summary>
    /// 把快照的檔案放到 liveRoot。
    /// encrypted 是 null 的話，代表「清空登入狀態」（只刪除，不放新檔案）。
    /// 呼叫前必須確定 Riot Client 已經完全關閉。
    /// </summary>
    public void Apply(byte[]? encrypted, string liveRoot, IReadOnlyList<LoginItem> items)
    {
        var session = Path.Combine(_stagingRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        var incoming = Path.Combine(session, "incoming");
        var rollback = Path.Combine(session, "rollback");

        // ---- 準備階段：這裡失敗的話，Riot 的檔案完全沒被動過 ----
        try
        {
            Directory.CreateDirectory(incoming);
            Directory.CreateDirectory(rollback);

            // 步驟 1：解密並解壓到暫存區
            if (encrypted is not null)
            {
                ExtractTo(Decrypt(encrypted), incoming, items);
            }
            TestHook?.Invoke("extracted");

            // 步驟 2：把目前的檔案複製一份當還原備份
            try
            {
                CopyItems(liveRoot, rollback, items);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                throw new UserFacingException(
                    "切換失敗",
                    "無法讀取目前的 Riot Client 登入檔案，可能是 Riot Client 還沒完全關閉，或檔案被其他程式鎖住。\n\n" +
                    "沒有任何檔案被修改。請稍後再試一次。\n\n" + FriendlyError.ReasonLine(ex), ex);
            }
            TestHook?.Invoke("backed-up");
        }
        catch
        {
            TryDeleteDirectory(session);
            throw;
        }

        // ---- 修改階段：失敗的話自動用還原備份放回去 ----
        try
        {
            // 步驟 3：刪除目前的登入檔案
            DeleteItems(liveRoot, items);
            TestHook?.Invoke("cleared");

            // 步驟 4：放入快照的檔案
            CopyItems(incoming, liveRoot, items);
            TestHook?.Invoke("placed");
        }
        catch (Exception ex)
        {
            AppLog.Error("套用快照失敗，開始自動還原", ex);
            try
            {
                RestoreFromRollback(rollback, liveRoot, items);
            }
            catch (Exception rollbackEx)
            {
                // 連還原都失敗：保留暫存區，告訴使用者備份在哪裡
                AppLog.Error("自動還原也失敗，保留暫存區：" + rollback, rollbackEx);
                throw new UserFacingException(
                    "切換失敗，而且無法自動還原",
                    "寫入 Riot Client 登入檔案時發生錯誤，自動還原也失敗了。\n\n" +
                    "你切換前的檔案已經完整保留在：\n" + rollback + "\n\n" +
                    "請確認 Riot Client 已關閉後，把裡面的資料夾內容複製回：\n" + liveRoot + "\n\n" +
                    "或是使用「還原原本狀態」。\n\n" + FriendlyError.ReasonLine(ex), ex);
            }

            TryDeleteDirectory(session);
            throw new UserFacingException(
                "切換失敗",
                "寫入 Riot Client 登入檔案時發生錯誤，已經自動還原成切換前的狀態。\n\n" +
                "可能原因：Riot Client 沒有完全關閉，或檔案被防毒軟體鎖住。請稍後再試一次。\n\n" + FriendlyError.ReasonLine(ex), ex);
        }

        TryDeleteDirectory(session);
    }

    // ------------------------------------------------------------------
    // 內部小工具
    // ------------------------------------------------------------------

    /// <summary>解密，並檢查檔頭是否正確。回傳 zip 的內容。</summary>
    private byte[] Decrypt(byte[] encrypted)
    {
        byte[] plain;
        try
        {
            plain = _protector.Unprotect(encrypted);
        }
        catch (CryptographicException ex)
        {
            throw new UserFacingException(
                "快照無法解密",
                "這個快照無法解密。快照只能在建立它的同一台電腦、同一個 Windows 使用者帳號上使用。\n\n" +
                "如果你重灌過 Windows、換了電腦或換了 Windows 使用者，請對這個帳號按「重新登入」。", ex);
        }

        if (plain.Length < Magic.Length || !plain.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new UserFacingException("快照檔案格式不正確", "這個快照檔案已損毀或不是本程式建立的。\n請對這個帳號按「重新登入」。");
        }

        var zipBytes = plain[Magic.Length..];
        CryptographicOperations.ZeroMemory(plain);
        return zipBytes;
    }

    /// <summary>把 zip 解壓到 destRoot。只會解出清單裡允許的路徑，避免壞掉的快照寫到奇怪的地方。</summary>
    private static void ExtractTo(byte[] zipBytes, string destRoot, IReadOnlyList<LoginItem> items)
    {
        var rootFull = Path.GetFullPath(destRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/')) continue; // 資料夾項目

                var allowed = items.Any(i => i.IsDirectory
                    ? name.StartsWith(i.ZipPath + "/", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(name, i.ZipPath, StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    AppLog.Info("快照中有不在清單內的項目，已略過：" + name);
                    continue;
                }

                var dest = Path.GetFullPath(Path.Combine(destRoot, name));
                if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("快照中有不合法的路徑：" + name);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new UserFacingException("快照檔案已損毀", "這個快照的內容已損毀，無法使用。\n請對這個帳號按「重新登入」。\n\n" + FriendlyError.ReasonLine(ex), ex);
        }
    }

    /// <summary>列出 root 底下屬於清單項目、而且實際存在的所有檔案（相對路徑）</summary>
    private static List<string> EnumerateItemFiles(string root, IReadOnlyList<LoginItem> items)
    {
        var result = new List<string>();
        foreach (var item in items)
        {
            var full = Path.Combine(root, item.RelativePath);
            if (!item.IsDirectory)
            {
                if (File.Exists(full)) result.Add(item.RelativePath);
            }
            else if (Directory.Exists(full))
            {
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    result.Add(Path.GetRelativePath(root, file));
                }
            }
        }
        return result;
    }

    /// <summary>把清單裡的項目從 fromRoot 複製到 toRoot（不存在的就略過）</summary>
    private void CopyItems(string fromRoot, string toRoot, IReadOnlyList<LoginItem> items)
    {
        foreach (var rel in EnumerateItemFiles(fromRoot, items))
        {
            CopyFile(Path.Combine(fromRoot, rel), Path.Combine(toRoot, rel));
        }
    }

    /// <summary>
    /// 自動還原：讓 liveRoot 的內容回到 rollbackRoot（切換前的備份）的樣子。
    /// 1. 把「原本沒有、後來才放進去」的檔案刪掉（盡量刪，刪不掉也繼續）
    /// 2. 把備份檔案放回去；內容已經一樣的檔案就不動（例如剛剛因為被鎖住而沒刪掉的檔案）
    /// 第 2 步失敗才算還原失敗。
    /// </summary>
    private void RestoreFromRollback(string rollbackRoot, string liveRoot, IReadOnlyList<LoginItem> items)
    {
        var backupFiles = new HashSet<string>(EnumerateItemFiles(rollbackRoot, items), StringComparer.OrdinalIgnoreCase);

        foreach (var rel in EnumerateItemFiles(liveRoot, items))
        {
            if (backupFiles.Contains(rel)) continue;
            try
            {
                var path = Path.Combine(liveRoot, rel);
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                AppLog.Error("還原時無法刪除多出來的檔案：" + rel, ex);
            }
        }

        // 備份裡本來就沒有的資料夾（例如快照帶進來的 Cookies），整個移除
        foreach (var item in items.Where(i => i.IsDirectory))
        {
            var live = Path.Combine(liveRoot, item.RelativePath);
            if (Directory.Exists(live) && !Directory.Exists(Path.Combine(rollbackRoot, item.RelativePath)))
            {
                TryDeleteDirectory(live);
            }
        }

        foreach (var rel in backupFiles)
        {
            var backup = Path.Combine(rollbackRoot, rel);
            var live = Path.Combine(liveRoot, rel);
            if (HasSameContent(backup, live)) continue;
            CopyFile(backup, live);
        }
    }

    private static bool HasSameContent(string a, string b)
    {
        try
        {
            if (!File.Exists(b)) return false;
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private void CopyFile(string src, string dst)
    {
        var bytes = ReadWithRetry(src);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        Retry(() => File.WriteAllBytes(dst, bytes));
    }

    /// <summary>刪除清單裡的項目（不存在的就略過）</summary>
    private void DeleteItems(string root, IReadOnlyList<LoginItem> items)
    {
        foreach (var item in items)
        {
            var path = Path.Combine(root, item.RelativePath);
            if (!item.IsDirectory)
            {
                if (File.Exists(path))
                {
                    Retry(() =>
                    {
                        File.SetAttributes(path, FileAttributes.Normal); // 去掉「唯讀」才刪得掉
                        File.Delete(path);
                    });
                }
            }
            else if (Directory.Exists(path))
            {
                Retry(() =>
                {
                    foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                    }
                    Directory.Delete(path, recursive: true);
                });
            }
        }
    }

    /// <summary>讀檔；如果被鎖住就等一下再試</summary>
    private byte[] ReadWithRetry(string path) => Retry(() =>
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    });

    private void Retry(Action action) => Retry(() => { action(); return 0; });

    private T Retry<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsFileError(ex) && attempt < RetryCount)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("清除暫存區失敗：" + path, ex);
        }
    }
}
