using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiotAccountHub.Core;
using RiotAccountHub.Core.Crosshairs;
using RiotAccountHub.Helpers;
using RiotAccountHub.Services;

namespace RiotAccountHub.ViewModels;

/// <summary>
/// 「熱門準星」視窗的 ViewModel：
///   我的準星 + 範例樣式的卡片網格、搜尋、收藏、新增、大圖預覽、複製代碼、到 VCRDB 挑選。
/// 這一階段只做「複製代碼」，不會修改任何遊戲設定。
/// </summary>
public sealed partial class CrosshairsViewModel : ObservableObject
{
    /// <summary>複製後跳出的提示：標題</summary>
    public const string CopiedTitle = "已複製準星代碼";

    /// <summary>複製後跳出的提示：教學</summary>
    public const string CopiedHint = "到 Valorant：設定 → 準星 → 匯入設定檔代碼，貼上後按匯入。";

    public const string VcrdbUrl = "https://www.vcrdb.net/";

    private readonly CrosshairLibrary _library;
    private readonly IDialogService _dialogs;

    public CrosshairsViewModel(CrosshairLibrary library, IDialogService dialogs)
    {
        _library = library;
        _dialogs = dialogs;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Matches;
        Reload();
    }

    /// <summary>全部準星</summary>
    public ObservableCollection<CrosshairItemViewModel> Items { get; } = new();

    /// <summary>經過搜尋 / 收藏篩選後，畫面上顯示的準星</summary>
    public ICollectionView ItemsView { get; }

    /// <summary>搜尋文字（用名字搜尋）</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>只顯示收藏</summary>
    [ObservableProperty]
    private bool _showFavoritesOnly;

    /// <summary>預覽背景（牆面 / 深色 / 淺色），預設是模擬遊戲牆面的材質</summary>
    [ObservableProperty]
    private CrosshairBackground _backgroundMode = CrosshairBackground.Map;

    /// <summary>
    /// 要跳出提示（Snackbar）時觸發，參數是（標題, 內容）。
    /// 用事件而不是文字屬性：連續複製兩次一樣的東西，也會再跳一次提示。
    /// </summary>
    public event Action<string, string>? NoticeRaised;

    /// <summary>在視窗底部跳出提示</summary>
    public void Notify(string title, string message) => NoticeRaised?.Invoke(title, message);

    /// <summary>篩選後是不是沒有任何準星</summary>
    public bool IsEmpty => ItemsView.IsEmpty;

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnShowFavoritesOnlyChanged(bool value) => RefreshView();

    // 背景切換用的三個選項（綁到單選標籤）
    public bool IsDarkBackground
    {
        get => BackgroundMode == CrosshairBackground.Dark;
        set { if (value) BackgroundMode = CrosshairBackground.Dark; }
    }

    public bool IsLightBackground
    {
        get => BackgroundMode == CrosshairBackground.Light;
        set { if (value) BackgroundMode = CrosshairBackground.Light; }
    }

    public bool IsMapBackground
    {
        get => BackgroundMode == CrosshairBackground.Map;
        set { if (value) BackgroundMode = CrosshairBackground.Map; }
    }

    partial void OnBackgroundModeChanged(CrosshairBackground value)
    {
        OnPropertyChanged(nameof(IsDarkBackground));
        OnPropertyChanged(nameof(IsLightBackground));
        OnPropertyChanged(nameof(IsMapBackground));
    }

    // ==================================================================
    // 按鈕
    // ==================================================================

    /// <summary>新增我的準星（剪貼簿裡如果是準星代碼，會自動帶入）</summary>
    [RelayCommand]
    private void Add() => AddInternal(prefillFromClipboard: true);

    /// <summary>點卡片：打開大圖預覽</summary>
    [RelayCommand]
    private void Open(CrosshairItemViewModel item) => _dialogs.ShowCrosshairDetail(item, this);

    /// <summary>複製代碼到剪貼簿</summary>
    [RelayCommand]
    private void Copy(CrosshairItemViewModel item)
    {
        if (TrySetClipboard(item.Code))
        {
            Notify(CopiedTitle, CopiedHint);
            AppLog.Info("已複製準星代碼");
        }
    }

    [RelayCommand]
    private void ToggleFavorite(CrosshairItemViewModel item)
    {
        item.IsFavorite = !item.IsFavorite;
        _library.SetFavorite(item.Model.Id, item.IsFavorite);
        RefreshView();
    }

