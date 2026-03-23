using HPD.Events;
using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class TrainingProgressEventTests
{
    [Fact]
    public void Kind_IsContent()
    {
        var evt = new TrainingProgressEvent(new ProgressEvent());
        Assert.Equal(EventKind.Content, evt.Kind);
    }

    [Fact]
    public void Priority_IsNormal()
    {
        var evt = new TrainingProgressEvent(new ProgressEvent());
        Assert.Equal(EventPriority.Normal, evt.Priority);
    }

    [Fact]
    public void Progress_ContainsOriginalEvent()
    {
        var progress = new ProgressEvent { Epoch = 3, MetricValue = 0.95 };
        var evt = new TrainingProgressEvent(progress);

        Assert.Same(progress, evt.Progress);
    }
}
