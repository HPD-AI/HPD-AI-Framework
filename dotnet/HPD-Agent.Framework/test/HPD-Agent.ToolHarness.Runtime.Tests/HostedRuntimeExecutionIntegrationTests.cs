using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Middleware;

public sealed class HostedRuntimeExecutionIntegrationTests
{
    [Fact]
    public async Task ConcurrentHostedInputs_DoNotShareMiddlewareInstancesOrRegistries()
    {
        HostedIsolationMiddleware.Reset();
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var sessions = new TestSessionManager(store);
        await using var agents = new TestAgentManager(new InMemoryAgentStore(), store);
        var service = new AgentStreamingService(sessions, agents);
        var definition = await agents.CreateDefinitionAsync(
            new AgentConfig { Name = "hosted-isolation" },
            "hosted-isolation");
        var first = await sessions.CreateSessionAsync(definition.Id, "hosted-first");
        var second = await sessions.CreateSessionAsync(definition.Id, "hosted-second");

        var submissionsTask = Task.WhenAll(
            service.SubmitInputAsync(definition.Id, first.sessionId, first.threadId, Input("first")),
            service.SubmitInputAsync(definition.Id, second.sessionId, second.threadId, Input("second")));

        await HostedIsolationMiddleware.BothActivated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, HostedIsolationMiddleware.CreatedCount);
        Assert.Equal(2, HostedIsolationMiddleware.ExecutionIds.Count);
        Assert.Equal(2, HostedIsolationMiddleware.InstanceIds.Count);
        Assert.Equal(0, HostedIsolationMiddleware.DisposedCount);
        HostedIsolationMiddleware.ReleaseActivations.TrySetResult();
        var submissions = await submissionsTask;
        Assert.All(submissions, result => Assert.Equal(AgentServiceStatus.Success, result.Status));
        await WaitUntilAsync(() => HostedIsolationMiddleware.DisposedCount == 2);
        Assert.Equal(2, HostedIsolationMiddleware.CreatedCount);
        Assert.Equal(2, HostedIsolationMiddleware.ExecutionIds.Count);
        Assert.Equal(2, HostedIsolationMiddleware.InstanceIds.Count);
    }

    private static UserMessagesInputEvent Input(string text) => new()
    {
        Messages = [new ChatMessage(ChatRole.User, text)]
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class TestSessionManager(ISessionStore store) : SessionManager(store);

    private sealed class TestAgentManager(IAgentStore definitions, ISessionStore sessions)
        : AgentManager(definitions)
    {
        private readonly HostedHarnessClient _client = new();

        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct) =>
            new AgentBuilder(new AgentConfig
                {
                    Name = agentId,
                    MaxAgenticIterations = 5
                })
                .WithAgentId(agentId)
                .WithSessionStore(sessions)
                .WithChatClient(_client)
                .WithEventComposition(CoreAgentEventComposition.Instance)
                .WithToolHarness<HostedIsolationHarness>()
                .BuildAsync(ct);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }

    private sealed class HostedHarnessClient : IChatClient
    {
        private readonly ConcurrentDictionary<string, int> _stages = new(StringComparer.Ordinal);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var key = chatMessages
                .Where(message => message.Role == ChatRole.User)
                .SelectMany(message => message.Contents)
                .OfType<TextContent>()
                .Select(content => content.Text)
                .First();
            var stage = _stages.AddOrUpdate(key, 1, static (_, current) => current + 1);
            return Task.FromResult(stage switch
            {
                1 => ToolCall(nameof(HostedIsolationHarness), "expand-" + key),
                2 => ToolCall(nameof(HostedIsolationHarness.Ping), "ping-" + key),
                _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")])
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(chatMessages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate
                {
                    Role = message.Role,
                    Contents = message.Contents,
                    FinishReason = message.Contents.OfType<FunctionCallContent>().Any()
                        ? ChatFinishReason.ToolCalls
                        : ChatFinishReason.Stop
                };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        private static ChatResponse ToolCall(string name, string callId) => new(
            [new ChatMessage(ChatRole.Assistant,
                [(AIContent)new FunctionCallContent(callId, name, new Dictionary<string, object?>())])]);
    }
}

[Collapse(
    "Hosted execution isolation acceptance harness",
    FunctionResult = "expanded",
    Middlewares = [typeof(HostedIsolationMiddleware)])]
public sealed partial class HostedIsolationHarness
{
    [AIFunction]
    public string Ping() => "pong";
}

public sealed class HostedIsolationMiddleware
    : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private static int _created;
    private static int _disposed;
    internal static TaskCompletionSource BothActivated { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource ReleaseActivations { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static ConcurrentDictionary<string, byte> ExecutionIds { get; } = new(StringComparer.Ordinal);
    internal static ConcurrentDictionary<string, byte> InstanceIds { get; } = new(StringComparer.Ordinal);
    public HostedIsolationMiddleware() => Interlocked.Increment(ref _created);
    internal static int CreatedCount => Volatile.Read(ref _created);
    internal static int DisposedCount => Volatile.Read(ref _disposed);
    internal static void Reset()
    {
        ExecutionIds.Clear();
        InstanceIds.Clear();
        Volatile.Write(ref _created, 0);
        Volatile.Write(ref _disposed, 0);
        BothActivated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ReleaseActivations = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        ExecutionIds.TryAdd(context.InputExecutionId, 0);
        InstanceIds.TryAdd(_instanceId, 0);
        if (ExecutionIds.Count == 2)
            BothActivated.TrySetResult();
        await BothActivated.Task.WaitAsync(cancellationToken);
        await ReleaseActivations.Task.WaitAsync(cancellationToken);
    }

    public ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposed);
        return ValueTask.CompletedTask;
    }
}
