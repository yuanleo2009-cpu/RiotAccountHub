using RiotAccountHub.Core;
using RiotAccountHub.Core.Models;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Tests;

/// <summary>帳號清單與設定存檔的測試</summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly AppPaths _paths;

    public RepositoryTests()
    {
        _paths = new AppPaths(_temp.Root);
        _paths.EnsureCreated();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void 帳號清單_存檔後讀回來順序與內容不變()
    {
        var repo = new AccountRepository(_paths);
        var accounts = new List<Account>
        {
            new() { GameName = "主號", TagLine = "TW1", LoginMethod = LoginMethod.Google, Note = "主號" },
            new() { GameName = "Smurf", TagLine = "0001", LoginMethod = LoginMethod.Facebook },
        };

        repo.Save(accounts);
        var loaded = repo.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("主號#TW1", loaded[0].RiotId);
        Assert.Equal(LoginMethod.Facebook, loaded[1].LoginMethod);
        Assert.Equal(accounts[0].Id, loaded[0].Id);
    }

    [Fact]
    public void 帳號清單裡不會有任何密碼或登入資料()
    {
        var repo = new AccountRepository(_paths);
        repo.Save(new[] { new Account { GameName = "A", TagLine = "B" } });

        var json = File.ReadAllText(_paths.AccountsFile);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"GameName\": \"A\"", json); // 中文、英文都直接可讀
    }

    [Fact]
    public void 帳號清單損毀時_另存起來並回報警告()
    {
        File.WriteAllText(_paths.AccountsFile, "{ 這不是正確的 JSON");
        var repo = new AccountRepository(_paths);

        var loaded = repo.Load();

        Assert.Empty(loaded);
        Assert.NotNull(repo.LoadWarning);
        Assert.False(File.Exists(_paths.AccountsFile));
        Assert.Single(Directory.GetFiles(_paths.DataRoot, "accounts.corrupt-*.json"));
    }

    [Fact]
    public void 設定_存檔後讀回來()
    {
        var repo = new SettingsRepository(_paths);
        repo.Save(new AppSettings { RiotClientPath = @"D:\Riot\RiotClientServices.exe" });

        Assert.Equal(@"D:\Riot\RiotClientServices.exe", repo.Load().RiotClientPath);
    }
}
