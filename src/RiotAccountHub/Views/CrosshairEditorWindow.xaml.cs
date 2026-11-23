using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RiotAccountHub.Core.Crosshairs;
using RiotAccountHub.Helpers;
using Wpf.Ui.Controls;

namespace RiotAccountHub.Views;

/// <summary>新增我的準星（輸入時即時預覽、檢查代碼格式）</summary>
public partial class CrosshairEditorWindow : FluentWindow
{
    public CrosshairEditorWindow(string title, string name, string code, CrosshairBackground background)
    {
        InitializeComponent();
        AppTheme.PrepareWindow(this);
        TitleText.Text = title;
        Title = title;
        NameBox.Text = name;
        Preview.BackgroundMode = background;
        CodeBox.Text = code; // 會觸發 OnCodeChanged 更新預覽
        if (code.Length == 0) UpdateStatus();
        else StatusText.Text += "（已自動帶入剪貼簿裡的代碼）";
    }

    public string ResultName { get; private set; } = "";
    public string ResultCode { get; private set; } = "";

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        Preview.Code = CodeBox.Text.Trim();
        UpdateStatus();
    }

    /// <summary>顯示代碼是否看得懂</summary>
    private void UpdateStatus()
    {
        var code = CodeBox.Text.Trim();
        if (code.Length == 0)
        {
            SetStatus("貼上代碼後，上面會顯示預覽。", StatusKind.Hint);
            return;
        }

        var r = CrosshairCode.Parse(code);
        if (!r.IsValid)
        {
            SetStatus(r.Error ?? "看不懂這個代碼。", StatusKind.Problem);
        }
        else if (r.Warnings.Count > 0)
        {
            SetStatus($"代碼可以使用；有 {r.Warnings.Count} 個欄位預覽圖還不支援，已略過（遊戲裡會正常顯示）。", StatusKind.Ok);
        }
        else
        {
            SetStatus("代碼格式正確", StatusKind.Ok);
        }
    }

    private enum StatusKind { Hint, Ok, Problem }

    /// <summary>代碼檢查結果：提示是灰色、正確是綠色勾、有問題是紅色</summary>
    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        var (symbol, brush) = kind switch
        {
            StatusKind.Ok => (SymbolRegular.CheckmarkCircle16, "Brush.Success"),
            StatusKind.Problem => (SymbolRegular.ErrorCircle16, "Brush.Accent"),
            _ => (SymbolRegular.Info16, "Brush.TextMuted"),
        };
        StatusIcon.Symbol = symbol;
        StatusIcon.Foreground = (Brush)FindResource(brush);
        StatusText.Foreground = (Brush)FindResource(kind == StatusKind.Problem ? "Brush.Accent" : "Brush.TextSecondary");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (!CrosshairCode.Parse(code).IsValid)
        {
            SetStatus("請貼上正確的準星代碼（以「0;」開頭，例如 0;P;c;5;h;0;0l;4;0o;2）。", StatusKind.Problem);
            CodeBox.Focus();
            return;
        }

        var name = NameBox.Text.Trim();
        ResultName = name.Length > 0 ? name : "我的準星";
        ResultCode = code;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
