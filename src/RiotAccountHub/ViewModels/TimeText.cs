namespace RiotAccountHub.ViewModels;

/// <summary>把時間變成好讀的文字</summary>
public static class TimeText
{
    /// <summary>「今天 14:30」、「昨天 09:10」或「2026/11/20 18:00」</summary>
    public static string FormatWhen(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTime.Today;
        return local.Date == today ? $"今天 {local:HH:mm}"
            : local.Date == today.AddDays(-1) ? $"昨天 {local:HH:mm}"
            : local.ToString("yyyy/MM/dd HH:mm");
    }
}
