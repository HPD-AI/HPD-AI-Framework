namespace HPD.Base.Tests.Runtime.Activations;

public sealed class ActivationHandlerExecutionGateTests
{
    [Fact]
    public async Task Noncooperative_handler_retains_capacity_until_late_completion()
    {
        await using var gate = new BaseActivationHandlerExecutionGate();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        BaseActivationHandlerExecutionResult<string> result = await gate.ExecuteAsync(
            _ => completion.Task,
            TimeSpan.FromMilliseconds(10),
            default);

        result.Outcome.Should().Be(BaseActivationHandlerExecutionOutcome.TimedOut);
        gate.RetainedCount.Should().Be(1);
        completion.SetResult("late");
        await Task.Delay(10);
        gate.RetainedCount.Should().Be(0);
    }

    [Fact]
    public async Task Noncooperative_provider_retains_capacity_until_late_completion()
    {
        var state = new BaseActivationOperationalState();
        await using var gate = new BaseActivationProviderExecutionGate(state);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        BaseActivationProviderCallResult<string> result = await gate.ExecuteAsync(
            _ => new ValueTask<string>(completion.Task),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            default);

        result.Outcome.Should().Be(BaseActivationProviderCallOutcome.TimedOut);
        gate.RetainedCount.Should().Be(1);
        state.Quarantined.Should().Be(1);
        completion.SetResult("late");
        await Task.Delay(10);
        gate.RetainedCount.Should().Be(0);
        state.Quarantined.Should().Be(0);
    }
}
