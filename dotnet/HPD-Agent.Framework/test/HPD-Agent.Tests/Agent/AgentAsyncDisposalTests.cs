using HPD.Agent.Tests.Infrastructure;
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
            .BuildAsync();
        var turn = agent.RunAsync("block");
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await agent.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        Assert.True(client.CancellationObserved);
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
            serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("blocking", null, "blocking")
                : null;

        public void Dispose() { }
    }
}
