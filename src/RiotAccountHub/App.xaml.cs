using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Cards;
using RiotAccountHub.Core.GameSettings;
using RiotAccountHub.Core.Riot;
using RiotAccountHub.Core.Snapshots;
using RiotAccountHub.Core.Startup;
using RiotAccountHub.Core.Storage;
using RiotAccountHub.Core.Updates;
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

        // 記錄檔先準備好（下面的更新步驟也要寫記錄）
        _paths = AppPaths.CreateDefault();
        _paths.EnsureCreated();
        AppLog.Initialize(_paths.LogsDir);

        // 剛更新完、由舊版重新開啟的：等舊版完全結束，才不會被「已經在執行中」擋住
        var updatedFrom = WaitForPreviousVersion(e.Args);

        // ---- 1. 只允許開一個 ----
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\RiotAccountHub.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            // 正在執行的是舊版（例如使用者下載了更新包、打開新版）：問要不要把它更新成這個版本
            if (!OfferToUpdateOlderCopy(running: true))
            {
                MessageBox.Show("Riot Account Hub 已經在執行中了。", "Riot Account Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        // 電腦上別的位置有舊版（例如捷徑、開機自動開啟指向的那一份）：問要不要直接把它換成這個版本
        if (updatedFrom is null && OfferToUpdateOlderCopy(running: false))
        {
            Shutdown();
            return;
        }

        try
        {
            Start(e, updatedFrom is not null);
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

    private AppPaths _paths = null!;

    /// <summary>更新完重新開啟時帶的參數：後面接舊版的程序編號</summary>
    public const string AfterUpdateArgument = "--after-update";

    /// <summary>只給測試用：換成本機（127.0.0.1）的 update.json，其他網址不接受</summary>
    private const string UpdateTestUrlArgument = "--update-test-url";

    /// <summary>準備資料夾、組裝服務、顯示主畫面</summary>
    private void Start(StartupEventArgs e, bool justUpdated)
    {
        // 套用深色主題與唯一的強調色（紅色）
        AppTheme.Apply();

        // ---- 2. 準備資料夾與記錄檔 ----
        var paths = _paths;
        AppLog.Info($"===== 程式啟動 =====（v{AppVersion.Display(AppVersion.Current)}）");

        // 上次更新留下的舊程式檔（.old）：舊版已經關掉了，可以刪
        if (Environment.ProcessPath is { } exe && SelfReplacer.CleanupBackups(exe) > 0) AppLog.Info("已刪除上次更新留下的舊程式檔");
        if (justUpdated) DeleteUpdateDownloads(paths);

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
                new SettingsProfileStore(paths.GameSettingsDir)),
            updates: new UpdateService(TestUpdateUrl(e.Args)));

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
        if (justUpdated) viewModel.ShowJustUpdated();
    }

    // ==================================================================
    // 更新
    // ==================================================================

    /// <summary>
    /// 有 --after-update 參數（舊版下載好新版後重新開啟我）：等舊版的程序結束。回傳舊版的程序編號；沒有這個參數就是 null。
    /// </summary>
    private static int? WaitForPreviousVersion(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals(AfterUpdateArgument, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return null;

        try
        {
            using var previous = Process.GetProcessById(pid);
            if (!previous.WaitForExit(20000)) AppLog.Info("更新：舊版 20 秒內沒有結束");
        }
        catch (ArgumentException)
        {
            // 已經結束了
        }
        AppLog.Info($"更新完成，現在是 v{AppVersion.Display(AppVersion.Current)}");
        return pid;
    }

    /// <summary>測試用的 update.json 網址（只接受本機）；沒有就是 null，用 GitHub</summary>
    private static Uri? TestUpdateUrl(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals(UpdateTestUrlArgument, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return null;
        return Uri.TryCreate(args[i + 1], UriKind.Absolute, out var url) && url.IsLoopback && url.Scheme == Uri.UriSchemeHttp ? url : null;
    }

    /// <summary>
    /// 電腦上有舊版的 Riot Account Hub（不在這個位置）：問使用者要不要直接把它換成這個版本。
    /// 換好之後從舊版的位置重新開啟，捷徑、工作列釘選、開機自動開啟都照常可以用。
    /// 回傳 true 表示已經交給更新後的那一份，這一份要結束。
    /// </summary>
    private bool OfferToUpdateOlderCopy(bool running)
    {
        try
        {
            var self = Environment.ProcessPath;
            if (self is null) return false;
            var autostart = StartupRegistration.ExePathFromCommand(new StartupRegistration().RegisteredCommand());
            var older = InstalledCopies.FindOlder(self, AppVersion.Current, autostart);
            if (older is null || (running && older.RunningPid is null)) return false;

            AppTheme.Apply();
            var newText = AppVersion.Display(AppVersion.Current);
            var question =
                $"你的電腦上有舊版的 Riot Account Hub（v{AppVersion.Display(older.Version)}）" +
                (older.RunningPid is not null ? "，現在正在執行" : "") + "：\n" + older.Path + "\n\n" +
                $"要把它更新成 v{newText} 嗎？\n帳號和設定都會保留，捷徑和開機自動開啟也照常可以用。";
            var update = MessageDialog.Show(null, "更新 Riot Account Hub", question, DialogKind.Question,
                "更新", running ? "取消" : "不用，直接開這一份");
            if (!update) return running; // 舊版正在執行又不更新：這一份不能再開（只能開一個）

            if (older.RunningPid is { } pid && !CloseOldVersion(pid))
            {
                MessageDialog.Show(null, "沒辦法更新", "舊版的 Riot Account Hub 沒有關閉，所以沒有更新。\n請先關掉舊版，再打開這個程式一次。",
                    DialogKind.Error, "確定", null);
                return true;
            }

            SelfReplacer.Replace(self, older.Path);
            AppLog.Info($"已把舊版 v{AppVersion.Display(older.Version)} 更新成 v{newText}：{older.Path}");
            Process.Start(new ProcessStartInfo(older.Path, $"{AfterUpdateArgument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(older.Path)!,
            })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("更新舊版失敗", ex);
            MessageDialog.Show(null, "沒辦法更新舊版",
                "更新舊版的時候發生錯誤，舊版維持原樣。\n\n" + FriendlyError.ReasonLine(ex), DialogKind.Error, "確定", null);
            return running;
        }
    }

    /// <summary>更新完成後，下載的更新包已經用不到了（約 60 MB）：刪掉</summary>
    private static void DeleteUpdateDownloads(AppPaths paths)
    {
        var dir = System.IO.Path.Combine(paths.DataRoot, "updates");
        try
        {
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Info("下載的更新包暫時刪不掉，下次再刪");
        }
    }

    /// <summary>請舊版正常關閉（像按右上角的 X），最多等 15 秒</summary>
    private static bool CloseOldVersion(int pid)
    {
        try
        {
            using var old = Process.GetProcessById(pid);
            old.CloseMainWindow();
            return old.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            return true; // 已經結束了
        }
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
