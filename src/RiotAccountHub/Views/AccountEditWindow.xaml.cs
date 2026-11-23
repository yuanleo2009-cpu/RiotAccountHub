using System.Windows;
using RiotAccountHub.Helpers;
using RiotAccountHub.ViewModels;

namespace RiotAccountHub.Views;

public partial class AccountEditWindow : Window
{
    public AccountEditWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // 輸入正確才關閉視窗；不正確的話，ViewModel 會設定 ErrorMessage 顯示紅字
        if (DataContext is AccountEditViewModel vm && vm.Validate())
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
