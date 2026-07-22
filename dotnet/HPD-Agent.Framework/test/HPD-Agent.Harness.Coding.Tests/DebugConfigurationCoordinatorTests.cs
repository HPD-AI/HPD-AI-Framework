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
}
