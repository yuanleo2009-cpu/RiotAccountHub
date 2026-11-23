namespace RiotAccountHub.Core.Models;

/// <summary>
/// 登入方式。只是給使用者看的標籤，程式不會用它做任何登入動作。
/// </summary>
public enum LoginMethod
{
    Google,
    Facebook,
    Apple,
    /// <summary>Riot 帳號密碼登入</summary>
    RiotAccount,
}

public static class LoginMethodNames
{
    /// <summary>把登入方式轉成中文顯示文字</summary>
    public static string ToDisplay(this LoginMethod method) => method switch
    {
        LoginMethod.Google => "Google",
        LoginMethod.Facebook => "Facebook",
        LoginMethod.Apple => "Apple",
        LoginMethod.RiotAccount => "帳密",
        _ => method.ToString(),
    };
}
