using System.Windows;
using RiotAccountHub.Helpers;
using RiotAccountHub.ViewModels;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

/// <summary>「我的準星」視窗。按鈕都綁定到 CrosshairsViewModel。</summary>
public partial class CrosshairsWindow : FluentWindow
{
    private CrosshairsViewModel? _viewModel;

    public CrosshairsWindow()
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);
        DataContextChanged += (_, _) => Attach(DataContext as CrosshairsViewModel);
        Closed += (_, _) => Attach(null);
    }

    /// <summary>ViewModel 說要跳提示時，在這個視窗底部顯示 Snackbar</summary>
    private void Attach(CrosshairsViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.NoticeRaised -= OnNotice;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.NoticeRaised += OnNotice;
    }

    private void OnNotice(string title, string message) => Toast.Success(Snackbars, title, message);
}
