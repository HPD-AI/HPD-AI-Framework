namespace HPD.Agent.Sandbox;

using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Provider-specific inputs required by the backend-neutral sandbox composer.
/// </summary>
public sealed record EnvironmentSandboxBackendDefinition
{
    /// <summary>Placement mechanism represented by the Environment provider.</summary>
    public required SandboxBackendKind Kind { get; init; }

    /// <summary>Concrete Environment provider module.</summary>
    public required IProviderModule Module { get; init; }

    /// <summary>Component that enforces the sandbox policy.</summary>
    public required SandboxEnforcementLocation EnforcementLocation { get; init; }

    /// <summary>Constructs a provider-specific runtime-host request.</summary>
    public required Func<SandboxSessionRequest, RuntimeHostSpec>
        HostSpecFactory { get; init; }

    /// <summary>Constructs an execution-unit request after the host exists.</summary>
    public Func<SandboxSessionRequest, ResourceRef<RuntimeHost>,
        ExecutionUnitSpec> UnitSpecFactory { get; init; } =
        static (_, host) => new ExecutionUnitSpec
        {
            PreferredHost = host,
        };

    /// <summary>
    /// Builds the activation request for provider activators. Backends with
    /// activators must provide this factory; in-process backends may omit it.
    /// </summary>
    public Func<IProviderActivator, ProviderActivationSpec>?
        ActivationSpecFactory { get; init; }
}

