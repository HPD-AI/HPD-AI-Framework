using HPD.TUI.Models;

namespace HPD.TUI.Flows;

public sealed class ActivityScope : IDisposable
{
    private readonly ActivityModel _activity;
    private bool _completed;

    public ActivityScope(ActivityModel activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _activity.State = ActivityState.Running;
    }

    public ActivityModel Activity => _activity;

    public void SetProgress(double progress)
    {
        _activity.Progress = Math.Clamp(progress, 0, 1);
    }

    public void Complete()
    {
        _completed = true;
        _activity.Progress = 1;
        _activity.State = ActivityState.Completed;
        _activity.Severity = ActivitySeverity.Success;
    }

    public void Fail()
    {
        _completed = true;
        _activity.State = ActivityState.Failed;
        _activity.Severity = ActivitySeverity.Error;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            Complete();
        }
    }

    public static ActivityScope Start(ActivityGroupModel group, string label)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new ActivityScope(group.Add(label));
    }
}
