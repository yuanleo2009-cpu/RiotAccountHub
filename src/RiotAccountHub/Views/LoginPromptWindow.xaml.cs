using System.Windows;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

public partial class LoginPromptWindow : Window
{
    public LoginPromptWindow(string headline)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        HeadlineText.Text = headline;
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
