using System.Windows;
using System.Windows.Threading;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;
using RiotAccountHub.Core.Storage;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // ---- 2. 準備資料夾與記錄檔 ----
        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();
        AppLog.Initialize(paths.LogsDir);
        AppLog.Info("===== 程式啟動 =====");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("背景工作發生錯誤", args.Exception);
            args.SetObserved();
        };

        // ---- 3. 組裝服務 ----
        var liveRoot = RiotLoginFiles.DefaultLiveRoot;              // Riot Client 的登入檔案位置
        var store = new SnapshotStore(new DpapiProtector(), paths.StagingDir); // 快照儲存與加密
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
        var (title, message) = e.Exception is UserFacingException ufe
            ? (ufe.Title, ufe.Message)
            : ("發生未預期的錯誤", "很抱歉，程式發生了未預期的錯誤。\n\n詳細資訊：" + e.Exception.Message);
        new DialogService().ShowError(title, message);
        e.Handled = true;
    }
}
