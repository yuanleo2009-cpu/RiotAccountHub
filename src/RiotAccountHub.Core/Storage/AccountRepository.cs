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

    public List<Account> Load()
    {
        LoadWarning = null;
        if (!File.Exists(_paths.AccountsFile)) return new List<Account>();

        try
        {
            return JsonStore.Read<List<Account>>(_paths.AccountsFile) ?? new List<Account>();
        }
        catch (JsonException ex)
        {
            AppLog.Error("accounts.json 讀取失敗", ex);
            var moved = JsonStore.QuarantineCorruptFile(_paths.AccountsFile);
            LoadWarning = $"帳號清單檔案已損毀，已另存為：\n{moved}\n\n加密快照檔案沒有被刪除，但帳號清單需要重新建立。";
            return new List<Account>();
        }
    }

    public void Save(IEnumerable<Account> accounts)
    {
        JsonStore.Write(_paths.AccountsFile, accounts.ToList());
    }
}
