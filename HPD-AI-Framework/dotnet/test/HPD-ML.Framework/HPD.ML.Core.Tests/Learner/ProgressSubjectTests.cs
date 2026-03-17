using HPD.Events;
using HPD.Events.Core;
using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ProgressSubjectTests
{
    private static ProgressEvent MakeProgress(int epoch = 1, double metric = 0.5)
        => new() { Epoch = epoch, MetricValue = metric, MetricName = "accuracy" };

    [Fact]
    public void Subscribe_ReceivesOnNext()
    {
        using var subject = new ProgressSubject();
        var received = new List<ProgressEvent>();
        using var sub = subject.Subscribe(new TestObserver(received));

        subject.OnNext(MakeProgress());
        Assert.Single(received);
    }

    [Fact]
    public void MultipleSubscribers_AllReceive()
    {
        using var subject = new ProgressSubject();
        var r1 = new List<ProgressEvent>();
        var r2 = new List<ProgressEvent>();
        var r3 = new List<ProgressEvent>();
        using var s1 = subject.Subscribe(new TestObserver(r1));
        using var s2 = subject.Subscribe(new TestObserver(r2));
        using var s3 = subject.Subscribe(new TestObserver(r3));

        subject.OnNext(MakeProgress());
        Assert.Single(r1);
        Assert.Single(r2);
        Assert.Single(r3);
    }

    [Fact]
    public void Unsubscribe_StopsReceiving()
    {
        using var subject = new ProgressSubject();
        var received = new List<ProgressEvent>();
        var sub = subject.Subscribe(new TestObserver(received));
        sub.Dispose();

        subject.OnNext(MakeProgress());
        Assert.Empty(received);
    }

    [Fact]
    public void OnCompleted_NotifiesAllObservers()
    {
        using var subject = new ProgressSubject();
        bool completed = false;
        using var sub = subject.Subscribe(new TestObserver(onCompleted: () => completed = true));

        subject.OnCompleted();
        Assert.True(completed);
    }

    [Fact]
    public void OnError_NotifiesAllObservers()
    {
        using var subject = new ProgressSubject();
        Exception? received = null;
        using var sub = subject.Subscribe(new TestObserver(onError: e => received = e));

        var ex = new InvalidOperationException("test");
        subject.OnError(ex);
        Assert.Same(ex, received);
    }

    [Fact]
    public void Dispose_CallsOnCompleted()
    {
        var subject = new ProgressSubject();
        bool completed = false;
        using var sub = subject.Subscribe(new TestObserver(onCompleted: () => completed = true));

        subject.Dispose();
        Assert.True(completed);
    }

    [Fact]
    public void WithCoordinator_EmitsTrainingProgressEvent()
    {
        using var coordinator = new EventCoordinator();
        using var subject = new ProgressSubject(coordinator);

        subject.OnNext(MakeProgress(epoch: 5));

        Assert.True(coordinator.TryRead(out var evt));
        var tpe = Assert.IsType<TrainingProgressEvent>(evt);
        Assert.Equal(5, tpe.Progress.Epoch);
    }

    [Fact]
    public void WithoutCoordinator_StillNotifiesObservers()
    {
        using var subject = new ProgressSubject(coordinator: null);
        var received = new List<ProgressEvent>();
        using var sub = subject.Subscribe(new TestObserver(received));

        subject.OnNext(MakeProgress());
        Assert.Single(received);
    }

    private sealed class TestObserver(
        List<ProgressEvent>? events = null,
        Action? onCompleted = null,
        Action<Exception>? onError = null) : IObserver<ProgressEvent>
    {
        public void OnNext(ProgressEvent value) => events?.Add(value);
        public void OnCompleted() => onCompleted?.Invoke();
        public void OnError(Exception error) => onError?.Invoke(error);
    }
}
