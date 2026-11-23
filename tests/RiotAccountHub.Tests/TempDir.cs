namespace RiotAccountHub.Tests;

/// <summary>
/// 測試用的暫存資料夾：測試結束後自動刪除，絕對不會動到真正的 Riot 檔案。
/// </summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "RiotAccountHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Sub(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // 清不掉就算了
        }
    }
}
