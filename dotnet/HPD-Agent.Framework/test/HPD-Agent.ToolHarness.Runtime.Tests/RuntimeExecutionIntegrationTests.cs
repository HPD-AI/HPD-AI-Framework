using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Middleware;

public sealed class RuntimeExecutionIntegrationTests
{
    [Fact]
    public async Task ConcurrentRunAsync_OnOneAgent_UsesIsolatedExecutionRegistriesAndInstances()
    {
        ExecutionIsolationMiddleware.Reset();
        await using var agent = await BuildAgentAsync("direct-execution-isolation");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(
            agent.RunAsync("first", cancellationToken: timeout.Token),
            agent.RunAsync("second", cancellationToken: timeout.Token));

        Assert.Equal(2, ExecutionIsolationMiddleware.CreatedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.DisposedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.ExecutionCount);
    }

    [Fact]
    public async Task ContinuousRuntimeInputs_UseSeparateExecutionRegistries()
    {
        ExecutionIsolationMiddleware.Reset();
        await using var agent = await BuildAgentAsync("continuous-execution-isolation");
        await agent.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Task.WhenAll(
            agent.RunAsync("runtime-first", cancellationToken: timeout.Token),
            agent.RunAsync("runtime-second", cancellationToken: timeout.Token));
        await agent.StopAsync();

        Assert.Equal(2, ExecutionIsolationMiddleware.CreatedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.DisposedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.ExecutionCount);
    }

    [Fact]
    public async Task FailedAfterMessageTurn_DoesNotOwnMandatoryExecutionCleanup()
    {
        ExecutionIsolationMiddleware.Reset();
        ExecutionIsolationMiddleware.ThrowAfterMessageTurn = true;
        await using var agent = await BuildAgentAsync("failed-after-message-turn-cleanup");

        await Assert.ThrowsAsync<AggregateException>(() =>
            agent.RunAsync("failing-finalization"));

        Assert.Equal(1, ExecutionIsolationMiddleware.CreatedCount);
        Assert.Equal(1, ExecutionIsolationMiddleware.DisposedCount);
        Assert.Equal(1, ExecutionIsolationMiddleware.ExecutionCount);
    }

    [Fact]
    public async Task AspNetCoreEndpointAndDirectRun_UseTheSameExecutionOwnerSemantics()
    {
        ExecutionIsolationMiddleware.Reset();
        var webBuilder = WebApplication.CreateBuilder();
        webBuilder.WebHost.UseTestServer();
        webBuilder.Services.AddSingleton(new SingletonRetentionSentinel());
        await using var app = webBuilder.Build();
        await using var agent = await BuildAgentAsync("aspnet-owner-convergence", app.Services);
        app.MapGet("/run", async () =>
        {
            await agent.RunAsync("aspnet-owner");
            return Results.Ok("done");
        });
        await app.StartAsync();

        await agent.RunAsync("direct-owner");
        using var response = await app.GetTestClient().GetAsync("/run");
        response.EnsureSuccessStatusCode();

        Assert.Equal(2, ExecutionIsolationMiddleware.CreatedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.DisposedCount);
        Assert.Equal(2, ExecutionIsolationMiddleware.ExecutionCount);
    }

    [Fact]
    public async Task CompletedRegistry_IsNotRetainedByAgentContainerOrSingletonProvider()
    {
        ExecutionIsolationMiddleware.Reset();
        var services = new ServiceCollection();
        services.AddSingleton(new SingletonRetentionSentinel());
        await using var provider = services.BuildServiceProvider();
        await using var agent = await BuildAgentAsync("registry-collectability", provider);

        var registry = await RunAndCaptureRegistryAsync(agent);
        await ForceCollectionAsync(registry);

        Assert.False(registry.IsAlive);
        Assert.NotNull(provider.GetRequiredService<SingletonRetentionSentinel>());
        GC.KeepAlive(agent);
        GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> RunAndCaptureRegistryAsync(Agent agent)
    {
        await agent.RunAsync("collect-registry");
        return ExecutionIsolationMiddleware.LastRegistryReference
            ?? throw new InvalidOperationException("Middleware did not observe its execution registry.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ForceCollectionAsync(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }
    }

    private static Task<Agent> BuildAgentAsync(string name, IServiceProvider? services = null)
    {
        var builder = new AgentBuilder(new AgentConfig
        {
            Name = name,
            MaxAgenticIterations = 5
        })
        .WithChatClient(new HarnessDrivingChatClient())
        .WithEventComposition(CoreAgentEventComposition.Instance)
        .WithToolHarness<ExecutionIsolationHarness>();
        if (services is not null)
            builder.WithServiceProvider(services);
        return builder.BuildAsync();
    }

    private sealed class SingletonRetentionSentinel;

    private sealed class HarnessDrivingChatClient : IChatClient
    {
        private readonly ConcurrentDictionary<string, int> _stages = new(StringComparer.Ordinal);
        private int _callId;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var executionKey = chatMessages
                .Where(static message => message.Role == ChatRole.User)
                .SelectMany(static message => message.Contents)
                .OfType<TextContent>()
                .Select(static content => content.Text)
                .First();
            var stage = _stages.AddOrUpdate(executionKey, 1, static (_, current) => current + 1);
            var id = Interlocked.Increment(ref _callId).ToString();
            if (Volatile.Read(ref _callId) > 12)
                throw new InvalidOperationException("Harness-driving client exceeded the expected model-call bound.");
            if (stage == 1)
                return ToolCall(nameof(ExecutionIsolationHarness), "expand-" + id);
            if (stage == 2)
                return ToolCall(nameof(ExecutionIsolationHarness.Ping), "ping-" + id);
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]);
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
    "Execution isolation acceptance harness",
    FunctionResult = "expanded",
    Middlewares = [typeof(ExecutionIsolationMiddleware)])]
public sealed partial class ExecutionIsolationHarness
{
    [AIFunction]
    public string Ping() => "pong";
}

public sealed class ExecutionIsolationMiddleware : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, byte> SeenExecutions = new(StringComparer.Ordinal);
    private static int _created;
    private static int _disposed;
    internal static bool ThrowAfterMessageTurn { get; set; }

    public ExecutionIsolationMiddleware() => Interlocked.Increment(ref _created);
    internal static int CreatedCount => Volatile.Read(ref _created);
    internal static int DisposedCount => Volatile.Read(ref _disposed);
    internal static int ExecutionCount => SeenExecutions.Count;
    internal static WeakReference? LastRegistryReference { get; private set; }
    internal static void Reset()
    {
        SeenExecutions.Clear();
        Volatile.Write(ref _created, 0);
        Volatile.Write(ref _disposed, 0);
        ThrowAfterMessageTurn = false;
        LastRegistryReference = null;
    }

    public ValueTask OnHarnessActivatedAsync(ToolHarnessActivationContext context, CancellationToken cancellationToken)
    {
        SeenExecutions.TryAdd(context.InputExecutionId, 0);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnHarnessDeactivatingAsync(ToolHarnessDeactivationContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public Task BeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        LastRegistryReference = new WeakReference(context.Base.ToolHarnessPipelines!);
        return Task.CompletedTask;
    }

    public Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken) =>
        ThrowAfterMessageTurn
            ? Task.FromException(new InvalidOperationException("after-message-turn failed"))
            : Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposed);
        return ValueTask.CompletedTask;
    }
}
