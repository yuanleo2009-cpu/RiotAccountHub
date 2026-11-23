using System.Windows;
using RiotAccountHub.Helpers;

namespace RiotAccountHub.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string defaultText)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = defaultText;
        InputBox.SelectAll();
    }

    /// <summary>使用者輸入的文字（按確定後才有）</summary>
    public string ResultText { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0)
        {
            ErrorText.Text = "請輸入名稱。";
            return;
        }
        ResultText = text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
