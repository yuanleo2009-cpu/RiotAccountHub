using System.ComponentModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RiotAccountHub.Core;

/// <summary>
/// 把程式內部的錯誤（通常是英文、很技術性）翻成一句簡單的中文原因，顯示給使用者看。
/// 完整的英文錯誤內容仍然會寫進記錄檔（logs\app.log），方便之後查問題。
/// </summary>
public static class FriendlyError
{
    // Windows 的錯誤代碼
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int HandleDiskFull = unchecked((int)0x80070027);

    /// <summary>一句話說明「為什麼失敗」</summary>
    public static string Reason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Windows 不允許存取這個檔案（可能被防毒軟體擋住，或沒有權限）。",
        FileNotFoundException or DirectoryNotFoundException => "找不到需要的檔案或資料夾。",
        IOException io when io.HResult is SharingViolation or LockViolation => "檔案正被其他程式使用中（例如 Riot Client 或防毒軟體）。",
        IOException io when io.HResult is DiskFull or HandleDiskFull => "電腦的磁碟空間不夠了。",
        InvalidDataException or JsonException => "資料的格式不對，檔案可能已經損毀。",
        IOException => "讀寫檔案時出了問題。",
        CryptographicException => "加密的資料解不開（可能是在別台電腦或別的 Windows 帳號建立的）。",
        HttpRequestException => "連不上伺服器，請確認網路是否正常。",
        TaskCanceledException or TimeoutException => "等了太久都沒有回應。",
        Win32Exception => "Windows 沒辦法執行這個動作。",
        _ => "發生了沒有預料到的狀況。",
    };

    /// <summary>「原因：…」這一段，接在錯誤訊息最後面</summary>
    public static string ReasonLine(Exception ex) => "原因：" + Reason(ex);
}
