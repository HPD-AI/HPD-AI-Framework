using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugConfigurationCoordinatorTests
{
    [Fact]
    public async Task Initialized_before_launch_response_configures_once_without_deadlock()
    {
        var configured = 0;
        var releaseLaunch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new DebugConfigurationCoordinator(_ =>
        {
            Interlocked.Increment(ref configured);
            return Task.CompletedTask;
        }, CancellationToken.None);

        var launch = coordinator.RunLaunchAsync(_ => releaseLaunch.Task, CancellationToken.None);
        coordinator.ObserveInitialized();
        coordinator.ObserveInitialized();
        await coordinator.ConfigurationCompletion;
        launch.IsCompleted.Should().BeFalse();
        releaseLaunch.SetResult();
        await launch;
        await coordinator.AwaitStartBoundaryAsync(CancellationToken.None);

        configured.Should().Be(1);
    }

    [Fact]
    public async Task Launch_response_before_initialized_waits_for_configuration_boundary()
    {
        var coordinator = new DebugConfigurationCoordinator(_ => Task.CompletedTask, CancellationToken.None);
        await coordinator.RunLaunchAsync(_ => Task.CompletedTask, CancellationToken.None);
        var boundary = coordinator.AwaitStartBoundaryAsync(CancellationToken.None);

        boundary.IsCompleted.Should().BeFalse();
        coordinator.ObserveInitialized();
        await boundary;
    }

    [Fact]
    public async Task Terminal_event_settles_both_start_boundaries()
    {
        var coordinator = new DebugConfigurationCoordinator(_ => Task.CompletedTask, CancellationToken.None);
        coordinator.ObserveTerminal("ADAPTER_EXITED");
        var boundary = async () => await coordinator.AwaitStartBoundaryAsync(CancellationToken.None);

        (await boundary.Should().ThrowAsync<DebugStartTerminatedException>()).Which.ReasonCode.Should().Be("ADAPTER_EXITED");
    }

    [Fact]
    public async Task Launch_and_configuration_failures_preserve_both_causes()
    {
        var coordinator = new DebugConfigurationCoordinator(
            _ => Task.FromException(new InvalidOperationException(
                "configurationDone: Expected process to be stopped.")),
            CancellationToken.None);
        coordinator.ObserveInitialized();
        Func<Task> configuration = async () =>
            await coordinator.ConfigurationCompletion;
        await configuration.Should().ThrowAsync<InvalidOperationException>();

        var launch = coordinator.RunLaunchAsync(
            _ => Task.FromException(new InvalidOperationException(
                "launch: program is not a valid executable")),
            CancellationToken.None);
        Func<Task> launchAction = async () => await launch;
        await launchAction.Should().ThrowAsync<InvalidOperationException>();

        var boundary = async () =>
            await coordinator.AwaitStartBoundaryAsync(CancellationToken.None);
        var error = (await boundary.Should()
            .ThrowAsync<DebugStartBoundaryException>()).Which;
        error.Message.Should().Contain("launch: program is not a valid executable");
        error.Message.Should().Contain(
            "configurationDone: Expected process to be stopped.");
        error.LaunchException.Message.Should().StartWith("launch:");
        error.ConfigurationException.Message.Should().StartWith("configurationDone:");
    }

    [Fact]
    public async Task Delayed_launch_failure_is_not_hidden_by_earlier_configuration_failure()
    {
        var releaseLaunch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new DebugConfigurationCoordinator(
            _ => Task.FromException(new InvalidOperationException(
                "configurationDone: cascade failure")),
            CancellationToken.None);
        var launch = coordinator.RunLaunchAsync(
            _ => releaseLaunch.Task, CancellationToken.None);
        coordinator.ObserveInitialized();
        var boundary = coordinator.AwaitStartBoundaryAsync(CancellationToken.None);
        boundary.IsCompleted.Should().BeFalse();

        releaseLaunch.SetException(new InvalidOperationException(
            "launch: delayed primary failure"));
        Func<Task> launchAction = async () => await launch;
        await launchAction.Should().ThrowAsync<InvalidOperationException>();
        Func<Task> boundaryAction = async () => await boundary;
        var error = (await boundaryAction.Should()
            .ThrowAsync<DebugStartBoundaryException>()).Which;
        error.Message.Should().Contain("launch: delayed primary failure");
        error.Message.Should().Contain("configurationDone: cascade failure");
    }
}
