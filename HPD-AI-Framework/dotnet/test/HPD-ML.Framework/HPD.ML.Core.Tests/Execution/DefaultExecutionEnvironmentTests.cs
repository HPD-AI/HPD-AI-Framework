using HPD.Events;
using HPD.Events.Core;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.ML.Core.Tests;

public class DefaultExecutionEnvironmentTests
{
    [Fact]
    public void Defaults_NullLogger_NullSeed_DefaultBackend()
    {
        var env = new DefaultExecutionEnvironment();

        Assert.Same(NullLogger.Instance, env.Logger);
        Assert.Null(env.Seed);
        Assert.Equal(ComputeBackend.Default, env.ComputeBackend);
    }

    [Fact]
    public void CustomLogger_IsPreserved()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger("test");
        var env = new DefaultExecutionEnvironment(logger: logger);

        Assert.Same(logger, env.Logger);
    }

    [Fact]
    public void Seed_IsPreserved()
    {
        var env = new DefaultExecutionEnvironment(seed: 42);
        Assert.Equal(42, env.Seed);
    }

    [Fact]
    public void CancellationToken_IsPreserved()
    {
        using var cts = new CancellationTokenSource();
        var env = new DefaultExecutionEnvironment(cancellationToken: cts.Token);

        Assert.Equal(cts.Token, env.CancellationToken);
    }

    [Fact]
    public void CreateProgress_WithoutCoordinator_ReturnsProgress()
    {
        var env = new DefaultExecutionEnvironment();
        var progress = env.CreateProgress<int>("test");

        Assert.IsType<Progress<int>>(progress);
    }

    [Fact]
    public void CreateProgressSubject_WiredToCoordinator()
    {
        using var coordinator = new EventCoordinator();
        var env = new DefaultExecutionEnvironment(coordinator: coordinator);
        using var subject = env.CreateProgressSubject();

        subject.OnNext(new ProgressEvent { Epoch = 1 });
        Assert.True(coordinator.TryRead(out var evt));
        Assert.IsType<TrainingProgressEvent>(evt);
    }

    [Fact]
    public void CreateChild_InheritsSeedPlusOne()
    {
        var env = new DefaultExecutionEnvironment(seed: 42);
        var child = env.CreateChild();

        Assert.Equal(43, child.Seed);
    }

    [Fact]
    public void CreateChild_NullSeed_ChildAlsoNull()
    {
        var env = new DefaultExecutionEnvironment(seed: null);
        var child = env.CreateChild();

        Assert.Null(child.Seed);
    }

    [Fact]
    public void CreateChild_InheritsSchedulerAndDevice()
    {
        var device = new DevicePreference("gpu:0");
        var env = new DefaultExecutionEnvironment(
            defaultDevicePreference: device,
            computeBackend: ComputeBackend.MKL);

        var child = env.CreateChild();

        Assert.Equal("gpu:0", child.DefaultDevicePreference.DeviceId);
        Assert.Equal(ComputeBackend.MKL, child.ComputeBackend);
    }

    [Fact]
    public void CreateChild_CreatesChildCoordinator_WithParent()
    {
        using var coordinator = new EventCoordinator();
        var env = new DefaultExecutionEnvironment(coordinator: coordinator);
        var child = env.CreateChild();

        // The child's progress subject should bubble events to the parent coordinator
        using var subject = child.CreateProgressSubject();
        subject.OnNext(new ProgressEvent { Epoch = 99 });

        Assert.True(coordinator.TryRead(out var evt));
        var tpe = Assert.IsType<TrainingProgressEvent>(evt);
        Assert.Equal(99, tpe.Progress.Epoch);
    }

    [Fact]
    public void CreateChild_NoCoordinator_ChildAlsoNone()
    {
        var env = new DefaultExecutionEnvironment(coordinator: null);
        var child = env.CreateChild();

        // Should not throw — just works without coordinator
        var progress = child.CreateProgress<int>("test");
        Assert.IsType<Progress<int>>(progress);
    }
}
