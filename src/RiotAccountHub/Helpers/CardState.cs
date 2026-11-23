using System.Windows;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 卡片的附加狀態。
/// helpers:CardState.IsActive="True"：這張卡片是「目前使用中」的帳號，左側的紅線會一直亮著。
/// （樣式在 Themes/Controls.xaml 的 Card.Interactive）
/// </summary>
public static class CardState
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(CardState), new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject d) => (bool)d.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject d, bool value) => d.SetValue(IsActiveProperty, value);
}