    [RelayCommand]
    private void Delete(CrosshairItemViewModel item)
    {
        if (item.IsExample) return;
        if (!_dialogs.Confirm("刪除準星", $"確定要刪除「{item.Name}」嗎？", "刪除", "取消", danger: true)) return;
        _library.Delete(item.Model.Id);
        Items.Remove(item);
        RefreshView();
    }

    /// <summary>「在 VCRDB 看更多」按鈕要不要顯示（從瀏覽器打開這個視窗時就不用再顯示）</summary>
    [ObservableProperty]
    private bool _showVcrdbButton = true;

    /// <summary>在程式內開啟 VCRDB（從「我的準星」視窗按的）</summary>
    [RelayCommand]
    private void OpenVcrdb() => OpenVcrdbCore(withMyCrosshairsButton: false);

    /// <summary>
    /// 主畫面的「熱門準星」：直接用內建瀏覽器開 VCRDB，
    /// 工具列上有「我的準星」可以打開收藏清單。
    /// </summary>
    public void OpenVcrdbFromMain() => OpenVcrdbCore(withMyCrosshairsButton: true);

    /// <summary>開啟 VCRDB；關掉時如果剪貼簿有新的準星代碼，就問要不要加入「我的準星」</summary>
    private void OpenVcrdbCore(bool withMyCrosshairsButton)
    {
        _dialogs.ShowWebBrowser(VcrdbUrl, "熱門準星 · VCRDB（Valorant Crosshair Database）",
            "在網站上按準星的複製按鈕，就能直接到遊戲「設定 → 準星 → 匯入設定檔代碼」貼上。關掉這個視窗時，程式會問你要不要存進「我的準星」。",
            withMyCrosshairsButton ? "我的準星" : null,
            withMyCrosshairsButton ? ShowMyCrosshairsFromBrowser : null);

        var clip = ReadClipboard();
        if (CrosshairCode.LooksLikeCode(clip) && Items.All(i => i.Code != clip!.Trim()) &&
            _dialogs.Confirm("加入我的準星？", "剪貼簿裡有一個準星代碼，要把它加入「我的準星」嗎？", "加入", "不用"))
        {
            AddInternal(prefillFromClipboard: true);
        }
    }

    // ==================================================================
    // 小工具
    // ==================================================================

    /// <summary>從瀏覽器工具列打開「我的準星」</summary>
    private void ShowMyCrosshairsFromBrowser()
    {
        ShowVcrdbButton = false;
        _dialogs.ShowCrosshairs(this);
        ShowVcrdbButton = true;
    }

    private void AddInternal(bool prefillFromClipboard)
    {
        var clip = prefillFromClipboard ? ReadClipboard() : null;
        var code = CrosshairCode.LooksLikeCode(clip) ? clip!.Trim() : "";
        var result = _dialogs.ShowCrosshairEditor("新增我的準星", "", code, BackgroundMode);
        if (result is null) return;

        var saved = _library.Add(result.Value.Name, result.Value.Code);
        Items.Insert(0, new CrosshairItemViewModel(saved));
        RefreshView();
        Notify("已加入我的準星", $"「{saved.Name}」已經存好了。");
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var c in _library.LoadAll()) Items.Add(new CrosshairItemViewModel(c));
        RefreshView();
    }

    private void RefreshView()
    {
        ItemsView.Refresh();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(object obj)
    {
        if (obj is not CrosshairItemViewModel item) return false;
        if (ShowFavoritesOnly && !item.IsFavorite) return false;
        var q = SearchText.Trim();
        return q.Length == 0 || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>寫入剪貼簿（剪貼簿被其他程式佔用時會重試幾次）</summary>
    public bool TrySetClipboard(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
        }
        _dialogs.ShowError("無法複製", "剪貼簿正被其他程式使用，請稍後再試一次。");
        return false;
    }

    private static string? ReadClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

/// <summary>網格上的一張準星卡片</summary>
public sealed partial class CrosshairItemViewModel : ObservableObject
{
    public CrosshairItemViewModel(SavedCrosshair model)
    {
        Model = model;
        _isFavorite = model.IsFavorite;
    }

    public SavedCrosshair Model { get; }

    public string Name => Model.Name;

    public string Code => Model.Code;

    public bool IsExample => Model.IsExample;

    /// <summary>卡片下方的小字</summary>
    public string SourceText => Model.IsExample ? "範例樣式" : $"我的準星 · {TimeText.FormatWhen(Model.CreatedUtc)}";

    [ObservableProperty]
    private bool _isFavorite;
}
