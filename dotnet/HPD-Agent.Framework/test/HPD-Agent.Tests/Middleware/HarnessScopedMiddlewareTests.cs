using HPD.Agent.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.AspNetCore.Builder;

namespace HPD.Agent.Tests.Middleware;

public sealed class HarnessScopedMiddlewareTests
{
    [Fact]
    public async Task Registry_ActivatesOnceAndDrainsInvocationLeases()
    {
        var created = 0;
        var registry = new ToolHarnessPipelineRegistry();
        var harness = Harness(_ =>
        {
            Interlocked.Increment(ref created);
            return ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe());
        });
        var leases = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(async _ => await registry.AcquireAsync(harness, Context())));
        Assert.Equal(1, created);
        Assert.Equal(16, registry.GetDiagnosticsSnapshot().Entries.Single().AdmissionCount);

        var teardown = registry.DeactivateAllAsync(ToolHarnessDeactivationReason.Completed).AsTask();
        await Task.Yield();
        Assert.False(teardown.IsCompleted);
        foreach (var lease in leases) await lease.DisposeAsync();
        await teardown;
    }

    [Fact]
    public async Task Cleanup_IsReverseOrderedAndAllAttempted()
    {
        var calls = new List<string>();
        var registry = new ToolHarnessPipelineRegistry();
        var harness = Harness(
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe("first", calls, true)),
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe("second", calls, true)));
        await using (await registry.AcquireAsync(harness, Context())) { }

        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await registry.DeactivateAllAsync(ToolHarnessDeactivationReason.Completed));

        Assert.Equal(2, error.Flatten().InnerExceptions.Count);
        Assert.Equal(["deactivate:second", "deactivate:first", "dispose:second", "dispose:first"], calls);
    }

    [Fact]
    public async Task FailedActivation_UnwindsConstructedInstancesWithoutPublishingPipeline()
    {
        var disposed = false;
        var registry = new ToolHarnessPipelineRegistry();
        var harness = Harness(
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe(onDispose: () => disposed = true)),
            _ => throw new InvalidOperationException("activation failed"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await registry.AcquireAsync(harness, Context()));
        Assert.Equal("activation failed", error.Message);
        Assert.True(disposed);
        Assert.Empty(registry.GetDiagnosticsSnapshot().Entries);
    }

    [Fact]
    public async Task TransferredOwner_DelaysExecutionCleanup()
    {
        var disposed = false;
        var scope = ToolHarnessExecutionScope.Create(null);
        await using (await scope.Registry.AcquireAsync(
            Harness(_ => ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe(onDispose: () => disposed = true))),
            Context())) { }
        var operation = scope.TransferToOperation("operation-1");

        await scope.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        Assert.False(scope.Completion.IsCompleted);
        await operation.DisposeAsync();
        await scope.Completion;
        Assert.True(disposed);
    }

    [Fact]
    public async Task ServicesOwnedMiddleware_UsesExecutionChildScopeAndScopeOwnsDisposal()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedProbe>();
        await using var provider = services.BuildServiceProvider();
        var scope = ToolHarnessExecutionScope.Create(provider);
        ScopedProbe? resolved = null;
        var harness = HarnessOf<ScopedProbe>(context =>
        {
            resolved = context.GetRequiredService<ScopedProbe>();
            return ToolHarnessMiddlewareActivation.ServicesOwned(resolved);
        });

        await using (await scope.Registry.AcquireAsync(harness, Context(scope.Services))) { }
        Assert.NotNull(resolved);
        Assert.False(resolved.Disposed);

        await scope.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await scope.Completion;
        Assert.True(resolved.Disposed);
    }

    [Fact]
    public void PersistentContainerState_ContainsNoRuntimeOwnershipObjects()
    {
        var forbidden = new[]
        {
            typeof(IAgentMiddleware), typeof(AgentMiddlewarePipeline), typeof(IServiceProvider),
            typeof(Task), typeof(CancellationTokenSource), typeof(IDisposable), typeof(IAsyncDisposable)
        };

        Assert.DoesNotContain(typeof(ContainerMiddlewareState).GetProperties(), property =>
            forbidden.Any(type => type.IsAssignableFrom(property.PropertyType)));
    }

    [Fact]
    public async Task ActivationRacingRegistryClosure_CannotPublishOrphanPipeline()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var registry = new ToolHarnessPipelineRegistry();
        var harness = HarnessOf<BlockingActivationProbe>(_ => ToolHarnessMiddlewareActivation.ExecutionOwned(
            new BlockingActivationProbe(entered, release, () => disposed = true)));

        var acquire = registry.AcquireAsync(harness, Context()).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = registry.DeactivateAllAsync(ToolHarnessDeactivationReason.Shutdown).AsTask();
        release.TrySetResult();

        try
        {
            await using var lease = await acquire;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // Closure won admission after the already-started activation; this is also valid.
        }
        await close;

        Assert.True(disposed);
        Assert.Empty(registry.GetDiagnosticsSnapshot().Entries);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            registry.AcquireAsync(harness, Context()).AsTask());
    }

    [Fact]
    public async Task SeparateExecutions_ReceiveDifferentMiddlewareInstances()
    {
        var instances = new List<Probe>();
        var harness = Harness(_ =>
        {
            var instance = new Probe();
            lock (instances) instances.Add(instance);
            return ToolHarnessMiddlewareActivation.ExecutionOwned(instance);
        });
        var first = ToolHarnessExecutionScope.Create(null);
        var second = ToolHarnessExecutionScope.Create(null);

        await using (await first.Registry.AcquireAsync(harness, Context())) { }
        await using (await second.Registry.AcquireAsync(harness, Context())) { }

        Assert.Equal(2, instances.Count);
        Assert.NotSame(instances[0], instances[1]);
        await first.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await second.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
    }

    [Fact]
    public async Task ServicesOwnedActivationWithoutChildScope_FailsClosed()
    {
        var registry = new ToolHarnessPipelineRegistry();
        var harness = Harness(_ => ToolHarnessMiddlewareActivation.ServicesOwned(new Probe()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AcquireAsync(harness, Context()).AsTask());

        Assert.Contains("must be the exact instance resolved", error.Message);
        Assert.Empty(registry.GetDiagnosticsSnapshot().Entries);
    }

    [Fact]
    public async Task Diagnostics_ExposeOnlyIdentityOrdinalStateAndAdmission()
    {
        var registry = new ToolHarnessPipelineRegistry();
        await using var lease = await registry.AcquireAsync(Harness(_ =>
            ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe())), Context());

        var entry = Assert.Single(registry.GetDiagnosticsSnapshot().Entries);
        Assert.Equal("tests:Harness", entry.HarnessIdentity);
        Assert.True(entry.ActivationOrdinal > 0);
        Assert.Equal(ToolHarnessPipelineLifecycleState.Active, entry.State);
        Assert.Equal(1, entry.AdmissionCount);
        Assert.DoesNotContain(entry.GetType().GetProperties(), property =>
            typeof(IAgentMiddleware).IsAssignableFrom(property.PropertyType) ||
            typeof(AgentMiddlewarePipeline).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public async Task Diagnostics_ReflectActivatingDeactivatingAndAdmissionTransitions()
    {
        var activationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishActivation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolHarnessPipelineRegistry();
        var harness = HarnessOf<BlockingActivationProbe>(_ => ToolHarnessMiddlewareActivation.ExecutionOwned(
            new BlockingActivationProbe(activationEntered, finishActivation, static () => { })));

        var acquiring = registry.AcquireAsync(harness, Context()).AsTask();
        await activationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var activating = Assert.Single(registry.GetDiagnosticsSnapshot().Entries);
        Assert.Equal(ToolHarnessPipelineLifecycleState.Activating, activating.State);
        Assert.Equal(0, activating.AdmissionCount);

        finishActivation.TrySetResult();
        var lease = await acquiring;
        var admitted = Assert.Single(registry.GetDiagnosticsSnapshot().Entries);
        Assert.Equal(ToolHarnessPipelineLifecycleState.Active, admitted.State);
        Assert.Equal(1, admitted.AdmissionCount);

        var deactivation = registry.DeactivateAllAsync(ToolHarnessDeactivationReason.Completed).AsTask();
        await WaitUntilAsync(() => registry.GetDiagnosticsSnapshot().Entries.SingleOrDefault()?.State ==
            ToolHarnessPipelineLifecycleState.Deactivating);
        var deactivating = Assert.Single(registry.GetDiagnosticsSnapshot().Entries);
        Assert.Equal(1, deactivating.AdmissionCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            registry.AcquireAsync(harness, Context()).AsTask());

        await lease.DisposeAsync();
        await deactivation;
        Assert.Empty(registry.GetDiagnosticsSnapshot().Entries);
    }

    [Fact]
    public async Task ScopedDispatch_UsesDeclarationOrderBefore_AndReverseOrderAfterAndError()
    {
        var calls = new List<string>();
        var registry = new ToolHarnessPipelineRegistry();
        var harness = HarnessOf<OrderedHookProbe>(
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new OrderedHookProbe("first", calls)),
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new OrderedHookProbe("second", calls)),
            _ => ToolHarnessMiddlewareActivation.ExecutionOwned(new OrderedHookProbe("third", calls)));
        await using var lease = await registry.AcquireAsync(harness, Context());

        await lease.Pipeline.DispatchBeforeFunctionAsync(
            MiddlewareTestHelpers.CreateBeforeFunctionContext(), CancellationToken.None);
        await lease.Pipeline.DispatchAfterFunctionAsync(
            MiddlewareTestHelpers.CreateAfterFunctionContext(), CancellationToken.None);
        await lease.Pipeline.DispatchOnErrorAsync(
            MiddlewareTestHelpers.CreateErrorContext(), CancellationToken.None);

        Assert.Equal([
            "before:first", "before:second", "before:third",
            "after:third", "after:second", "after:first",
            "error:third", "error:second", "error:first"
        ], calls);
    }

    [Fact]
    public async Task RawNestedDispatch_DoesNotClearOuterMiddlewareStateGuard()
    {
        var registry = new ToolHarnessPipelineRegistry();
        await using var lease = await registry.AcquireAsync(
            Harness(_ => ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe())), Context());
        var context = MiddlewareTestHelpers.CreateBeforeFunctionContext();
        context.Base.SetMiddlewareExecuting(true);

        await lease.Pipeline.DispatchBeforeFunctionAsync(context, CancellationToken.None);

        var error = Assert.Throws<InvalidOperationException>(() => context.Base.SyncState(context.State));
        Assert.Contains("during middleware execution", error.Message);
        context.Base.SetMiddlewareExecuting(false);
    }

    [Theory]
    [InlineData(ToolHarnessDeactivationReason.Completed)]
    [InlineData(ToolHarnessDeactivationReason.Failed)]
    [InlineData(ToolHarnessDeactivationReason.Cancelled)]
    public async Task EveryTerminalReason_DisposesExecutionOwnedMiddlewareExactlyOnce(
        ToolHarnessDeactivationReason reason)
    {
        var disposeCount = 0;
        var scope = ToolHarnessExecutionScope.Create(null);
        await using (await scope.Registry.AcquireAsync(Harness(_ =>
            ToolHarnessMiddlewareActivation.ExecutionOwned(
                new Probe(onDispose: () => Interlocked.Increment(ref disposeCount)))), Context())) { }

        await scope.ReleaseForegroundAsync(reason);
        await scope.Completion;

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task ServicesOwnedMiddleware_RemainsAliveDuringDeactivation_ThenScopeDisposesIt()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeLifetimeProbe>();
        await using var provider = services.BuildServiceProvider();
        var execution = ToolHarnessExecutionScope.Create(provider);
        ScopeLifetimeProbe? probe = null;
        var harness = HarnessOf<ScopeLifetimeProbe>(context =>
        {
            probe = context.GetRequiredService<ScopeLifetimeProbe>();
            return ToolHarnessMiddlewareActivation.ServicesOwned(probe);
        });

        await using (await execution.Registry.AcquireAsync(harness, Context(execution.Services))) { }
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await execution.Completion;

        Assert.NotNull(probe);
        Assert.True(probe.WasAliveDuringDeactivation);
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public async Task ServicesOwnedMiddleware_IsResolvedFromInputScope_NotRootProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedProbe>();
        await using var root = services.BuildServiceProvider();
        var rootInstance = root.GetRequiredService<ScopedProbe>();
        var execution = ToolHarnessExecutionScope.Create(root);
        ScopedProbe? activated = null;
        var harness = HarnessOf<ScopedProbe>(context =>
        {
            activated = context.GetRequiredService<ScopedProbe>();
            return ToolHarnessMiddlewareActivation.ServicesOwned(activated);
        });

        await using (await execution.Registry.AcquireAsync(harness, Context(execution.Services))) { }

        Assert.NotNull(activated);
        Assert.NotSame(rootInstance, activated);
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await execution.Completion;
        Assert.True(activated.Disposed);
        Assert.False(rootInstance.Disposed);
    }

    [Fact]
    public async Task AspNetCoreHost_ResolvesServicesOwnedMiddlewareOnlyFromRequestLikeChildScope()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Services.AddScoped<ScopedProbe>();
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() =>
            app.Services.GetRequiredService<ScopedProbe>());
        var execution = ToolHarnessExecutionScope.Create(app.Services);
        ScopedProbe? activated = null;
        var harness = HarnessOf<ScopedProbe>(context =>
        {
            activated = context.GetRequiredService<ScopedProbe>();
            return ToolHarnessMiddlewareActivation.ServicesOwned(activated);
        });

        await using (await execution.Registry.AcquireAsync(
            harness, Context(execution.Services))) { }

        Assert.NotNull(activated);
        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        await execution.Completion;
        Assert.True(activated.Disposed);
    }

    [Fact]
    public async Task ForegroundRelease_ReturnsBeforeTransferredOwnerAndCompletionWaitsForIt()
    {
        var scope = ToolHarnessExecutionScope.Create(null);
        var owner = scope.TransferToOperation("operation-tail");

        var release = scope.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);

        Assert.True(release.IsCompletedSuccessfully);
        Assert.False(scope.Completion.IsCompleted);
        await owner.DisposeAsync();
        await scope.Completion;
    }

    [Fact]
    public async Task CleanupFailure_IsObservedAndFaultsCompletionAfterAllCleanup()
    {
        Exception? observed = null;
        var scope = ToolHarnessExecutionScope.Create(null, exception => observed = exception);
        await using (await scope.Registry.AcquireAsync(Harness(_ =>
            ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe(failDispose: true))), Context())) { }

        await Assert.ThrowsAsync<AggregateException>(() =>
            scope.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Failed).AsTask());
        var completionError = await Assert.ThrowsAsync<AggregateException>(() => scope.Completion);

        Assert.Same(completionError, observed);
    }

    [Fact]
    public async Task ContainerState_SerializesWhilePipelineIsActive_AndHydrationKeepsRuntimeSeparate()
    {
        var registry = new ToolHarnessPipelineRegistry();
        var constructed = 0;
        var harness = Harness(_ =>
        {
            Interlocked.Increment(ref constructed);
            return ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe());
        });
        await using var lease = await registry.AcquireAsync(harness, Context());
        var state = new ContainerMiddlewareState().WithExpandedContainer("Harness");

        var json = JsonSerializer.Serialize(state, SessionJsonContext.Default.ContainerMiddlewareState);
        var hydrated = JsonSerializer.Deserialize(json, SessionJsonContext.Default.ContainerMiddlewareState);

        Assert.Contains("Harness", hydrated!.ExpandedContainers);
        Assert.Equal(1, constructed);
        var freshRegistry = new ToolHarnessPipelineRegistry();
        Assert.Empty(freshRegistry.GetDiagnosticsSnapshot().Entries);
        await using (await freshRegistry.AcquireAsync(harness, Context())) { }
        Assert.Equal(2, constructed);
    }

    [Fact]
    public async Task FileCheckpoint_RoundTripsActiveContainerState_AndHydratesFreshRuntimePipeline()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-v9-checkpoint-{Guid.NewGuid():N}");
        var constructed = 0;
        var harness = Harness(_ =>
        {
            Interlocked.Increment(ref constructed);
            return ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe());
        });
        var registry = new ToolHarnessPipelineRegistry();
        var factories = new Dictionary<string, MiddlewareStateFactory>
        {
            ["HPD.Agent.ContainerMiddlewareState"] = new(
                "HPD.Agent.ContainerMiddlewareState",
                typeof(ContainerMiddlewareState),
                "ContainerMiddleware",
                1,
                true,
                StateScope.Thread,
                json => JsonSerializer.Deserialize(
                    json, SessionJsonContext.Default.ContainerMiddlewareState),
                value => JsonSerializer.Serialize(
                    (ContainerMiddlewareState)value,
                    SessionJsonContext.Default.ContainerMiddlewareState))
        };

        try
        {
            await using var activeLease = await registry.AcquireAsync(harness, Context());
            var state = new MiddlewareState().SetState(
                "HPD.Agent.ContainerMiddlewareState",
                new ContainerMiddlewareState().WithExpandedContainer("Harness"));
            var session = new global::HPD.Agent.Session("checkpoint-session");
            var thread = session.CreateThread("test-agent", "main");
            state.SaveToThread(thread, factories);

            var store = new FileSessionStore(
                directory, HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, thread);

            var reopened = new FileSessionStore(
                directory, HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
            var projected = await reopened.ProjectThreadAsync(
                session.Id, thread.Id, ThreadProjectionPurpose.CompleteSemanticExport);
            var hydrated = MiddlewareState.LoadFromThread(projected, factories)
                .GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");

            Assert.NotNull(hydrated);
            Assert.Contains("Harness", hydrated.ExpandedContainers);
            Assert.Equal(1, constructed);

            var freshRegistry = new ToolHarnessPipelineRegistry();
            Assert.Empty(freshRegistry.GetDiagnosticsSnapshot().Entries);
            await using (await freshRegistry.AcquireAsync(harness, Context())) { }
            Assert.Equal(2, constructed);
            await freshRegistry.DeactivateAllAsync(ToolHarnessDeactivationReason.Completed);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ContainerStateTransforms_DoNotConstructMiddleware()
    {
        var constructed = 0;
        _ = Harness(_ =>
        {
            Interlocked.Increment(ref constructed);
            return ToolHarnessMiddlewareActivation.ExecutionOwned(new Probe());
        });

        var transformed = new ContainerMiddlewareState().WithExpandedContainer("Harness");

        Assert.Contains("Harness", transformed.ExpandedContainers);
        Assert.Equal(0, constructed);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }

    private static ToolHarnessFactory Harness(params ToolHarnessMiddlewareFactory[] factories) => new(
        "Harness", typeof(object), static () => new object(), static (_, _, _) => [],
        static () => [], static () => [], StableIdentity: "tests:Harness",
        Middleware: factories.Select(factory => new ToolHarnessMiddlewareDescriptor
        {
            MiddlewareType = typeof(Probe), Factory = factory
        }).ToArray());

    private static ToolHarnessFactory HarnessOf<TMiddleware>(params ToolHarnessMiddlewareFactory[] factories)
        where TMiddleware : IToolHarnessMiddleware => new(
        "Harness", typeof(object), static () => new object(), static (_, _, _) => [],
        static () => [], static () => [], StableIdentity: "tests:Harness",
        Middleware: factories.Select(factory => new ToolHarnessMiddlewareDescriptor
        {
            MiddlewareType = typeof(TMiddleware), Factory = factory
        }).ToArray());

    private static ToolHarnessActivationContext Context(IServiceProvider? services = null) => new(
        "tests:Harness", Guid.NewGuid().ToString("N"), services, new AgentRunConfig());

    private sealed class Probe(string name = "probe", List<string>? calls = null, bool failDispose = false, Action? onDispose = null)
        : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
    {
        public ValueTask OnHarnessActivatedAsync(ToolHarnessActivationContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask OnHarnessDeactivatingAsync(ToolHarnessDeactivationContext context, CancellationToken cancellationToken)
        { calls?.Add($"deactivate:{name}"); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            calls?.Add($"dispose:{name}"); onDispose?.Invoke();
            return failDispose ? ValueTask.FromException(new InvalidOperationException(name)) : ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedProbe : IToolHarnessMiddleware, IAsyncDisposable
    {
        internal bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ScopeLifetimeProbe : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
    {
        internal bool WasAliveDuringDeactivation { get; private set; }
        internal int DisposeCount { get; private set; }
        public ValueTask OnHarnessActivatedAsync(ToolHarnessActivationContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask OnHarnessDeactivatingAsync(ToolHarnessDeactivationContext context, CancellationToken cancellationToken)
        {
            WasAliveDuringDeactivation = DisposeCount == 0;
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingActivationProbe(
        TaskCompletionSource entered,
        TaskCompletionSource release,
        Action onDispose) : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
    {
        public async ValueTask OnHarnessActivatedAsync(ToolHarnessActivationContext context, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask OnHarnessDeactivatingAsync(ToolHarnessDeactivationContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() { onDispose(); return ValueTask.CompletedTask; }
    }

    private sealed class OrderedHookProbe(string name, List<string> calls) : IToolHarnessMiddleware
    {
        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
        {
            calls.Add($"before:{name}");
            return Task.CompletedTask;
        }

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
        {
            calls.Add($"after:{name}");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(ErrorContext context, CancellationToken cancellationToken)
        {
            calls.Add($"error:{name}");
            return Task.CompletedTask;
        }
    }
}
