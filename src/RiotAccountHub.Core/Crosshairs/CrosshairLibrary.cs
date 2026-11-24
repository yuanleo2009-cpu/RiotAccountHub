using System.Text.Json;
using System.Text.Json.Serialization;
using RiotAccountHub.Core.Storage;

namespace RiotAccountHub.Core.Crosshairs;

/// <summary>一個準星（使用者自己加的，或程式附的範例樣式）</summary>
public sealed class SavedCrosshair
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsFavorite { get; set; }

    /// <summary>程式附的範例樣式（不能刪除、不會存進使用者清單）</summary>
    [JsonIgnore]
    public bool IsExample { get; init; }
}

/// <summary>
/// 準星清單的存檔：%AppData%\RiotAccountHub\crosshairs.json
///   mine：使用者自己新增的準星（程式更新也不會被覆蓋）
///   favoriteExampleIds：被收藏的範例樣式
/// </summary>
public sealed class CrosshairLibrary
{
    private readonly string _file;

    public CrosshairLibrary(string file)
    {
        _file = file;
    }

    /// <summary>
    /// 程式附的範例樣式：幾種常見造型，由本程式自己設計（不是從任何網站收集的）。
    /// 用來讓清單一開始不是空的，也方便比較不同樣式。
    /// </summary>
    public static readonly IReadOnlyList<SavedCrosshair> Examples = new[]
    {
        Example("ex-default", "遊戲預設", "0;P"),
        Example("ex-dot", "小圓點", "0;P;h;0;d;1;z;2;0b;0;1b;0"),
        Example("ex-dot-outline", "紅點加外框", "0;P;c;7;h;1;t;1;o;1;d;1;z;3;0b;0;1b;0"),
        Example("ex-small-cross", "青色小十字", "0;P;c;5;h;0;0l;3;0o;2;0a;1;0f;0;1b;0"),
        Example("ex-thin-cross", "黃色細十字", "0;P;c;4;h;0;0t;1;0l;5;0o;2;0a;1;0f;0;1b;0"),
        Example("ex-cross-dot", "綠十字加中心點", "0;P;c;1;h;1;o;1;d;1;z;1;0l;4;0o;2;0a;1;0f;0;1b;0"),
        Example("ex-big-cross", "白色大十字", "0;P;h;1;t;1;o;1;0t;2;0l;7;0o;3;0a;1;0f;0;1b;0"),
        Example("ex-tall-cross", "粉紅長直線", "0;P;c;6;h;0;0t;2;0l;3;0v;7;0g;1;0o;2;0a;1;0f;0;1b;0"),
    };

    private static SavedCrosshair Example(string id, string name, string code) =>
        new() { Id = id, Name = name, Code = code, IsExample = true, CreatedUtc = DateTime.MinValue };

    private sealed class Data
    {
        public List<SavedCrosshair> Mine { get; set; } = new();
        public List<string> FavoriteExampleIds { get; set; } = new();
    }

    /// <summary>讀取全部準星：使用者的在前，範例樣式在後</summary>
    public List<SavedCrosshair> LoadAll()
    {
        var data = Load();
        var favorites = new HashSet<string>(data.FavoriteExampleIds);
        var list = new List<SavedCrosshair>(data.Mine.OrderByDescending(c => c.CreatedUtc));
        foreach (var ex in Examples)
        {
            list.Add(new SavedCrosshair
            {
                Id = ex.Id,
                Name = ex.Name,
                Code = ex.Code,
                CreatedUtc = ex.CreatedUtc,
                IsExample = true,
                IsFavorite = favorites.Contains(ex.Id),
            });
        }
        return list;
    }

    /// <summary>新增使用者自己的準星</summary>
    public SavedCrosshair Add(string name, string code)
    {
        var data = Load();
        var item = new SavedCrosshair { Name = name.Trim(), Code = code.Trim() };
        data.Mine.Add(item);
        Save(data);
        return item;
    }

    /// <summary>刪除使用者自己的準星（範例樣式不能刪）</summary>
    public void Delete(string id)
    {
        var data = Load();
        data.Mine.RemoveAll(c => c.Id == id);
        Save(data);
    }

    /// <summary>設定收藏</summary>
    public void SetFavorite(string id, bool favorite)
    {
        var data = Load();
        var mine = data.Mine.FirstOrDefault(c => c.Id == id);
        if (mine is not null)
        {
            mine.IsFavorite = favorite;
        }
        else if (Examples.Any(e => e.Id == id))
        {
            data.FavoriteExampleIds.Remove(id);
            if (favorite) data.FavoriteExampleIds.Add(id);
        }
        Save(data);
    }

    private Data Load()
    {
        if (!File.Exists(_file)) return new Data();
        try
        {
            var data = JsonStore.Read<Data>(_file) ?? new Data();

            // 檔案被手動改過時可能缺欄位：先補好，之後才不會出錯
            data.Mine ??= new();
            data.FavoriteExampleIds ??= new();
            data.Mine.RemoveAll(c => c is null);
            foreach (var c in data.Mine)
            {
                c.Name ??= "";
                c.Code ??= "";
            }
            return data;
        }
        catch (JsonException ex)
        {
            AppLog.Error("crosshairs.json 讀取失敗，已另存並重新開始", ex);
            try
            {
                JsonStore.QuarantineCorruptFile(_file);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("壞掉的 crosshairs.json 無法另存", moveEx);
            }
            return new Data();
        }
    }

    private void Save(Data data) => JsonStore.Write(_file, data);
}
