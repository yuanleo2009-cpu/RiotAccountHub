using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;

namespace RiotAccountHub.Core;

/// <summary>
/// 把「Riot Client 操作」和「快照儲存」組合成完整的流程：
///   - 切換帳號並啟動 VALORANT
///   - 新增帳號 / 重新登入（開始 → 使用者登入 → 擷取）
///   - 還原原本狀態
///
/// 每個流程都遵守同一個原則：先確定 Riot Client 完全關閉，才讀寫登入檔案。
/// </summary>
public sealed class AccountSwitcher
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(15);

    private readonly AppPaths _paths;
    private readonly SnapshotStore _store;
    private readonly RiotClientService _riot;
    private readonly RiotLocalApi _api;
    private readonly string _liveRoot;
    private readonly IReadOnlyList<LoginItem> _items;

    // 同一時間只允許一個流程在跑
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AccountSwitcher(
        AppPaths paths,
        SnapshotStore store,
        RiotClientService riot,
        RiotLocalApi api,
        string liveRoot,
        IReadOnlyList<LoginItem> items)
    {
        _paths = paths;
        _store = store;
        _riot = riot;
        _api = api;
        _liveRoot = liveRoot;
        _items = items;
    }

    /// <summary>是否已經有「原本狀態」的備份</summary>
    public bool HasOriginalBackup => File.Exists(_paths.OriginalBackupFile);

    /// <summary>這個帳號有沒有快照檔</summary>
    public bool HasSnapshot(string accountId) => File.Exists(_paths.SnapshotFile(accountId));

    /// <summary>有沒有遊戲正在執行</summary>
    public bool IsGameRunning() => _riot.IsGameRunning();

    /// <summary>
    /// 讀取目前正在玩的帳號與段位（只讀本機資料）。
    /// VALORANT 沒在執行、或讀不到就回傳 null。
    /// </summary>
    public async Task<CurrentRank?> ReadCurrentRankAsync()
    {
        if (!_riot.IsValorantRunning()) return null;
        return await _api.GetCurrentRankAsync();
    }

    // ------------------------------------------------------------------
    // 切換並啟動
    // ------------------------------------------------------------------

    /// <summary>
    /// Riot Client 現在是不是已經登入這個帳號？
    /// 是的話就不需要「關閉 → 換檔案 → 重開」，可以直接開遊戲，快很多。
    /// </summary>
    public async Task<bool> IsCurrentlyLoggedInAsync(Account account)
    {
        if (!_riot.IsAnyRiotProcessRunning()) return false;
        var current = await _api.GetRiotIdAsync();
        return current is not null && account.IsSameAccount(current);
    }

    /// <summary>
    /// 快速路徑：帳號已經登入時使用，不關閉也不換檔案。
    ///   - VALORANT 已經開著 → 把遊戲視窗叫到最前面
    ///   - 還沒開 → 請正在執行的 Riot Client 直接啟動 VALORANT
    /// </summary>
    public Task<LaunchResult> LaunchWithoutSwitchAsync(string clientPath, IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(() =>
        {
            progress.Report(SwitchStep.Launching);
            if (_riot.IsValorantRunning())
            {
                _riot.BringValorantToFront();
                AppLog.Info("帳號已在遊戲中，切換到遊戲視窗");
                return Task.FromResult(LaunchResult.AlreadyPlaying);
            }

            _riot.StartValorant(clientPath);
            AppLog.Info("帳號已登入，直接啟動 VALORANT（不需要切換）");
            return Task.FromResult(LaunchResult.LaunchedWithoutSwitch);
        });

    /// <summary>
    /// 自動「開始遊戲」：等 Riot Client 登入完成後，請它啟動 VALORANT。
    /// 每秒檢查一次，最多等 timeout；遊戲已經在跑（例如使用者自己按了）就不再動作。
    /// 只用本機 Riot Client API，不碰遊戲或 Vanguard。回傳是否成功送出開始遊戲。
    /// </summary>
    public async Task<bool> AutoStartGameAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        const int maxAttempts = 3; // 「開始遊戲」最多只送 3 次，絕不會一直重送
        var attempts = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            if (_riot.IsValorantRunning())
            {
                AppLog.Info("自動開始遊戲：VALORANT 已經在執行，不需要再按");
                return true;
            }

            // 先等 Riot Client 登入完成，再送「開始遊戲」
            if (!await _api.IsLoggedInAsync(ct))
            {
                await Task.Delay(1000, ct);
                continue;
            }

            attempts++;
            var result = await _api.LaunchValorantAsync(ct);
            switch (result)
            {
                case LaunchRequestResult.Accepted:
                    AppLog.Info($"自動開始遊戲：已送出（等待 {sw.ElapsedMilliseconds} ms）");
                    return true;

                case LaunchRequestResult.NoResponse:
                    // 請求可能已經送到 Riot Client，只是它沒回應；為了不重複開遊戲，就此停止
                    AppLog.Info("自動開始遊戲：已送出，但 Riot Client 沒有回應，不再重送。若遊戲沒開，請在 Riot 介面按開始");
                    return false;

                case LaunchRequestResult.Rejected when attempts >= maxAttempts:
                    AppLog.Info("自動開始遊戲：Riot Client 拒絕了 3 次，請在 Riot 介面手動按開始");
                    return false;
            }

            // Rejected：Riot Client 可能還在準備（例如更新中），等 3 秒再試
            await Task.Delay(3000, ct);
        }

        AppLog.Info("自動開始遊戲：等待逾時（Riot Client 可能還沒登入），請在 Riot 介面手動按開始");
        return false;
    }

    /// <summary>
    /// 切換到指定帳號，但「只開 Riot Client、先不開遊戲」。
    /// 用在「套用主帳號設定」：要先登入、套用設定，最後才開遊戲（遊戲開著的話會把舊設定寫回去）。
    /// </summary>
    public Task SwitchAndStartClientAsync(string accountId, string clientPath, IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            var snapshot = _store.LoadFromFile(_paths.SnapshotFile(accountId));

            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Switching);
            await Task.Run(() =>
            {
                EnsureOriginalBackup();
                _store.Apply(snapshot, _liveRoot, _items);
            });

            _riot.StartClient(clientPath);
            AppLog.Info("已切換帳號並開啟 Riot Client（先不開遊戲）：" + accountId);
        });

    /// <summary>等 Riot Client 登入完成；逾時回傳 false（通常代表登入狀態過期）</summary>
    public async Task<bool> WaitForLoginAsync(TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await _api.IsLoggedInAsync()) return true;
            await Task.Delay(1000);
        }
        return false;
    }

    /// <summary>切換到指定帳號的快照，並啟動 VALORANT</summary>
    public Task SwitchAndLaunchAsync(string accountId, string clientPath, IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            var total = System.Diagnostics.Stopwatch.StartNew();

            // 先讀快照（檔案不見的話，在關閉 Riot Client 之前就先報錯）
            var snapshot = _store.LoadFromFile(_paths.SnapshotFile(accountId));

            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Switching);
            await Task.Run(() =>
            {
                EnsureOriginalBackup();
                _store.Apply(snapshot, _liveRoot, _items);
            });

            progress.Report(SwitchStep.Launching);
            _riot.StartValorant(clientPath);
            AppLog.Info($"切換完成並啟動 VALORANT：{accountId}（本程式花費 {total.ElapsedMilliseconds} ms）");
        });

    // ------------------------------------------------------------------
    // 新增帳號 / 重新登入
    // ------------------------------------------------------------------

    /// <summary>
    /// 登入流程第 1 步：關閉 Riot Client → 記住目前狀態 → 清空登入狀態 → 開啟 Riot Client。
    /// 回傳的 PendingLogin 用來在「取消」時把狀態放回去。
    /// </summary>
    public Task<PendingLogin> BeginLoginAsync(string clientPath, IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Clearing);
            var previous = await Task.Run(() =>
            {
                EnsureOriginalBackup();
                var captured = _store.Capture(_liveRoot, _items); // 加密後放在記憶體，取消時用
                _store.Apply(null, _liveRoot, _items);            // null = 清空
                return captured;
            });

            progress.Report(SwitchStep.Launching);
            _riot.StartClient(clientPath);
            AppLog.Info("已清空登入狀態並開啟 Riot Client，等待使用者登入");
            return new PendingLogin(previous);
        });

    /// <summary>登入流程第 2 步：問 Riot Client 目前有沒有登入、有沒有勾保持登入</summary>
    public Task<ApiLoginStatus> CheckLoginAsync() => _api.GetLoginStatusAsync();

    /// <summary>
    /// 自動抓目前登入帳號的 Riot ID。
    /// 剛登入時 Riot Client 可能還在載入，所以最多試 4 次（約 4 秒）。
    /// 抓不到就回傳 null，讓使用者手動輸入，不會讓流程失敗。
    /// </summary>
    public async Task<RiotIdInfo?> DetectRiotIdAsync()
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var info = await _api.GetRiotIdAsync();
            if (info is not null)
            {
                AppLog.Info("已自動取得 Riot ID");
                return info;
            }
            if (attempt < 4) await Task.Delay(1000);
        }
        AppLog.Info("無法自動取得 Riot ID，改用手動輸入");
        return null;
    }

    /// <summary>
    /// 登入流程第 3 步：關閉 Riot Client → 檢查保持登入 → 把登入狀態加密擷取下來。
    /// 這一步還不會存檔，由呼叫的人決定要存成哪個帳號。
    /// </summary>
    public Task<CapturedLogin> FinishLoginAsync(IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Saving);
            return await Task.Run(() =>
            {
                var persist = RiotLoginFiles.DetectPersistedLogin(_liveRoot);
                var data = _store.Capture(_liveRoot, _items);
                AppLog.Info($"已擷取登入狀態(保持登入：{persist})");
                return new CapturedLogin(data, persist);
            });
        });

    /// <summary>把擷取到的登入狀態存成某個帳號的快照</summary>
    public void SaveSnapshot(string accountId, CapturedLogin captured)
    {
        _store.SaveToFile(_paths.SnapshotFile(accountId), captured.EncryptedData);
        AppLog.Info("已儲存快照：" + accountId);
    }

    /// <summary>取消登入流程：關閉 Riot Client，把開始前的狀態放回去</summary>
    public Task CancelLoginAsync(PendingLogin pending, IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Switching);
            await Task.Run(() => _store.Apply(pending.PreviousState, _liveRoot, _items));
            AppLog.Info("已取消登入流程並放回原本的登入狀態");
        });

    // ------------------------------------------------------------------
    // 還原原本狀態 / 刪除
    // ------------------------------------------------------------------

    /// <summary>把第一次使用前備份的登入狀態放回去</summary>
    public Task RestoreOriginalAsync(IProgress<SwitchStep> progress) =>
        RunExclusiveAsync(async () =>
        {
            if (!HasOriginalBackup)
            {
                throw new UserFacingException("沒有原本狀態的備份", "目前還沒有建立原本狀態的備份（程式還沒切換過任何帳號），不需要還原。");
            }
            var original = _store.LoadFromFile(_paths.OriginalBackupFile);

            progress.Report(SwitchStep.Closing);
            await _riot.CloseAllAsync(CloseTimeout);

            progress.Report(SwitchStep.Switching);
            await Task.Run(() => _store.Apply(original, _liveRoot, _items));
            AppLog.Info("已還原原本狀態");
        });

    /// <summary>刪除帳號的快照檔</summary>
    public void DeleteSnapshot(string accountId)
    {
        var path = _paths.SnapshotFile(accountId);
        if (File.Exists(path)) File.Delete(path);
        AppLog.Info("已刪除快照：" + accountId);
    }

    // ------------------------------------------------------------------
    // 內部
    // ------------------------------------------------------------------

    /// <summary>
    /// 第一次動到登入檔案之前，先把使用者原本的狀態備份起來（只做一次）。
    /// 呼叫時 Riot Client 必須已經關閉。
    /// </summary>
    private void EnsureOriginalBackup()
    {
        if (HasOriginalBackup) return;
        var data = _store.Capture(_liveRoot, _items);
        _store.SaveToFile(_paths.OriginalBackupFile, data);
        AppLog.Info("已建立原本狀態的備份");
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        await RunExclusiveAsync(async () => { await action(); return 0; });
    }

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
    {
        if (!await _gate.WaitAsync(0))
        {
            throw new UserFacingException("請稍候", "目前已經有一個切換動作正在進行中，請等它完成。");
        }
        try
        {
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>「啟動」按下去之後實際做了什麼</summary>
public enum LaunchResult
{
    /// <summary>關閉 Riot Client、換成這個帳號、再啟動</summary>
    Switched,
    /// <summary>帳號本來就登入著，直接啟動 VALORANT</summary>
    LaunchedWithoutSwitch,
    /// <summary>本來就在玩這個帳號，只是把遊戲視窗叫到前面</summary>
    AlreadyPlaying,
}

/// <summary>進度步驟（介面會把它顯示成「關閉中 → 切換中 → 啟動中」）</summary>
public enum SwitchStep
{
    Closing,
    Clearing,
    Switching,
    Launching,
    Saving,
    /// <summary>等 Riot Client 登入</summary>
    LoggingIn,
    /// <summary>套用遊戲設定</summary>
    ApplyingSettings,
    /// <summary>讀取遊戲設定</summary>
    ReadingSettings,
}

/// <summary>登入流程進行中：記住開始前的狀態（已加密），取消時放回去</summary>
public sealed record PendingLogin(byte[] PreviousState);

/// <summary>剛擷取到的登入狀態（已加密）與保持登入檢查結果</summary>
public sealed record CapturedLogin(byte[] EncryptedData, PersistState Persist);
