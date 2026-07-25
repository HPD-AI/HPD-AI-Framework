using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>
/// Converts model-facing semantic starts into inert plans and delegates tree-scoped activation.
/// </summary>
internal sealed class DebugExecutionPlanningService(
    DebugExecutionTargetPlannerRegistry planners,
    DebugAdapterSelector selector,
    DebugAdapterCatalog catalog,
    IWorkspaceRootMarkerResolver markerResolver,
    IDebugAdapterConfigurationComposer configurationComposer,
    IDebugAdapterTrustPolicy trustPolicy,
    DebugExecutionStartOrchestrator starts,
    DebugStartResultProjector startResults,
    IDebugAdapterDiagnosticStore diagnosticStore,
    DebugBreakpointSelectionEventFactory breakpointEvents,
    IDebugHostRequestBroker? hostRequestBroker = null,
    IDebugChildSessionPlanFactory? childSessionPlanFactory = null)
{
    public async Task<string> LaunchAsync(
        LaunchDebugOperation operation,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await PlanLaunchAsync(
            operation,
            permission,
            context,
            isRestart: false,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<string> RestartAsync(
        LaunchDebugOperation operation,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
        => PlanLaunchAsync(
            operation,
            permission,
            context,
            isRestart: true,
            cancellationToken);

    private async Task<string> PlanLaunchAsync(
        LaunchDebugOperation operation,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        bool isRestart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);
        var workspace = AgentWorkspace.From(context.RunConfig);
        var canonicalTarget = ResolveTarget(workspace, operation.Target);
        AgentFilesystemAuthorization targetAuthorization;
        try
        {
            targetAuthorization = await AgentFilesystemAccess.AuthorizeReadCapabilityAsync(
                canonicalTarget,
                "Debug.launch.target",
                context,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AgentCapabilityDeniedException)
        {
            throw new DebugStartPlanningException(
                "debug_capability_denied",
                "Reading the selected debug target was not authorized.");
        }
        if (targetAuthorization.Escalated)
        {
            runtime = runtime with
            {
                ProcessSandbox = runtime.ProcessSandbox.WithPathGrant(
                    AgentSandboxPathAccess.Read,
                    Directory.Exists(canonicalTarget)
                        ? canonicalTarget
                        : Path.GetDirectoryName(canonicalTarget)!)
            };
        }
        if (operation.Target is ApplicationProjectDebugTarget or TestDebugTarget)
        {
            var projectDirectory = Directory.Exists(canonicalTarget)
                ? canonicalTarget
                : Path.GetDirectoryName(canonicalTarget)!;
            AgentFilesystemAuthorization writeAuthorization;
            try
            {
                writeAuthorization = await AgentFilesystemAccess.AuthorizeWriteCapabilityAsync(
                    projectDirectory,
                    "Debug.launch.build-output",
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AgentCapabilityDeniedException)
            {
                throw new DebugStartPlanningException(
                    "debug_capability_denied",
                    "Writing the selected project's build outputs was not authorized.");
            }
            if (writeAuthorization.Escalated)
            {
                runtime = runtime with
                {
                    ProcessSandbox = runtime.ProcessSandbox.WithPathGrant(
                        AgentSandboxPathAccess.Write,
                        projectDirectory)
                };
            }
        }
        var discoveryWorkspace = workspace.WithExplicitDiscoveryRoot(canonicalTarget);
        string workspacePath;
        if (string.IsNullOrWhiteSpace(operation.WorkspacePath))
        {
            workspacePath = discoveryWorkspace.GetOwningRoot(canonicalTarget).Path;
        }
        else
        {
            workspacePath = workspace.CanonicalizeExplicitPath(operation.WorkspacePath);
            AgentFilesystemAuthorization workspaceAuthorization;
            try
            {
                workspaceAuthorization = await AgentFilesystemAccess.AuthorizeReadCapabilityAsync(
                    workspacePath,
                    "Debug.launch.workspace",
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AgentCapabilityDeniedException)
            {
                throw new DebugStartPlanningException(
                    "debug_capability_denied",
                    "Reading the selected debug workspace was not authorized.");
            }
            if (workspaceAuthorization.Escalated)
            {
                runtime = runtime with
                {
                    ProcessSandbox = runtime.ProcessSandbox.WithPathGrant(
                        AgentSandboxPathAccess.Read,
                        workspacePath)
                };
            }
            discoveryWorkspace = discoveryWorkspace.WithExplicitDiscoveryRoot(workspacePath);
        }
        var markers = catalog.Entries.SelectMany(entry => entry.Descriptor.RootMarkers)
            .Concat(["*.csproj", "*.fsproj", "*.vbproj", "*.sln", "*.slnx"])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var evidence = await markerResolver.ResolveAsync(
            discoveryWorkspace, canonicalTarget, markers, cancellationToken).ConfigureAwait(false);
        var plan = await planners.PlanAsync(new DebugExecutionPlanningContext
        {
            Operation = operation,
            Target = operation.Target,
            Runtime = runtime,
            Workspace = discoveryWorkspace,
            CanonicalWorkspacePath = workspacePath,
            CanonicalTargetPath = canonicalTarget,
            Evidence = evidence,
            ExplicitAdapterId = operation.AdapterId,
            LanguageHint = operation.Language,
            AuthorizePath = (path, access, current, token) =>
                AuthorizePlannerPathAsync(path, access, current, context, token)
        }, cancellationToken).ConfigureAwait(false);
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugExecutionPlan,
            CreatePlanMetadata(plan, operation.Target));
        if (plan.ProjectEvaluation is not null)
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugProjectEvaluation,
                plan.ProjectEvaluation);
        return await StartAsync(
            isRestart ? "restart" : "launch",
            plan,
            permission,
            runtime,
            context,
            operation,
            discoveryWorkspace,
            isRestart,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AttachAsync(
        AttachDebugOperation operation,
        DebugPermissionDecision permission,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);
        var workspace = AgentWorkspace.From(context.RunConfig);
        var workspacePath = string.IsNullOrWhiteSpace(operation.WorkspacePath)
            ? workspace.DefaultRootPath
            : workspace.CanonicalizeExplicitPath(operation.WorkspacePath);
        AgentFilesystemAuthorization workspaceAuthorization;
        try
        {
            workspaceAuthorization = await AgentFilesystemAccess.AuthorizeReadCapabilityAsync(
                workspacePath,
                "Debug.attach.workspace",
                context,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AgentCapabilityDeniedException)
        {
            throw new DebugStartPlanningException(
                "debug_capability_denied",
                "Reading the selected debug workspace was not authorized.");
        }
        if (workspaceAuthorization.Escalated)
        {
            runtime = runtime with
            {
                ProcessSandbox = runtime.ProcessSandbox.WithPathGrant(
                    AgentSandboxPathAccess.Read,
                    workspacePath)
            };
        }
        var targetKind = operation.Target switch
        {
            ProcessDebugAttachTarget => DebugTargetKind.Process,
            EndpointDebugAttachTarget => DebugTargetKind.RegisteredRemoteEndpoint,
            _ => throw new ArgumentOutOfRangeException(nameof(operation.Target))
        };
        ValidateExplicitAdapter(operation.AdapterId, targetKind);
        var resolution = Resolution(runtime, workspacePath, "debug.adapter.attach");
        var selection = await selector.SelectAsync(new DebugAdapterSelectionContext
        {
            Operation = DebugAdapterSelectionOperation.Attach,
            ExplicitAdapterId = operation.AdapterId,
            Language = operation.Language,
            TargetKind = targetKind,
            ProjectMarkerFingerprint = "none",
            Resolution = resolution
        }, cancellationToken).ConfigureAwait(false);
        var (descriptor, factory) = RequireAvailable(selection);
        resolution = resolution with { TrustDecision = trustPolicy.Evaluate(descriptor) };
        var processId = (operation.Target as ProcessDebugAttachTarget)?.ProcessId
            .ToString(CultureInfo.InvariantCulture);
        var endpointId = (operation.Target as EndpointDebugAttachTarget)?.EndpointId;
        var adapter = await factory.CreateSemanticAttachPlanAsync(
            configurationComposer,
            descriptor,
            resolution,
            new DebugSemanticAttachConfiguration(workspacePath, processId),
            endpointId,
            cancellationToken).ConfigureAwait(false);
        var plan = new DirectAdapterDebugExecutionPlan
        {
            PlannerId = "explicit-attach",
            SemanticStartKind = DebugSemanticStartKind.ExplicitAttach,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = workspacePath,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                operation.InitialConfiguration,
                stopOnEntry: false,
                workspace),
            Adapter = adapter
        };
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugExecutionPlan,
            CreatePlanMetadata(plan, operation.Target));
        return await StartAsync(
            "attach",
            plan,
            permission,
            runtime,
            context,
            semanticLaunchOperation: null,
            workspace,
            isRestart: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartAsync(
        string action,
        DebugExecutionPlan plan,
        DebugPermissionDecision permission,
        DebugRuntimeBinding runtime,
        FunctionExecutionContext context,
        LaunchDebugOperation? semanticLaunchOperation,
        AgentWorkspace workspace,
        bool isRestart,
        CancellationToken cancellationToken)
    {
        var backgroundHandles = context.BackgroundHandles ??
            throw new InvalidOperationException(
                "Debug sessions require a background-handle registry.");
        DebugSessionStartResult result;
        try
        {
            result = await starts.StartAsync(new DebugExecutionStartRequest
            {
                Runtime = runtime,
                Workspace = workspace,
                ExecutionPlan = plan,
                Permission = permission,
                SemanticLaunchOperation = semanticLaunchOperation,
                IsRestart = isRestart,
                BackgroundHandles = backgroundHandles,
                InitializeFeatures = new DebugInitializeFeatures
                {
                    RunInTerminalHandler = hostRequestBroker is not null,
                    StartDebuggingHandler = childSessionPlanFactory is not null,
                    ProgressHandling = true,
                    InvalidatedEventHandling = true,
                    MemoryOperations = true,
                    MemoryEventHandling = true,
                    VariablePaging = true,
                    VariableTypeRendering = true
                },
                EventPublisher = context.EventCoordinator is not null &&
                    context.ThreadEvents is not null
                    ? new DebugEventPublisher(context.EventCoordinator, context.ThreadEvents)
                    : null,
                HostRequestBroker = hostRequestBroker,
                ChildSessionPlanFactory = childSessionPlanFactory,
                Authorization = new DebugTreeAuthorizationOptions
                {
                    WorkingDirectoryScope = plan.CanonicalWorkingDirectory,
                    Grants = DebugTreeGrant.Routine |
                        DebugTreeGrant.DataBreakpoints |
                        DebugTreeGrant.ChildSessions |
                        DebugTreeGrant.TerminalProcesses |
                        DebugTreeGrant.Evaluate |
                        DebugTreeGrant.MutateVariables |
                        DebugTreeGrant.WriteMemory
                },
                ContentStore = context.ContentStore
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DebugAdapterStartException exception)
        {
            var reference = diagnosticStore.Retain(
                exception.AdapterId, exception.Phase, exception.Diagnostics);
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugAdapterDiagnosticReference, reference);
            throw new DebugStartPlanningException(
                exception.Phase switch
                {
                    "initialize" => "adapter_initialize_failed",
                    "launch" or "attach" => "adapter_request_failed",
                    "configuration" => "adapter_configuration_failed",
                    _ => "adapter_start_failed"
                },
                exception.Phase switch
                {
                    "initialize" => "The debug adapter failed during initialization.",
                    "launch" or "attach" => "The debug adapter rejected the start request.",
                    "configuration" => "The debug adapter failed during configuration.",
                    _ => "The debug adapter process could not be started."
                });
        }
        await PublishInitialBreakpointSelectionsAsync(
            result,
            plan,
            runtime,
            workspace,
            context,
            action).ConfigureAwait(false);
        return startResults.Project(action, plan, result, context);
    }

    private async ValueTask PublishInitialBreakpointSelectionsAsync(
        DebugSessionStartResult result,
        DebugExecutionPlan plan,
        DebugRuntimeBinding runtime,
        AgentWorkspace workspace,
        FunctionExecutionContext context,
        string action)
    {
        if (runtime.SessionManager is not DebugSessionManager manager) return;
        var owner = new DebugTreeLookupScope(
            runtime.AgentRuntimeRegistrationId,
            runtime.SessionId,
            runtime.ThreadId);
        var tree = manager.ResolveTree(owner, result.DebugTreeId);
        if (tree.EventPublisher is not { } publisher) return;
        var session = tree.SelectSession(result.DebugSessionId);
        var after = tree.Breakpoints.Snapshot;
        var kinds = new[]
        {
            (DebugBreakpointKind.Source, after.Source.Length),
            (DebugBreakpointKind.Function, after.Function.Length),
            (DebugBreakpointKind.Exception, after.Exception.Length)
        };
        foreach (var (kind, count) in kinds)
        {
            if (count == 0) continue;
            try
            {
                var @event = await breakpointEvents.CreateAsync(
                    new DebugBreakpointMutationResult(
                        kind,
                        new DebugDesiredBreakpointSnapshot(),
                        after,
                        session.AdapterBreakpoints.Snapshot,
                        result.Breakpoints,
                        result.DebugSessionId),
                    workspace,
                    context.FunctionCallId,
                    action,
                    result.DebugTreeId,
                    session.AdapterPlan.AdapterId,
                    CancellationToken.None).ConfigureAwait(false);
                _ = await publisher.PublishDurableAsync(@event, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                tree.RecordObserverFailure();
            }
        }
    }

    private static DebugExecutionPlanMetadata CreatePlanMetadata(
        DebugExecutionPlan plan,
        object target)
        => new(
            plan.PlannerId,
            plan.SemanticStartKind,
            target.GetType().Name,
            Path.GetFileName(plan.CanonicalWorkingDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)));

    private static async ValueTask<AgentSandboxRuntime> AuthorizePlannerPathAsync(
        string path,
        AgentSandboxPathAccess access,
        AgentSandboxRuntime current,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        AgentFilesystemAuthorization authorization;
        try
        {
            authorization = access == AgentSandboxPathAccess.Read
                ? await AgentFilesystemAccess.AuthorizeReadCapabilityAsync(
                    path,
                    "Debug.plan.read",
                    context,
                    cancellationToken).ConfigureAwait(false)
                : await AgentFilesystemAccess.AuthorizeWriteCapabilityAsync(
                    path,
                    "Debug.plan.write",
                    context,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (AgentCapabilityDeniedException)
        {
            throw new DebugStartPlanningException(
                "debug_capability_denied",
                "A filesystem capability required by the debug plan was not authorized.");
        }

        return authorization.Escalated
            ? current.WithPathGrant(access, authorization.Path)
            : current;
    }

    private static string ResolveTarget(AgentWorkspace workspace, DebugTarget target)
        => target switch
        {
            SourceFileDebugTarget source => workspace.CanonicalizeExplicitPath(source.Path),
            ApplicationProjectDebugTarget project =>
                ResolveProjectLike(workspace, project.Path),
            ExecutableDebugTarget executable => workspace.CanonicalizeExplicitPath(executable.Path),
            TestDebugTarget test => ResolveProjectLike(workspace, test.Path),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    private static string ResolveProjectLike(AgentWorkspace workspace, string path)
    {
        var candidate = workspace.CanonicalizeExplicitPath(path);
        return File.Exists(candidate)
            ? candidate
            : workspace.ResolveDirectory(path);
    }

    private static DebugAdapterResolutionContext Resolution(
        DebugRuntimeBinding runtime,
        string workspacePath,
        string authorizationScope)
    {
        var process = runtime.ProcessExecution ??
            throw new InvalidOperationException(
                "The selected runtime has no debug environment.");
        return new DebugAdapterResolutionContext
        {
            WorkspaceRoot = workspacePath,
            EnvironmentId = process.EnvironmentId,
            EnvironmentRevision = process.EnvironmentRevision,
            TargetPlatform = RuntimeInformation.RuntimeIdentifier,
            PolicyRevision = 0,
            EndpointCatalogRevision = 0,
            AuthorizationScope = authorizationScope,
            FilteredEnvironment = ImmutableDictionary<string, string?>.Empty,
            ProcessExecution = process,
            ProcessSandbox = runtime.ProcessSandbox,
            TrustDecision = new DebugAdapterTrustDecision
            {
                TrustLevel = DebugAdapterTrustLevel.Denied,
                PolicyRevision = "unresolved",
                ReasonCode = "SELECTION_PENDING"
            }
        };
    }

    private void ValidateExplicitAdapter(string? adapterId, DebugTargetKind targetKind)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
            return;
        if (!catalog.TryGet(adapterId, out var entry))
            throw new DebugStartPlanningException(
                "adapter_not_registered",
                "The requested debug adapter is not registered.");
        if ((entry.Descriptor.TargetKinds & targetKind) == 0)
            throw new DebugStartPlanningException(
                "adapter_incompatible_with_target",
                "The requested debug adapter is incompatible with the target.");
        if (trustPolicy.Evaluate(entry.Descriptor).TrustLevel != DebugAdapterTrustLevel.Trusted)
            throw new DebugStartPlanningException(
                "permission_denied",
                "The requested debug adapter is not trusted by current policy.");
    }

    private static (DebugAdapterDescriptor Descriptor, IDebugAdapterFactory Factory)
        RequireAvailable(DebugAdapterSelectionResult selection) => selection.Kind switch
    {
        DebugAdapterSelectionKind.Available
            when selection.Entry is not null && selection.Factory is not null =>
            (selection.Entry.Descriptor, selection.Factory),
        DebugAdapterSelectionKind.NoMatch =>
            throw new DebugStartPlanningException(
                "adapter_not_found",
                "No trusted debug adapter matches the target."),
        DebugAdapterSelectionKind.Unavailable =>
            throw new DebugStartPlanningException(
                "adapter_unavailable",
                "Matching debug adapters are unavailable."),
        DebugAdapterSelectionKind.Ambiguous =>
            throw new DebugStartPlanningException(
                "adapter_ambiguous",
                "Multiple debug adapters match the target."),
        _ => throw new DebugStartPlanningException(
            "adapter_unavailable",
            "Debug adapter selection failed.")
    };

}

/// <summary>Stable classified failure produced by semantic execution planning.</summary>
internal sealed class DebugStartPlanningException(string kind, string message)
    : InvalidOperationException(message)
{
    public string Kind { get; } = kind;
}
