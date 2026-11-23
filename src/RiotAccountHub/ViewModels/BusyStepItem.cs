using CommunityToolkit.Mvvm.ComponentModel;
using RiotAccountHub.Core;

namespace RiotAccountHub.ViewModels;

/// <summary>進度畫面上的一個步驟（例如「關閉中」）</summary>
public sealed partial class BusyStepItem : ObservableObject
{
    public BusyStepItem(SwitchStep step, string text)
    {
        Step = step;
        Text = text;
    }

    public SwitchStep Step { get; }
    public string Text { get; }

    [ObservableProperty]
    private StepState _state = StepState.Pending;
}

public enum StepState
{
    Pending, // 還沒開始（灰色）
    Active,  // 進行中（紅色）
    Done,    // 完成（綠色勾勾）
}
