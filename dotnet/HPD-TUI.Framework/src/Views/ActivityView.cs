using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;

namespace HPD.TUI.Views;

public sealed class ActivityView : IComponent
{
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private readonly ActivityModel _model;

    public ActivityView(ActivityModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ActivityModel Model => _model;

    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromMilliseconds(80);

    public bool AnimationsEnabled { get; init; } = true;

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 2 + UnicodeWidth.GetWidth(_model.Label);
        if (_model.Progress is not null)
        {
            width += 8;
        }

        width = Math.Min(width, maxWidth);
        return new Measurement(Math.Min(width, maxWidth), width);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var style = ResolveStyle(in context);
        output.Write(GetIndicator(context).AsSpan(), style);
        output.Write(" ", context.Theme.Text);
        output.Write(_model.Label.AsSpan(), style);

        if (_model.Progress is not null && maxWidth > UnicodeWidth.GetWidth(_model.Label) + 8)
        {
            output.Write(" ", context.Theme.Text);
            WritePercent(_model.Progress.Value, style, ref output);
        }
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static ActivityView Spinner(string label) => new(new ActivityModel(label));

    public static ActivityView Progress(string label, double progress) => new(new ActivityModel(label)
    {
        Progress = progress,
        State = ActivityState.Running
    });

    public static ActivityView Pending(string label) => new(new ActivityModel(label)
    {
        State = ActivityState.Pending
    });

    public static ActivityView Completed(string label) => new(new ActivityModel(label)
    {
        State = ActivityState.Completed,
        Severity = ActivitySeverity.Success
    });

    public static ActivityView Failed(string label) => new(new ActivityModel(label)
    {
        State = ActivityState.Failed,
        Severity = ActivitySeverity.Error
    });

    private string GetIndicator(in RenderContext context)
    {
        return _model.State switch
        {
            ActivityState.Pending => "○",
            ActivityState.Completed => "●",
            ActivityState.Failed => "×",
            _ when _model.IsIndeterminate => AnimationsEnabled ? SpinnerFrames[GetFrameIndex(context)] : "⋯",
            _ => "●"
        };
    }

    private int GetFrameIndex(in RenderContext context)
    {
        var interval = Math.Max(1, (int)FrameInterval.TotalMilliseconds);
        return (int)(context.Elapsed.TotalMilliseconds / interval % SpinnerFrames.Length);
    }

    private Style ResolveStyle(in RenderContext context)
    {
        return _model.Severity switch
        {
            ActivitySeverity.Success => context.Theme.Success,
            ActivitySeverity.Warning => context.Theme.Warning,
            ActivitySeverity.Error => context.Theme.Error,
            _ => _model.State switch
            {
                ActivityState.Completed => context.Theme.Success,
                ActivityState.Failed => context.Theme.Error,
                _ => context.Theme.Accent
            }
        };
    }

    private static void WritePercent(double progress, Style style, ref SegmentWriter output)
    {
        progress = Math.Clamp(progress, 0, 1);
        Span<char> buffer = stackalloc char[4];
        var percent = (int)Math.Round(progress * 100);
        if (percent.TryFormat(buffer, out var written))
        {
            output.Write(buffer[..written], style);
            output.Write("%", style);
        }
    }
}
