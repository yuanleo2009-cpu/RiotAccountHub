using RiotAccountHub.ViewModels;

namespace RiotAccountHub.Services;

/// <summary>
/// 跳出視窗的介面。
/// ViewModel 只透過這個介面「要求」跳出視窗，不直接碰任何 Window,
/// 這就是 MVVM 把「邏輯」和「畫面」分開的做法。
/// </summary>
public interface IDialogService
{
    /// <summary>一般提示（只有「確定」按鈕）</summary>
    void ShowInfo(string title, string message);

    /// <summary>錯誤訊息（只有「確定」按鈕）</summary>
    void ShowError(string title, string message);

    /// <summary>需要使用者確認；按確認回傳 true</summary>
    /// <param name="bringToFront">
    /// 主視窗可能被別的視窗（例如 Riot Client）蓋住時設成 true：
    /// 會先把主視窗叫到前面，而且這個確認視窗會保持在最上層，使用者才看得到。
    /// </param>
    bool Confirm(string title, string message, string confirmText, string cancelText, bool danger = false, bool warning = false, bool bringToFront = false);

    /// <summary>「請在 Riot Client 登入」提示；按「我登入好了」回傳 true，按取消回傳 false</summary>
    bool ShowLoginPrompt(string headline);

    /// <summary>帳號資料填寫視窗；按儲存回傳 true</summary>
    bool ShowAccountEditor(AccountEditViewModel viewModel);

    /// <summary>打開「熱門準星」視窗</summary>
    void ShowCrosshairs(CrosshairsViewModel viewModel);

    /// <summary>準星大圖預覽</summary>
    void ShowCrosshairDetail(CrosshairItemViewModel item, CrosshairsViewModel owner);

    /// <summary>新增 / 編輯準星；按儲存回傳（名稱, 代碼），取消回傳 null</summary>
    (string Name, string Code)? ShowCrosshairEditor(string title, string name, string code, Helpers.CrosshairBackground background);

    /// <summary>
    /// 在程式內用 WebView2 開網頁（關掉視窗才會回來）。
    /// extraButtonText / extraButtonAction：工具列上額外的按鈕（例如「我的準星」），不需要就傳 null。
    /// </summary>
    void ShowWebBrowser(string url, string title, string hint, string? extraButtonText = null, Action? extraButtonAction = null);

    /// <summary>請使用者選擇 RiotClientServices.exe；取消回傳 null</summary>
    string? PickRiotClientExe(string? initialDirectory);
}
