using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiotAccountHub.Core.Storage;

/// <summary>
/// 讀寫 JSON 檔案的共用設定。
/// </summary>
internal static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,                                   // 排版整齊，打開來人也看得懂
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // 中文直接存成中文，不要變成 中 這種編碼
        Converters = { new JsonStringEnumConverter() },         // 列舉存成文字(例如 "Google")
    };

    public static T? Read<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static void Write<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        AtomicFile.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// 檔案壞掉讀不出來時，把它改名保留下來（例如 accounts.corrupt-20261123-101500.json），
    /// 讓程式可以繼續用，同時不會把使用者的資料直接蓋掉。
    /// </summary>
    public static string QuarantineCorruptFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dest = Path.Combine(Path.GetDirectoryName(path)!, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(path, dest, overwrite: true);
        return dest;
    }
}
