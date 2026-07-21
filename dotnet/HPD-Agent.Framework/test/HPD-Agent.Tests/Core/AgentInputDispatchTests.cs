using FluentAssertions;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public class AgentInputDispatchTests
{
    [Fact]
    public void BuiltInRegistrations_DeclareRequiredDelivery()
    {
        AgentInputDispatcher.GetBuiltInRegistration(typeof(UserMessagesInputEvent)).Delivery
            .Should().Be(AgentInputDelivery.QueuedWork);
        AgentInputDispatcher.GetBuiltInRegistration(typeof(CompactThreadInputEvent)).Delivery
            .Should().Be(AgentInputDelivery.QueuedWork);
        AgentInputDispatcher.GetBuiltInRegistration(typeof(ClientToolBackgroundOperationOutcomeEvent)).Delivery
            .Should().Be(AgentInputDelivery.ActiveControl);
        AgentInputDispatcher.GetBuiltInRegistration(typeof(InterruptionRequestEvent)).Delivery
            .Should().Be(AgentInputDelivery.ActiveControl);
        AgentInputDispatcher.GetBuiltInRegistration(typeof(SteeringInputEvent)).Delivery
            .Should().Be(AgentInputDelivery.ActiveControl);
    }

    [Fact]
    public async Task HandlerAdapter_CanDispatchMatchingInput()
    {
        var handler = new RecordingInputHandler();
        var adapter = new AgentInputHandlerAdapter<TestInputEvent>(handler);
        var input = new TestInputEvent("hello");

        await adapter.HandleAsync(input, CreateContext(), CancellationToken.None);

        handler.Seen.Should().Equal("hello");
    }

    [Fact]
    public async Task HandlerAdapter_RejectsMismatchedInput()
    {
        var adapter = new AgentInputHandlerAdapter<TestInputEvent>(new RecordingInputHandler());

        var act = () => adapter.HandleAsync(new UnknownInputEvent(), CreateContext(), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*TestInputEvent*UnknownInputEvent*");
    }

    [Fact]
    public async Task DispatchAsync_BeforeInput_CanReplaceInput()
    {
        var middleware = new ReplacingInputMiddleware(
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "replacement")] });
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([middleware]));
        var seen = new List<string>();
        var context = CreateContext(runMessages: input =>
        {
            seen.Add(input.Messages.Single().Text!);
            return Task.FromResult(AgentTurnResult.Empty);
        });

        await dispatcher.DispatchAsync(
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "original")] },
            AgentInputDispatcher.GetBuiltInRegistration(typeof(UserMessagesInputEvent)),
            context,
            CancellationToken.None);

        seen.Should().Equal("replacement");
        middleware.BeforeSeen.Should().Equal("original");
        middleware.AfterSeen.Should().Equal("replacement");
    }

    [Fact]
    public async Task DispatchAsync_BeforeInput_CanCancelInput()
    {
        var middleware = new CancellingInputMiddleware();
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([middleware]));
        var runMessagesCalled = false;
        var context = CreateContext(runMessages: _ =>
        {
            runMessagesCalled = true;
            return Task.FromResult(AgentTurnResult.Empty);
        });

        await dispatcher.DispatchAsync(
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "skip")] },
            AgentInputDispatcher.GetBuiltInRegistration(typeof(UserMessagesInputEvent)),
            context,
            CancellationToken.None);

        runMessagesCalled.Should().BeFalse();
        middleware.AfterCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_BeforeInput_CannotChangeDelivery()
    {
        var middleware = new ReplacingInputMiddleware(
            new SteeringInputEvent
            {
                ThreadExecutionId = "execution",
                Messages = [new ChatMessage(ChatRole.User, "steer")]
            });
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([middleware]));
        var input = new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, "queued")]
        };

        var act = () => dispatcher.DispatchAsync(
            input,
            AgentInputDispatcher.GetBuiltInRegistration(input.GetType()),
            CreateContext(),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*QueuedWork*ActiveControl*");
    }

    [Fact]
    public async Task DispatchAsync_AfterInput_ObservesHandlerFailure()
    {
        var middleware = new RecordingAfterInputMiddleware();
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([middleware]));
        var input = new ClientToolBackgroundOperationOutcomeEvent
        {
            ClientOperationId = "missing-operation",
            State = ClientToolBackgroundOperationOutcomeState.Completed
        };

        var act = () => dispatcher.DispatchAsync(
            input,
            AgentInputDispatcher.GetBuiltInRegistration(input.GetType()),
            CreateContext(),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing-operation*");
        middleware.Error.Should().BeOfType<InvalidOperationException>();
        middleware.AfterSeen.Should().BeSameAs(input);
    }

    [Fact]
    public void Registration_UnknownInput_ThrowsBeforeDelivery()
    {
        var middleware = new RecordingAfterInputMiddleware();
        var dispatcher = new AgentInputDispatcher(new AgentMiddlewarePipeline([middleware]));
        var input = new UnknownInputEvent();

        var act = () => AgentInputDispatcher.GetBuiltInRegistration(input.GetType());

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*UnknownInputEvent*cannot be used as agent input*");
    }

    private static AgentInputHandlingContext CreateContext(
        Func<UserMessagesInputEvent, Task<AgentTurnResult>>? runMessages = null)
        => new()
        {
            AgentName = "InputDispatchAgent",
            Config = new AgentConfig { Name = "InputDispatchAgent" },
            EventCoordinator = new EventCoordinator(),
            RunMessagesAsync = (input, _, _, _) => runMessages?.Invoke(input)
                ?? Task.FromResult(AgentTurnResult.Empty),
            InterruptAsync = (input, _) => Task.FromResult(new AgentInputResult
            {
                Disposition = AgentInputDisposition.Accepted,
                ThreadExecutionId = input.ThreadExecutionId
            }),
            SteerAsync = (input, _) => Task.FromResult(new AgentInputResult
            {
                Disposition = AgentInputDisposition.Accepted,
                ThreadExecutionId = input.ThreadExecutionId
            }),
            TryResolveClientToolBackgroundOperation = _ => false
        };

    private sealed record TestInputEvent(string Value) : AgentInputEvent;

    private sealed record UnknownInputEvent : AgentInputEvent;

    private sealed class RecordingInputHandler : IAgentInputHandler<TestInputEvent>
    {
        public List<string> Seen { get; } = [];

        public ValueTask<AgentInputResult> HandleAsync(
            TestInputEvent input,
            AgentInputHandlingContext context,
            CancellationToken cancellationToken)
        {
            Seen.Add(input.Value);
            return ValueTask.FromResult(new AgentInputResult
            {
                Disposition = AgentInputDisposition.Completed
            });
        }
    }

    private sealed class ReplacingInputMiddleware : IAgentMiddleware
    {
        private readonly AgentInputEvent _replacement;

        public ReplacingInputMiddleware(AgentInputEvent replacement)
        {
            _replacement = replacement;
        }

        public List<string> BeforeSeen { get; } = [];
        public List<string> AfterSeen { get; } = [];

        public Task BeforeInputAsync(BeforeInputContext context, CancellationToken cancellationToken)
        {
            if (context.Input is UserMessagesInputEvent input)
                BeforeSeen.Add(input.Messages.Single().Text!);

            context.ReplaceInput(_replacement);
            return Task.CompletedTask;
        }

        public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
        {
            if (context.Input is UserMessagesInputEvent input)
                AfterSeen.Add(input.Messages.Single().Text!);

            return Task.CompletedTask;
        }
    }

    private sealed class CancellingInputMiddleware : IAgentMiddleware
    {
        public bool AfterCancelled { get; private set; }

        public Task BeforeInputAsync(BeforeInputContext context, CancellationToken cancellationToken)
        {
            context.CancelInput("test cancel");
            return Task.CompletedTask;
        }

        public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
        {
            AfterCancelled = context.Cancelled;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAfterInputMiddleware : IAgentMiddleware
    {
        public AgentInputEvent? AfterSeen { get; private set; }
        public Exception? Error { get; private set; }

        public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
        {
            AfterSeen = context.Input;
            Error = context.Error;
            return Task.CompletedTask;
        }
    }
}
