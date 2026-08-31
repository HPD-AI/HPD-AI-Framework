using HPD.Agent.Middleware;

namespace HPD.Agent.Tests.Tools;

public sealed class FunctionOperationCommitGateTests
{
    [Fact]
    public async Task FailureBeforeCommitReopensGate()
    {
        var gate = new FunctionOperationCommitGate();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gate.StartOperationAsync(() =>
            {
                attempts++;
                throw new InvalidOperationException("pre-commit");
            }, CancellationToken.None));

        var receipt = await gate.StartOperationAsync(() =>
        {
            attempts++;
            return ValueTask.FromResult(Receipt("committed"));
        }, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Same(receipt, gate.CommittedReceipt);
    }

    [Fact]
    public async Task ConcurrentStartsCommitExactlyOnce()
    {
        var gate = new FunctionOperationCommitGate();
        var starts = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<AgentOperationReceipt> Start()
        {
            Interlocked.Increment(ref starts);
            await release.Task;
            return Receipt("only");
        }

        var first = gate.StartOperationAsync(Start, CancellationToken.None).AsTask();
        var second = gate.StartOperationAsync(Start, CancellationToken.None).AsTask();
        release.SetResult();

        await first;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal("tool_body_operation_already_committed", error.Message);
        Assert.Equal(1, starts);
    }

    private static AgentOperationReceipt Receipt(string id) => new()
    {
        OperationId = id,
        SourceKind = AgentOperationSourceKind.LocalTool,
        Name = "test",
        Address = new AgentExecutionAddress("agent", "session", "thread"),
        ProviderStatus = AgentOperationProviderStatus.Accepted,
        ObservationStatus = AgentOperationObservationStatus.Attached,
        Control = new AgentOperationControl(id, AgentOperationKind.Task, AgentOperationCapabilities.Cancel)
    };
}
