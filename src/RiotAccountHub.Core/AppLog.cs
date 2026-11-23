using System.Text;

namespace RiotAccountHub.Core;

/// <summary>
/// 非常簡單的記錄檔工具。出問題時可以打開 logs\app.log 看發生了什麼事。
/// 注意：這裡只會記錄「做了什麼步驟」，絕對不會寫入任何登入資料或檔案內容。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logFile;

    /// <summary>程式啟動時呼叫一次，指定記錄檔資料夾</summary>
    public static void Initialize(string logsDir)
    {
        try
        {
            Directory.CreateDirectory(logsDir);
            _logFile = Path.Combine(logsDir, "app.log");

            // 記錄檔超過 1 MB 就換一個新的，避免越來越大
            var info = new FileInfo(_logFile);
            if (info.Exists && info.Length > 1024 * 1024)
            {
                File.Move(_logFile, Path.Combine(logsDir, "app.old.log"), overwrite: true);
            }
        }
        catch
        {
            _logFile = null; // 記錄檔失敗不應該讓程式壞掉
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (_logFile is null) return;
        try
        {
            lock (Gate)
            {
                File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // 寫不進去就算了
        }
    }
}
