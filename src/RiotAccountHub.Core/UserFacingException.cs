namespace RiotAccountHub.Core;

/// <summary>
/// 「可以直接顯示給使用者看」的錯誤。
/// Message 一律寫成清楚的繁體中文，介面收到這種錯誤時會原封不動顯示出來。
/// </summary>
public sealed class UserFacingException : Exception
{
    public UserFacingException(string title, string message, Exception? inner = null)
        : base(message, inner)
    {
        Title = title;
    }

    /// <summary>錯誤視窗的標題</summary>
    public string Title { get; }
}
