using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.Crosshairs;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Startup;
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
    private readonly StartupRegistration _startup;

    private AppSettings _settings = new();

    // 段位自動更新：每 15 秒看一次 VALORANT 有沒有在執行，有的話讀目前帳號的段位（只讀本機資料）
    // 每 5 秒更新一次「哪個帳號正在使用」；每 15 秒（第 3 次）順便更新段位
    private readonly DispatcherTimer _rankTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private int _timerTicks;
    private bool _activityCheckRunning;

    // 剛按了「啟動」的帳號：在遊戲真的開起來之前，卡片顯示「啟動中」（最多 3 分鐘）
    private (string AccountId, DateTime Utc)? _pendingLaunch;
    private static readonly TimeSpan LaunchPendingTime = TimeSpan.FromMinutes(3);
    private bool _rankCheckRunning;
    private DateTime _lastRankSaveUtc = DateTime.MinValue;

    // 目前正在等待的「自動開始遊戲」（同一時間只留一個）
    private CancellationTokenSource? _autoStartCts;

    public MainViewModel(
        AppPaths paths,
        AccountSwitcher switcher,
        RiotClientService riot,
        AccountRepository accountRepo,
        SettingsRepository settingsRepo,
        IDialogService dialogs,
        PlayerCardCache? cards = null,
        SettingsSync? settingsSync = null,
        StartupRegistration? startup = null)
    {
        _startup = startup ?? new StartupRegistration();
        _paths = paths;
        _switcher = switcher;
        _riot = riot;
        _accountRepo = accountRepo;
        _settingsRepo = settingsRepo;
        _dialogs = dialogs;
        _cards = cards;
        _settingsSync = settingsSync;

        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AccountSummary));
        };
    }

    // ==================================================================
    // 畫面綁定用的屬性
    // ==================================================================

    /// <summary>帳號卡片列表</summary>
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = new();

    /// <summary>沒有任何帳號時顯示引導畫面</summary>
    public bool IsEmpty => Accounts.Count == 0;

    /// <summary>頁面標題下方的小字，例如「2 個帳號 · 按「啟動」就會切換並開始遊戲」</summary>
    public string AccountSummary => Accounts.Count == 0
        ? "還沒有帳號"
        : $"{Accounts.Count} 個帳號 · 按「啟動」就會切換到那個帳號並開啟遊戲";

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

    /// <summary>進度畫面上額外的綠色提示（例如「已套用遊戲設定」）；空字串就不顯示</summary>
    [ObservableProperty]
    private string _busyNotice = "";

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

    /// <summary>開機（登入 Windows）時自動開啟本程式</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    // 程式剛開啟、正在讀取目前設定時，不要把「讀到的值」又寫回登錄檔
    private bool _loadingStartupSetting;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loadingStartupSetting) return;
        try
        {
            if (value)
            {
                _startup.Enable(Environment.ProcessPath ?? throw new IOException("找不到程式本身的路徑"));
            }
            else
            {
                _startup.Disable();
            }
            AppLog.Info("開機時自動啟動：" + (value ? "開啟" : "關閉"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Error("無法變更開機自動啟動", ex);
            _dialogs.ShowError("無法變更開機自動啟動",
                "Windows 不允許本程式變更這個設定（可能被防毒軟體或公司的電腦政策擋住）。\n\n" +
                "你也可以到「工作管理員 → 開機應用程式」手動調整。");

            // 把開關撥回原本的位置
            _loadingStartupSetting = true;
            StartWithWindows = !value;
            _loadingStartupSetting = false;
        }
    }

    /// <summary>讀取目前的開機啟動設定（以 Windows 登錄檔為準）</summary>
    private void LoadStartupSetting()
    {
        try
        {
            if (Environment.ProcessPath is { } exe) _startup.RepairPath(exe);
            _loadingStartupSetting = true;
            StartWithWindows = _startup.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Error("讀取開機自動啟動設定失敗", ex);
        }
        finally
        {
            _loadingStartupSetting = false;
        }
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
        LoadStartupSetting();

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
        _rankTimer.Tick += async (_, _) =>
        {
            await UpdateActivityAsync();
            if (++_timerTicks % 3 == 0) await UpdateRankAsync();
        };
        _rankTimer.Start();
        _ = UpdateActivityAsync();
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
            MaybeCaptureSettings(card);

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
        RefreshSettingsFlags();
    }

    // ==================================================================
    // 遊戲設定：主帳號、複製 / 套用、還原（全部在首頁完成）
    // ==================================================================

    /// <summary>目前的主帳號卡片；沒設定就是 null</summary>
    private AccountCardViewModel? MainCard =>
        _settings.MainAccountId is { } id ? Accounts.FirstOrDefault(a => a.Model.Id == id) : null;

    /// <summary>目前「複製」的來源卡片；沒有複製就是 null</summary>
    private AccountCardViewModel? CopiedCard =>
        _settings.CopiedSettingsAccountId is { } id ? Accounts.FirstOrDefault(a => a.Model.Id == id) : null;

    /// <summary>首頁上方的「已複製」橫幅要不要顯示</summary>
    [ObservableProperty]
    private bool _hasCopiedSettings;

    /// <summary>橫幅文字，例如「已複製 夜行者#TW1 的遊戲設定（記錄於 今天 14:30）」</summary>
    [ObservableProperty]
    private string _copiedSettingsText = "";

    // 正在背景記錄設定的帳號（避免同一個帳號同時記兩次）
    private readonly HashSet<string> _capturing = new();

    /// <summary>某個帳號自動記下的設定（主帳號沿用舊版資料當備用）</summary>
    private SettingsProfile? ProfileOf(AccountCardViewModel card)
    {
        if (_settingsSync is null) return null;
        var profile = _settingsSync.Store.AccountProfile(card.Model.Id);
        if (profile is null && card == MainCard) profile = _settingsSync.Store.LegacyMainProfile();
        return profile;
    }

    /// <summary>更新每張卡片的主帳號 / 套用 / 還原按鈕，以及上方的「已複製」橫幅</summary>
    private void RefreshSettingsFlags()
    {
        var main = MainCard;
        var copied = CopiedCard;
        foreach (var card in Accounts)
        {
            card.IsMain = card == main;
            card.CanApplyMain = _settingsSync is not null && main is not null && card != main;
            card.IsCopySource = card == copied;
            card.CanApplyCopied = _settingsSync is not null && copied is not null && card != copied;
            card.HasSettingsBackup = _settingsSync?.Store.LatestBackup(card.Model.Puuid) is not null;
        }

        var profile = copied is null ? null : ProfileOf(copied);
        HasCopiedSettings = copied is not null && profile is not null;
        CopiedSettingsText = HasCopiedSettings
            ? $"已複製「{copied!.RiotId}」的遊戲設定（記錄於 {TimeText.FormatWhen(profile!.CreatedUtc)}）· 到其他帳號按「套用設定」就能用"
            : "";
    }

    /// <summary>
    /// 玩某個帳號時，自動記下它的遊戲設定（每個帳號最多 30 分鐘記一次）。
    /// 這樣「複製設定」通常不用切換帳號，按一下就好。失敗只寫記錄，不打擾使用者。
    /// </summary>
    private void MaybeCaptureSettings(AccountCardViewModel card)
    {
        if (_settingsSync is null || card.Model.Puuid is null || _capturing.Contains(card.Model.Id)) return;
        var existing = ProfileOf(card);
        var fresh = existing is not null
            && string.Equals(existing.SourcePuuid, card.Model.Puuid, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow - existing.CreatedUtc < TimeSpan.FromMinutes(30);
        if (fresh) return;

        var accountId = card.Model.Id;
        var puuid = card.Model.Puuid;
        var sync = _settingsSync;
        _capturing.Add(accountId);
        _ = Task.Run(async () =>
        {
            try
            {
                await sync.CaptureAccountAsync(accountId, puuid);
            }
            catch (Exception ex)
            {
                AppLog.Error("自動記下遊戲設定失敗", ex);
            }
        }).ContinueWith(_ =>
        {
            _capturing.Remove(accountId);
            RefreshSettingsFlags(); // 橫幅上的「記錄於」時間跟著更新
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// 在某個帳號上做一件需要「登入那個帳號」的事（讀取 / 寫入遊戲設定）：
    ///   已經登入 → 直接做；沒登入 → 切換過去（只開 Riot Client）→ 等自動登入 → 確認帳號 → 做
    ///   startGame = true 的話，做完再開始遊戲。
    /// 回傳目前登入的帳號；使用者取消或找不到 Riot Client 時回傳 null。
    /// </summary>
    private async Task<RiotIdInfo?> RunOnAccountAsync(
        AccountCardViewModel card, string title, SwitchStep actionStep, bool startGame, Func<RiotIdInfo, Task> action)
    {
        if (_settingsSync is null) return null;
        var clientPath = EnsureClientPath();
        if (clientPath is null) return null;

        var loggedIn = await _switcher.IsCurrentlyLoggedInAsync(card.Model);
        var steps = new List<SwitchStep>();
        if (!loggedIn) steps.AddRange(new[] { SwitchStep.Closing, SwitchStep.Switching, SwitchStep.LoggingIn });
        steps.Add(actionStep);
        if (startGame) steps.Add(SwitchStep.Launching);
        var progress = BeginBusy(title, steps.ToArray());

        if (!loggedIn)
        {
            await _switcher.SwitchAndStartClientAsync(card.Model.Id, clientPath, progress);
            progress.Report(SwitchStep.LoggingIn);
            if (!await _switcher.WaitForLoginAsync(TimeSpan.FromSeconds(60)))
            {
                throw new UserFacingException("沒有自動登入",
                    $"Riot Client 打開了，但「{card.RiotId}」沒有自動登入（登入狀態可能過期了）。\n\n" +
                    "請對這張卡片按「重新登入」更新登入狀態後再試。沒有修改任何遊戲設定。");
            }
        }

        // 再確認一次登入的是這個帳號
        var current = await _settingsSync.GetCurrentAccountAsync();
        if (!card.Model.IsSameAccount(current))
        {
            throw new UserFacingException("登入的帳號不一樣",
                $"Riot Client 現在登入的是「{current.RiotId}」，不是「{card.RiotId}」。為了安全，沒有做任何修改。");
        }
        card.Model.Puuid ??= current.Puuid;

        progress.Report(actionStep);
        await action(current);

        if (startGame)
        {
            progress.Report(SwitchStep.Launching);
            await _switcher.AutoStartGameAsync(TimeSpan.FromSeconds(60));
            MarkUsed(card);
        }
        return current;
    }

    // ---------------- 主帳號 ----------------

    /// <summary>設為主帳號 / 取消主帳號</summary>
    [RelayCommand]
    private Task ToggleMainAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        if (card.IsMain)
        {
            _settings.MainAccountId = null;
            _settingsRepo.Save(_settings);
            RefreshSettingsFlags();
            return;
        }

        _settings.MainAccountId = card.Model.Id;
        _settingsRepo.Save(_settings);
        RefreshSettingsFlags();

        var hint = ProfileOf(card) is null
            ? "下一步：對主帳號按一次「啟動」，進到遊戲大廳後，程式會在約 15 秒內自動記下它的設定。"
            : "主帳號的設定已經記錄過了，可以直接到其他帳號按「套用主帳號」。";
        _dialogs.ShowInfo("已設為主帳號",
            $"「{card.RiotId}」已經設為主帳號。\n\n{hint}\n之後每次玩主帳號，程式都會自動更新記下的設定。");
        await Task.CompletedTask;
    });

    /// <summary>一鍵套用主帳號設定</summary>
    [RelayCommand]
    private Task ApplyMainSettingsAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        var main = MainCard;
        if (main is null) return;
        var profile = ProfileOf(main);
        if (profile is null)
        {
            _dialogs.ShowInfo("還沒有主帳號的設定",
                $"程式還沒記下主帳號「{main.RiotId}」的遊戲設定。\n\n" +
                "請先對主帳號按一次「啟動」，進到遊戲大廳後，程式會在約 15 秒內自動記下。之後就能用這個按鈕了。");
            return;
        }
        await ApplyProfileAsync(card, profile, $"主帳號「{main.RiotId}」");
    });

    // ---------------- 複製 / 套用 ----------------

    /// <summary>
    /// 複製這個帳號的遊戲設定。
    ///   這個帳號正登入著 → 讀取最新的設定
    ///   有自動記錄過 → 直接用記錄（瞬間完成）
    ///   都沒有 → 問要不要切換過去讀取
    /// </summary>
    [RelayCommand]
    private Task CopySettingsAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        if (_settingsSync is null) return;

        if (await _switcher.IsCurrentlyLoggedInAsync(card.Model))
        {
            await RunOnAccountAsync(card, $"複製 {card.RiotId} 的設定", SwitchStep.ReadingSettings, startGame: false,
                current => _settingsSync.CaptureAccountAsync(card.Model.Id, current.Puuid!));
        }
        else if (ProfileOf(card) is null)
        {
            var ok = _dialogs.Confirm("還沒有這個帳號的設定紀錄",
                $"程式還沒記下「{card.RiotId}」的遊戲設定。\n\n" +
                "要現在切換到這個帳號讀取設定嗎？（會關閉目前的 Riot Client 和遊戲，讀完不會自動開遊戲）\n\n" +
                "也可以按「取消」，之後用這個帳號玩一次，程式會自動記下。",
                "切換並讀取", "取消");
            if (!ok) return;
            await RunOnAccountAsync(card, $"複製 {card.RiotId} 的設定", SwitchStep.ReadingSettings, startGame: false,
                current => _settingsSync.CaptureAccountAsync(card.Model.Id, current.Puuid!));
        }

        _settings.CopiedSettingsAccountId = card.Model.Id;
        _settingsRepo.Save(_settings);
        RefreshSettingsFlags();
    });

    /// <summary>清除「已複製」</summary>
    [RelayCommand]
    private void ClearCopiedSettings()
    {
        _settings.CopiedSettingsAccountId = null;
        _settingsRepo.Save(_settings);
        RefreshSettingsFlags();
    }

    /// <summary>把複製的設定套用到這個帳號</summary>
    [RelayCommand]
    private Task ApplyCopiedSettingsAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        var source = CopiedCard;
        var profile = source is null ? null : ProfileOf(source);
        if (source is null || profile is null) return;
        await ApplyProfileAsync(card, profile, $"「{source.RiotId}」");
    });

    /// <summary>把某份設定套用到這個帳號，然後開始遊戲（主帳號、複製共用）</summary>
    private async Task ApplyProfileAsync(AccountCardViewModel card, SettingsProfile profile, string sourceLabel)
    {
        if (!card.HasSnapshot)
        {
            _dialogs.ShowInfo("這個帳號還沒有登入狀態", $"「{card.RiotId}」還沒有儲存登入狀態。\n請先按這張卡片上的「重新登入」。");
            return;
        }

        var message =
            $"要把{sourceLabel}的遊戲設定（鍵位、靈敏度、準星…，記錄於 {TimeText.FormatWhen(profile.CreatedUtc)}）\n" +
            $"套用到「{card.RiotId}」，然後開始遊戲嗎？\n\n" +
            $"・套用前會備份「{card.RiotId}」原本的設定，卡片上可以按「還原設定」改回去。\n" +
            "・帳號自己的紀錄（例如看過的公告）會保留。";
        if (_switcher.IsGameRunning()) message += "\n・會先關閉目前正在執行的遊戲。";
        if (!_dialogs.Confirm("套用遊戲設定", message, "套用並開始遊戲", "取消", warning: true)) return;

        var current = await RunOnAccountAsync(card, $"{card.RiotId}：套用設定", SwitchStep.ApplyingSettings, startGame: true,
            who => _settingsSync!.ApplyAsync(profile, who.Puuid!));
        if (current is null) return;

        OnGameSettingsChanged(new SettingsChange(current, profile.Name, Restored: false, Relaunched: true));
        BusyNotice = $"已套用{sourceLabel}的設定，進到遊戲就是新的鍵位、靈敏度和準星";
        await ShowCompletedAsync("完成！VALORANT 正在開啟。");
        await Task.Delay(2000); // 讓使用者看清楚綠色提示
    }

    /// <summary>還原：把這個帳號改回最近一次套用前的設定，然後開始遊戲</summary>
    [RelayCommand]
    private Task RestoreSettingsAsync(AccountCardViewModel card) => RunGuardedAsync(async () =>
    {
        var backup = _settingsSync?.Store.LatestBackup(card.Model.Puuid);
        if (backup is null)
        {
            _dialogs.ShowInfo("沒有可以還原的備份", $"「{card.RiotId}」還沒有套用過設定，所以沒有備份可以還原。");
            return;
        }

        var message = $"要把「{card.RiotId}」的遊戲設定改回 {TimeText.FormatWhen(backup.CreatedUtc)} 的備份，然後開始遊戲嗎？\n（{backup.Name}）";
        if (_switcher.IsGameRunning()) message += "\n\n會先關閉目前正在執行的遊戲。";
        if (!_dialogs.Confirm("還原遊戲設定", message, "還原並開始遊戲", "取消", warning: true)) return;

        var current = await RunOnAccountAsync(card, $"{card.RiotId}：還原設定", SwitchStep.ApplyingSettings, startGame: true,
            who => _settingsSync!.RestoreAsync(who.Puuid!));
        if (current is null) return;

        OnGameSettingsChanged(new SettingsChange(current, null, Restored: true, Relaunched: true));
        BusyNotice = "已還原成套用前的遊戲設定";
        await ShowCompletedAsync("完成！VALORANT 正在開啟。");
        await Task.Delay(2000);
    });

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
                : AutoStartGame ? "這個帳號已經登入，正在自動開始遊戲！" : "這個帳號已經登入，直接啟動 VALORANT！", card);
            return;
        }

        if (!ConfirmCloseGame()) return;

        var progress = BeginBusy($"切換到 {card.RiotId}", SwitchStep.Closing, SwitchStep.Switching, SwitchStep.Launching);
        await _switcher.SwitchAndLaunchAsync(card.Model.Id, clientPath, progress);
        MarkUsed(card);
        StartAutoStartGame();

        await ShowCompletedAsync(AutoStartGame
            ? "已啟動！Riot Client 登入完成後會自動開始遊戲。"
            : "已啟動！請在 Riot Client 按「開始遊戲」。", card);
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
        // 刪掉的帳號如果是主帳號 / 複製來源，一起清掉；它自動記下的設定也刪掉
        if (_settings.MainAccountId == card.Model.Id) _settings.MainAccountId = null;
        if (_settings.CopiedSettingsAccountId == card.Model.Id) _settings.CopiedSettingsAccountId = null;
        _settingsRepo.Save(_settings);
        _settingsSync?.Store.DeleteProfile(SettingsProfileStore.AccountProfileId(card.Model.Id));
        Accounts.Remove(card);
        RefreshSettingsFlags();
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

    /// <summary>
    /// 遊戲設定套用 / 還原成功：記在對應帳號的卡片上，
    /// 下次對這個帳號按「啟動」時會顯示「設定已套用」的提示。
    /// </summary>
    private void OnGameSettingsChanged(SettingsChange change)
    {
        var card = Accounts.FirstOrDefault(a => IsSameAccount(a.Model, change.Account));
        if (card is null) return; // 這個帳號不在清單裡

        if (change.Restored)
        {
            card.Model.AppliedSettingsName = null;
            card.Model.AppliedSettingsUtc = null;
            card.Model.PendingLaunchNotice = "已還原成套用前的遊戲設定，這次進遊戲就會生效";
        }
        else
        {
            card.Model.AppliedSettingsName = change.ProfileName;
            card.Model.AppliedSettingsUtc = DateTime.UtcNow;
            card.Model.PendingLaunchNotice = $"已套用遊戲設定「{change.ProfileName}」，這次進遊戲就會生效";
        }

        // 已經自動重開遊戲了 → 設定已經生效，下次啟動不用再提示
        if (change.Relaunched) card.Model.PendingLaunchNotice = null;
        SaveAccounts();
        card.Refresh();
        RefreshSettingsFlags();
    }

    /// <summary>
    /// 「熱門準星」：直接用內建瀏覽器打開 VCRDB 網站（工具列上可以打開「我的準星」）。
    /// 只做瀏覽與複製代碼，不會修改遊戲設定。
    /// </summary>
    [RelayCommand]
    private void OpenCrosshairs()
    {
        var vm = new CrosshairsViewModel(new CrosshairLibrary(_paths.CrosshairsFile), _dialogs);
        vm.OpenVcrdbFromMain();
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

        // 同一時間只留一個「自動開始遊戲」在等待（例如連按兩次啟動時，取消前一個）
        _autoStartCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoStartCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await _switcher.AutoStartGameAsync(TimeSpan.FromSeconds(90), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 被新的啟動取代了，正常
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

        // 卡片馬上顯示「啟動中」，之後由 UpdateActivityAsync 換成「遊戲中」
        _pendingLaunch = (card.Model.Id, DateTime.UtcNow);
        foreach (var other in Accounts)
        {
            other.Activity = other == card ? AccountActivity.Launching : AccountActivity.None;
        }
        _ = UpdateActivityAsync();
    }

    /// <summary>
    /// 更新每張卡片的狀態標籤（啟動中 / 遊戲中 / 已登入）。
    /// 只讀本機 Riot Client 的資料，而且在背景執行緒做，不會卡住畫面。
    /// </summary>
    private async Task UpdateActivityAsync()
    {
        if (_activityCheckRunning || IsBusy) return;
        _activityCheckRunning = true;
        try
        {
            var active = await _switcher.ReadActiveAccountAsync();
            if (IsBusy) return; // 等待期間開始切換帳號了，這次的結果不要用

            if (_pendingLaunch is { } pending && DateTime.UtcNow - pending.Utc > LaunchPendingTime)
            {
                _pendingLaunch = null; // 等太久了（例如使用者把 Riot Client 關掉），不再顯示啟動中
            }

            foreach (var card in Accounts)
            {
                var isCurrent = active is not null && IsSameAccount(card.Model, active.Who);
                if (isCurrent && active!.InGame)
                {
                    card.Activity = AccountActivity.InGame;
                    if (_pendingLaunch?.AccountId == card.Model.Id) _pendingLaunch = null;
                }
                else if (_pendingLaunch?.AccountId == card.Model.Id)
                {
                    card.Activity = AccountActivity.Launching;
                }
                else
                {
                    card.Activity = isCurrent ? AccountActivity.LoggedIn : AccountActivity.None;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("更新帳號狀態失敗", ex); // 狀態只是附加資訊，失敗不打擾使用者
        }
        finally
        {
            _activityCheckRunning = false;
        }
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

    private async Task ShowCompletedAsync(string message, AccountCardViewModel? card = null)
    {
        foreach (var item in BusySteps) item.State = StepState.Done;
        BusyCompleted = true;
        BusyDetail = message;

        // 這個帳號剛套用過遊戲設定 → 這次啟動時顯示「設定已套用」，顯示後就清掉
        var notice = card?.Model.PendingLaunchNotice;
        if (!string.IsNullOrEmpty(notice))
        {
            BusyNotice = notice;
            card!.Model.PendingLaunchNotice = null;
            SaveAccounts();
            await Task.Delay(3000); // 有提示時多停一下，讓使用者看清楚
        }
        else
        {
            await Task.Delay(900); // 讓使用者看一眼「完成」就收起來
        }
    }

    private void EndBusy()
    {
        IsBusy = false;
        BusyCompleted = false;
        BusyNotice = "";
    }

    private static string StepLabel(SwitchStep step) => step switch
    {
        SwitchStep.Closing => "關閉中",
        SwitchStep.Clearing => "清除中",
        SwitchStep.Switching => "切換中",
        SwitchStep.Launching => "啟動中",
        SwitchStep.Saving => "儲存中",
        SwitchStep.LoggingIn => "登入中",
        SwitchStep.ApplyingSettings => "套用設定",
        SwitchStep.ReadingSettings => "讀取設定",
        _ => step.ToString(),
    };

    private static string StepDetail(SwitchStep step) => step switch
    {
        SwitchStep.Closing => "正在關閉 Riot Client 與遊戲…",
        SwitchStep.Clearing => "正在清空目前的登入狀態（已先備份）…",
        SwitchStep.Switching => "正在放入登入狀態…",
        SwitchStep.Launching => "正在開啟 Riot Client…",
        SwitchStep.Saving => "正在加密儲存登入狀態…",
        SwitchStep.LoggingIn => "等待 Riot Client 自動登入…",
        SwitchStep.ApplyingSettings => "正在備份並套用遊戲設定…",
        SwitchStep.ReadingSettings => "正在讀取遊戲設定…",
        _ => "",
    };
}
