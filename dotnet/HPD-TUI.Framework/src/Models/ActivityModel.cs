namespace HPD.TUI.Models;

public sealed class ActivityModel
{
    public ActivityModel(string label)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
    }

    public string Label { get; set; }

    public ActivityState State { get; set; } = ActivityState.Running;

    public ActivitySeverity Severity { get; set; } = ActivitySeverity.Info;

    public double? Progress { get; set; }

    public bool IsIndeterminate => Progress is null && State == ActivityState.Running;

    internal ActivityModel Clone()
        => new(Label)
        {
            State = State,
            Severity = Severity,
            Progress = Progress
        };
}

public enum ActivityState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum ActivitySeverity
{
    Info,
    Success,
    Warning,
    Error
}
