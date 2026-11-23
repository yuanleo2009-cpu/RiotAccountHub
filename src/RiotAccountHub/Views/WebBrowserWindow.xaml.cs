using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Web;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

/// <summary>
/// 程式內的簡單瀏覽器（WebView2）。
///   - 只是讓使用者自己瀏覽網站；程式不會讀取網頁內容、不會自動抓資料
///   - 瀏覽器資料放在 %AppData%\RiotAccountHub\webview2
///   - 網站想開新視窗時，同一個網站就在這裡開，其他網站交給預設瀏覽器
///   - 電腦沒有 WebView2 執行環境時，改用預設瀏覽器開啟
/// </summary>
public partial class WebBrowserWindow : Window
{
    private readonly Uri _startUri;
    private readonly Action? _extraAction;

    public WebBrowserWindow(string url, string title, string hint, string? extraButtonText = null, Action? extraButtonAction = null)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _startUri = new Uri(url);
        Title = title;
        HintText.Text = hint;

        // 工具列上額外的按鈕（例如「我的準星」）
        _extraAction = extraButtonAction;
        if (extraButtonText is not null && extraButtonAction is not null)
        {
            ExtraButtonText.Text = extraButtonText;
            ExtraButton.Visibility = Visibility.Visible;
        }
        Loaded += async (_, _) => await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var dataDir = AppPaths.CreateDefault().WebViewDataDir;
            var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: dataDir);
            await Browser.EnsureCoreWebView2Async(env);

            // 擋廣告：拒絕廣告網域的請求（包含網頁裡的 iframe），並藏起空出來的廣告欄位
            _environment = env;
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            Browser.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(AdBlocker.HideAdsScript);

            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            Browser.CoreWebView2.NavigationCompleted += (_, _) => LoadingText.Visibility = Visibility.Collapsed;
            Browser.CoreWebView2.Navigate(_startUri.ToString());
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            AppLog.Error("找不到 WebView2 執行環境", ex);
            FallbackToExternal("這台電腦沒有安裝 Microsoft Edge WebView2 執行環境，改用預設瀏覽器開啟。");
        }
        catch (Exception ex)
        {
            AppLog.Error("內建瀏覽器開啟失敗", ex);
            FallbackToExternal("內建瀏覽器開啟失敗，改用預設瀏覽器開啟。\n\n詳細資訊：" + ex.Message);
        }
    }

    private CoreWebView2Environment? _environment;
    private int _blockedCount;

    /// <summary>廣告網址直接回應「已封鎖」，不讓它下載</summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_environment is null || !AdBlocker.IsBlocked(e.Request.Uri)) return;
        e.Response = _environment.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
        _blockedCount++;
    }

    /// <summary>網站要開新視窗：同一個網站就在這裡開，其他網站用預設瀏覽器</summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith(_startUri.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase))
        {
            Browser.CoreWebView2.Navigate(uri.ToString());
        }
        else
        {
            OpenExternal(e.Uri);
        }
    }

    private void FallbackToExternal(string message)
    {
        LoadingText.Text = message;
        OpenExternal(_startUri.ToString());
    }

    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("無法用預設瀏覽器開啟網址", ex);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true) Browser.CoreWebView2.GoBack();
    }

    private void OnForward(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoForward == true) Browser.CoreWebView2.GoForward();
    }

    private void OnReload(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.Reload();

    private void OnOpenExternal(object sender, RoutedEventArgs e) =>
        OpenExternal(Browser.CoreWebView2?.Source ?? _startUri.ToString());

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnExtra(object sender, RoutedEventArgs e) => _extraAction?.Invoke();

    protected override void OnClosed(EventArgs e)
    {
        if (_blockedCount > 0) AppLog.Info($"內建瀏覽器已擋掉 {_blockedCount} 個廣告請求");
        Browser.Dispose();
        base.OnClosed(e);
    }
}
