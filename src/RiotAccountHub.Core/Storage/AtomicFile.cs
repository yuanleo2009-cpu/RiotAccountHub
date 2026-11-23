namespace RiotAccountHub.Core.Storage;

/// <summary>
/// 「安全寫檔」小工具。
/// 先把內容寫到 xxx.tmp，確定寫完了，再一次把 xxx.tmp 改名成 xxx。
/// 這樣就算寫到一半當機或斷電，原本的檔案也不會變成只寫了一半的壞檔。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data, 0, data.Length);
            fs.Flush(flushToDisk: true); // 確實寫進硬碟
        }

        // 同一個磁碟內的改名（取代）是一次完成的動作
        File.Move(tmp, path, overwrite: true);
    }
}