/// <summary>
/// Composes one Environment-backed sandbox session. The same lifecycle is used
/// for Host Process, Host Containers, and Virtual Machine providers.
/// </summary>
public sealed class EnvironmentSandboxBackendFactory
    : ISandboxBackendFactory, IAsyncDisposable
{
    private readonly EnvironmentSandboxBackendDefinition _definition;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<(
        IProviderActivator Activator,
        TargetHandle<ProviderActivation> Handle)> _activations = [];
    private EnvironmentProviderRegistry? _registry;
    private InMemoryEnvironmentRuntime? _runtime;
    private bool _disposed;

    public EnvironmentSandboxBackendFactory(
        EnvironmentSandboxBackendDefinition definition)
    {
        _definition = definition ??
            throw new ArgumentNullException(nameof(definition));
    }

    /// <inheritdoc />
    public SandboxBackendKind Kind => _definition.Kind;

    /// <summary>Provider selected by this factory.</summary>
    public ProviderId ProviderId => _definition.Module.Descriptor.Id;

    /// <summary>Registered Environment provider surface.</summary>
    public EnvironmentProviderRegistry? Registry => _registry;

    /// <summary>Environment runtime used to create session resources.</summary>
    public IEnvironmentRuntime? Runtime => _runtime;

    /// <summary>
    /// Registers and activates the selected Environment provider without
    /// creating a runtime host or execution unit.
    /// </summary>
    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<SandboxBackendCapabilities>
        GetCapabilitiesAsync(
            SandboxCapabilitiesQuery query,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        ProviderCapabilityReport report = await _registry!
            .GetCapabilitiesAsync(
                ProviderId,
                new ProviderCapabilityQuery(
                    HostPlatform: null,
                    GuestPlatform: null,
                    Scope: null),
                cancellationToken)
            .ConfigureAwait(false);
        return new SandboxBackendCapabilities(
            report,
            report.Capabilities
                .Where(fact =>
                    fact.Id.Value.StartsWith(
                        StandardSandboxEnvironmentCapabilities.Prefix,
                        StringComparison.Ordinal) ||
                    fact.Id.Value.StartsWith(
                        StandardWorkspaceEnvironmentCapabilities.Prefix,
                        StringComparison.Ordinal))
                .Select(fact => new SandboxCapabilityFact(
                    fact,
                    _definition.EnforcementLocation))
                .ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<ISandboxSession> CreateSessionAsync(
        SandboxSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorkspaceRequests(request.Workspaces);

        SandboxCapabilitiesQuery capabilityQuery = new()
        {
            Isolation = request.Isolation,
            Process = request.Process,
            Workspaces = request.Workspaces,
            Resources = request.Resources,
            PreferredProvider = request.PreferredProvider,
        };
        SandboxBackendCapabilities capabilities =
            await GetCapabilitiesAsync(
                capabilityQuery,
                cancellationToken).ConfigureAwait(false);
        SandboxCapabilityAdmission.EnsureAdmitted(request, capabilities);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        RuntimeHostSpec hostSpec =
            _definition.HostSpecFactory(request);
        ensurePreferredProvider(hostSpec);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host =
            await _runtime!.EnsureHostAsync(
                hostSpec,
                cancellationToken).ConfigureAwait(false);
        EnsureHostReady(host);
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        ResourceRef<ExecutionUnit>? unitRef = null;
        var projections = new List<SandboxWorkspaceProjection>();
        var attachments = new List<WorkspaceFileStoreAttachment>();
        try
        {
            ExecutionUnitSpec unitSpec =
                _definition.UnitSpecFactory(request, hostRef);
            ResourceSnapshot<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus> unit =
                await _runtime!.EnsureExecutionUnitAsync(
                    unitSpec with { PreferredHost = hostRef },
                    cancellationToken).ConfigureAwait(false);
            EnsureUnitReady(unit);
            unitRef = new ResourceRef<ExecutionUnit>(
                unit.Metadata.Id,
                unit.Metadata.Scope,
                unit.Metadata.Generation);

            IProcessProvider processProvider =
                _registry!.ProcessProviders.Single(provider =>
                    provider.ProviderId.Equals(ProviderId));
            RuntimeWorkspaceBinding workspace =
                await CreateWorkspaceBindingAsync(
                        request.Workspaces,
                        hostRef,
                        unitRef.Value,
                        unit.Status.Handle!.Value,
                        projections,
                        attachments,
                        cancellationToken)
                    .ConfigureAwait(false);
            RuntimeProcessExecutionBinding execution = new()
            {
                EnvironmentId = ProviderId.Value,
                EnvironmentRevision =
                    host.Metadata.Generation.Value,
                ProcessProvider = processProvider,
                EnvironmentRuntime = _runtime,
                ExecutionTarget = unit.Status.Handle!.Value,
                WorkspaceContent = workspace.Content,
                Isolation = request.Isolation,
            };
            return new EnvironmentSandboxSession(
                _definition.Kind,
                _runtime,
                unitRef.Value,
                execution,
                workspace,
                capabilities,
                projections,
                attachments);
        }
        catch
        {
            await CleanupFailedSessionAsync(
                    unitRef,
                    projections,
                    attachments)
                .ConfigureAwait(false);
            throw;
        }

        void ensurePreferredProvider(RuntimeHostSpec spec)
        {
            if (spec.PreferredProvider is { } preferred &&
                !preferred.Equals(ProviderId))
            {
                throw new SandboxCapabilityAdmissionException(
                    new SandboxCapabilityAssessment(
                        false,
                        [
                            new SandboxCapabilityIssue(
                                StandardSandboxEnvironmentCapabilities
                                    .ProcessExecution,
                                CapabilityState.Unsupported,
                                $"Backend factory '{ProviderId.Value}' cannot create the requested provider '{preferred.Value}'.",
                                _definition.EnforcementLocation),
                        ]));
            }
        }
    }

    private async ValueTask<RuntimeWorkspaceBinding>
        CreateWorkspaceBindingAsync(
            IReadOnlyList<SandboxWorkspaceRequest> requests,
            ResourceRef<RuntimeHost> host,
            ResourceRef<ExecutionUnit> unit,
            TargetHandle<ExecutionUnit> unitHandle,
            ICollection<SandboxWorkspaceProjection> projections,
            ICollection<WorkspaceFileStoreAttachment> attachments,
            CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return EmptyRuntimeWorkspaceBinding.Create(ProviderId);
        }

        IWorkspaceFileStoreProvider fileStoreProvider =
            _registry!.WorkspaceFileStoreProviders.SingleOrDefault(
                provider => provider.ProviderId.Equals(ProviderId)) ??
            throw new NotSupportedException(
                $"Provider '{ProviderId.Value}' does not expose workspace file access.");
        var roots = new List<WorkspaceRootBinding>(requests.Count);
        foreach (SandboxWorkspaceRequest request in requests)
        {
            ResourceSnapshot<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus> projection =
                await _runtime!.EnsureAsync(
                        new ContentProjectionSpec
                        {
                            Source = request.Source,
                            Target = new ContentProjectionTarget
                            {
                                Host = host,
                                Unit = unit,
                            },
                            View = new ProjectionView
                            {
                                Kind = ProjectionViewKind.FilesystemTree,
                                GuestPath = new GuestPath(
                                    request.ExecutionPath.Value),
                            },
                            Role = request.Role,
                            AccessMode = request.Root.Access,
                            SyncPolicy = request.SyncPolicy,
                            Lifecycle =
                                ContentProjectionLifecycle.ExecutionUnit,
                            FinalizationPolicy =
                                request.FinalizationPolicy,
                            SecurityPolicy = request.SecurityPolicy,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            var projectionRef = new ResourceRef<ContentProjection>(
                projection.Metadata.Id,
                projection.Metadata.Scope,
                projection.Metadata.Generation);
            projections.Add(new SandboxWorkspaceProjection(
                projectionRef,
                request.FinalizationPolicy));

            WorkspaceFileStoreAttachment attachment =
                await fileStoreProvider.OpenAsync(
                        new WorkspaceFileStoreSpec
                        {
                            RootId = request.Root.Id,
                            Source = request.Source,
                            ExecutionPath =
                                request.ExecutionPath,
                            Access = request.Root.Access,
                            PathSemantics =
                                request.Root.PathSemantics,
                        },
                        unitHandle,
                        cancellationToken)
                    .ConfigureAwait(false);
            attachments.Add(attachment);
            WorkspaceExecutionPath defaultExecutionPath =
                attachment.Paths.ToExecutionPath(
                    request.Root.DefaultExecutionPath);
            if (!IsExecutionDirectoryWithin(
                    defaultExecutionPath,
                    request.ExecutionPath,
                    request.Root.PathSemantics.CaseSensitive))
            {
                throw new InvalidOperationException(
                    $"Workspace root '{request.Root.Id.Value}' maps its default directory outside the verified projection.");
            }
            roots.Add(request.Root);
        }

        WorkspaceRootBinding firstRoot = roots[0];
        WorkspaceFileStoreRegistration[] registrations =
            requests.Select((request, index) =>
                    new WorkspaceFileStoreRegistration(
                        request.Root.Id,
                        attachments.ElementAt(index)))
                .ToArray();
        return new RuntimeWorkspaceBinding
        {
            Files = attachments.Count == 1
                ? attachments.First().Files
                : new CompositeWorkspaceFileStore(registrations),
            Paths = attachments.Count == 1
                ? attachments.First().Paths
                : new CompositeWorkspacePathMapper(registrations),
            WorkingDirectory = firstRoot.DefaultExecutionPath,
            Roots = roots,
            Content = firstRoot.Content,
        };
    }

    private static void ValidateWorkspaceRequests(
        IReadOnlyList<SandboxWorkspaceRequest> workspaces)
    {
        HashSet<WorkspaceRootId> rootIds = [];
        foreach (SandboxWorkspaceRequest workspace in workspaces)
        {
            if (workspace.Root.Id.Value.Length == 0)
            {
                throw new ArgumentException(
                    "Every sandbox workspace requires a non-empty logical root identifier.",
                    nameof(workspaces));
            }
            if (!rootIds.Add(workspace.Root.Id))
            {
                throw new ArgumentException(
                    $"Sandbox workspace root '{workspace.Root.Id.Value}' is duplicated.",
                    nameof(workspaces));
            }
            if (workspace.ExecutionPath.Value.Length == 0)
            {
                throw new ArgumentException(
                    "Every sandbox workspace requires an execution path.",
                    nameof(workspaces));
            }
        }
    }

    private static bool IsExecutionDirectoryWithin(
        WorkspaceExecutionPath directory,
        WorkspaceExecutionPath root,
        bool caseSensitive)
    {
        static string Normalize(string value) =>
            value.Replace('\\', '/')
                .TrimEnd('/');
        string candidate = Normalize(directory.Value);
        string boundary = Normalize(root.Value);
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(candidate, boundary, comparison) ||
            candidate.StartsWith(boundary + "/", comparison);
    }

    private async ValueTask CleanupFailedSessionAsync(
        ResourceRef<ExecutionUnit>? unit,
        IReadOnlyList<SandboxWorkspaceProjection> projections,
        IReadOnlyList<WorkspaceFileStoreAttachment> attachments)
    {
        foreach (WorkspaceFileStoreAttachment attachment in
                 attachments.Reverse())
        {
            await attachment.DisposeAsync().ConfigureAwait(false);
        }
        foreach (SandboxWorkspaceProjection projection in
                 projections.Reverse())
        {
            await _runtime!.ReleaseAsync(
                    projection.Projection)
                .ConfigureAwait(false);
        }
        if (unit is { } unitRef)
        {
            await _runtime!.DeleteExecutionUnitAsync(unitRef)
                .ConfigureAwait(false);
        }
        await _runtime!.DeleteHostAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtime is not null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (_runtime is not null)
            {
                return;
            }

            EnvironmentProviderRegistry registry = new();
            registry.RegisterModule(_definition.Module);
            foreach (IProviderActivator activator in
                     registry.ProviderActivators)
            {
                ProviderActivationSpec spec =
                    _definition.ActivationSpecFactory?.Invoke(activator) ??
                    throw new InvalidOperationException(
                        $"Sandbox backend '{ProviderId.Value}' has provider activators but no activation specification factory.");
                ResourceSnapshot<
                    ProviderActivation,
                    ProviderActivationSpec,
                    ProviderActivationStatus> activation =
                    await activator.ActivateAsync(
                        spec,
                        cancellationToken).ConfigureAwait(false);
                if (activation.Status.ActivationPhase !=
                    ProviderActivationPhase.Ready ||
                    activation.Status.ActivationHandle is null)
                {
                    throw new InvalidOperationException(
                        $"Sandbox provider activation failed for '{ProviderId.Value}': " +
                        string.Join(
                            " | ",
                            activation.Status.Diagnostics.Select(
                                diagnostic =>
                                    $"{diagnostic.Code.Value}: {diagnostic.Message}")));
                }

                _activations.Add((
                    activator,
                    activation.Status.ActivationHandle.Value));
            }

            _registry = registry;
            _runtime = new InMemoryEnvironmentRuntime(registry);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void EnsureHostReady(
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            host)
    {
        if (host.Status.HostPhase == RuntimeHostPhase.Ready &&
            host.Status.Handle is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Runtime host '{host.Metadata.Id.Value}' is not ready: " +
            string.Join(
                " | ",
                host.Status.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code.Value}: {diagnostic.Message}")));
    }

    private void EnsureUnitReady(
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit)
    {
        if (unit.Status.UnitPhase == ExecutionUnitPhase.Ready &&
            unit.Status.Handle is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Execution unit '{unit.Metadata.Id.Value}' is not ready: " +
            string.Join(
                " | ",
                unit.Status.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code.Value}: {diagnostic.Message}")));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((IProviderActivator activator,
                  TargetHandle<ProviderActivation> handle) in
                 _activations.AsEnumerable().Reverse())
        {
            await activator.StopAsync(
                handle,
                new ProviderStopOptions(
                    TimeSpan.FromSeconds(2),
                    Force: true,
                    Reason: "sandbox backend factory dispose"))
                .ConfigureAwait(false);
        }

        _activations.Clear();
        _runtime = null;
        _registry = null;
        if (_definition.Module is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }

        _initLock.Dispose();
    }
}

internal sealed class EnvironmentSandboxSession(
    SandboxBackendKind backend,
    InMemoryEnvironmentRuntime runtime,
    ResourceRef<ExecutionUnit> unit,
    RuntimeProcessExecutionBinding execution,
    RuntimeWorkspaceBinding workspace,
    SandboxBackendCapabilities capabilities,
    IReadOnlyList<SandboxWorkspaceProjection> projections,
    IReadOnlyList<WorkspaceFileStoreAttachment> workspaceAttachments)
    : ISandboxSession
{
    private readonly IReadOnlyList<SandboxWorkspaceProjection>
        _projections = projections.ToArray();
    private readonly IReadOnlyList<WorkspaceFileStoreAttachment>
        _workspaceAttachments = workspaceAttachments.ToArray();
    private int _disposed;

    public SandboxBackendKind Backend { get; } = backend;
    public RuntimeProcessExecutionBinding Execution { get; } =
        execution.WorkspaceContent == workspace.Content
            ? execution
            : throw new InvalidOperationException(
                "Process execution and workspace access must share one content identity.");
    public RuntimeWorkspaceBinding Workspace { get; } = workspace;
    public SandboxBackendCapabilities Capabilities { get; } = capabilities;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (SandboxWorkspaceProjection projection in
                 _projections.Reverse())
        {
            if (projection.FinalizationPolicy is not (
                    FinalizationPolicy.None or
                    FinalizationPolicy.PromoteExplicitly))
            {
                try
                {
                    _ = await runtime.FinalizeAsync(
                            projection.Projection,
                            new FinalizationRequest
                            {
                                Kind =
                                    FinalizationKind.ManifestAndChangedContent,
                                IncludeProvenance = true,
                                IncludeDeletedEntries = true,
                                ProducerId =
                                    "hpd-agent-sandbox-session",
                            })
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            try
            {
                await runtime.ReleaseAsync(projection.Projection)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        foreach (WorkspaceFileStoreAttachment attachment in
                 _workspaceAttachments.Reverse())
        {
            try
            {
                await attachment.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        try
        {
            await runtime.DeleteExecutionUnitAsync(unit)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        try
        {
            await runtime.DeleteHostAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "Sandbox session cleanup did not complete.",
                failures);
        }
    }
}
