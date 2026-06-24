using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class ActivityGroupView : IComponent
{
    private readonly ActivityGroupModel _model;

    public ActivityGroupView(ActivityGroupModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ActivityGroupModel Model => _model;

    public ActivityGroupDisplayMode Mode { get; init; } = ActivityGroupDisplayMode.Detailed;

    public bool AnimationsEnabled { get; init; } = true;

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = string.IsNullOrEmpty(_model.Title) ? 0 : UnicodeWidth.GetWidth(_model.Title);
        var activities = _model.GetVisibleActivities();
        foreach (var activity in activities)
        {
            width = Math.Max(width, new ActivityView(activity).Measure(in context, maxWidth).MaxWidth);
        }

        width = Math.Min(width, maxWidth);

        var height = string.IsNullOrEmpty(_model.Title) ? 0 : 1;
        if (activities.Count > 0)
        {
            height += Mode == ActivityGroupDisplayMode.Compact ? 1 : activities.Count;
        }

        return new Measurement(width, width, height);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var activities = _model.GetVisibleActivities();
        if (!string.IsNullOrEmpty(_model.Title))
        {
            WriteClipped(_model.Title, maxWidth, context.Theme.Accent, ref output);
            if (activities.Count > 0)
            {
                output.WriteLineBreak();
            }
        }

        if (activities.Count == 0)
        {
            return;
        }

        if (Mode == ActivityGroupDisplayMode.Compact)
        {
            RenderCompact(activities, in context, maxWidth, ref output);
            return;
        }

        RenderDetailed(activities, in context, maxWidth, ref output);
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    private void RenderDetailed(IReadOnlyList<ActivityModel> activities, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        for (var i = 0; i < activities.Count; i++)
        {
            var view = new ActivityView(activities[i]) { AnimationsEnabled = AnimationsEnabled };
            view.Render(in context, maxWidth, ref output);
            if (i < activities.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    private static void RenderCompact(IReadOnlyList<ActivityModel> activities, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var running = 0;
        var completed = 0;
        var failed = 0;
        var pending = 0;

        foreach (var activity in activities)
        {
            switch (activity.State)
            {
                case ActivityState.Completed:
                    completed++;
                    break;
                case ActivityState.Failed:
                    failed++;
                    break;
                case ActivityState.Pending:
                    pending++;
                    break;
                default:
                    running++;
                    break;
            }
        }

        var text = $"running {running}  done {completed}  failed {failed}  pending {pending}";
        WriteClipped(text, maxWidth, failed > 0 ? context.Theme.Error : context.Theme.Text, ref output);
    }

    private static void WriteClipped(string value, int maxWidth, Style style, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var used = 0;
        var enumerator = new RuneEnumerator(value.AsSpan());
        Span<char> buffer = stackalloc char[2];
        while (enumerator.MoveNext())
        {
            var runeWidth = UnicodeWidth.GetWidth(enumerator.Current);
            if (used + runeWidth > maxWidth)
            {
                break;
            }

            if (enumerator.Current.TryEncodeToUtf16(buffer, out var written))
            {
                output.Write(buffer[..written], style);
            }

            used += runeWidth;
        }
    }
}

public enum ActivityGroupDisplayMode
{
    Detailed,
    Compact
}
