using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Tests.Lifecycle;

public sealed class AgentAsyncDisposalTests
{
    [Fact]
    public async Task DisposeAsync_IsAwaitablyIdempotentAndRejectsEveryNewWorkEntryPoint()
    {
        var agent = await new AgentBuilder(new AgentConfig { Name = "async-disposal-test" })
            .WithChatClient(new FakeChatClient())
            .WithEventApplicationIdentity("HPD-Agent")
            .BuildAsync();

        var first = agent.DisposeAsync().AsTask();
        var second = agent.DisposeAsync().AsTask();
        await Task.WhenAll(first, second);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => agent.RunAsync("new turn"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => agent.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            agent.RefreshCapabilitiesAsync().AsTask());
    }

    [Fact]
    public async Task DisposeAsync_DrainsThenCancelsAnAcceptedNonCooperativeTurn()
    {
        var client = new BlockingChatClient();
        var agent = await new AgentBuilder(new AgentConfig
            {
                Name = "turn-drain-test",
                Shutdown = new AgentShutdownOptions
                {
                    GracefulDrainTimeout = TimeSpan.FromMilliseconds(10),
                    CancellationDrainTimeout = TimeSpan.FromSeconds(1)
                }
            })
            .WithChatClient(client)
            .WithEventApplicationIdentity("HPD-Agent")
            .BuildAsync();
        var turn = agent.RunAsync("block");
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await agent.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        Assert.True(client.CancellationObserved);
    }

    [Fact]
    public async Task StopAsync_CallerCancellationStillCompletesRuntimeCleanupAndAllowsRestart()
    {
        var client = new BrieflyNonCooperativeChatClient(TimeSpan.FromMilliseconds(75));
        var lifecycle = new RuntimeLifecycleProbe();
        var config = new AgentConfig
        {
            Name = "stop-cancellation-test",
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Override = ClientOverride<IChatClient>.Borrow(
                        client,
                        providerKey: "test",
                        backendKey: "local",
                        operationAdapterKey: "test/chat")
                }
            }
        };
        var agent = await new AgentBuilder(config)
            .WithMiddleware(lifecycle)
            .WithEventApplicationIdentity("HPD-Agent")
            .BuildAsync();

        await agent.StartAsync();
        var turn = agent.RunAsync("block briefly", threadId: null);
        var enteredOrCompleted = await Task.WhenAny(
            client.Entered.Task,
            turn,
            Task.Delay(TimeSpan.FromSeconds(2)));
        if (ReferenceEquals(enteredOrCompleted, turn))
            await turn;
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var stopCancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        await agent.StopAsync(stopCancellation.Token);

        await turn;
        Assert.False(agent.IsRunning);
        Assert.True(lifecycle.Resource.Disposed);
        Assert.Equal(1, lifecycle.AfterStoppedCount);

        await agent.StartAsync();
        Assert.True(agent.IsRunning);
        await agent.StopAsync();
        Assert.False(agent.IsRunning);

        await agent.DisposeAsync();
    }

    private sealed class BlockingChatClient : IChatClient
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CancellationObserved { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            throw new InvalidOperationException("unreachable");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ProviderClientExecutionIdentity)
                ? new ProviderClientExecutionIdentity
                {
                    ProviderKey = "test",
                    BackendKey = "local",
                    Family = ProviderClientFamily.Chat,
                    ModelName = "blocking",
                    OperationAdapterKey = "test/chat",
                    UsageSemanticsKey = "test",
                    SafeConfigurationFingerprint = "test-fixture"
                }
                : serviceType == typeof(ChatClientMetadata)
                    ? new ChatClientMetadata("blocking", null, "blocking")
                    : null;

        public void Dispose() { }
    }

    private sealed class BrieflyNonCooperativeChatClient(TimeSpan delay) : IChatClient
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(delay, CancellationToken.None);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(delay, CancellationToken.None);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("briefly-non-cooperative", null, "test")
                : null;

        public void Dispose() { }
    }

    private sealed class RuntimeLifecycleProbe : IAgentMiddleware
    {
        internal TrackingDisposable Resource { get; private set; } = new();
        internal int AfterStoppedCount { get; private set; }

        public Task BeforeStartAsync(
            BeforeStartContext context,
            CancellationToken cancellationToken)
        {
            Resource = new TrackingDisposable();
            context.RegisterDisposable(Resource);
            return Task.CompletedTask;
        }

        public Task AfterStoppedAsync(
            AfterStoppedContext context,
            CancellationToken cancellationToken)
        {
            AfterStoppedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
