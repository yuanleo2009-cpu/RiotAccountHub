using System.Windows;
using RiotAccountHub.Helpers;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

public partial class LoginPromptWindow : FluentWindow
{
    public LoginPromptWindow(string headline)
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);
        HeadlineText.Text = headline;
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
