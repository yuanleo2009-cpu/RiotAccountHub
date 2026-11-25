using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Updates;

namespace RiotAccountHub.Tests;

/// <summary>程式內更新：版本號、update.json、下載檢查、替換程式檔、找舊版</summary>
public sealed class UpdateTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string ManifestJson(string version, string url, string sha, long size, string notes = "[\"新功能\"]") =>
        $"{{\"version\":\"{version}\",\"date\":\"2026-11-25\",\"notes\":{notes},\"url\":\"{url}\",\"sha256\":\"{sha}\",\"size\":{size}," +
        "\"page\":\"https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/tag/v1.0.5\"}";

    private const string GoodUrl = "https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/download/v1.0.5/RiotAccountHub-update-v1.0.5.zip";
    private static readonly string FakeSha = new('a', 64);

    // ---------------- 版本號 ----------------

    [Theory]
    [InlineData("1.0.4", "1.0.4")]
    [InlineData("v1.0.4", "1.0.4")]
    [InlineData("1.0.4+197bf26abc", "1.0.4")]
    [InlineData("1.0.4.0", "1.0.4")]
    [InlineData("1.2", "1.2.0")]
    [InlineData(" V2.10.3 ", "2.10.3")]
    public void 版本號_各種寫法都看得懂(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), AppVersion.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("最新版")]
    [InlineData("1")]
    public void 版本號_看不懂的回傳null(string? text)
    {
        Assert.Null(AppVersion.Parse(text));
    }

    [Fact]
    public void 版本號_比較大小是照數字不是照文字()
    {
        Assert.True(AppVersion.Parse("1.0.10") > AppVersion.Parse("1.0.9"));
    }

    // ---------------- update.json ----------------

    [Fact]
    public void 說明檔_正常的內容讀得出來()
    {
        var m = UpdateManifest.Parse(ManifestJson("1.0.5", GoodUrl, FakeSha, 1234, "[\"第一點\", \"\", 5, \"第二點\"]"));

        Assert.NotNull(m);
        Assert.Equal(new Version(1, 0, 5), m!.Version);
        Assert.Equal(new[] { "第一點", "第二點" }, m.Notes); // 空的和不是文字的跳過
        Assert.Equal(1234, m.Size);
        Assert.Equal(GoodUrl, m.PackageUrl.ToString());
    }

    [Theory]
    [InlineData("http://github.com/x/y/releases/download/v1/a.zip")]   // 不是 https
    [InlineData("https://evil.example.com/a.zip")]                     // 不是 GitHub
    [InlineData("https://github.com.evil.com/a.zip")]
    [InlineData("http://127.0.0.1:8080/a.zip")]                        // 本機只有測試時才可以
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void 說明檔_更新包不在GitHub上就不接受(string url)
    {
        Assert.Null(UpdateManifest.Parse(ManifestJson("1.0.5", url, FakeSha, 1234)));
    }

    [Fact]
    public void 說明檔_測試模式才接受本機網址()
    {
        var json = ManifestJson("1.0.5", "http://127.0.0.1:8080/a.zip", FakeSha, 1234);
        Assert.Null(UpdateManifest.Parse(json, allowLocalTest: false));
        Assert.NotNull(UpdateManifest.Parse(json, allowLocalTest: true));
    }

    [Theory]
    [InlineData("1.0.5", "abc", 1234)]                                 // 指紋格式不對
    [InlineData("1.0.5", null, 0)]                                     // 大小是 0
    [InlineData("1.0.5", null, 999999999999)]                          // 大小太誇張
    [InlineData("新版", null, 1234)]                                   // 版本號看不懂
    public void 說明檔_內容不合理就不接受(string version, string? sha, long size)
    {
        Assert.Null(UpdateManifest.Parse(ManifestJson(version, GoodUrl, sha ?? FakeSha, size)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ 壞掉")]
    public void 說明檔_格式壞掉也不會出錯(string? json)
    {
        Assert.Null(UpdateManifest.Parse(json));
    }

    // ---------------- 下載與檢查（用本機的小伺服器假裝是 GitHub） ----------------

    private static byte[] MakePackage(byte[] exe)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("RiotAccountHub.exe").Open()) s.Write(exe);
            using (var s = zip.CreateEntry("更新說明.txt").Open()) s.Write("說明"u8);
        }
        return ms.ToArray();
    }

    private UpdateService ServiceFor(LocalServer server, Version reportedExeVersion) =>
        new(new Uri(server.Url("/update.json"))) { ReadExeVersion = _ => reportedExeVersion };

    [Fact]
    public async Task 下載_檔案正確時解開新版程式檔()
    {
        var exe = Encoding.ASCII.GetBytes("MZ fake new version");
        var package = MakePackage(exe);
        using var server = new LocalServer();
        server.Files["/pkg.zip"] = package;
        server.Files["/update.json"] = Encoding.UTF8.GetBytes(ManifestJson("1.0.5", server.Url("/pkg.zip"), Sha(package), package.Length));
        var service = ServiceFor(server, new Version(1, 0, 5));

        var manifest = await service.FetchLatestAsync();
        Assert.NotNull(manifest);

        var reported = new List<double>();
        var path = await service.DownloadAsync(manifest!, _temp.Sub("updates"), percent: new SyncProgress<double>(reported.Add));

        Assert.Equal(exe, File.ReadAllBytes(path));
        Assert.Equal(1.0, reported.Last(), 3);
        Assert.False(File.Exists(Path.Combine(_temp.Root, "updates", "package.zip"))); // 下載的壓縮檔用完就刪
    }

    [Fact]
    public async Task 下載_指紋不對就不更新()
    {
        var package = MakePackage("MZ"u8.ToArray());
        using var server = new LocalServer();
        server.Files["/pkg.zip"] = package;
        server.Files["/update.json"] = Encoding.UTF8.GetBytes(ManifestJson("1.0.5", server.Url("/pkg.zip"), FakeSha, package.Length));
        var service = ServiceFor(server, new Version(1, 0, 5));

        var manifest = (await service.FetchLatestAsync())!;
        var ex = await Assert.ThrowsAsync<UserFacingException>(() => service.DownloadAsync(manifest, _temp.Sub("updates")));
        Assert.Equal("更新包檢查沒通過", ex.Title);
        Assert.False(File.Exists(Path.Combine(_temp.Root, "updates", "RiotAccountHub.exe")));
    }

    [Fact]
    public async Task 下載_大小跟說明檔不一樣就不更新()
    {
        var package = MakePackage("MZ"u8.ToArray());
        using var server = new LocalServer();
        server.Files["/pkg.zip"] = package;
        server.Files["/update.json"] = Encoding.UTF8.GetBytes(ManifestJson("1.0.5", server.Url("/pkg.zip"), Sha(package), package.Length + 10));
        var service = ServiceFor(server, new Version(1, 0, 5));

        var manifest = (await service.FetchLatestAsync())!;
        await Assert.ThrowsAsync<UserFacingException>(() => service.DownloadAsync(manifest, _temp.Sub("updates")));
    }

    [Fact]
    public async Task 下載_程式檔版本跟說明檔不一樣就不更新()
    {
        var package = MakePackage("MZ"u8.ToArray());
        using var server = new LocalServer();
        server.Files["/pkg.zip"] = package;
        server.Files["/update.json"] = Encoding.UTF8.GetBytes(ManifestJson("1.0.5", server.Url("/pkg.zip"), Sha(package), package.Length));
        var service = ServiceFor(server, new Version(1, 0, 4)); // 包裡其實是舊版

        var manifest = (await service.FetchLatestAsync())!;
        var ex = await Assert.ThrowsAsync<UserFacingException>(() => service.DownloadAsync(manifest, _temp.Sub("updates")));
        Assert.Contains("版本", ex.Message);
    }

    [Fact]
    public async Task 檢查更新_連不上時回傳null不會出錯()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        var service = new UpdateService(new Uri($"http://127.0.0.1:{port}/update.json"));

        Assert.Null(await service.FetchLatestAsync());
    }

    [Fact]
    public void 檢查更新_不是本機的網址一律改回GitHub()
    {
        // 就算有人傳了別的網址進來，也不會拿去用（只有本機測試網址例外）
        Assert.Equal(UpdateService.DefaultManifestUrl, new UpdateService(new Uri("http://evil.example.com/update.json")).ManifestUrl);
        Assert.Equal(UpdateService.DefaultManifestUrl, new UpdateService(new Uri("https://127.0.0.1/update.json")).ManifestUrl);
        Assert.Equal(UpdateService.DefaultManifestUrl, new UpdateService().ManifestUrl);
        Assert.Equal("http://127.0.0.1:5000/update.json", new UpdateService(new Uri("http://127.0.0.1:5000/update.json")).ManifestUrl.ToString());
    }

    // ---------------- 替換程式檔 ----------------

    [Fact]
    public void 替換_新版放進去_舊版留成old()
    {
        var dir = _temp.Sub("app");
        var target = Path.Combine(dir, "RiotAccountHub.exe");
        var newExe = Path.Combine(_temp.Sub("dl"), "RiotAccountHub.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(newExe, "new");

        SelfReplacer.Replace(newExe, target);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.Equal("old", File.ReadAllText(target + ".old"));
        Assert.Equal(1, SelfReplacer.CleanupBackups(target));
        Assert.False(File.Exists(target + ".old"));
    }

    [Fact]
    public void 替換_新版讀不到時_舊版放回原位()
    {
        var dir = _temp.Sub("app");
        var target = Path.Combine(dir, "RiotAccountHub.exe");
        var newExe = Path.Combine(_temp.Sub("dl"), "RiotAccountHub.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(newExe, "new");

        using (new FileStream(newExe, FileMode.Open, FileAccess.Read, FileShare.None)) // 被別的程式鎖住
        {
            Assert.ThrowsAny<IOException>(() => SelfReplacer.Replace(newExe, target));
        }

        Assert.Equal("old", File.ReadAllText(target)); // 程式還是原本那一版，可以正常開
    }

    [Fact]
    public void 替換_新版檔案不見了_什麼都不動()
    {
        var target = Path.Combine(_temp.Sub("app"), "RiotAccountHub.exe");
        File.WriteAllText(target, "old");

        Assert.Throws<FileNotFoundException>(() => SelfReplacer.Replace(Path.Combine(_temp.Root, "missing.exe"), target));
        Assert.Equal("old", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".old"));
    }

    [Fact]
    public void 替換_上次留下的old還被佔用_也能更新()
    {
        var dir = _temp.Sub("app");
        var target = Path.Combine(dir, "RiotAccountHub.exe");
        var newExe = Path.Combine(_temp.Sub("dl"), "RiotAccountHub.exe");
        File.WriteAllText(target, "v2");
        File.WriteAllText(target + ".old", "v1");
        File.WriteAllText(newExe, "v3");

        using (new FileStream(target + ".old", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            SelfReplacer.Replace(newExe, target);
        }

        Assert.Equal("v3", File.ReadAllText(target));
        Assert.Equal(2, SelfReplacer.CleanupBackups(target)); // .old 和 .old-日期 都清掉
    }

    [Fact]
    public void 資料夾能不能寫入()
    {
        Assert.True(SelfReplacer.CanWriteTo(_temp.Root));
        Assert.False(SelfReplacer.CanWriteTo(Path.Combine(_temp.Root, "不存在")));
        Assert.False(SelfReplacer.CanWriteTo(null));
    }

    // ---------------- 找舊版 ----------------

    [Fact]
    public void 找舊版_跳過自己和比較新的_優先選正在執行的()
    {
        var self = @"C:\Downloads\RiotAccountHub.exe";
        var v = new Version(1, 0, 4);
        var candidates = new (string, Version?, int?)[]
        {
            (@"C:\Downloads\RiotAccountHub.exe", new Version(1, 0, 3), 11),  // 自己
            (@"C:\Newer\RiotAccountHub.exe", new Version(1, 0, 5), 12),      // 比自己新
            (@"C:\Same\RiotAccountHub.exe", new Version(1, 0, 4), null),     // 一樣新
            (@"C:\NotApp\RiotAccountHub.exe", null, null),                   // 不是這個程式
            (@"C:\Installed\RiotAccountHub.exe", new Version(1, 0, 2), null),
            (@"C:\Running\RiotAccountHub.exe", new Version(1, 0, 3), 42),
        };

        var found = InstalledCopies.ChooseOlder(candidates, self, v);

        Assert.NotNull(found);
        Assert.Equal(@"C:\Running\RiotAccountHub.exe", found!.Path);
        Assert.Equal(42, found.RunningPid);
    }

    [Fact]
    public void 找舊版_沒有比較舊的就是null()
    {
        var found = InstalledCopies.ChooseOlder(
            new (string, Version?, int?)[] { (@"C:\A\RiotAccountHub.exe", new Version(1, 0, 4), null) },
            @"C:\B\RiotAccountHub.exe", new Version(1, 0, 4));
        Assert.Null(found);
    }

    [Fact]
    public void 找舊版_不是這個程式的檔案讀不到版本()
    {
        var notApp = Path.Combine(_temp.Root, "RiotAccountHub.exe");
        File.WriteAllText(notApp, "not a real exe");
        Assert.Null(InstalledCopies.ReadVersion(notApp));
        Assert.Null(InstalledCopies.ReadVersion(Path.Combine(_temp.Root, "missing.exe")));
    }

    /// <summary>馬上回報的 IProgress（內建的 Progress 會晚一點才回報，測試不好檢查）</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>測試用的小網頁伺服器：只在本機，照路徑回傳指定的檔案</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    public Dictionary<string, byte[]> Files { get; } = new();
    public int Port { get; }

    public LocalServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(LoopAsync);
    }

    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var buffer = new byte[4096];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
                if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n")) break;
            }
            var path = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0].Split(' ').ElementAtOrDefault(1) ?? "/";
            var found = Files.TryGetValue(path, out var body);
            body ??= "not found"u8.ToArray();
            var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            stream.Write(Encoding.ASCII.GetBytes(head));
            stream.Write(body);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
