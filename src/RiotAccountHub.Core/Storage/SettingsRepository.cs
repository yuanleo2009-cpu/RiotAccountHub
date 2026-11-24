using System.Text.Json;
using RiotAccountHub.Core.Models;

namespace RiotAccountHub.Core.Storage;

/// <summary>
/// 負責讀寫程式設定(settings.json)。
/// </summary>
public sealed class SettingsRepository
{
    private readonly AppPaths _paths;

    public SettingsRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile)) return new AppSettings();
        try
        {
            return JsonStore.Read<AppSettings>(_paths.SettingsFile) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            // 設定壞掉不是大問題：另存起來，改用預設值
            AppLog.Error("settings.json 讀取失敗，改用預設設定", ex);
            try
            {
                JsonStore.QuarantineCorruptFile(_paths.SettingsFile);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("壞掉的 settings.json 無法另存", moveEx);
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) => JsonStore.Write(_paths.SettingsFile, settings);
}
