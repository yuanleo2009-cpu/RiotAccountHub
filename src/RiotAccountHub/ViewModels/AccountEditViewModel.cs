using CommunityToolkit.Mvvm.ComponentModel;
using RiotAccountHub.Core.Models;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 「填寫 / 編輯帳號資料」視窗的 ViewModel。
/// </summary>
public sealed partial class AccountEditViewModel : ObservableObject
{
    private AccountEditViewModel(string windowTitle, string headline)
    {
        WindowTitle = windowTitle;
        Headline = headline;
        Methods = Enum.GetValues<LoginMethod>()
            .Select(m => new LoginMethodOption(m, m.ToDisplay()))
            .ToList();
    }

    /// <summary>新增帳號用（登入完成後填資料）</summary>
    public static AccountEditViewModel ForNew()
    {
        var vm = new AccountEditViewModel("新增帳號", "登入狀態已加密儲存！最後請填寫這個帳號的資料。");
        vm.Select(LoginMethod.Google);
        return vm;
    }

    /// <summary>編輯既有帳號用</summary>
    public static AccountEditViewModel ForEdit(Account account)
    {
        var vm = new AccountEditViewModel("編輯帳號", "修改帳號的顯示資料（不會影響登入狀態）。")
        {
            GameName = account.GameName,
            TagLine = account.TagLine,
            Note = account.Note,
        };
        vm.Select(account.LoginMethod);
        return vm;
    }

    public string WindowTitle { get; }
    public string Headline { get; }

    /// <summary>登入方式的選項（Google / Facebook / Apple / 帳密）</summary>
    public IReadOnlyList<LoginMethodOption> Methods { get; }

    [ObservableProperty]
    private string _gameName = "";

    [ObservableProperty]
    private string _tagLine = "";

    [ObservableProperty]
    private string _note = "";

    /// <summary>驗證失敗時顯示的紅字</summary>
    [ObservableProperty]
    private string _errorMessage = "";

    public LoginMethod SelectedMethod => Methods.FirstOrDefault(m => m.IsSelected)?.Value ?? LoginMethod.RiotAccount;

    private void Select(LoginMethod method)
    {
        foreach (var m in Methods) m.IsSelected = m.Value == method;
    }

    /// <summary>
    /// 檢查輸入是否正確。順便做一些貼心處理：
    ///   - 在名稱欄直接貼上「名字#Tag」，會自動拆開
    ///   - Tag 前面多打的 # 會自動去掉
    /// </summary>
    public bool Validate()
    {
        var name = (GameName ?? "").Trim();
        var tag = (TagLine ?? "").Trim().TrimStart('#').Trim();

        var hash = name.LastIndexOf('#');
        if (hash >= 0)
        {
            if (tag.Length == 0) tag = name[(hash + 1)..].Trim();
            name = name[..hash].Trim();
        }

        GameName = name;
        TagLine = tag;
        Note = (Note ?? "").Trim();

        if (name.Length == 0)
        {
            ErrorMessage = "請輸入遊戲名稱（Riot ID 的 # 前面那段）。";
            return false;
        }
        if (tag.Length == 0)
        {
            ErrorMessage = "請輸入 Tag（Riot ID 的 # 後面那段，例如 TW1）。";
            return false;
        }
        if (tag.Contains('#'))
        {
            ErrorMessage = "Tag 裡面不能有 # 符號。";
            return false;
        }
        if (Note.Length > 40)
        {
            ErrorMessage = "備註最多 40 個字。";
            return false;
        }

        ErrorMessage = "";
        return true;
    }

    /// <summary>把填好的內容寫回帳號資料</summary>
    public void ApplyTo(Account account)
    {
        account.GameName = GameName;
        account.TagLine = TagLine;
        account.Note = Note;
        account.LoginMethod = SelectedMethod;
    }
}

/// <summary>一個登入方式選項</summary>
public sealed partial class LoginMethodOption : ObservableObject
{
    public LoginMethodOption(LoginMethod value, string display)
    {
        Value = value;
        Display = display;
    }

    public LoginMethod Value { get; }
    public string Display { get; }

    [ObservableProperty]
    private bool _isSelected;
}
