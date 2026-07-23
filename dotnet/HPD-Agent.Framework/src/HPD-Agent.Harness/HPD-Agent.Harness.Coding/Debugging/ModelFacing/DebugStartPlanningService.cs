using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class DebugStartPlanningService(
    DebugAdapterSelector selector,
    IDebugAdapterConfigurationComposer configurationComposer,
    IDebugAdapterTrustPolicy trustPolicy,
    DebugSessionStartOrchestrator starts,
    DebugResultFormatter formatter,
    IDebugHostRequestBroker? hostRequestBroker = null,
    IDebugChildSessionPlanFactory? childSessionPlanFactory = null)
{
    public async Task<string> LaunchAsync(
        LaunchDebugOperation operation,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);
        var workspace = AgentWorkspace.From(context.RunConfig);
        var workspacePath = workspace.ResolveDirectory(operation.WorkspacePath);
        var (target, targetKind) = ResolveLaunchTarget(workspace, operation.Target);
        var resolution = Resolution(runtime, workspacePath, "debug.adapter.launch");
        var selection = await selector.SelectAsync(new DebugAdapterSelectionContext
        {
            Operation = DebugAdapterSelectionOperation.Launch,
            ExplicitAdapterId = operation.AdapterId,
            Language = operation.Language,
            FileExtension = Path.GetExtension(target),
            TargetKind = targetKind,
            ProjectMarkerFingerprint = "none",
            Resolution = resolution
        }, cancellationToken).ConfigureAwait(false);
        var (descriptor, factory) = RequireAvailable(selection);
        resolution = resolution with { TrustDecision = trustPolicy.Evaluate(descriptor) };
        var plan = await factory.CreateSemanticLaunchPlanAsync(
            configurationComposer,
            descriptor,
            resolution,
            new DebugSemanticLaunchConfiguration(
                target, workspacePath, targetKind, operation.Arguments, operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return await StartAsync(
            "launch", MapInitial(operation.InitialConfiguration, operation.StopOnEntry, workspace),
            isAttach: false, runtime, plan, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AttachAsync(
        AttachDebugOperation operation,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);
        var workspace = AgentWorkspace.From(context.RunConfig);
        var workspacePath = workspace.ResolveDirectory(operation.WorkspacePath);
        var targetKind = operation.Target switch
        {
            ProcessDebugAttachTarget => DebugTargetKind.Process,
            EndpointDebugAttachTarget => DebugTargetKind.RegisteredRemoteEndpoint,
            _ => throw new ArgumentOutOfRangeException(nameof(operation.Target))
        };
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
        var plan = await factory.CreateSemanticAttachPlanAsync(
            configurationComposer,
            descriptor,
            resolution,
            new DebugSemanticAttachConfiguration(workspacePath, processId),
            endpointId,
            cancellationToken).ConfigureAwait(false);
        return await StartAsync(
            "attach", MapInitial(operation.InitialConfiguration, false, workspace),
            isAttach: true, runtime, plan, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartAsync(
        string action,
        DebugInitialConfiguration initial,
        bool isAttach,
        DebugRuntimeBinding runtime,
        DebugAdapterLaunchPlan plan,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var backgroundHandles = context.BackgroundHandles
            ?? throw new InvalidOperationException("Debug sessions require a background-handle registry.");
        var result = await starts.StartAsync(new DebugSessionStartRequest
        {
            Runtime = runtime,
            LaunchPlan = plan,
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
            InitialConfiguration = initial,
            IsAttach = isAttach,
            EventPublisher = context.EventCoordinator is not null && context.ThreadEvents is not null
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
        context.ResultMetadata.Set(CodingToolMetadataKeys.DebugOperation,
            new DebugOperationMetadata(action, result.DebugTreeId, result.DebugSessionId, true));
        return formatter.Success(action, Attributes(
            ("debugTreeId", result.DebugTreeId),
            ("debugSessionId", result.DebugSessionId),
            ("adapter", plan.AdapterId),
            ("status", result.Status),
            ("backgroundHandleId", result.Handle.HandleId)));
    }

    private static DebugAdapterResolutionContext Resolution(
        DebugRuntimeBinding runtime,
        string workspacePath,
        string authorizationScope)
    {
        var process = runtime.ProcessExecution;
        var environmentId = process?.EnvironmentId
            ?? throw new InvalidOperationException("The selected runtime has no debug Environment.");
        return new DebugAdapterResolutionContext
        {
            WorkspaceRoot = workspacePath,
            EnvironmentId = environmentId,
            EnvironmentRevision = process.EnvironmentRevision,
            TargetPlatform = RuntimeInformation.RuntimeIdentifier,
            PolicyRevision = 0,
            EndpointCatalogRevision = 0,
            AuthorizationScope = authorizationScope,
            FilteredEnvironment = ImmutableDictionary<string, string?>.Empty,
            ProcessExecution = process,
            TrustDecision = new DebugAdapterTrustDecision
            {
                TrustLevel = DebugAdapterTrustLevel.Denied,
                PolicyRevision = "unresolved",
                ReasonCode = "SELECTION_PENDING"
            }
        };
    }

    private static (string Target, DebugTargetKind Kind) ResolveLaunchTarget(
        AgentWorkspace workspace,
        DebugLaunchTarget target) => target switch
    {
        SourceFileDebugLaunchTarget source =>
            (workspace.ResolvePath(source.Path), DebugTargetKind.SourceFile),
        ProjectDirectoryDebugLaunchTarget project =>
            (workspace.ResolveDirectory(project.Path), DebugTargetKind.ProjectDirectory),
        ExecutableDebugLaunchTarget executable =>
            (workspace.ResolvePath(executable.Path), DebugTargetKind.Executable),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private static (DebugAdapterDescriptor Descriptor, IDebugAdapterFactory Factory) RequireAvailable(
        DebugAdapterSelectionResult selection) => selection.Kind switch
    {
        DebugAdapterSelectionKind.Available when selection.Entry is not null && selection.Factory is not null =>
            (selection.Entry.Descriptor, selection.Factory),
        DebugAdapterSelectionKind.NoMatch =>
            throw new DebugStartPlanningException("adapter_not_found", "No trusted debug adapter matches the target."),
        DebugAdapterSelectionKind.Unavailable =>
            throw new DebugStartPlanningException("adapter_unavailable", "Matching debug adapters are unavailable."),
        DebugAdapterSelectionKind.Ambiguous =>
            throw new DebugStartPlanningException("adapter_ambiguous", "Multiple debug adapters match the target."),
        _ => throw new DebugStartPlanningException("adapter_unavailable", "The debug adapter selection failed.")
    };

    private static DebugInitialConfiguration MapInitial(
        DebugInitialConfigurationInput? input,
        bool stopOnEntry,
        AgentWorkspace workspace) => new()
    {
        SourceBreakpoints = input?.SourceBreakpoints?
            .Select(item => new DebugSourceBreakpoint(
                workspace.ResolvePath(item.Path), item.Line, item.Column, item.Condition, item.HitCondition, item.LogMessage))
            .ToArray() ?? [],
        FunctionBreakpoints = input?.FunctionBreakpoints?
            .Select(item => new DebugFunctionBreakpoint(item.Name, item.Condition, item.HitCondition))
            .ToArray() ?? [],
        ExceptionFilters = input?.ExceptionBreakpoints?
            .Select(item => new DebugExceptionFilter(item.FilterId, item.Condition))
            .ToArray() ?? [],
        StopOnEntry = stopOnEntry
    };

    private static IReadOnlyList<KeyValuePair<string, object?>> Attributes(
        params (string Key, object? Value)[] values)
        => values.Select(value => KeyValuePair.Create(value.Key, value.Value)).ToArray();
}

internal sealed class DebugStartPlanningException(string kind, string message) : InvalidOperationException(message)
{
    public string Kind { get; } = kind;
}
