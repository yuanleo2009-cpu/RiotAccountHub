namespace RiotAccountHub.Core.Snapshots;

/// <summary>
/// 一個要被備份或還原的「檔案」或「資料夾」。
/// RelativePath 是相對於 Riot Client 資料夾的路徑，例如 Data\RiotGamesPrivateSettings.yaml。
/// </summary>
public sealed record LoginItem(string RelativePath, bool IsDirectory)
{
    /// <summary>在快照壓縮檔裡用的路徑（用 / 分隔）</summary>
    internal string ZipPath => RelativePath.Replace('\\', '/').Trim('/');
}
