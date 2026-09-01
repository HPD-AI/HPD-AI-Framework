using HPDOS.ToolHarnesses.Middleware;
using HPD.Agent;
using HPD.Agent.Middleware;

namespace HPD.Agent.Harness.Coding.Tests;

public sealed class LanguageServerWorkspaceRegistryTests
{
    [Fact]
    public async Task SameWorkspaceAndConfiguration_SharesAgentOwnedService()
    {
        var created = 0;
        await using var registry = new LanguageServerWorkspaceRegistry(_ =>
        {
            Interlocked.Increment(ref created);
            return new ProbeService();
        });
        var options = new LanguageServerOptions();

        await using var first = registry.Acquire("/workspace", options);
        await using var second = registry.Acquire("/workspace", options);

        Assert.Same(first.Service, second.Service);
        Assert.Equal(1, created);
    }

    [Fact]
    public async Task DifferentWorkspaces_AreIsolatedAndDisposedByRegistry()
    {
        var services = new List<ProbeService>();
        var registry = new LanguageServerWorkspaceRegistry(_ =>
        {
            var service = new ProbeService();
            services.Add(service);
            return service;
        });

        await using var first = registry.Acquire("/workspace/one", new LanguageServerOptions());
        await using var second = registry.Acquire("/workspace/two", new LanguageServerOptions());
        Assert.NotSame(first.Service, second.Service);

        await registry.DisposeAsync();
        Assert.Equal(2, services.Count);
        Assert.All(services, static service => Assert.True(service.Disposed));
    }

    [Fact]
    public async Task AcquireRacingShutdown_CannotPublishOrphanService()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var service = new ProbeService();
        var registry = new LanguageServerWorkspaceRegistry(_ =>
        {
            factoryEntered.Set();
            releaseFactory.Wait();
            return service;
        });

