using System.IO;
using System.Windows;
using Microsoft.Win32;
using RiotAccountHub.ViewModels;
using RiotAccountHub.Views;

namespace RiotAccountHub.Services;

/// <summary>
/// IDialogService 的實作：真正把視窗開出來。
/// </summary>
public sealed class DialogService : IDialogService
{
    public void ShowInfo(string title, string message) =>
        MessageDialog.Show(Owner(), title, message, DialogKind.Info, "確定", null);

    public void ShowError(string title, string message) =>
        MessageDialog.Show(Owner(), title, message, DialogKind.Error, "確定", null);

    public bool Confirm(string title, string message, string confirmText, string cancelText, bool danger = false, bool warning = false)
    {
        var kind = danger ? DialogKind.Danger : warning ? DialogKind.Warning : DialogKind.Question;
        return MessageDialog.Show(Owner(), title, message, kind, confirmText, cancelText);
    }

    public bool ShowLoginPrompt(string headline)
    {
        var window = new LoginPromptWindow(headline) { Owner = Owner() };
        return window.ShowDialog() == true;
    }

    public bool ShowAccountEditor(AccountEditViewModel viewModel)
    {
        var window = new AccountEditWindow { DataContext = viewModel, Owner = Owner() };
        return window.ShowDialog() == true;
    }

    public string? PickRiotClientExe(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "請選擇 RiotClientServices.exe",
            Filter = "Riot Client (RiotClientServices.exe)|RiotClientServices.exe|執行檔 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    /// <summary>找出目前最上層、可以當「父視窗」的視窗</summary>
    private static Window? Owner()
    {
        var app = Application.Current;
        if (app is null) return null;
        var active = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (active is not null) return active;
        return app.MainWindow is { IsVisible: true } main ? main : null;
    }
}
