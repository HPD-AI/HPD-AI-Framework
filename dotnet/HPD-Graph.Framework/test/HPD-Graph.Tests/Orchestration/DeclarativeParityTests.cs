using HPD.Events;
using HPD.Events.Core;
using HPD.Graph.Abstractions.Events;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Core.Builders;
using HPD.Graph.Core.Config;
using HPD.Graph.Core.Context;
using HPD.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Orchestration;

public sealed class DeclarativeParityTests
{
    [Theory]
    [InlineData(true, true, 3)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 3)]
    [InlineData(false, false, 3)]
    public async Task Retry_SeparatesClassificationFromBudget(bool onlyTransient, bool transient, int expected)
    {
        var calls = 0;
        var handler = new Handler("primary", (_, _, _) =>
        { calls++; return Task.FromResult<NodeExecutionResult>(Fail(transient)); });
        var graph = new GraphBuilder().WithName("Retry")
            .AddHandlerNode("primary", "Primary", "primary", n => n.WithRetryPolicy(Retry(3, onlyTransient)).WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEdge("START", "primary").AddEdge("primary", "END").Build();
        using var services = Services(handler);
        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        await new GraphOrchestrator<GraphContext>(services).ExecuteAsync(context);
        Assert.Equal(expected, calls);
        Assert.Equal(expected, context.ExecutionLedger.Attempts.Count);
        Assert.Contains("primary", context.InternalFailedNodes.Keys);
    }

    [Fact]
    public void Retry_FilterAndCancellationAreIndependentOfTransience()
    {
        var policy = Retry(3, false) with { RetryableExceptions = [typeof(ArgumentException)] };
        Assert.True(policy.ShouldRetry(new(new ArgumentNullException("x"), ErrorSeverity.Fatal, false, TimeSpan.Zero), 1));
        Assert.False(policy.ShouldRetry(Fail(false), 1));
        Assert.False(policy.ShouldRetry(new(new OperationCanceledException(), ErrorSeverity.Transient, true, TimeSpan.Zero), 1));
        Assert.False(policy.ShouldRetry(new(new ArgumentException(), ErrorSeverity.Fatal, false, TimeSpan.Zero), 3));
    }

    [Fact]
    public async Task Recovery_ResolvesPrimaryOnceAndPreservesAttemptsAndLifecycle()
    {
        var primaryCalls = 0; var fallbackCalls = 0; var collectorCalls = 0;
        var primary = new Handler("primary", (_, _, _) => { primaryCalls++; return Task.FromResult<NodeExecutionResult>(Fail(false)); });
        var fallback = new Handler("fallback", (context, _, _) =>
        {
            fallbackCalls++;
            Assert.Equal("fallback", context.CurrentNodeId);
            Assert.Equal("primary", context.Invocation!.LogicalNodeId);
            return Task.FromResult<NodeExecutionResult>(Success("recovered"));
        });
        var collector = new Handler("collector", (_, inputs, _) =>
        {
            collectorCalls++;
            Assert.Equal("recovered", inputs.Get<string>("primary.answer"));
            Assert.NotNull(Assert.IsType<NodeExecutionResult.Success>(inputs.SourceResults["primary"]).Recovery);
            return Task.FromResult<NodeExecutionResult>(Success("collected"));
        });
        using var services = Services(primary, fallback, collector);
        var graph = RecoveryGraph(PropagationMode.Isolate, primaryRetry: Retry(2, false));
        using var coordinator = new EventCoordinator();
        var events = new System.Collections.Concurrent.ConcurrentQueue<Event>();
        using var subscription = coordinator.SubscribeAny(e => { events.Enqueue(e); return ValueTask.CompletedTask; });
        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services) { EventCoordinator = coordinator };
        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await orchestrator.ExecuteAsync(context);
        Assert.Equal(2, primaryCalls); Assert.Equal(1, fallbackCalls); Assert.Equal(1, collectorCalls);
        Assert.False(context.IsNodeComplete("fallback"));
        Assert.Empty(context.InternalFailedNodes);
        Assert.Equal(2, context.ExecutionLedger.Attempts.Values.Count(a => a.NodeId == "primary" && a.Result is NodeExecutionResult.Failure));
        Assert.Equal(new[] { "started", "recovered" }, events.OfType<NodeRecoveryEvent>().Select(e => e.State));
        Assert.Single(events.OfType<NodeExecutionCompletedEvent>().Where(e => e.NodeId == "primary"));
        await orchestrator.ExecuteAsync(context);
        Assert.Equal(1, fallbackCalls);
    }

    [Theory]
    [InlineData(PropagationMode.Isolate, true)]
    [InlineData(PropagationMode.SkipDependents, false)]
    public async Task RecoveryExhaustion_UsesIndependentBudgetAndExplicitPolicy(PropagationMode exhaustion, bool collected)
    {
        var calls = 0; var collectorCalls = 0;
        using var services = Services(
            new Handler("primary", (_, _, _) => Task.FromResult<NodeExecutionResult>(Fail(false))),
            new Handler("fallback", (_, _, _) => { calls++; return Task.FromResult<NodeExecutionResult>(SuccessWithMissingOutput()); }),
            new Handler("collector", (_, inputs, _) =>
            {
                collectorCalls++;
                Assert.IsType<NodeExecutionResult.Failure>(inputs.SourceResults["primary"]);
                return Task.FromResult<NodeExecutionResult>(Success("partial"));
            }));
        var context = new GraphContext(Guid.NewGuid().ToString(), RecoveryGraph(exhaustion, fallbackRetry: Retry(2, false)), services);
        await new GraphOrchestrator<GraphContext>(services).ExecuteAsync(context);
        Assert.Equal(2, calls);
        Assert.Equal(collected ? 1 : 0, collectorCalls);
        Assert.False(context.Channels.Contains("node_output:primary:port:0"));
        Assert.Equal("exhausted", Assert.Single(context.ExecutionLedger.Recoveries.Values).State);
        Assert.Contains("primary", context.InternalFailedNodes.Keys);
    }

    [Fact]
    public async Task RecoveryCancellation_DoesNotRetryOrRestart()
    {
        var calls = 0;
        using var services = Services(
            new Handler("primary", (_, _, _) => Task.FromResult<NodeExecutionResult>(Fail(false))),
            new Handler("fallback", (_, _, _) => { calls++; return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Cancelled(CancellationReason.UserRequested)); }),
            new Handler("collector", (_, _, _) => throw new InvalidOperationException("Collector must not run.")));
        var context = new GraphContext(Guid.NewGuid().ToString(), RecoveryGraph(PropagationMode.Isolate, fallbackRetry: Retry(3, false)), services);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GraphOrchestrator<GraphContext>(services).ExecuteAsync(context));
        Assert.Equal(1, calls);
        Assert.Equal("cancelled", Assert.Single(context.ExecutionLedger.Recoveries.Values).State);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("ordinary")]
    [InlineData("incompatible")]
    [InlineData("noExhaustion")]
    [InlineData("noContract")]
    public void InvalidRecovery_FailsConstruction(string invalid)
    {
        var builder = new GraphBuilder().WithName("Invalid")
            .AddHandlerNode("primary", "Primary", "primary", n =>
            {
                if (invalid != "noContract") n.WithOutputContract(Contract());
                n.WithErrorPolicy(new ErrorPropagationPolicy
                {
                    Mode = PropagationMode.ExecuteFallback,
                    FallbackNodeId = invalid == "missing" ? "absent" : invalid == "self" ? "primary" : "fallback",
                    ExhaustionMode = invalid == "noExhaustion" ? null : PropagationMode.Isolate
                });
            })
            .AddHandlerNode("fallback", "Fallback", "fallback", n => n.AsRecoveryOnly().WithOutputContract(Contract(invalid == "incompatible" ? typeof(int) : typeof(string))))
            .AddEdge("START", "primary").AddEdge("primary", "END");
        if (invalid == "ordinary") builder.AddEdge("START", "fallback");
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void GraphExport_ReloadsOutputAndRetryContracts()
    {
        var graph = RecoveryGraph(PropagationMode.Isolate, primaryRetry: Retry(3, false) with { RetryableExceptions = [typeof(TimeoutException)] });
        var config = new GraphConfigExporter().Export(graph);
        var reloaded = new GraphConfigCompiler().Compile(config);
        Assert.False(reloaded.GetNode("primary")!.RetryPolicy!.OnlyTransient);
        Assert.Equal(typeof(TimeoutException), Assert.Single(reloaded.GetNode("primary")!.RetryPolicy!.RetryableExceptions!));
        Assert.Equal(PropagationMode.Isolate, reloaded.GetNode("primary")!.ErrorPolicy!.ExhaustionMode);
        Assert.True(reloaded.GetNode("fallback")!.RecoveryOnly);
    }

    [Fact]
    public async Task WorkflowDeadline_CancelsRetryDelay()
    {
        var calls = 0;
        using var services = Services(new Handler("primary", (_, _, _) => { calls++; return Task.FromResult<NodeExecutionResult>(Fail(true)); }));
        var graph = new GraphBuilder().WithName("Deadline").WithExecutionTimeout(TimeSpan.FromMilliseconds(200))
            .AddHandlerNode("primary", "Primary", "primary", n => n.WithRetryPolicy(Retry(3, true) with { InitialDelay = TimeSpan.FromSeconds(10) }))
            .AddEdge("START", "primary").AddEdge("primary", "END").Build();
        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GraphOrchestrator<GraphContext>(services).ExecuteAsync(context));
        Assert.Equal(1, calls);
    }

    private static HPD.Graph.Abstractions.Graph.Graph RecoveryGraph(PropagationMode exhaustion, RetryPolicy? primaryRetry = null, RetryPolicy? fallbackRetry = null)
    {
        return new GraphBuilder().WithName("Recovery")
            .AddHandlerNode("primary", "Primary", "primary", n =>
            {
                n.WithOutputContract(Contract()).WithErrorPolicy(ErrorPropagationPolicy.ExecuteFallback("fallback", exhaustion));
                if (primaryRetry is not null) n.WithRetryPolicy(primaryRetry);
            })
            .AddHandlerNode("fallback", "Fallback", "fallback", n =>
            { n.AsRecoveryOnly().WithOutputContract(Contract()); if (fallbackRetry is not null) n.WithRetryPolicy(fallbackRetry); })
            .AddHandlerNode("collector", "Collector", "collector")
            .AddEdge("START", "primary").AddEdge("primary", "collector").AddEdge("collector", "END").Build();
    }
    private static NodeOutputContract Contract(Type? type = null) => new()
    {
        Values = [new OutputValueContract { Key = "answer", TypeName = (type ?? typeof(string)).AssemblyQualifiedName!, RuntimeType = type ?? typeof(string) }]
    };
    private static RetryPolicy Retry(int attempts, bool transient) => new() { MaxAttempts = attempts, InitialDelay = TimeSpan.Zero, OnlyTransient = transient };
    private static NodeExecutionResult.Failure Fail(bool transient) => new(new InvalidOperationException("malformed"), ErrorSeverity.Fatal, transient, TimeSpan.Zero);
    private static NodeExecutionResult.Success Success(string text) => NodeExecutionResult.Success.Single(new() { ["answer"] = text }, TimeSpan.Zero, new());
    private static NodeExecutionResult.Success SuccessWithMissingOutput() => NodeExecutionResult.Success.Single(new(), TimeSpan.Zero, new());
    private static ServiceProvider Services(params Handler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers) services.AddSingleton<IGraphNodeHandler<GraphContext>>(handler);
        return services.BuildServiceProvider();
    }
    private sealed class Handler(string name, Func<GraphContext, HandlerInputs, CancellationToken, Task<NodeExecutionResult>> run) : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => name;
        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default) => run(context, inputs, cancellationToken);
    }
}
