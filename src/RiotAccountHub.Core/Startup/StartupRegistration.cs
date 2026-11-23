using Microsoft.Win32;

namespace RiotAccountHub.Core.Startup;

/// <summary>
/// 「開機時自動啟動」的開關。
///
/// 做法是 Windows 的標準方式：在「目前使用者」的登錄檔
///   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
/// 寫入一筆「RiotAccountHub = "程式路徑" --startup」。登入 Windows 時就會自動執行。
/// 只影響目前這個 Windows 使用者，不需要系統管理員權限；
/// 在「工作管理員 → 開機應用程式」裡也看得到、可以關掉。
/// </summary>
public sealed class StartupRegistration
{
    /// <summary>開機自動啟動時帶的參數：程式看到它就會縮小開啟，不會突然跳出視窗</summary>
    public const string StartupArgument = "--startup";

    private const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "RiotAccountHub";

    private readonly string _keyPath;
    private readonly string _valueName;

    /// <summary>參數只有單元測試會用到（換成測試專用的位置，避免動到真正的開機設定）</summary>
    public StartupRegistration(string keyPath = DefaultKeyPath, string valueName = DefaultValueName)
    {
        _keyPath = keyPath;
        _valueName = valueName;
    }

    /// <summary>目前登錄的指令；沒有開啟就是 null</summary>
    public string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(_valueName) is string s && s.Length > 0 ? s : null;
    }

    /// <summary>有沒有開啟開機自動啟動</summary>
    public bool IsEnabled() => RegisteredCommand() is not null;

    /// <summary>開啟：登入 Windows 時自動執行 exePath</summary>
    public void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue(_valueName, BuildCommand(exePath), RegistryValueKind.String);
    }

    /// <summary>關閉</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 如果已經開啟、但登錄的程式檔案不見了（例如程式搬到別的資料夾），改成現在的路徑。
    /// </summary>
    public void RepairPath(string currentExePath)
    {
        var registered = ExePathFromCommand(RegisteredCommand());
        if (registered is not null && !File.Exists(registered))
        {
            Enable(currentExePath);
        }
    }

    /// <summary>登錄檔裡要寫的指令："程式路徑" --startup</summary>
    public static string BuildCommand(string exePath) => $"\"{exePath}\" {StartupArgument}";

    /// <summary>從指令裡取出程式路徑（引號中間那一段）</summary>
    public static string? ExePathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}
