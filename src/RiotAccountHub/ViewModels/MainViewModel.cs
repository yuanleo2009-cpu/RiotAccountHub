using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Storage;
using RiotAccountHub.Services;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 主畫面的 ViewModel：帳號卡片列表，以及所有按鈕背後的流程。
///
/// 每個按鈕對應一個 [RelayCommand] 方法，例如 LaunchAsync 會自動變成 LaunchCommand,
/// 畫面上的按鈕用 Command="{Binding LaunchCommand}" 連過來。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly AccountSwitcher _switcher;
    private readonly RiotClientService _riot;
    private readonly AccountRepository _accountRepo;
    private readonly SettingsRepository _settingsRepo;
    private readonly IDialogService _dialogs;
    private readonly PlayerCardCache? _cards;
    private readonly SettingsSync? _settingsSync;

    private AppSettings _settings = new();

    // 段位自動更新：每 15 秒看一次 VALORANT 有沒有在執行，有的話讀目前帳號的段位（只讀本機資料）
    private readonly DispatcherTimer _rankTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _rankCheckRunning;
    private DateTime _lastRankSaveUtc = DateTime.MinValue;

    public MainViewModel(
        AppPaths paths,
        AccountSwitcher switcher,
        RiotClientService riot,
        AccountRepository accountRepo,
        SettingsRepository settingsRepo,
        IDialogService dialogs,
        PlayerCardCache? cards = null,
        SettingsSync? settingsSync = null)
    {
        _paths = paths;
        _switcher = switcher;
        _riot = riot;
        _accountRepo = accountRepo;
        _settingsRepo = settingsRepo;
        _dialogs = dialogs;
        _cards = cards;
        _settingsSync = settingsSync;

        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    // ==================================================================
    // 畫面綁定用的屬性
    // ==================================================================

    /// <summary>帳號卡片列表</summary>
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = new();

    /// <summary>沒有任何帳號時顯示引導畫面</summary>
    public bool IsEmpty => Accounts.Count == 0;

    /// <summary>進行中的步驟（關閉中 → 切換中 → 啟動中）</summary>
    public ObservableCollection<BusyStepItem> BusySteps { get; } = new();

    /// <summary>是否正在執行切換等動作（會顯示進度畫面並鎖住按鈕）</summary>
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyTitle = "";

    [ObservableProperty]
    private string _busyDetail = "";

    /// <summary>進度畫面是否顯示「完成」</summary>
    [ObservableProperty]
    private bool _busyCompleted;

    /// <summary>有沒有原本狀態的備份（決定「還原原本狀態」按鈕能不能按）</summary>
    [ObservableProperty]
    private bool _hasOriginalBackup;

    /// <summary>按「啟動」後自動在 Riot Client 按「開始遊戲」（存在設定裡）</summary>
    [ObservableProperty]
    private bool _autoStartGame;

    partial void OnAutoStartGameChanged(bool value)
    {
        if (_settings.AutoStartGame == value) return;
        _settings.AutoStartGame = value;
        _settingsRepo.Save(_settings);
        AppLog.Info("自動開始遊戲：" + (value ? "開啟" : "關閉"));
    }

    /// <summary>畫面下方顯示的 Riot Client 路徑</summary>
    [ObservableProperty]
    private string _riotClientPathText = "";

    // ==================================================================
    // 初始化
    // ==================================================================

    /// <summary>主視窗顯示後呼叫：讀取設定與帳號清單</summary>
    public void Initialize()
    {
        _settings = _settingsRepo.Load();
        AutoStartGame = _settings.AutoStartGame;

        foreach (var account in _accountRepo.Load())
        {
            Accounts.Add(new AccountCardViewModel(account, _switcher.HasSnapshot(account.Id), _cards));
        }
        if (_accountRepo.LoadWarning is { } warning)
        {
            _dialogs.ShowError("帳號清單讀取失敗", warning);
        }

        RefreshStatus();

        // 開始自動更新段位（程式一打開就先檢查一次）
        _rankTimer.Tick += async (_, _) => await UpdateRankAsync();
        _rankTimer.Start();
        _ = UpdateRankAsync();

        // 之前下載失敗（例如當時沒網路）的卡面圖片，重新試一次
        foreach (var card in Accounts) _ = LoadCardArtAsync(card);
    }

    /// <summary>
    /// 下載這張卡片的卡面圖片（已經有了就不會再下載），完成後更新畫面。
    /// 下載失敗也沒關係，畫面會繼續顯示文字頭像。
    /// </summary>
    private async Task LoadCardArtAsync(AccountCardViewModel card)
    {
        if (_cards is null || !PlayerCardCache.IsValidId(card.Model.PlayerCardId)) return;
        if (await _cards.EnsureAsync(card.Model.PlayerCardId)) card.Refresh();
    }

    /// <summary>
    /// 讀取「目前正在玩的帳號」的段位，存到對應的卡片上。
    /// 只讀本機 Riot Client 的資料；VALORANT 沒開、讀不到、或帳號不在清單裡都會直接略過。
    /// </summary>
    private async Task UpdateRankAsync()
    {
        if (_rankCheckRunning || IsBusy) return;
        _rankCheckRunning = true;
        try
        {
            var current = await _switcher.ReadCurrentRankAsync();
            if (current is null) return;

            var card = Accounts.FirstOrDefault(a => IsSameAccount(a.Model, current.Who));
            if (card is null) return; // 正在玩的帳號不在清單裡

            var model = card.Model;
            var rank = current.Rank;
            var changed = model.Rank is null
                || model.Rank.CompetitiveTier != rank.CompetitiveTier
                || model.Rank.LeaderboardPosition != rank.LeaderboardPosition
                || model.Rank.AccountLevel != rank.AccountLevel
                || (model.Puuid is null && current.Who.Puuid is not null);

            model.Rank = new RankInfo
            {
                CompetitiveTier = rank.CompetitiveTier,
                LeaderboardPosition = rank.LeaderboardPosition,
                AccountLevel = rank.AccountLevel,
                UpdatedUtc = DateTime.UtcNow,
            };
            model.Puuid ??= current.Who.Puuid; // 舊帳號順便補上 puuid
            if (PlayerCardCache.IsValidId(rank.PlayerCardId) &&
                !string.Equals(model.PlayerCardId, rank.PlayerCardId, StringComparison.OrdinalIgnoreCase))
            {
                model.PlayerCardId = rank.PlayerCardId; // 在遊戲裡換了卡面
                changed = true;
                _ = LoadCardArtAsync(card);
            }
            card.Refresh();

            // 段位有變才馬上存檔；沒變的話最多 5 分鐘存一次「更新時間」
            if (changed || DateTime.UtcNow - _lastRankSaveUtc > TimeSpan.FromMinutes(5))
            {
                SaveAccounts();
                _lastRankSaveUtc = DateTime.UtcNow;
                if (changed) AppLog.Info($"段位更新：{RankNames.ToDisplay(rank.CompetitiveTier)}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("讀取段位失敗", ex); // 段位只是附加資訊，失敗不打擾使用者
        }
        finally
        {
            _rankCheckRunning = false;
        }
    }

    /// <summary>更新「還原」按鈕狀態、路徑文字、每張卡片的快照狀態</summary>
    private void RefreshStatus()
    {
        HasOriginalBackup = _switcher.HasOriginalBackup;
        var path = _riot.FindClientPath(_settings.RiotClientPath);
        RiotClientPathText = path ?? "找不到（按「變更」手動選擇）";
        foreach (var card in Accounts) card.HasSnapshot = _switcher.HasSnapshot(card.Model.Id);
    }

    // ==================================================================
    // 按鈕：啟動
    // ==================================================================

    /// <summary>切換到這個帳號並啟動 VALORANT</summary>
    [RelayCommand]
    private Task LaunchAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        if (!card.HasSnapshot)
        {
            _dialogs.ShowInfo("這個帳號還沒有登入狀態", $"「{card.RiotId}」還沒有儲存登入狀態。\n請先按這張卡片上的「重新登入」。");
            return;
        }

        var clientPath = EnsureClientPath();
        if (clientPath is null) return;

        // 快速路徑：Riot Client 已經登入這個帳號 → 不用關閉重開，直接開遊戲
        if (await _switcher.IsCurrentlyLoggedInAsync(card.Model))
        {
            var quick = BeginBusy($"啟動 {card.RiotId}", SwitchStep.Launching);
            var result = await _switcher.LaunchWithoutSwitchAsync(clientPath, quick);
            MarkUsed(card);
            if (result == LaunchResult.LaunchedWithoutSwitch) StartAutoStartGame();
            await ShowCompletedAsync(result == LaunchResult.AlreadyPlaying
                ? "你已經在玩這個帳號了，已切換到遊戲視窗。"
                : AutoStartGame ? "這個帳號已經登入，正在自動開始遊戲！" : "這個帳號已經登入，直接啟動 VALORANT！");
            return;
        }

        if (!ConfirmCloseGame()) return;

        var progress = BeginBusy($"切換到 {card.RiotId}", SwitchStep.Closing, SwitchStep.Switching, SwitchStep.Launching);
        await _switcher.SwitchAndLaunchAsync(card.Model.Id, clientPath, progress);
        MarkUsed(card);
        StartAutoStartGame();

        await ShowCompletedAsync(AutoStartGame
            ? "已啟動！Riot Client 登入完成後會自動開始遊戲。"
            : "已啟動！請在 Riot Client 按「開始遊戲」。");
    });

    // ==================================================================
    // 按鈕：新增帳號 / 重新登入
    // ==================================================================

    [RelayCommand]
    private Task AddAccountAsync() => RunGuardedAsync(() => RunLoginFlowAsync(existing: null));

    [RelayCommand]
    private Task ReloginAsync(AccountCardViewModel card) => RunGuardedAsync(() => RunLoginFlowAsync(existing: card));

    /// <summary>
    /// 新增帳號 / 重新登入的完整流程：
    ///   1. 關閉 Riot Client → 清空登入狀態（先備份）→ 開啟 Riot Client
    ///   2. 請使用者登入並勾選「保持登入」，按「我登入好了」
    ///   3. 關閉 Riot Client → 加密擷取登入狀態
    ///   4. 新增：填寫帳號資料；重新登入：直接更新快照
    /// 中途取消的話，會把開始前的登入狀態放回去。
    /// </summary>
    private async Task RunLoginFlowAsync(AccountCardViewModel? existing)
    {
        var clientPath = EnsureClientPath();
        if (clientPath is null) return;

        var intro = existing is null
            ? "接下來程式會：\n\n1. 關閉 Riot Client（和正在執行的遊戲）\n2. 暫時清空目前的登入狀態（會先備份）\n3. 打開 Riot Client 讓你登入新的帳號\n\n準備好了嗎？"
            : $"接下來程式會關閉 Riot Client，並打開登入畫面，讓你重新登入「{existing.RiotId}」。\n\n準備好了嗎？";
        if (!_dialogs.Confirm(existing is null ? "新增帳號" : "重新登入", intro, "開始", "取消")) return;

        // ---- 步驟 1 ----
        var progress = BeginBusy("準備登入畫面", SwitchStep.Closing, SwitchStep.Clearing, SwitchStep.Launching);
        var pending = await _switcher.BeginLoginAsync(clientPath, progress);
        RefreshStatus();
        EndBusy();

        // ---- 步驟 2：等使用者登入 ----
        var headline = existing is null ? "請在 Riot Client 登入要新增的帳號" : $"請在 Riot Client 登入「{existing.RiotId}」";
        ApiLoginStatus status;
        RiotIdInfo? detected;
        while (true)
        {
            if (!_dialogs.ShowLoginPrompt(headline))
            {
                await CancelLoginAsync(pending);
                return;
            }

            status = await _switcher.CheckLoginAsync();
            if (status.State == ApiLoginState.NotLoggedIn)
            {
                _dialogs.ShowInfo("還沒有偵測到登入", "Riot Client 目前顯示還沒有登入。\n請先在 Riot Client 完成登入，看到主畫面後再按「我登入好了」。");
                continue;
            }
            if (status.PersistLogin == false &&
                !_dialogs.Confirm(
                    "好像沒有勾選「保持登入」",
                    "Riot Client 回報這次登入「沒有」勾選保持登入，這樣存下來的快照之後無法自動登入。\n\n" +
                    "建議做法：在 Riot Client 右上角的個人選單登出，重新登入時記得勾選「保持登入」，再回來按「我登入好了」。",
                    "仍然儲存", "我去重新登入", warning: true))
            {
                continue;
            }

            // 自動抓 Riot ID（抓不到也沒關係，之後讓使用者手動輸入）
            BeginBusy("正在讀取帳號的 Riot ID…");
            detected = await _switcher.DetectRiotIdAsync();
            EndBusy();

            // 重新登入時，確認登入的是不是這張卡片的帳號
            if (existing is not null && detected is not null && !IsSameAccount(existing.Model, detected) &&
                !_dialogs.Confirm(
                    "登入的帳號不一樣",
                    $"你在 Riot Client 登入的是「{detected.RiotId}」，\n但這張卡片是「{existing.RiotId}」。\n\n" +
                    $"如果繼續，這張卡片會改成「{detected.RiotId}」。",
                    "改存成這個帳號", "我去換帳號登入", warning: true))
            {
                continue;
            }
            break;
        }

        // ---- 步驟 3：擷取登入狀態 ----
        progress = BeginBusy("儲存登入狀態", SwitchStep.Closing, SwitchStep.Saving);
        var captured = await _switcher.FinishLoginAsync(progress);
        EndBusy();

        if (captured.Persist != PersistState.Persisted && status.PersistLogin != true)
        {
            var detail = captured.Persist == PersistState.NotPersisted
                ? "登入檔案裡找不到「保持登入」的資料。這個快照之後很可能需要重新登入。"
                : "無法確認登入檔案裡有沒有「保持登入」的資料（可能是 Riot Client 更新了格式）。";
            if (!_dialogs.Confirm("保持登入檢查", detail + "\n\n要儲存這個快照嗎？", "仍然儲存", "放棄", warning: true))
            {
                await CancelLoginAsync(pending);
                return;
            }
        }

        // ---- 步驟 4：存檔 ----

        // 新增帳號時，如果這個帳號其實已經在清單裡，就問要不要直接更新它
        if (existing is null && detected is not null)
        {
            var duplicate = Accounts.FirstOrDefault(a => IsSameAccount(a.Model, detected));
            if (duplicate is not null &&
                _dialogs.Confirm("這個帳號已經存在",
                    $"清單裡已經有「{duplicate.RiotId}」了。\n\n要直接更新它的登入狀態嗎？",
                    "更新它", "另外新增一張"))
            {
                existing = duplicate;
            }
        }

        if (existing is not null)
        {
            _switcher.SaveSnapshot(existing.Model.Id, captured);
            existing.Model.SnapshotUpdatedUtc = DateTime.UtcNow;
            if (detected is not null)
            {
                // 順便更新名稱（例如在 Riot 改過 Riot ID）
                existing.Model.GameName = detected.GameName;
                existing.Model.TagLine = detected.TagLine;
                existing.Model.Puuid = detected.Puuid ?? existing.Model.Puuid;
            }
            SaveAccounts();
            RefreshStatus();
            existing.Refresh();
            _dialogs.ShowInfo("重新登入完成", $"「{existing.RiotId}」的登入狀態已經更新。\n之後按「啟動」就會用這個帳號登入。");
            return;
        }

        var editor = AccountEditViewModel.ForNew(detected);
        while (!_dialogs.ShowAccountEditor(editor))
        {
            if (_dialogs.Confirm("放棄這個帳號？", "確定不要儲存這個帳號嗎？\n剛剛的登入狀態會被丟棄，並放回開始前的登入狀態。", "放棄", "繼續填寫", danger: true))
            {
                await CancelLoginAsync(pending);
                return;
            }
        }

        var account = new Account { SnapshotUpdatedUtc = DateTime.UtcNow, Puuid = detected?.Puuid };
        editor.ApplyTo(account);
        _switcher.SaveSnapshot(account.Id, captured);
        Accounts.Add(new AccountCardViewModel(account, hasSnapshot: true, _cards));
        SaveAccounts();
        RefreshStatus();
        _dialogs.ShowInfo("新增完成", $"已新增「{account.RiotId}」！\n之後在卡片上按「啟動」就能直接切換到這個帳號。");
    }

    /// <summary>
    /// 判斷是不是同一個帳號：兩邊都有 puuid 就比 puuid（改名也認得出來），
    /// 不然就比 Riot ID（不分大小寫）。
    /// </summary>
    private static bool IsSameAccount(Account account, RiotIdInfo detected) => account.IsSameAccount(detected);

    /// <summary>取消登入流程：放回開始前的狀態</summary>
    private async Task CancelLoginAsync(PendingLogin pending)
    {
        var progress = BeginBusy("取消並還原", SwitchStep.Closing, SwitchStep.Switching);
        await _switcher.CancelLoginAsync(pending, progress);
        EndBusy();
    }

    // ==================================================================
    // 按鈕：編輯、刪除
    // ==================================================================

    [RelayCommand]
    private void Edit(AccountCardViewModel card)
    {
        var editor = AccountEditViewModel.ForEdit(card.Model);
        if (!_dialogs.ShowAccountEditor(editor)) return;

        editor.ApplyTo(card.Model);
        SaveAccounts();
        card.Refresh();
    }

    [RelayCommand]
    private void Delete(AccountCardViewModel card)
    {
        var ok = _dialogs.Confirm(
            "刪除帳號",
            $"確定要刪除「{card.RiotId}」嗎？\n\n這個帳號在本程式裡的登入快照也會一起刪除，之後要再使用必須重新新增。\n（這不會影響你的 Riot 帳號本身。）",
            "刪除", "取消", danger: true);
        if (!ok) return;

        try
        {
            _switcher.DeleteSnapshot(card.Model.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("刪除失敗", "無法刪除快照檔案，請稍後再試。\n\n詳細資訊：" + ex.Message);
            return;
        }
        Accounts.Remove(card);
        SaveAccounts();
    }

    // ==================================================================
    // 按鈕：還原原本狀態
    // ==================================================================

    [RelayCommand]
    private Task RestoreOriginalAsync() => RunGuardedAsync(async () =>
    {
        var ok = _dialogs.Confirm(
            "還原原本狀態",
            "這會關閉 Riot Client（和正在執行的遊戲），並把登入狀態還原成你「第一次使用本程式之前」的樣子。\n\n你在本程式裡存的帳號不會被刪除。",
            "還原", "取消", warning: true);
        if (!ok) return;

        var progress = BeginBusy("還原原本狀態", SwitchStep.Closing, SwitchStep.Switching);
        await _switcher.RestoreOriginalAsync(progress);
        await ShowCompletedAsync("已還原！下次打開 Riot Client 就會是原本的登入狀態。");
    });

    // ==================================================================
    // 按鈕：Riot Client 路徑、開啟資料夾
    // ==================================================================

    /// <summary>打開「同步遊戲設定」視窗</summary>
    [RelayCommand]
    private void OpenSettingsSync()
    {
        if (_settingsSync is null) return;
        _dialogs.ShowSettingsSync(new SettingsSyncViewModel(_settingsSync, _dialogs));
    }

    [RelayCommand]
    private void ChangeRiotClientPath() => PickClientPath();

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _paths.DataRoot, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("無法開啟資料夾", ex.Message);
        }
    }

    // ==================================================================
    // 小工具
    // ==================================================================

    /// <summary>取得 Riot Client 路徑；找不到就請使用者選</summary>
    private string? EnsureClientPath()
    {
        var path = _riot.FindClientPath(_settings.RiotClientPath);
        if (path is not null) return path;

        _dialogs.ShowInfo("找不到 Riot Client",
            "程式找不到 Riot Client 的安裝位置。\n\n接下來請選擇 RiotClientServices.exe（通常在 C:\\Riot Games\\Riot Client\\ 裡面）。");
        return PickClientPath();
    }

    private string? PickClientPath()
    {
        var current = _riot.FindClientPath(_settings.RiotClientPath);
        var picked = _dialogs.PickRiotClientExe(current is null ? null : Path.GetDirectoryName(current));
        if (picked is null) return null;

        if (!string.Equals(Path.GetFileName(picked), "RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) &&
            !_dialogs.Confirm("檔案名稱不太對", $"你選的是「{Path.GetFileName(picked)}」，通常應該選「RiotClientServices.exe」。\n\n確定要用這個檔案嗎？", "確定使用", "重新選擇", warning: true))
        {
            return null;
        }

        _settings.RiotClientPath = picked;
        _settingsRepo.Save(_settings);
        RefreshStatus();
        return picked;
    }

    /// <summary>遊戲正在執行的話，先問使用者能不能關掉</summary>
    private bool ConfirmCloseGame()
    {
        if (!_switcher.IsGameRunning()) return true;
        return _dialogs.Confirm("遊戲正在執行", "切換帳號需要關閉目前正在執行的遊戲。\n\n確定要關閉遊戲並切換嗎？", "關閉並切換", "取消", warning: true);
    }

    /// <summary>
    /// 如果有開「自動開始遊戲」，在背景等 Riot Client 登入完成後幫忙按「開始遊戲」（最多等 90 秒）。
    /// 失敗也不會跳錯誤，只是需要使用者自己在 Riot 介面按一下。
    /// </summary>
    private void StartAutoStartGame()
    {
        if (!AutoStartGame) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _switcher.AutoStartGameAsync(TimeSpan.FromSeconds(90));
            }
            catch (Exception ex)
            {
                AppLog.Error("自動開始遊戲失敗", ex);
            }
        });
    }

    /// <summary>記錄「上次使用時間」並存檔</summary>
    private void MarkUsed(AccountCardViewModel card)
    {
        card.Model.LastUsedUtc = DateTime.UtcNow;
        SaveAccounts();
        card.Refresh();
    }

    private void SaveAccounts() => _accountRepo.Save(Accounts.Select(a => a.Model));

    /// <summary>
    /// 執行一個流程，並統一處理錯誤：
    ///   - UserFacingException：直接顯示中文錯誤訊息
    ///   - 其他錯誤：記錄到 log 並顯示通用訊息
    /// 不管成功或失敗，最後都會關掉進度畫面。
    /// </summary>
    private async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (UserFacingException ex)
        {
            AppLog.Error(ex.Title, ex.InnerException ?? ex);
            EndBusy();
            _dialogs.ShowError(ex.Title, ex.Message);
        }
        catch (Exception ex)
        {
            AppLog.Error("未預期的錯誤", ex);
            EndBusy();
            _dialogs.ShowError("發生未預期的錯誤",
                "很抱歉，發生了未預期的錯誤。\n\n詳細資訊：" + ex.Message +
                "\n\n記錄檔位置：\n" + Path.Combine(_paths.LogsDir, "app.log"));
        }
        finally
        {
            EndBusy();
            RefreshStatus();
        }
    }

    // ---- 進度畫面 ----

    private IProgress<SwitchStep> BeginBusy(string title, params SwitchStep[] steps)
    {
        BusyTitle = title;
        BusyDetail = "";
        BusyCompleted = false;
        BusySteps.Clear();
        foreach (var s in steps) BusySteps.Add(new BusyStepItem(s, StepLabel(s)));
        IsBusy = true;

        // Progress<T> 會自動切回畫面執行緒更新，所以背景工作也可以安全回報進度
        return new Progress<SwitchStep>(step =>
        {
            var reached = false;
            foreach (var item in BusySteps)
            {
                if (item.Step == step)
                {
                    item.State = StepState.Active;
                    reached = true;
                }
                else
                {
                    item.State = reached ? StepState.Pending : StepState.Done;
                }
            }
            BusyDetail = StepDetail(step);
        });
    }

    private async Task ShowCompletedAsync(string message)
    {
        foreach (var item in BusySteps) item.State = StepState.Done;
        BusyCompleted = true;
        BusyDetail = message;
        await Task.Delay(900); // 讓使用者看一眼「完成」就收起來
    }

    private void EndBusy()
    {
        IsBusy = false;
        BusyCompleted = false;
    }

    private static string StepLabel(SwitchStep step) => step switch
    {
        SwitchStep.Closing => "關閉中",
        SwitchStep.Clearing => "清除中",
        SwitchStep.Switching => "切換中",
        SwitchStep.Launching => "啟動中",
        SwitchStep.Saving => "儲存中",
        _ => step.ToString(),
    };

    private static string StepDetail(SwitchStep step) => step switch
    {
        SwitchStep.Closing => "正在關閉 Riot Client 與遊戲…",
        SwitchStep.Clearing => "正在清空目前的登入狀態（已先備份）…",
        SwitchStep.Switching => "正在放入登入狀態…",
        SwitchStep.Launching => "正在開啟 Riot Client…",
        SwitchStep.Saving => "正在加密儲存登入狀態…",
        _ => "",
    };
}
