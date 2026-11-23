using System.Windows;
using System.Windows.Controls;

namespace RiotAccountHub.Helpers;

/// <summary>
/// 整齊的卡片網格：
///   依視窗寬度自動決定一排放幾張（每張至少 MinItemWidth 寬），
///   再把卡片平均撐開填滿整排，右邊不會留下參差不齊的空白。
/// 視窗變寬變窄時會自動重新排。
/// </summary>
public sealed class TileGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(TileGrid),
        new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(TileGrid),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>每張卡片最少多寬</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>卡片之間的距離（左右、上下都一樣）</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private (int Columns, double ItemWidth) Layout(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return (1, MinItemWidth);
        var columns = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        var itemWidth = (width - Spacing * (columns - 1)) / columns;
        return (columns, Math.Floor(itemWidth));
    }

    protected override Size MeasureOverride(Size available)
    {
        var (columns, itemWidth) = Layout(available.Width);
        double total = 0, rowHeight = 0;
        var count = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++count % columns == 0)
            {
                total += rowHeight + Spacing;
                rowHeight = 0;
            }
        }
        if (count % columns != 0) total += rowHeight;
        else if (count > 0) total -= Spacing;

        var width = double.IsInfinity(available.Width) ? columns * itemWidth + (columns - 1) * Spacing : available.Width;
        return new Size(width, Math.Max(0, total));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        double y = 0, rowHeight = 0;
        var index = 0;
        var children = InternalChildren;
        while (index < children.Count)
        {
            // 同一排的卡片一樣高（以最高的為準）
            rowHeight = 0;
            for (var c = 0; c < columns && index + c < children.Count; c++)
            {
                rowHeight = Math.Max(rowHeight, children[index + c].DesiredSize.Height);
            }
            for (var c = 0; c < columns && index < children.Count; c++, index++)
            {
                children[index].Arrange(new Rect(c * (itemWidth + Spacing), y, itemWidth, rowHeight));
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
