using System.Windows;
using System.Windows.Threading;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;
using RiotAccountHub.Core.Startup;
using RiotAccountHub.Core.Storage;
using RiotAccountHub.Helpers;
using RiotAccountHub.Services;
using RiotAccountHub.ViewModels;
using RiotAccountHub.Views;

namespace RiotAccountHub;

/// <summary>
/// 程式的進入點。這裡負責：
///   1. 確認程式只開一個（避免兩個視窗同時改檔案）
///   2. 把各個服務「組裝」起來，交給主畫面的 ViewModel
///   3. 攔截沒被處理到的錯誤，顯示中文訊息而不是直接當掉
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;

    // 錯誤視窗正在顯示中（避免同一個錯誤一直重複發生時，視窗一個接一個疊上來）
    private bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- 0. 最先裝好「錯誤攔截」：之後任何一步出錯，都會顯示中文訊息並寫進記錄，而不是直接閃退 ----
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("背景工作發生錯誤", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("程式意外結束", args.ExceptionObject as Exception);

        // ---- 1. 只允許開一個 ----
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\RiotAccountHub.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Riot Account Hub 已經在執行中了。", "Riot Account Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            // 啟動到一半失敗（例如帳號清單暫時讀不到）：說明原因後關閉。
            // 不能帶著「讀到一半」的資料繼續執行，否則之後存檔可能把原本的資料蓋掉。
            AppLog.Error("程式啟動失敗", ex);
            var (title, message) = ex is UserFacingException ufe
                ? (ufe.Title, ufe.Message)
                : ("程式無法啟動", "很抱歉，程式啟動時發生錯誤，所以先關閉了。你的帳號資料沒有被修改。\n\n" +
                                   FriendlyError.ReasonLine(ex) + "\n\n請稍後再開啟一次。");
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>準備資料夾、組裝服務、顯示主畫面</summary>
    private void Start(StartupEventArgs e)
    {
        // 套用深色主題與唯一的強調色（紅色）
        AppTheme.Apply();

        // ---- 2. 準備資料夾與記錄檔 ----
        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();
        AppLog.Initialize(paths.LogsDir);
        AppLog.Info("===== 程式啟動 =====");

        // ---- 3. 組裝服務 ----
        var liveRoot = RiotLoginFiles.DefaultLiveRoot;              // Riot Client 的登入檔案位置
        var store = new SnapshotStore(new DpapiProtector(), paths.StagingDir); // 快照儲存與加密

        // 上次如果切換到一半被強制關閉，暫存區可能留著還沒加密的登入檔案：啟動時清掉
        var cleaned = store.CleanupStaleStaging();
        if (cleaned > 0) AppLog.Info($"已清除上次留下的暫存資料夾：{cleaned} 個");
        var riot = new RiotClientService();                          // Riot Client 操作
        var api = new RiotLocalApi(liveRoot);                        // Riot Client 本機 API
        var switcher = new AccountSwitcher(paths, store, riot, api, liveRoot, RiotLoginFiles.Items);

        var viewModel = new MainViewModel(
            paths,
            switcher,
            riot,
            new AccountRepository(paths),
            new SettingsRepository(paths),
            new DialogService(),
            new PlayerCardCache(paths.CardsDir),                      // 卡面圖片快取
            new SettingsSync(api, riot, new PlayerSettingsClient(),   // 同步遊戲設定
                new SettingsProfileStore(paths.GameSettingsDir)));

        // ---- 4. 顯示主畫面 ----
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;

        // 如果是 Windows 開機時自動啟動的（帶 --startup 參數），就縮小到工作列，不要突然跳出視窗
        if (e.Args.Contains(StartupRegistration.StartupArgument, StringComparer.OrdinalIgnoreCase))
        {
            window.ShowActivated = false;
            window.WindowState = WindowState.Minimized;
            AppLog.Info("由開機自動啟動，縮小開啟");
        }
        window.Show();
        viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("程式結束");
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>任何沒被處理到的錯誤都會跑到這裡：記錄下來並顯示中文訊息</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("未處理的錯誤", e.Exception);
        e.Handled = true;

        // 已經有一個錯誤視窗開著：只寫記錄，不要再疊一個視窗上去
        if (_showingError) return;
        _showingError = true;
        try
        {
            var (title, message) = e.Exception is UserFacingException ufe
                ? (ufe.Title, ufe.Message)
                : ("發生未預期的錯誤", "很抱歉，程式發生了未預期的錯誤。\n\n" + FriendlyError.ReasonLine(e.Exception) + "\n\n詳細的錯誤內容已經寫進記錄檔。");
            new DialogService().ShowError(title, message);
        }
        catch (Exception dialogEx)
        {
            AppLog.Error("無法顯示錯誤視窗", dialogEx);
        }
        finally
        {
            _showingError = false;
        }
    }
}
