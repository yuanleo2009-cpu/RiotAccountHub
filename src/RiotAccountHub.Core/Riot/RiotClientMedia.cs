using System.Text;
using System.Text.RegularExpressions;

namespace RiotAccountHub.Core.Riot;

/// <summary>
/// 關掉 Riot Client 主畫面上的「背景影片」和「影片聲音」。
///
/// 這兩個開關（Riot Client 右下角的喇叭、攝影機按鈕）存在
///   %LocalAppData%\Riot Games\Riot Client\Config\RiotClientSettings.yaml
/// 實際看到的格式：
///   install:
///       media_settings:
///           game_hubs:
///               （遊戲頁面的編號）:
///                   video: false      ← 背景影片關閉
///       volume_settings:
///           videos:
///               muted: true          ← 聲音關閉
///               volume: 1
///
/// 為什麼需要：每個帳號的快照裡都有一份這個檔案，切換帳號時會被換回「新增帳號當時」的版本，
/// 所以使用者關掉的影片和聲音又會跑回來。切換完成後（Riot Client 還關著）呼叫 TurnOff 就好。
///
/// 只改這兩個區塊，其他內容原封不動；看不懂檔案格式時什麼都不改。
/// </summary>
public static class RiotClientMedia
{
    /// <summary>設定檔的位置（相對於 Riot Client 資料夾）</summary>
    public const string SettingsFile = @"Config\RiotClientSettings.yaml";

    /// <summary>VALORANT 在 Riot Client 裡的遊戲頁面編號（在 RiotClientSettings.yaml 實際觀察到）</summary>
    public static readonly IReadOnlyList<string> KnownHubIds = new[]
    {
        "08576ee94efdc0fab77ace996f69fd0cdefa3d58e1fc59070a54019ad70944f6",
    };

    private static readonly Regex HubIdPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 把 Riot Client 的影片和聲音設成關閉。
    /// 只能在 Riot Client 完全關閉時呼叫。檔案不存在就什麼都不做。
    /// 回傳 true 代表有修改檔案。
    /// </summary>
    public static bool TurnOff(string liveRoot)
    {
        var path = Path.Combine(liveRoot, SettingsFile);
        if (!File.Exists(path)) return false;

        var text = File.ReadAllText(path);
        var updated = TurnOffInYaml(text);
        if (updated == text) return false;

        // 先寫到暫存檔，再一次換掉，避免寫到一半留下壞掉的檔案
        var temp = path + ".rah-tmp";
        File.WriteAllText(temp, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>
    /// 在設定檔文字裡把影片和聲音設成關閉，回傳改好的文字。
    /// 找不到 install: 區塊（看不懂的格式）就原樣回傳。
    /// </summary>
    public static string TurnOffInYaml(string yaml)
    {
        var newline = yaml.Contains("\r\n") ? "\r\n" : "\n";
        var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();

        var installIndex = lines.FindIndex(l => l.TrimEnd() == "install:");
        if (installIndex < 0) return yaml;

        // install: 區塊到哪裡結束（下一個沒有縮排的行）
        var end = installIndex + 1;
        while (end < lines.Count && (lines[end].Trim().Length == 0 || lines[end][0] == ' ')) end++;

        // 子項目的縮排（Riot 用 4 個空白）
        var childIndent = 4;
        for (var i = installIndex + 1; i < end; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            childIndent = Indent(lines[i]);
            break;
        }
        if (childIndent <= 0) return yaml;

        var hubIds = new SortedSet<string>(KnownHubIds, StringComparer.Ordinal);
        string? volume = null;
        var kept = new List<string>();

        for (var i = installIndex + 1; i < end; i++)
        {
            var line = lines[i];
            var key = Indent(line) == childIndent ? KeyOf(line) : null;

            if (key is "media_settings" or "volume_settings")
            {
                // 整個舊區塊拿掉（等一下換成新的），順便記下裡面的遊戲頁面編號和音量
                var j = i + 1;
                while (j < end && (lines[j].Trim().Length == 0 || Indent(lines[j]) > childIndent))
                {
                    var inner = lines[j];
                    if (key == "media_settings" && Indent(inner) == childIndent * 3 && KeyOf(inner) is { } hub && HubIdPattern.IsMatch(hub))
                    {
                        hubIds.Add(hub);
                    }
                    if (key == "volume_settings" && Indent(inner) == childIndent * 3 && KeyOf(inner) == "volume")
                    {
                        volume = ValueOf(inner);
                    }
                    j++;
                }
                // 區塊後面的空行不屬於這個區塊，留著
                while (j > i + 1 && lines[j - 1].Trim().Length == 0) j--;
                i = j - 1;
                continue;
            }

            if (key == "whats-new-modal-dismissed")
            {
                // 這裡也會列出看過的遊戲頁面編號
                for (var j = i + 1; j < end && (lines[j].Trim().Length == 0 || Indent(lines[j]) > childIndent); j++)
                {
                    if (Indent(lines[j]) == childIndent * 2 && KeyOf(lines[j]) is { } hub && HubIdPattern.IsMatch(hub)) hubIds.Add(hub);
                }
            }

            kept.Add(line);
        }

        // 區塊最後面的空行留在最後
        var trailingBlank = new List<string>();
        while (kept.Count > 0 && kept[^1].Trim().Length == 0)
        {
            trailingBlank.Insert(0, kept[^1]);
            kept.RemoveAt(kept.Count - 1);
        }

        string Pad(int level) => new(' ', childIndent * level);
        var block = new List<string> { Pad(1) + "media_settings:", Pad(2) + "game_hubs:" };
        foreach (var hub in hubIds)
        {
            block.Add(Pad(3) + hub + ":");
            block.Add(Pad(4) + "video: false");
        }
        block.Add(Pad(1) + "volume_settings:");
        block.Add(Pad(2) + "videos:");
        block.Add(Pad(3) + "muted: true");
        block.Add(Pad(3) + "volume: " + (string.IsNullOrWhiteSpace(volume) ? "1" : volume));

        var result = new List<string>();
        result.AddRange(lines.Take(installIndex + 1));
        result.AddRange(kept);
        result.AddRange(block);
        result.AddRange(trailingBlank);
        result.AddRange(lines.Skip(end));
        return string.Join(newline, result);
    }

    private static int Indent(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    /// <summary>「    key: value」→ key（去掉引號）；不是 key: 格式就回傳 null</summary>
    private static string? KeyOf(string line)
    {
        var text = line.Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0) return null;
        return text[..colon].Trim().Trim('"', '\'');
    }

    private static string? ValueOf(string line)
    {
        var text = line.Trim();
        var colon = text.IndexOf(':');
        return colon < 0 ? null : text[(colon + 1)..].Trim();
    }
}
