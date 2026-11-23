using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiotAccountHub.Core;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Services;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 「同步遊戲設定」視窗的 ViewModel：
///   匯出目前帳號的設定 → 存成設定檔 → 套用到另一個帳號（先自動備份）→ 不喜歡可以還原
/// </summary>
public sealed partial class SettingsSyncViewModel : ObservableObject
{
    private readonly SettingsSync _sync;
    private readonly IDialogService _dialogs;
    private RiotIdInfo? _current;

    public SettingsSyncViewModel(SettingsSync sync, IDialogService dialogs)
    {
        _sync = sync;
        _dialogs = dialogs;
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// 套用或還原成功時通知主畫面（主畫面會記在對應的帳號卡片上，下次啟動時顯示提示）
    /// </summary>
    public event Action<SettingsChange>? Changed;

    /// <summary>存好的設定檔</summary>
    public ObservableCollection<SettingsProfileItem> Profiles { get; } = new();

    public bool IsEmpty => Profiles.Count == 0;

    /// <summary>目前登入的帳號（或「沒有登入」之類的狀態文字）</summary>
    [ObservableProperty]
    private string _currentAccountText = "讀取中…";

    /// <summary>帳號下面的小字說明</summary>
    [ObservableProperty]
    private string _currentAccountHint = "";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = "";

    [ObservableProperty]
    private bool _hasBackup;

    [ObservableProperty]
    private string _backupText = "";

    /// <summary>視窗打開時呼叫</summary>
    public async Task InitializeAsync()
    {
        LoadProfiles();
        await RefreshAsync();
    }

    // ==================================================================
    // 按鈕
    // ==================================================================

    /// <summary>重新讀取目前登入的帳號</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            _current = await _sync.GetCurrentAccountAsync();
            CurrentAccountText = _current.RiotId;
            CurrentAccountHint = "匯出、套用、還原都會針對這個帳號。";
            IsLoggedIn = true;
        }
        catch (UserFacingException ex)
        {
            _current = null;
            CurrentAccountText = ex.Title;
            CurrentAccountHint = ex.Message;
            IsLoggedIn = false;
        }
        UpdateBackupInfo();
    }

    /// <summary>匯出目前帳號的設定</summary>
    [RelayCommand]
    private Task ExportAsync() => RunAsync(async () =>
    {
        if (!await EnsureLoggedInAsync()) return;
        var who = _current!;

        var name = _dialogs.PromptText("匯出設定",
            $"讀取「{who.RiotId}」的遊戲設定（鍵位、靈敏度、準星…），幫這份設定取個名字：",
            $"{who.GameName} 的設定");
        if (name is null) return;

        BusyText = "正在讀取遊戲設定…";
        IsBusy = true;
        var profile = await _sync.ExportAsync(name, who.Puuid!);
        IsBusy = false;

        LoadProfiles();
        _dialogs.ShowInfo("匯出完成",
            $"已把「{who.RiotId}」的設定存成「{profile.Name}」。\n\n" +
            "接下來切換到另一個帳號，再回到這裡按「套用」就可以了。\n" +
            "（小提醒：如果剛在遊戲裡改過設定，最好先回到大廳再匯出，確保是最新的。）");
    });

    /// <summary>把設定檔套用到目前登入的帳號</summary>
    [RelayCommand]
    private Task ApplyAsync(SettingsProfileItem item) => RunAsync(async () =>
    {
        if (!await EnsureLoggedInAsync()) return;
        var who = _current!;

        var sameAccount = string.Equals(item.Model.SourcePuuid, who.Puuid, StringComparison.OrdinalIgnoreCase);
        var message =
            $"要把設定檔「{item.Name}」\n（來自 {item.Model.SourceRiotId}，{TimeText.FormatWhen(item.Model.CreatedUtc)} 匯出）\n\n" +
            $"套用到目前登入的帳號「{who.RiotId}」嗎？\n\n" +
            $"・套用前會自動備份「{who.RiotId}」原本的設定，不喜歡可以按「還原」改回去。\n" +
            "・帳號自己的紀錄（例如看過的公告）會保留，不會被覆蓋。";
        if (sameAccount)
        {
            message += "\n・注意：這個設定檔就是從這個帳號匯出的。";
        }
        var inGame = _sync.IsValorantRunning();
        if (inGame)
        {
            message += "\n\nVALORANT 正在執行：會先關閉遊戲（不然遊戲會把舊設定寫回去），套用完成後自動重新開啟遊戲。";
        }

        if (!_dialogs.Confirm("套用遊戲設定", message, inGame ? "關閉遊戲、套用並重開" : "套用", "取消", warning: true))
        {
            return;
        }

        BusyText = inGame ? $"正在關閉遊戲並套用「{item.Name}」…" : $"正在把「{item.Name}」套用到 {who.RiotId}…";
        IsBusy = true;
        await WithRelaunchAsync(inGame, () => _sync.ApplyAsync(item.Model, who.Puuid!));
        var relaunched = inGame && await RelaunchAsync();
        IsBusy = false;
        Changed?.Invoke(new SettingsChange(who, item.Name, Restored: false, Relaunched: relaunched));

        UpdateBackupInfo();
        _dialogs.ShowInfo("套用成功", relaunched
            ? $"已把「{item.Name}」套用到「{who.RiotId}」，VALORANT 正在重新開啟，進到遊戲就是新的設定了。"
            : $"已把「{item.Name}」套用到「{who.RiotId}」。\n\n" +
              "請重新開啟 VALORANT（在主畫面按這個帳號的「啟動」），新的設定才會生效。\n" +
              "啟動時會再顯示一次「設定已套用」的提示。");
    });

    /// <summary>刪除設定檔</summary>
    [RelayCommand]
    private void Delete(SettingsProfileItem item)
    {
        if (!_dialogs.Confirm("刪除設定檔", $"確定要刪除設定檔「{item.Name}」嗎？\n（只會刪除本機存的這份設定，不會影響任何帳號。）", "刪除", "取消", danger: true))
        {
            return;
        }
        _sync.Store.DeleteProfile(item.Model.Id);
        LoadProfiles();
    }

    /// <summary>還原：把目前帳號改回「最近一次套用前」的設定</summary>
    [RelayCommand]
    private Task RestoreAsync() => RunAsync(async () =>
    {
        if (!await EnsureLoggedInAsync()) return;
        var who = _current!;
        var backup = _sync.Store.LatestBackup(who.Puuid);
        if (backup is null)
        {
            _dialogs.ShowInfo("沒有可以還原的備份", $"「{who.RiotId}」還沒有套用過設定檔，所以沒有備份可以還原。");
            return;
        }

        var message = $"要把「{who.RiotId}」的設定改回 {TimeText.FormatWhen(backup.CreatedUtc)} 的備份嗎？\n（{backup.Name}）";
        var inGame = _sync.IsValorantRunning();
        if (inGame) message += "\n\nVALORANT 正在執行：會先關閉遊戲，還原完成後自動重新開啟。";
        if (!_dialogs.Confirm("還原遊戲設定", message, inGame ? "關閉遊戲、還原並重開" : "還原", "取消", warning: true)) return;

        BusyText = "正在還原設定…";
        IsBusy = true;
        await WithRelaunchAsync(inGame, () => _sync.RestoreAsync(who.Puuid!));
        var relaunched = inGame && await RelaunchAsync();
        IsBusy = false;
        Changed?.Invoke(new SettingsChange(who, null, Restored: true, Relaunched: relaunched));

        _dialogs.ShowInfo("還原完成", relaunched
            ? $"「{who.RiotId}」的設定已經改回原本的樣子，VALORANT 正在重新開啟。"
            : $"「{who.RiotId}」的設定已經改回原本的樣子。\n請重新開啟 VALORANT，設定才會生效。");
    });

    // ==================================================================
    // 小工具
    // ==================================================================

    /// <summary>
    /// 執行套用 / 還原。如果原本在遊戲中、而且失敗了，也要把遊戲開回來
    /// （因為遊戲是我們關掉的），再把錯誤交給外層顯示。
    /// </summary>
    private async Task WithRelaunchAsync(bool inGame, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch when (inGame)
        {
            if (!_sync.IsValorantRunning())
            {
                BusyText = "發生錯誤，正在把遊戲開回來…";
                await _sync.RelaunchGameAsync();
            }
            throw;
        }
    }

    /// <summary>套用完成後重新開啟遊戲</summary>
    private async Task<bool> RelaunchAsync()
    {
        BusyText = "設定已套用，正在重新開啟 VALORANT…";
        return await _sync.RelaunchGameAsync();
    }

    /// <summary>確認 Riot Client 有開而且有登入；沒有的話顯示中文提示</summary>
    private async Task<bool> EnsureLoggedInAsync()
    {
        await RefreshAsync();
        if (IsLoggedIn) return true;
        _dialogs.ShowInfo(CurrentAccountText, CurrentAccountHint);
        return false;
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _sync.Store.ListProfiles()) Profiles.Add(new SettingsProfileItem(p));
    }

    private void UpdateBackupInfo()
    {
        var backup = _current is null ? null : _sync.Store.LatestBackup(_current.Puuid);
        HasBackup = backup is not null;
        BackupText = backup is null
            ? "這個帳號還沒有套用過設定檔，所以沒有備份。"
            : $"最近一次備份：{TimeText.FormatWhen(backup.CreatedUtc)}（{backup.Name}）";
    }

    /// <summary>執行一個動作，並把錯誤轉成中文訊息</summary>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (UserFacingException ex)
        {
            IsBusy = false;
            AppLog.Error(ex.Title, ex.InnerException ?? ex);
            _dialogs.ShowError(ex.Title, ex.Message);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            AppLog.Error("遊戲設定同步發生未預期的錯誤", ex);
            _dialogs.ShowError("發生未預期的錯誤", "很抱歉，發生了未預期的錯誤。\n\n詳細資訊：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>遊戲設定變更的通知：哪個帳號、套用了哪個設定檔（還原時是 null）</summary>
public sealed record SettingsChange(RiotIdInfo Account, string? ProfileName, bool Restored, bool Relaunched = false);

/// <summary>清單上的一個設定檔</summary>
public sealed class SettingsProfileItem
{
    public SettingsProfileItem(SettingsProfile model)
    {
        Model = model;
    }

    public SettingsProfile Model { get; }

    public string Name => Model.Name;

    public string Detail => $"來自 {Model.SourceRiotId} · {TimeText.FormatWhen(Model.CreatedUtc)}";
}
