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
}
