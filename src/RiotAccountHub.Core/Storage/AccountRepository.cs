using System.Text.Json;
using RiotAccountHub.Core.Models;

namespace RiotAccountHub.Core.Storage;

/// <summary>
/// 負責讀寫帳號清單(accounts.json)。
/// 清單的順序就是畫面上卡片的順序。
/// </summary>
public sealed class AccountRepository
{
    private readonly AppPaths _paths;

    public AccountRepository(AppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// 如果讀取時發現檔案壞掉，這裡會放一段要提醒使用者的訊息。
    /// </summary>
    public string? LoadWarning { get; private set; }

    /// <summary>檔案暫時被其他程式佔用時，最多重試幾次、每次隔多久</summary>
    internal int RetryCount { get; set; } = 5;
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 讀取帳號清單。
    ///   - 檔案不存在 → 空清單（第一次使用）
    ///   - 檔案內容壞掉 → 把壞掉的檔案另存起來，回傳空清單，並在 LoadWarning 留下提醒
    ///   - 檔案暫時讀不到（被防毒軟體、雲端同步佔用）→ 重試幾次；還是不行就丟出錯誤。
    ///     這時「不能」當成空清單繼續用，否則之後一存檔，就會把原本的帳號清單蓋掉。
    /// </summary>
    public List<Account> Load()
    {
        LoadWarning = null;
        if (!File.Exists(_paths.AccountsFile)) return new List<Account>();

        try
        {
            return Clean(ReadWithRetry());
        }
        catch (JsonException ex)
        {
            AppLog.Error("accounts.json 讀取失敗", ex);
            string moved;
            try
            {
                moved = JsonStore.QuarantineCorruptFile(_paths.AccountsFile);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                throw new UserFacingException("帳號清單檔案已損毀",
                    "帳號清單檔案已損毀，而且沒辦法把它另存起來。\n" +
                    "為了不把原本的檔案蓋掉，程式會先關閉。請稍後再開啟一次。\n\n" + FriendlyError.ReasonLine(moveEx), moveEx);
            }
            LoadWarning = $"帳號清單檔案已損毀，已另存為：\n{moved}\n\n加密快照檔案沒有被刪除，但帳號清單需要重新建立。";
            return new List<Account>();
        }
    }

    private List<Account>? ReadWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return JsonStore.Read<List<Account>>(_paths.AccountsFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < RetryCount)
                {
                    Thread.Sleep(RetryDelay);
                    continue;
                }
                AppLog.Error("accounts.json 暫時無法讀取", ex);
                throw new UserFacingException("無法讀取帳號清單",
                    "帳號清單檔案現在讀不到（可能正被防毒軟體或雲端同步程式使用）。\n" +
                    "為了不把你的帳號清單蓋掉，程式會先關閉。請稍後再開啟一次。\n\n" + FriendlyError.ReasonLine(ex), ex);
            }
        }
    }

    /// <summary>
    /// 整理讀進來的清單：檔案被手動改過時，可能有空的項目或缺少欄位，
    /// 先補好，之後畫面顯示時才不會出錯。正常的檔案不會被改動。
    /// </summary>
    private static List<Account> Clean(List<Account>? list)
    {
        var result = new List<Account>();
        if (list is null) return result;

        foreach (var account in list)
        {
            if (account is null) continue;
            account.GameName ??= "";
            account.TagLine ??= "";
            account.Note ??= "";
            if (string.IsNullOrWhiteSpace(account.Id)) account.Id = Guid.NewGuid().ToString("N");
            result.Add(account);
        }
        return result;
    }

    public void Save(IEnumerable<Account> accounts)
    {
        JsonStore.Write(_paths.AccountsFile, accounts.ToList());
    }
}
