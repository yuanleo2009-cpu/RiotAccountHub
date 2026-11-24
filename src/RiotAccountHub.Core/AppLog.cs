using System.Text;

namespace RiotAccountHub.Core;

/// <summary>
/// 非常簡單的記錄檔工具。出問題時可以打開 logs\app.log 看發生了什麼事。
/// 注意：這裡只會記錄「做了什麼步驟」，絕對不會寫入任何登入資料或檔案內容。
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1024 * 1024; // 記錄檔超過 1 MB 就換一個新的
    private const int CheckEvery = 200;        // 每寫 200 行檢查一次大小

    private static readonly object Gate = new();
    private static string? _logFile;
    private static int _writesSinceCheck;

    /// <summary>程式啟動時呼叫一次，指定記錄檔資料夾</summary>
    public static void Initialize(string logsDir)
    {
        try
        {
            Directory.CreateDirectory(logsDir);
            _logFile = Path.Combine(logsDir, "app.log");

            RotateIfTooBig(_logFile);
        }
        catch
        {
            _logFile = null; // 記錄檔失敗不應該讓程式壞掉
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    /// <summary>記錄檔太大就改名成 app.old.log（只保留一份舊的），避免越來越大</summary>
    private static void RotateIfTooBig(string logFile)
    {
        var info = new FileInfo(logFile);
        if (info.Exists && info.Length > MaxBytes)
        {
            File.Move(logFile, Path.Combine(info.DirectoryName!, "app.old.log"), overwrite: true);
        }
    }

    private static void Write(string level, string message)
    {
        if (_logFile is null) return;
        try
        {
            lock (Gate)
            {
                // 程式可能連續開好幾天（開機自動啟動），所以執行中也要定期檢查大小
                if (++_writesSinceCheck >= CheckEvery)
                {
                    _writesSinceCheck = 0;
                    RotateIfTooBig(_logFile);
                }
                File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // 寫不進去就算了
        }
    }
}
