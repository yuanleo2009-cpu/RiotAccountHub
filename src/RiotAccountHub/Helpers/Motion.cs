using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RiotAccountHub.Helpers;

/// <summary>出現 / 消失時要用哪一種動畫</summary>
public enum ShowMode
{
    /// <summary>淡入淡出</summary>
    Fade,
    /// <summary>淡入淡出，同時高度從 0 展開（下面的內容會被平順地推開，不會突然跳一下）</summary>
    Expand,
}

/// <summary>
/// 過渡動畫小工具（在 XAML 裡用 helpers:Motion.xxx 掛在任何元件上）：
///
///   helpers:Motion.Enter="True"
///       元件第一次出現時「淡入 + 往上滑 12px」。
///       如果它是清單裡的項目（例如帳號卡片），會依順序一張接一張出現（每張晚 40 毫秒）。
///
///   helpers:Motion.IsShown="{Binding 某個布林值}"
///       取代 Visibility：變成 true 時淡入，變成 false 時淡出後才收起來。
///       搭配 helpers:Motion.ShowMode="Expand" 會連高度一起展開 / 收合。
///
/// 動畫時間用 Tokens.xaml 的 Motion.Fast / Motion.Normal / Motion.Slow（150 / 200 / 250 毫秒）。
/// 如果使用者在 Windows 設定裡關掉了「動畫效果」，這裡會直接顯示結果、不播動畫。
/// </summary>
public static class Motion
{
    private const double EnterOffset = 12;      // 進場時從下面 12px 滑上來
    private const int StaggerMs = 40;           // 清單項目一個接一個的間隔
    private const int MaxStaggerItems = 8;      // 最多延遲 8 個項目，後面的不再多等

    private static readonly IEasingFunction EaseOut = CreateEase();

    private static IEasingFunction CreateEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    /// <summary>從 Tokens.xaml 讀動畫時間，讀不到就用預設值</summary>
    private static Duration Time(string key, int fallbackMs) =>
        Application.Current?.TryFindResource(key) is Duration d ? d : TimeSpan.FromMilliseconds(fallbackMs);

    /// <summary>使用者有沒有開啟 Windows 的動畫效果</summary>
    private static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    // ==================================================================
    // Enter：第一次出現時的進場動畫
    // ==================================================================

    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnterChanged));

    public static bool GetEnter(DependencyObject d) => (bool)d.GetValue(EnterProperty);

    public static void SetEnter(DependencyObject d, bool value) => d.SetValue(EnterProperty, value);

    private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.Loaded += OnEnterLoaded;
    }

    private static void OnEnterLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnEnterLoaded; // 只在第一次出現時播
        if (!AnimationsEnabled) return;

        var delay = TimeSpan.FromMilliseconds(Math.Min(StaggerIndex(element), MaxStaggerItems) * StaggerMs);
        var duration = Time("Motion.Slow", 250);

        var slide = new TranslateTransform(0, EnterOffset);
        element.RenderTransform = slide;
        element.Opacity = 0; // 等待輪到自己之前先保持看不見

        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            // 播完後把動畫放掉，之後其他動畫（例如淡出）才能正常接手
            element.Opacity = 1;
            element.BeginAnimation(UIElement.OpacityProperty, null);
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(EnterOffset, 0, duration) { BeginTime = delay, EasingFunction = EaseOut });
    }

    /// <summary>這個元件是清單裡的第幾個項目（不在清單裡就是 0）</summary>
    private static int StaggerIndex(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var list = ItemsControl.ItemsControlFromItemContainer(current);
            if (list is not null)
            {
                return Math.Max(0, list.ItemContainerGenerator.IndexFromContainer(current));
            }
        }
        return 0;
    }

    // ==================================================================
    // IsShown：有動畫的顯示 / 隱藏（取代 Visibility）
    // ==================================================================

    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown", typeof(bool?), typeof(Motion), new PropertyMetadata(null, OnIsShownChanged));

    public static bool? GetIsShown(DependencyObject d) => (bool?)d.GetValue(IsShownProperty);

    public static void SetIsShown(DependencyObject d, bool? value) => d.SetValue(IsShownProperty, value);

    public static readonly DependencyProperty ShowModeProperty = DependencyProperty.RegisterAttached(
        "ShowMode", typeof(ShowMode), typeof(Motion), new PropertyMetadata(ShowMode.Fade));

    public static ShowMode GetShowMode(DependencyObject d) => (ShowMode)d.GetValue(ShowModeProperty);

    public static void SetShowMode(DependencyObject d, ShowMode value) => d.SetValue(ShowModeProperty, value);

    private static void OnIsShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        var show = e.NewValue is true;
        var expand = GetShowMode(element) == ShowMode.Expand;

        // 第一次設定（畫面剛建立）或關閉動畫時：直接套用結果，不播動畫
        var animate = e.OldValue is bool && element.IsLoaded && AnimationsEnabled;
        if (!animate)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            if (expand) element.LayoutTransform = Transform.Identity;
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (show)
        {
            var duration = Time("Motion.Normal", 200);
            element.Visibility = Visibility.Visible;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
            if (expand)
            {
                var scale = new ScaleTransform(1, 0);
                element.LayoutTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
            }
        }
        else
        {
            var duration = Time("Motion.Fast", 150);
            var fade = new DoubleAnimation(element.Opacity, 0, duration) { EasingFunction = EaseOut };
            fade.Completed += (_, _) =>
            {
                // 淡出期間如果又變回顯示，就不要收起來
                if (GetIsShown(element) != true) element.Visibility = Visibility.Collapsed;
            };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            if (expand)
            {
                var scale = new ScaleTransform(1, 1);
                element.LayoutTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = EaseOut });
            }
        }
    }
}
