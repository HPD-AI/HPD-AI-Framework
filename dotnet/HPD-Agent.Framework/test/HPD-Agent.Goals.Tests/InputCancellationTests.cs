using HPD.Agent.Middleware;
using HPD.Events.Core;

namespace HPD.Agent.Tests;

public class InputCancellationTests
{
    private sealed class Recorder(bool stopBeforeDispatch = false) : IAgentMiddleware
    {
        public AfterInputContext? After { get; private set; }
        public Task BeforeInputAsync(BeforeInputContext context, CancellationToken cancellationToken)
        {
            if (stopBeforeDispatch) context.CancelInput("user decision required");
            return Task.CompletedTask;
        }
        public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
        {
            After = context;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void FirstTrustedCancellationRetainsItsReasonWhenRuntimeLaterStops()
    {
        using var cts = new CancellationTokenSource();
        var active = new ActiveRuntimeInput(new UserMessagesInputEvent { ThreadExecutionId = "e1" }, cts);
        active.RecordCancellation(new(AgentInputCancellationCause.Explicit, "pause requested", "thread_execution_controller"));
        active.RecordCancellation(new(AgentInputCancellationCause.RuntimeShutdown, "stopping", "runtime"));
        Assert.Equal(AgentInputCancellationCause.Explicit, active.CancellationInfo!.Cause);
        Assert.Equal("pause requested", active.CancellationInfo.Reason);
    }

    [Fact]
    public async Task InputMiddlewareCancellationPreservesReasonThroughAfterInput()
    {
        using var coordinator = new EventCoordinator();
        var recorder = new Recorder(true);
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([recorder]));
        var context = new AgentInputHandlingContext
        {
            AgentName = "test", Config = new(), EventCoordinator = coordinator,
            TryResolveClientToolOperation = _ => false,
            RunMessagesAsync = (_, _, _, _, _) => throw new Xunit.Sdk.XunitException("Cancelled input must not dispatch")
        };
        await dispatcher.DispatchAsync(new UserMessagesInputEvent(),
            AgentInputDispatcher.GetBuiltInRegistration(typeof(UserMessagesInputEvent)), context, default);
        Assert.True(recorder.After!.Cancelled);
        Assert.Equal(new AgentInputCancellation(AgentInputCancellationCause.Middleware,
            "user decision required", "input_middleware"), recorder.After.Cancellation);
    }
}
