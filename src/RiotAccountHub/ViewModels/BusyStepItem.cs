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
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsReached))]
    private StepState _state = StepState.Pending;

    /// <summary>正在進行（畫面上會轉圈）</summary>
    public bool IsActive => State == StepState.Active;

    /// <summary>已完成（畫面上會打勾）</summary>
    public bool IsDone => State == StepState.Done;

    /// <summary>已經輪到或做完（前面的連接線會填滿）</summary>
    public bool IsReached => State != StepState.Pending;
}

public enum StepState
{
    Pending, // 還沒開始（灰色）
    Active,  // 進行中（紅色、轉圈）
    Done,    // 完成（打勾）
}
