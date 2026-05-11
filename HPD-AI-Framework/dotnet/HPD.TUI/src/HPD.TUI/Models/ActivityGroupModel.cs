namespace HPD.TUI.Models;

public sealed class ActivityGroupModel
{
    private readonly List<ActivityModel> _activities = [];

    public string? Title { get; set; }

    public bool HideCompleted { get; set; }

    public bool AutoClearCompleted { get; set; }

    public IReadOnlyList<ActivityModel> Activities => _activities;

    public ActivityGroupModel Add(ActivityModel activity)
    {
        _activities.Add(activity ?? throw new ArgumentNullException(nameof(activity)));
        return this;
    }

    public ActivityModel Add(string label)
    {
        var activity = new ActivityModel(label);
        Add(activity);
        return activity;
    }

    public void ClearCompleted()
    {
        _activities.RemoveAll(static activity => activity.State is ActivityState.Completed);
    }

    public IReadOnlyList<ActivityModel> GetVisibleActivities()
    {
        if (AutoClearCompleted)
        {
            ClearCompleted();
        }

        if (!HideCompleted)
        {
            return _activities;
        }

        var visible = new List<ActivityModel>();
        foreach (var activity in _activities)
        {
            if (activity.State != ActivityState.Completed)
            {
                visible.Add(activity);
            }
        }

        return visible;
    }
}
