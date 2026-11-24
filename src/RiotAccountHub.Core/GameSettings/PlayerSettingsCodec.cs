using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace RiotAccountHub.Core.GameSettings;

/// <summary>
/// VALORANT 遊戲設定（Ares.PlayerSettings）的編碼與解碼。
///
/// 在使用者電腦上實際驗證過的格式：
///   伺服器回傳的 data 欄位 = base64( raw DEFLATE( JSON 文字 ) )
///   JSON 最外層有：actionMappings（鍵位）、axisMappings、boolSettings、floatSettings（含靈敏度）、
///   intSettings、stringSettings（含準星）、settingsProfileData、settingsProfiles、roamingSetttingsVersion
/// </summary>
public static class PlayerSettingsCodec
{
    /// <summary>base64 + raw deflate → JSON 文字</summary>
    public static string Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        using var input = new MemoryStream(bytes);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>JSON 文字 → raw deflate + base64</summary>
    public static string Encode(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            deflate.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// 檢查內容是不是看起來正常的遊戲設定，避免把壞掉的資料寫進帳號。
    /// </summary>
    public static bool LooksValid(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return false;
            // 至少要有幾個我們認得的區塊
            return obj["floatSettings"] is JsonArray && obj["actionMappings"] is JsonArray;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>兩份設定內容是否相同（忽略排版與項目順序的差異）</summary>
    public static bool AreEquivalent(string a, string b)
    {
        try
        {
            return JsonNode.DeepEquals(Normalize(JsonNode.Parse(a)), Normalize(JsonNode.Parse(b)));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>把各個設定清單排序，這樣「內容一樣但順序不同」也會被視為相同</summary>
    private static JsonNode? Normalize(JsonNode? node)
    {
        if (node is not JsonObject obj) return node;
        foreach (var (key, value) in obj.ToList())
        {
            if (value is not JsonArray array) continue;
            var sorted = array
                .Select(item => item?.DeepClone())
                .OrderBy(item => item?.ToJsonString(), StringComparer.Ordinal)
                .ToList();
            obj[key] = new JsonArray(sorted.ToArray());
        }
        return obj;
    }

    /// <summary>
    /// 合併：大部分設定用 source（要套用的設定檔），
    /// 但「跟帳號本身有關的紀錄」保留 target（目標帳號原本的值），例如：
    ///   LastSeen…（看過哪些公告 / 活動）、LastAccepted…（同意過的行為準則版本）
    /// 這些不是玩家設定，複製過去可能讓目標帳號跳過該看的公告。
    /// </summary>
    public static string MergeKeepingAccountState(string sourceJson, string targetJson)
    {
        var source = JsonNode.Parse(sourceJson) as JsonObject ?? throw new InvalidDataException("設定檔格式不正確");
        var target = JsonNode.Parse(targetJson) as JsonObject;

        foreach (var section in new[] { "boolSettings", "floatSettings", "intSettings", "stringSettings" })
        {
            if (source[section] is not JsonArray srcArray) continue;

            // 移除 source 裡的帳號紀錄
            for (var i = srcArray.Count - 1; i >= 0; i--)
            {
                if (IsAccountState(srcArray[i])) srcArray.RemoveAt(i);
            }

            // 加回 target 原本的帳號紀錄
            if (target?[section] is JsonArray targetArray)
            {
                foreach (var item in targetArray)
                {
                    if (IsAccountState(item)) srcArray.Add(item?.DeepClone());
                }
            }
        }

        return source.ToJsonString();
    }

    private static bool IsAccountState(JsonNode? item)
    {
        // 先確認形狀對（是一個物件，settingEnum 是文字）才讀；形狀不對就當作不是帳號紀錄
        if (item is not JsonObject obj || obj["settingEnum"] is not JsonValue value || !value.TryGetValue<string>(out var name))
        {
            return false;
        }
        var shortName = name.Contains("::") ? name[(name.LastIndexOf("::", StringComparison.Ordinal) + 2)..] : name;
        return shortName.StartsWith("LastSeen", StringComparison.Ordinal) ||
               shortName.StartsWith("LastAccepted", StringComparison.Ordinal);
    }
}
