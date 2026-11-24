using RiotAccountHub.Core.Riot;

namespace RiotAccountHub.Tests;

/// <summary>關閉 Riot Client 影片和聲音的測試（格式照實際的 RiotClientSettings.yaml）</summary>
public sealed class RiotClientMediaTests : IDisposable
{
    private const string Hub = "08576ee94efdc0fab77ace996f69fd0cdefa3d58e1fc59070a54019ad70944f6";
    private const string OtherHub = "1111111111111111111111111111111111111111111111111111111111111111";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rah-media-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // 影片和聲音都開著（新增帳號當時的狀態）：沒有 media_settings，volume 沒有靜音
    private const string MediaOn =
        "install:\n" +
        "    globals:\n" +
        "        locale: \"en_US\"\n" +
        "        region: \"JP\"\n" +
        "    player-affinity:\n" +
        "        product:\n" +
        "            valorant:\n" +
        "                live: \"na\"\n" +
        "    volume_settings:\n" +
        "        videos:\n" +
        "            muted: false\n" +
        "            volume: 0.5\n" +
        "    whats-new-modal-dismissed:\n" +
        "        " + OtherHub + ": 1\n";

    [Fact]
    public void 影片和聲音都會被關掉_其他設定不變()
    {
        var result = RiotClientMedia.TurnOffInYaml(MediaOn);

        Assert.Contains("    media_settings:\n        game_hubs:\n", result);
        Assert.Contains("            " + Hub + ":\n                video: false", result);
        Assert.Contains("            " + OtherHub + ":\n                video: false", result); // 看過的其他遊戲頁面也關
        Assert.Contains("    volume_settings:\n        videos:\n            muted: true\n            volume: 0.5", result); // 保留原本的音量
        Assert.DoesNotContain("muted: false", result);

        // 其他設定原封不動
        Assert.StartsWith("install:\n    globals:\n        locale: \"en_US\"\n        region: \"JP\"\n    player-affinity:", result);
        Assert.Contains("                live: \"na\"\n", result);
        Assert.Contains("    whats-new-modal-dismissed:\n        " + OtherHub + ": 1\n", result);
        Assert.EndsWith("\n", result);
    }

    [Fact]
    public void 已經關掉的再處理一次_結果一樣()
    {
        var once = RiotClientMedia.TurnOffInYaml(MediaOn);
        var twice = RiotClientMedia.TurnOffInYaml(once);
        Assert.Equal(once, twice);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "media_settings:"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "volume_settings:"));
    }

    [Fact]
    public void 保留Windows換行符號()
    {
        var crlf = MediaOn.Replace("\n", "\r\n");
        var result = RiotClientMedia.TurnOffInYaml(crlf);
        Assert.DoesNotContain("\r\r", result);
        Assert.Equal(result.Split("\r\n").Length - 1, result.Split('\n').Length - 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("something: else\n")]
    [InlineData("not yaml at all")]
    public void 看不懂的格式就不改(string text)
    {
        Assert.Equal(text, RiotClientMedia.TurnOffInYaml(text));
    }

    [Fact]
    public void 改檔案_檔案不存在時不會出錯()
    {
        Assert.False(RiotClientMedia.TurnOff(_root));

        var path = Path.Combine(_root, RiotClientMedia.SettingsFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, MediaOn);

        Assert.True(RiotClientMedia.TurnOff(_root));
        Assert.Contains("muted: true", File.ReadAllText(path));
        Assert.False(RiotClientMedia.TurnOff(_root)); // 已經是關的，不用再寫
        Assert.False(File.Exists(path + ".rah-tmp"));
    }
}