        var acquire = Task.Run(() => registry.Acquire("/workspace", new LanguageServerOptions()));
        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(5)));
        var shutdown = Task.Run(async () => await registry.DisposeAsync());
        await Task.Yield();
        Assert.False(shutdown.IsCompleted);

        releaseFactory.Set();
        await using var lease = await acquire;
        await shutdown;
        Assert.True(service.Disposed);
        Assert.Throws<ObjectDisposedException>(() => registry.Acquire("/workspace", new LanguageServerOptions()));
    }

    [Fact]
    public async Task ReleasingExecutionLeases_DoesNotDisposeAgentOwnedServiceUntilRegistryShutdown()
    {
        var service = new ProbeService();
        var registry = new LanguageServerWorkspaceRegistry(_ => service);
        var first = registry.Acquire("/workspace", new LanguageServerOptions());
        var second = registry.Acquire("/workspace", new LanguageServerOptions());

        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.False(service.Disposed);
        await registry.DisposeAsync();
        Assert.True(service.Disposed);
    }

    [Fact]
    public async Task SeparateAgentRegistries_NeverShareWorkspaceServices()
    {
        await using var firstRegistry = new LanguageServerWorkspaceRegistry(_ => new ProbeService());
        await using var secondRegistry = new LanguageServerWorkspaceRegistry(_ => new ProbeService());

        await using var first = firstRegistry.Acquire("/workspace", new LanguageServerOptions());
        await using var second = secondRegistry.Acquire("/workspace", new LanguageServerOptions());

        Assert.NotSame(first.Service, second.Service);
    }

    [Fact]
    public async Task SameWorkspaceDifferentConfiguration_DoesNotShareServices()
    {
        await using var registry = new LanguageServerWorkspaceRegistry(_ => new ProbeService());
        await using var first = registry.Acquire("/workspace", new LanguageServerOptions());
        await using var second = registry.Acquire("/workspace", new LanguageServerOptions { Enabled = false });

        Assert.NotSame(first.Service, second.Service);
    }

    [Theory]
    [InlineData(ToolHarnessDeactivationReason.Completed)]
    [InlineData(ToolHarnessDeactivationReason.Failed)]
    [InlineData(ToolHarnessDeactivationReason.Cancelled)]
    public async Task CodingMiddleware_ReleasesWorkspaceLeaseForEveryExecutionTerminalReason(
        ToolHarnessDeactivationReason reason)
    {
        var workspaces = new TrackingWorkspaceRegistry();
        var execution = ToolHarnessExecutionScope.Create(null);
        var harness = CodingHarness(workspaces);
        await using (await execution.Registry.AcquireAsync(harness, Context())) { }

        await execution.ReleaseForegroundAsync(reason);
        await execution.Completion;

        Assert.Equal(1, workspaces.AcquireCount);
        Assert.Equal(1, workspaces.ReleaseCount);
    }

    [Fact]
    public async Task CodingMiddleware_PartialActivationFailure_ReleasesWorkspaceLease()
    {
        var workspaces = new TrackingWorkspaceRegistry();
        var registry = new ToolHarnessPipelineRegistry();
        var harness = CodingHarness(
            workspaces,
            new ToolHarnessMiddlewareDescriptor
            {
                MiddlewareType = typeof(ThrowingLifecycleProbe),
                Factory = _ => throw new InvalidOperationException("activation failed")
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AcquireAsync(harness, Context()).AsTask());

        Assert.Equal(1, workspaces.AcquireCount);
        Assert.Equal(1, workspaces.ReleaseCount);
        Assert.Empty(registry.GetDiagnosticsSnapshot().Entries);
    }

    [Fact]
    public async Task CodingMiddleware_HookFailure_StillReleasesWorkspaceLease()
    {
        var workspaces = new TrackingWorkspaceRegistry();
        var registry = new ToolHarnessPipelineRegistry();
        var harness = CodingHarness(
            workspaces,
            new ToolHarnessMiddlewareDescriptor
            {
                MiddlewareType = typeof(ThrowingLifecycleProbe),
                Factory = _ => ToolHarnessMiddlewareActivation.ExecutionOwned(
                    new ThrowingLifecycleProbe())
            });
        await using (await registry.AcquireAsync(harness, Context())) { }

        await Assert.ThrowsAsync<AggregateException>(() =>
            registry.DeactivateAllAsync(ToolHarnessDeactivationReason.Failed).AsTask());

        Assert.Equal(1, workspaces.ReleaseCount);
    }

    [Fact]
    public async Task CodingMiddleware_DetachedOperationRetainsThenReleasesWorkspaceLease()
    {
        var workspaces = new TrackingWorkspaceRegistry();
        var execution = ToolHarnessExecutionScope.Create(null);
        await using (await execution.Registry.AcquireAsync(CodingHarness(workspaces), Context())) { }
        var operation = execution.TransferToOperation("coding-operation");

        await execution.ReleaseForegroundAsync(ToolHarnessDeactivationReason.Completed);
        Assert.Equal(0, workspaces.ReleaseCount);
        await operation.DisposeAsync();
        await execution.Completion;

        Assert.Equal(1, workspaces.ReleaseCount);
    }

    private static ToolHarnessFactory CodingHarness(
        TrackingWorkspaceRegistry workspaces,
        params ToolHarnessMiddlewareDescriptor[] additional) => new(
            "CodingHarness",
            typeof(object),
            static () => new object(),
            static (_, _, _) => [],
            static () => [],
            static () => [],
            "tests:coding",
            Middleware:
            [
                new ToolHarnessMiddlewareDescriptor
                {
                    MiddlewareType = typeof(CodingLanguageServerMiddleware),
                    Factory = _ => ToolHarnessMiddlewareActivation.ExecutionOwned(
                        new CodingLanguageServerMiddleware(
                            workspaces,
                            Path.GetFullPath("."),
                            new LanguageServerOptions()))
                },
                .. additional
            ]);

    private static ToolHarnessActivationContext Context() => new(
        "tests:coding", Guid.NewGuid().ToString("N"), null, new AgentRunConfig());

    private sealed class TrackingWorkspaceRegistry : ILanguageServerWorkspaceRegistry
    {
        private readonly ProbeService _service = new();
        private int _acquired;
        private int _released;
        internal int AcquireCount => Volatile.Read(ref _acquired);
        internal int ReleaseCount => Volatile.Read(ref _released);

        public ILanguageServerWorkspaceLease Acquire(
            string canonicalWorkspaceIdentity,
            LanguageServerOptions options)
        {
            Interlocked.Increment(ref _acquired);
            return new TrackingLease(_service, () => Interlocked.Increment(ref _released));
        }

        public ValueTask DisposeAsync() => _service.DisposeAsync();

        private sealed class TrackingLease(ILanguageServerService service, Action released)
            : ILanguageServerWorkspaceLease
        {
            private int _disposed;
            public ILanguageServerService Service { get; } = service;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) released();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingLifecycleProbe
        : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle
    {
        public ValueTask OnHarnessActivatedAsync(
            ToolHarnessActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnHarnessDeactivatingAsync(
            ToolHarnessDeactivationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("hook failed"));
    }

    private sealed class ProbeService : ILanguageServerService
    {
        internal bool Disposed { get; private set; }
        public ValueTask<IReadOnlyList<LanguageServerStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LanguageServerStatus>>([]);
        public ValueTask<bool> HasServerForFileAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<LanguageServerDocumentResolution> ResolveDocumentAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerDocumentResolution { Path = path, Uri = path });
        public ValueTask<LanguageServerOpenResult> OpenDocumentAsync(LanguageServerDocumentOpenRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerOpenResult { Path = request.Path, Uri = request.Uri, LanguageId = "test" });
        public ValueTask<LanguageServerChangeResult> ChangeDocumentAsync(LanguageServerDocumentChangeRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerChangeResult { Path = request.Path });
        public ValueTask SaveDocumentAsync(LanguageServerDocumentSaveRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseDocumentAsync(LanguageServerDocumentCloseRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask NotifyWatchedFileChangedAsync(LanguageServerWatchedFileChangeRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> GetDiagnosticsAsync(LanguageServerDiagnosticRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LanguageServerDiagnosticSet>>([]);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
