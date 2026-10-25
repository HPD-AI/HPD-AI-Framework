using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using HPD.Events;

namespace HPD.Agent.Tests;

public sealed class MultiAgentRuntimeTimeoutTests
{
    [Fact]
    public async Task Deadline_CancelsExecutionAndDisposesWorkflow()
    {
        var workflow = new SlowWorkflow();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            MultiAgentRuntime.InvokeAsync(Request(workflow, TimeSpan.FromMilliseconds(30)), timeout.Token));

        Assert.Contains("deadline", error.Message, StringComparison.OrdinalIgnoreCase);
        await workflow.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CallerCancellation_IsDistinctFromDeadline()
    {
        var workflow = new SlowWorkflow();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MultiAgentRuntime.InvokeAsync(Request(workflow, TimeSpan.FromSeconds(5)), cancellation.Token));
        await workflow.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SlowConstruction_IsBoundedAndLateResultIsDisposed()
    {
        var workflow = new SlowWorkflow();
        var request = Request(workflow, TimeSpan.FromMilliseconds(30)) with
        {
            WorkflowFactory = async () =>
            {
                await Task.Delay(120);
                return workflow;
            }
        };

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            MultiAgentRuntime.InvokeAsync(request, default));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        await workflow.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task BlockingParameterlessFactoryDoesNotBlockTheDeadline()
    {
        var workflow = new SlowWorkflow();
        var request = Request(workflow, TimeSpan.FromMilliseconds(30)) with
        {
            WorkflowFactory = () =>
            {
                System.Threading.Thread.Sleep(120);
                return Task.FromResult<IMultiAgentWorkflow>(workflow);
            }
        };

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            MultiAgentRuntime.InvokeAsync(request, default));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        await workflow.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task InvalidDeadline_FailsBeforeConstruction()
    {
        var constructed = false;
        var request = Request(new SlowWorkflow(), TimeSpan.Zero) with
        {
            WorkflowFactory = () =>
            {
                constructed = true;
                return Task.FromResult<IMultiAgentWorkflow>(new SlowWorkflow());
            }
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MultiAgentRuntime.InvokeAsync(request, default));
        Assert.False(constructed);
    }

    private static MultiAgentRuntime.MultiAgentInvocationRequest Request(
        SlowWorkflow workflow,
        TimeSpan timeout) => new()
    {
        WorkflowFactory = () => Task.FromResult<IMultiAgentWorkflow>(workflow),
        Timeout = timeout,
        Name = "timeout-test",
        Input = "work"
    };

    private sealed class SlowWorkflow : IMultiAgentWorkflow, IAsyncDisposable
    {
        internal TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<Event> ExecuteStreamingAsync(
            string input,
            FunctionExecutionContext? parentContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public async Task<string> RunAsync(
            string input,
            FunctionExecutionContext? parentContext,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return input;
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
