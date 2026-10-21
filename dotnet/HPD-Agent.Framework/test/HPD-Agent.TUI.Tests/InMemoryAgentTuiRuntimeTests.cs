using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.TUI.Runtime;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI.Tests;

public sealed class InMemoryAgentTuiRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitInput_UsesAgentAllocatedIdentityAndOneAuthoritativeLifecycle(bool fail)
    {
        await using var agent = await CreateBuilder(fail)
            .WithInMemorySessionStore()
            .BuildAsync();
        var scope = new AgentTuiRuntimeScope(agent.AgentId, "review", "main");
        await agent.CreateSessionAsync(scope.SessionId);
        await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
        var target = new DirectAgentTuiExecutionTarget(scope);

        for (var turn = 0; turn < 2; turn++)
        {
            var submitted = await runtime.SubmitInputAsync(target, new UserMessagesInputEvent
            {
                Messages = [new ChatMessage(ChatRole.User, "Review the evidence.")]
            });
            Assert.False(string.IsNullOrWhiteSpace(submitted.ThreadExecutionId));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((await runtime.GetThreadStateAsync(scope, timeout.Token)).ActiveExecution is not null)
                await Task.Delay(10, timeout.Token);

            var events = await agent.Config.SessionStore!.CollectThreadEventsAsync(
                new ThreadKey(scope.SessionId, scope.ThreadId), timeout.Token);
            var started = Assert.Single(events!.OfType<ThreadExecutionStartedEvent>(),
                e => e.ThreadExecutionId == submitted.ThreadExecutionId);
            var finished = Assert.Single(events.OfType<ThreadExecutionFinishedEvent>(),
                e => e.ThreadExecutionId == submitted.ThreadExecutionId);
            Assert.Equal(started.ThreadExecutionId, finished.ThreadExecutionId);
            Assert.True(finished.Outcome == (fail ? ThreadExecutionOutcome.Failed : ThreadExecutionOutcome.Succeeded), finished.Error?.Message);
            if (fail)
                Assert.Contains("Verification failed in test client.", finished.Error!.Message);
            if (!fail)
                Assert.Contains(events.OfType<TextDeltaEvent>(), e => e.Text.Contains("Reviewed"));
        }
    }

    [Fact]
    public async Task RejectedIdentity_DoesNotLeaveAGhostLifecycleOrBlockNextInput()
    {
        await using var agent = await CreateBuilder(false)
            .WithInMemorySessionStore()
            .BuildAsync();
        var scope = new AgentTuiRuntimeScope(agent.AgentId, "review", "main");
        await agent.CreateSessionAsync(scope.SessionId);
        await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
        var target = new DirectAgentTuiExecutionTarget(scope);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitInputAsync(target,
            new UserMessagesInputEvent
            {
                ThreadExecutionId = "caller-invented",
                Messages = [new ChatMessage(ChatRole.User, "Review")]
            }));
        var events = await agent.Config.SessionStore!.CollectThreadEventsAsync(new(scope.SessionId, scope.ThreadId));
        Assert.DoesNotContain(events!, e => e is ThreadExecutionStartedEvent or ThreadExecutionFinishedEvent);
        var accepted = await runtime.SubmitInputAsync(target, new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, "Review")]
        });
        Assert.NotEqual("caller-invented", accepted.ThreadExecutionId);
    }

    private static AgentBuilder CreateBuilder(bool fail) => new AgentBuilder(new AgentConfig
    {
        Clients = new AgentClientsConfig
        {
            Chat = new ChatClientConfig
            {
                Override = ClientOverride<IChatClient>.Borrow(new TestClient(fail), "test", "local", "test.adapter")
            }
        }
    }).WithEventComposition(CoreAgentEventComposition.Instance);

    private sealed class TestClient(bool fail) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (fail) throw new InvalidOperationException("Verification failed in test client.");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Reviewed")
            {
                MessageId = Guid.NewGuid().ToString("N"),
                FinishReason = ChatFinishReason.Stop
            };
        }
    }
}
