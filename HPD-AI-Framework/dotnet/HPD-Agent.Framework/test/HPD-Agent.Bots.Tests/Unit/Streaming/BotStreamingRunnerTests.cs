using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.AI;
using HpdAgent = HPD.Agent.Agent;

namespace HPD.Agent.Bots.Tests.Unit.Streaming;

public class BotStreamingRunnerTests
{
    [Fact]
    public async Task RunAsync_BufferAndPost_CompletesTextWithoutIntermediateUpdates()
    {
        var chatClient = new StreamingChatClient(["hel", "lo"]);
        var agent = new HpdAgent(CreateAgentConfig(), chatClient, mergedOptions: null);
        await agent.CreateSessionAsync("session-1");
        var runner = new BotStreamingRunner(
            new TestSessionManager(),
            new StaticAgentManager(agent));

        var updates = new List<string>();
        var completes = new List<string>();

        var started = await runner.RunAsync(
            new BotStreamingRequest<object>(
                AgentId: "test-agent",
                SessionId: "session-1",
                BranchId: "main",
                Text: "hello",
                Context: new object(),
                Strategy: StreamingStrategy.BufferAndPost,
                DebounceMs: 1),
            new BotStreamingCallbacks<object>
            {
                UpdateTextAsync = (_, text, _) =>
                {
                    updates.Add(text);
                    return Task.CompletedTask;
                },
                CompleteTextAsync = (_, text, _) =>
                {
                    completes.Add(text);
                    return Task.CompletedTask;
                },
                CompleteCardAsync = (_, _, _) => Task.CompletedTask,
            },
            CancellationToken.None);

        started.Should().BeTrue();
        updates.Should().BeEmpty();
        completes.Should().ContainSingle().Which.Should().Be("hello");
    }

    [Fact]
    public async Task RunAsync_PostAndEdit_UpdatesTextBeforeCompletion()
    {
        var chatClient = new StreamingChatClient(["hel", "lo"], TimeSpan.FromMilliseconds(25));
        var agent = new HpdAgent(CreateAgentConfig(), chatClient, mergedOptions: null);
        await agent.CreateSessionAsync("session-1");
        var runner = new BotStreamingRunner(
            new TestSessionManager(),
            new StaticAgentManager(agent));

        var updates = new List<string>();
        var completes = new List<string>();

        var started = await runner.RunAsync(
            new BotStreamingRequest<object>(
                AgentId: "test-agent",
                SessionId: "session-1",
                BranchId: "main",
                Text: "hello",
                Context: new object(),
                Strategy: StreamingStrategy.PostAndEdit,
                DebounceMs: 1),
            new BotStreamingCallbacks<object>
            {
                UpdateTextAsync = (_, text, _) =>
                {
                    updates.Add(text);
                    return Task.CompletedTask;
                },
                CompleteTextAsync = (_, text, _) =>
                {
                    completes.Add(text);
                    return Task.CompletedTask;
                },
                CompleteCardAsync = (_, _, _) => Task.CompletedTask,
            },
            CancellationToken.None);

        started.Should().BeTrue();
        updates.Should().NotBeEmpty();
        completes.Should().ContainSingle().Which.Should().Be("hello");
    }

    private static AgentConfig CreateAgentConfig()
        => new()
        {
            Name = "StreamingTestAgent",
            MaxAgenticIterations = 3,
            SystemInstructions = "You are a streaming test agent.",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                ProviderKey = "test",
                ModelName = "test-model",
            } },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromMinutes(1),
            },
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 0,
                NormalizeErrors = true,
            },
            SessionRepository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore()),
            SessionRepositoryOptions = new SessionRepositoryOptions
            {
                PersistAfterTurn = true,
            },
        };

    private sealed class StaticAgentManager(HpdAgent agent)
        : AgentManager(new WorkspaceAgentRepository(new InMemoryWorkspaceStore()))
    {
        public override Task<HpdAgent> GetOrBuildAgentAsync(string agentId, CancellationToken ct = default)
            => Task.FromResult(agent);

        protected override Task<HpdAgent> BuildAgentAsync(string agentId, CancellationToken ct)
            => Task.FromResult(agent);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(5);
    }

    private sealed class StreamingChatClient(
        IReadOnlyList<string> chunks,
        TimeSpan? delayBetweenChunks = null) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate
                {
                    Contents = [new TextContent(chunk)],
                };

                if (delayBetweenChunks is { } delay)
                    await Task.Delay(delay, cancellationToken);
            }

            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, string.Concat(chunks))]));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
