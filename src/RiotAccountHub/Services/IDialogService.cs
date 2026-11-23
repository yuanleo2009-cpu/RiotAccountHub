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
    bool Confirm(string title, string message, string confirmText, string cancelText, bool danger = false, bool warning = false);

    /// <summary>「請在 Riot Client 登入」提示；按「我登入好了」回傳 true，按取消回傳 false</summary>
    bool ShowLoginPrompt(string headline);

    /// <summary>帳號資料填寫視窗；按儲存回傳 true</summary>
    bool ShowAccountEditor(AccountEditViewModel viewModel);

    /// <summary>請使用者選擇 RiotClientServices.exe；取消回傳 null</summary>
    string? PickRiotClientExe(string? initialDirectory);
}
