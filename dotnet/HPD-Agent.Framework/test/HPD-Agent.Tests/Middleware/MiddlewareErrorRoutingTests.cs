using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Middleware;

public sealed class MiddlewareErrorRoutingTests
{
    [Fact]
    public async Task ModelFailure_ReachesOnErrorExactlyOnce()
    {
        var probe = new ErrorRoutingProbe();
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "model-error" })
            .WithChatClient(new ThrowingChatClient())
            .WithMiddleware(probe)
            .BuildAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => agent.RunAsync("hello"));

        var observation = Assert.Single(probe.Errors);
        Assert.Equal(ErrorSource.ModelCall, observation.Source);
    }

    [Fact]
    public async Task IterationFailure_ReachesOnErrorExactlyOnce()
    {
        var probe = new ErrorRoutingProbe { ThrowFromIteration = true };
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "iteration-error" })
            .WithChatClient(new FakeChatClient())
            .WithMiddleware(probe)
            .BuildAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("hello"));

        var observation = Assert.Single(probe.Errors);
        Assert.Equal(ErrorSource.Iteration, observation.Source);
    }

    [Fact]
    public async Task InputFailure_ReachesOnErrorExactlyOnce()
    {
        var probe = new ErrorRoutingProbe { ThrowFromInput = true };
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "input-error" })
            .WithChatClient(new FakeChatClient())
            .WithMiddleware(probe)
            .BuildAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("hello"));

        var observation = Assert.Single(probe.Errors);
        Assert.Equal(ErrorSource.Input, observation.Source);
    }

    [Fact]
    public async Task RuntimeFailure_ReachesOnErrorExactlyOnce()
    {
        var probe = new ErrorRoutingProbe { ThrowFromStart = true };
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "runtime-error" })
            .WithChatClient(new FakeChatClient())
            .WithMiddleware(probe)
            .BuildAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.StartAsync());

        var observation = Assert.Single(probe.Errors);
        Assert.Equal(ErrorSource.Runtime, observation.Source);
    }

    [Fact]
    public async Task ThreadForkFailure_ReachesOnErrorExactlyOnce()
    {
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var session = new global::HPD.Agent.Session("fork-error");
        await store.SaveSessionAsync(session);
        var source = session.CreateThread("fork-agent", "main");
        source.AddMessage(new ChatMessage(ChatRole.User, "hello")
        {
            MessageId = "message-1"
        });
        await store.SaveInitialThreadAsync(session.Id, source);
        var probe = new ErrorRoutingProbe { ThrowFromFork = true };
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "fork-agent" })
            .WithChatClient(new FakeChatClient())
            .WithSessionStore(store)
            .WithMiddleware(probe)
            .BuildAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ForkThreadAsync(session.Id, "main", "alt", "message-1"));

        var observation = Assert.Single(probe.Errors);
        Assert.Equal(ErrorSource.ThreadFork, observation.Source);
    }

    private sealed class ErrorRoutingProbe : IAgentMiddleware
    {
        public bool ThrowFromInput { get; init; }
        public bool ThrowFromIteration { get; init; }
        public bool ThrowFromStart { get; init; }
        public bool ThrowFromFork { get; init; }
        public List<(ErrorSource Source, Exception Error)> Errors { get; } = [];

        public Task BeforeInputAsync(
            BeforeInputContext context,
            CancellationToken cancellationToken)
        {
            if (ThrowFromInput)
                throw new InvalidOperationException("input failure");
            return Task.CompletedTask;
        }

        public Task BeforeIterationAsync(
            BeforeIterationContext context,
            CancellationToken cancellationToken)
        {
            if (ThrowFromIteration)
                throw new InvalidOperationException("iteration failure");
            return Task.CompletedTask;
        }

        public Task BeforeStartAsync(
            BeforeStartContext context,
            CancellationToken cancellationToken)
        {
            if (ThrowFromStart)
                throw new InvalidOperationException("runtime failure");
            return Task.CompletedTask;
        }

        public Task BeforeThreadForkCommitAsync(
            BeforeThreadForkCommitContext context,
            CancellationToken cancellationToken)
        {
            if (ThrowFromFork)
                throw new InvalidOperationException("fork failure");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(
            ErrorContext context,
            CancellationToken cancellationToken)
        {
            Errors.Add((context.Source, context.Error));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(
                new HttpRequestException("provider unavailable"));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new HttpRequestException("provider unavailable");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose()
        {
        }
    }
}
