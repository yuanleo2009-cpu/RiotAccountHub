using Microsoft.Win32;
using RiotAccountHub.Core.Startup;

namespace RiotAccountHub.Tests;

/// <summary>
/// 「開機時自動啟動」的測試。
/// 用測試專用的登錄檔位置，不會動到真正的開機設定。
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = @"Software\RiotAccountHub.Tests\Run-" + Guid.NewGuid().ToString("N");
    private readonly StartupRegistration _startup;

    public StartupRegistrationTests()
    {
        _startup = new StartupRegistration(_keyPath, "TestApp");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void 預設是關閉的()
    {
        Assert.False(_startup.IsEnabled());
        Assert.Null(_startup.RegisteredCommand());
    }

    [Fact]
    public void 開啟後_登錄的是加了引號的路徑和啟動參數()
    {
        _startup.Enable(@"C:\Users\測試 使用者\App\RiotAccountHub.exe");

        Assert.True(_startup.IsEnabled());
        Assert.Equal("\"C:\\Users\\測試 使用者\\App\\RiotAccountHub.exe\" --startup", _startup.RegisteredCommand());
    }

    [Fact]
    public void 關閉後就沒有了_重複關閉也不會出錯()
    {
        _startup.Enable(@"C:\App\a.exe");
        _startup.Disable();
        _startup.Disable();

        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void 程式檔案不見時_會改成現在的路徑()
    {
        _startup.Enable(@"C:\不存在的資料夾\old.exe");
        var current = Environment.ProcessPath!; // 一定存在的檔案

        _startup.RepairPath(current);

        Assert.Equal(StartupRegistration.BuildCommand(current), _startup.RegisteredCommand());
    }

    [Fact]
    public void 程式檔案還在_或沒有開啟時_不會亂改()
    {
        _startup.RepairPath(@"C:\new.exe");
        Assert.False(_startup.IsEnabled()); // 沒開啟就不會自己打開

        var existing = Environment.ProcessPath!;
        _startup.Enable(existing);
        _startup.RepairPath(@"C:\new.exe");
        Assert.Equal(StartupRegistration.BuildCommand(existing), _startup.RegisteredCommand());
    }

    [Theory]
    [InlineData("\"C:\\A B\\app.exe\" --startup", @"C:\A B\app.exe")]
    [InlineData(@"C:\app.exe --startup", @"C:\app.exe")]
    [InlineData(@"C:\app.exe", @"C:\app.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void 從指令取出程式路徑(string? command, string? expected)
    {
        Assert.Equal(expected, StartupRegistration.ExePathFromCommand(command));
    }
}
