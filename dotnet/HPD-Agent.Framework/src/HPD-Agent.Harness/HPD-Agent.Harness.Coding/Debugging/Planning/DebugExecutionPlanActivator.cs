using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Trusted host-readiness result containing one owned debuggee identity.</summary>
internal sealed record DebugHostReadyResult(
    int SystemProcessId,
    string? SafeProcessRole = null);

/// <summary>Classification of one bounded readiness transcript.</summary>
internal enum DebugHostReadinessStatus
{
    Incomplete,
    Ready,
    Invalid
}

/// <summary>Result of applying a trusted readiness grammar.</summary>
internal sealed record DebugHostReadinessObservation(
    DebugHostReadinessStatus Status,
    DebugHostReadyResult? Ready = null);

/// <summary>Parses one bounded official host-readiness transcript.</summary>
internal interface IDebugHostReadinessParser
{
    string ProtocolId { get; }
    DebugHostReadinessObservation Observe(
        ReadOnlySpan<char> transcript,
        DebugReadinessMultiplicity multiplicity);
}

/// <summary>Parses the invariant VSTest host-debug wait message.</summary>
internal sealed partial class VSTestHostDebugReadinessParser : IDebugHostReadinessParser
{
    public const string Protocol = "vstest-host-debug-v1";
    public string ProtocolId => Protocol;

    public DebugHostReadinessObservation Observe(
        ReadOnlySpan<char> transcript,
        DebugReadinessMultiplicity multiplicity)
    {
        var text = transcript.ToString();
        var handshakeCount = OfficialHandshakePattern().Matches(text).Count;
        if (handshakeCount > 1)
            return new(DebugHostReadinessStatus.Invalid);
        var processLines = ProcessLinePattern().Matches(text);
        if (handshakeCount == 0)
            return new(DebugHostReadinessStatus.Incomplete);
        if (processLines.Count == 0)
            return text.Contains("Process Id", StringComparison.OrdinalIgnoreCase)
                ? new(DebugHostReadinessStatus.Invalid)
                : new(DebugHostReadinessStatus.Incomplete);
        if (multiplicity == DebugReadinessMultiplicity.ExactlyOne &&
            processLines.Count != 1)
            return new(DebugHostReadinessStatus.Invalid);
        var selected = processLines[0];
        if (!int.TryParse(
                selected.Groups["pid"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pid) ||
            pid <= 0)
            return new(DebugHostReadinessStatus.Invalid);
        return new(
            DebugHostReadinessStatus.Ready,
            new(pid, "testhost"));
    }

    [GeneratedRegex(
        @"Host\s+debugging\s+is\s+enabled\.\s+Please\s+attach\s+(?:a\s+)?debugger\s+to\s+testhost(?:\s+process)?\s+to\s+continue\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OfficialHandshakePattern();

    [GeneratedRegex(
        @"(?im)^\s*Process\s+Id:\s*(?<pid>[^\s,]+)\s*,\s*Name:\s*[^\r\n]+\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProcessLinePattern();
}

/// <summary>Deterministic registry of trusted readiness parsers.</summary>
internal sealed class DebugHostReadinessParserRegistry(
    IEnumerable<IDebugHostReadinessParser> parsers)
{
    private readonly IReadOnlyDictionary<string, IDebugHostReadinessParser> _parsers =
        (parsers ?? throw new ArgumentNullException(nameof(parsers)))
        .ToDictionary(parser => parser.ProtocolId, StringComparer.Ordinal);

    public IDebugHostReadinessParser GetRequired(string protocolId)
        => _parsers.TryGetValue(protocolId, out var parser)
            ? parser
            : throw new DebugStartPlanningException(
                "debug_test_platform_unsupported",
                "The host readiness protocol is not registered.");
}

/// <summary>Activated adapter start plus resources whose ownership transfers to the tree.</summary>
internal sealed record DebugActivatedExecution
{
    public required DebugAdapterStartPlan AdapterPlan { get; init; }
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required IReadOnlyList<IDebugOwnedResource> OwnedResources { get; init; }
}

/// <summary>Reservation-bound authority and event scope supplied to activation.</summary>
internal sealed record DebugExecutionActivationContext
{
    public required DebugTreeOwnership Ownership { get; init; }
    public required DebugRuntimeBinding Runtime { get; init; }
    public required DebugPermissionDecision Permission { get; init; }
    public bool IsRestart { get; init; }
    public required string DebugSessionId { get; init; }
    public ITreeDebugEventPublisher? EventPublisher { get; init; }
}

/// <summary>Activates inert execution plans only after a tree reservation exists.</summary>
internal interface IDebugExecutionPlanActivator
{
    ValueTask<DebugActivatedExecution> ActivateAsync(
        DebugExecutionPlan plan,
        DebugExecutionActivationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Default tree-scoped execution-plan activator.</summary>
internal sealed class DebugExecutionPlanActivator(
    DebugAdapterCatalog catalog,
    IDebugAdapterConfigurationComposer configurationComposer,
    DebugHostReadinessParserRegistry readinessParsers,
    IDebugAdapterTrustPolicy trustPolicy) : IDebugExecutionPlanActivator
{
    public async ValueTask<DebugActivatedExecution> ActivateAsync(
        DebugExecutionPlan plan,
        DebugExecutionActivationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ValidateRuntime(plan, context.Runtime);
        ValidatePermission(plan, context);
        await RevalidateAdapterAsync(plan, context.Runtime, cancellationToken)
            .ConfigureAwait(false);
        return plan switch
        {
            DirectAdapterDebugExecutionPlan direct => ActivateDirect(direct),
            HostedAttachDebugExecutionPlan hosted =>
                await ActivateHostedAsync(hosted, context, cancellationToken).ConfigureAwait(false),
            PreparedAdapterDebugExecutionPlan prepared =>
                await ActivatePreparedAsync(prepared, context, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new DebugStartPlanningException(
                "debug_target_unsupported",
                "The execution-plan shape is not implemented.")
        };
    }

    private static DebugActivatedExecution ActivateDirect(
        DirectAdapterDebugExecutionPlan plan)
    {
        var requiredMethod = plan.SemanticStartKind switch
        {
            DebugSemanticStartKind.DirectLaunch => DebugAdapterStartMethod.Launch,
            DebugSemanticStartKind.ExplicitAttach => DebugAdapterStartMethod.Attach,
            _ => throw new DebugStartPlanningException(
                "debug_adapter_method_mismatch",
                "A direct adapter plan cannot represent a hosted semantic start.")
        };
        if (plan.Adapter.Method != requiredMethod)
            throw new DebugStartPlanningException(
                "debug_adapter_method_mismatch",
                "The direct adapter method does not match the semantic start.");
        return new()
        {
            AdapterPlan = plan.Adapter,
            SemanticStartKind = plan.SemanticStartKind,
            AdapterStartMethod = plan.Adapter.Method,
            OwnedResources = []
        };
    }

    private async ValueTask<DebugActivatedExecution> ActivatePreparedAsync(
        PreparedAdapterDebugExecutionPlan plan,
        DebugExecutionActivationContext context,
        CancellationToken cancellationToken)
    {
        var processBinding = context.Runtime.ProcessExecution ??
            throw new DebugStartPlanningException(
                "debug_preparation_unavailable",
                "The runtime has no process capability for preparation.");
        if (plan.Preparation.Invocation.Target != processBinding.ExecutionTarget)
            throw new UnauthorizedAccessException(
                "The preparation plan targets a different execution unit.");
        var rollback = new Stack<IDebugOwnedResource>();
        try
        {
            ProcessInvocationResult result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                timeout.CancelAfter(plan.Preparation.Timeout);
                try
                {
                    result = await processBinding.ProcessProvider.RunAsync(
                        plan.Preparation.Invocation,
                        output: null,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw new DebugStartPlanningException(
                        "debug_activation_cancelled",
                        "Debug execution activation was cancelled.");
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw new DebugStartPlanningException(
                        "debug_preparation_timeout",
                        "Debug preparation did not finish before the timeout.");
                }
                catch (DebugStartPlanningException)
                {
                    throw;
                }
                catch
                {
                    throw new DebugStartPlanningException(
                        "debug_preparation_failed",
                        "The trusted debug preparation failed.");
                }
            }
            if (result.ExitCode != 0 ||
                result.CompletionKind is not (
                    ProcessCompletionKind.Completed or ProcessCompletionKind.Exited) ||
                result.Output.Stdout.CapturedBytes.Length >
                    plan.Preparation.MaximumStdoutBytes ||
                result.Output.Stderr.CapturedBytes.Length >
                    plan.Preparation.MaximumStderrBytes)
                throw new DebugStartPlanningException(
                    "debug_preparation_failed",
                    "The trusted debug preparation did not complete successfully.");
            var outputPath = Path.GetFullPath(plan.Preparation.ExpectedOutputPath);
            var relativeOutput = Path.GetRelativePath(
                Path.GetFullPath(plan.CanonicalWorkingDirectory),
                outputPath);
            if (Path.IsPathRooted(relativeOutput) ||
                relativeOutput == ".." ||
                relativeOutput.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "The preparation output is outside the authorized working directory.");
            if (!File.Exists(outputPath))
                throw new DebugStartPlanningException(
                    "debug_preparation_output_missing",
                    "Debug preparation did not produce the exact expected output.");
            if (plan.Preparation.OwnsExpectedOutput)
                rollback.Push(new DebugOwnedFileResource(outputPath));

            if (!catalog.TryGet(plan.Launch.AdapterId, out var entry))
                throw new DebugStartPlanningException(
                    "adapter_not_registered",
                    "The prepared launch adapter is no longer registered.");
            DebugAdapterStartPlan adapter;
            try
            {
                adapter = await catalog.GetFactory(plan.Launch.AdapterId)
                    .CreateSemanticLaunchPlanAsync(
                        configurationComposer,
                        entry.Descriptor,
                        plan.Launch.Resolution,
                        new DebugSemanticLaunchConfiguration(
                            outputPath,
                            plan.Launch.WorkingDirectory,
                            plan.Launch.TargetKind,
                            plan.Launch.ProgramKind,
                            plan.Launch.Arguments,
                            plan.Launch.StopOnEntry),
                        cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw new DebugStartPlanningException(
                    "debug_activation_cancelled",
                    "Debug execution activation was cancelled.");
            }
            catch (DebugStartPlanningException)
            {
                throw;
            }
            catch
            {
                throw new DebugStartPlanningException(
                    "debug_prepared_launch_failed",
                    "The prepared adapter start plan could not be created.");
            }
            if (adapter.Method != DebugAdapterStartMethod.Launch)
                throw new DebugStartPlanningException(
                    "debug_adapter_method_mismatch",
                    "The prepared adapter returned a non-launch start method.");
            var resources = rollback.Reverse().ToArray();
            rollback.Clear();
            return new()
            {
                AdapterPlan = adapter,
                SemanticStartKind = plan.SemanticStartKind,
                AdapterStartMethod = adapter.Method,
                OwnedResources = resources
            };
        }
        catch
        {
            while (rollback.TryPop(out var resource))
                try { await resource.DisposeAsync().ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private async ValueTask<DebugActivatedExecution> ActivateHostedAsync(
        HostedAttachDebugExecutionPlan plan,
        DebugExecutionActivationContext context,
        CancellationToken cancellationToken)
    {
        ValidateHostedPlan(plan);
        var processBinding = context.Runtime.ProcessExecution ??
            throw new DebugStartPlanningException(
                "debug_host_start_failed",
                "The runtime has no process capability for the requested host.");
        if (plan.Host.Invocation.Target != processBinding.ExecutionTarget)
            throw new UnauthorizedAccessException(
                "The hosted execution plan targets a different execution unit.");

        IProcessInvocationHandle? process = null;
        DebugOwnedProcessResource? owned = null;
        try
        {
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                startup.CancelAfter(plan.Host.StartupTimeout);
                try
                {
                    process = await processBinding.ProcessProvider.StartAsync(
                        plan.Host.Invocation,
                        output: null,
                        startup.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    startup.IsCancellationRequested)
                {
                    throw new DebugStartPlanningException(
                        "debug_host_start_timeout",
                        "The debug host did not start before the timeout.");
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw new DebugStartPlanningException(
                        "debug_activation_cancelled",
                        "Debug execution activation was cancelled.");
                }
                catch (DebugStartPlanningException)
                {
                    throw;
                }
                catch
                {
                    throw new DebugStartPlanningException(
                        "debug_host_start_failed",
                        "The trusted debug host could not be started.");
                }
            }
            owned = new DebugOwnedProcessResource(
                process,
                plan.Host.Role,
                plan.Host.StopTimeout,
                () => PublishAsync(context, new DebugOwnedResourceCleanupFailedEvent
                {
                    DebugTreeId = context.Ownership.DebugTreeId,
                    DebugSessionId = context.DebugSessionId,
                    AdapterId = plan.Attach.AdapterId,
                    SafeResourceKind = "process",
                    SafeResourceIdentity = plan.Host.Role
                }));
            process = null;
            await PublishAsync(context, new DebugHostProcessStartedEvent
            {
                DebugTreeId = context.Ownership.DebugTreeId,
                DebugSessionId = context.DebugSessionId,
                AdapterId = plan.Attach.AdapterId,
                SafeProcessRole = plan.Host.Role
            }).ConfigureAwait(false);

            var ready = await WaitForReadinessAsync(
                owned, context,
                plan,
                cancellationToken).ConfigureAwait(false);
            owned.BeginObservation(
                plan.Host.MaximumStdoutBytes,
                plan.Host.MaximumStderrBytes,
                result => PublishAsync(context, new DebugHostProcessExitedEvent
                {
                    DebugTreeId = context.Ownership.DebugTreeId,
                    DebugSessionId = context.DebugSessionId,
                    AdapterId = plan.Attach.AdapterId,
                    SafeProcessRole = plan.Host.Role,
                    ExitCode = result.ExitCode
                }));
            if (!catalog.TryGet(plan.Attach.AdapterId, out var entry))
                throw new DebugStartPlanningException(
                    "adapter_not_registered",
                    "The planned attach adapter is no longer registered.");
            var factory = catalog.GetFactory(plan.Attach.AdapterId);
            DebugAdapterStartPlan adapterPlan;
            try
            {
                adapterPlan = await factory.CreateSemanticAttachPlanAsync(
                    configurationComposer,
                    entry.Descriptor,
                    plan.Attach.Resolution,
                    new DebugSemanticAttachConfiguration(
                        plan.Attach.WorkingDirectory,
                        ready.SystemProcessId.ToString(CultureInfo.InvariantCulture)),
                    endpointId: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw new DebugStartPlanningException(
                    "debug_activation_cancelled",
                    "Debug execution activation was cancelled.");
            }
            catch (DebugStartPlanningException)
            {
                throw;
            }
            catch
            {
                throw new DebugStartPlanningException(
                    "debug_host_attach_failed",
                    "The debug adapter attach plan could not be created.");
            }
            if (adapterPlan.Method != DebugAdapterStartMethod.Attach)
                throw new DebugStartPlanningException(
                    "debug_adapter_method_mismatch",
                    "The hosted adapter returned a non-attach start method.");
            return new DebugActivatedExecution
            {
                AdapterPlan = adapterPlan,
                SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
                AdapterStartMethod = adapterPlan.Method,
                OwnedResources = [owned]
            };
        }
        catch
        {
            if (owned is not null)
                await owned.DisposeAsync().ConfigureAwait(false);
            if (process is not null)
                await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateHostedPlan(HostedAttachDebugExecutionPlan plan)
    {
        if (plan.Host.StartupTimeout <= TimeSpan.Zero ||
            plan.Host.StopTimeout <= TimeSpan.Zero ||
            plan.Readiness.Timeout <= TimeSpan.Zero ||
            plan.Host.MaximumStdoutBytes <= 0 ||
            plan.Host.MaximumStderrBytes <= 0 ||
            plan.Readiness.MaximumObservationBytes <= 0)
            throw new DebugStartPlanningException(
                "debug_host_plan_invalid",
                "The hosted execution plan contains invalid resource bounds.");
        if (plan.Host.StopProcessTree &&
            !plan.Host.Invocation.Policy.StopProcessTree)
            throw new DebugStartPlanningException(
                "debug_host_plan_invalid",
                "The hosted execution plan does not enforce process-tree cleanup.");
        if (!string.Equals(
                plan.Attach.Resolution.AuthorizationScope,
                "debug.adapter.attach",
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "The hosted adapter resolution is not authorized for attach.");
    }

    private async ValueTask<DebugHostReadyResult> WaitForReadinessAsync(
        DebugOwnedProcessResource owned,
        DebugExecutionActivationContext context,
        HostedAttachDebugExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        var process = owned.Process;
        var parser = readinessParsers.GetRequired(plan.Readiness.ProtocolId);
        var transcript = new StringBuilder();
        var observedBytes = 0;
        var stdoutBytes = 0;
        var stderrBytes = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(plan.Readiness.Timeout);
        try
        {
            await foreach (var chunk in process.ReadOutputAsync(timeout.Token).ConfigureAwait(false))
            {
                observedBytes = checked(observedBytes + chunk.Bytes.Length);
                if (chunk.Stream == ProcessOutputStream.Stdout)
                    stdoutBytes = checked(stdoutBytes + chunk.Bytes.Length);
                else if (chunk.Stream == ProcessOutputStream.Stderr)
                    stderrBytes = checked(stderrBytes + chunk.Bytes.Length);
                if (observedBytes > plan.Readiness.MaximumObservationBytes ||
                    stdoutBytes > plan.Host.MaximumStdoutBytes ||
                    stderrBytes > plan.Host.MaximumStderrBytes)
                    throw new DebugStartPlanningException(
                        "debug_host_readiness_invalid",
                        "The host readiness transcript exceeded its bound.");
                var text = Encoding.UTF8.GetString(chunk.Bytes.Span);
                transcript.Append(text);
                var observation = parser.Observe(
                    transcript.ToString(),
                    plan.Readiness.Multiplicity);
                if (observation.Status == DebugHostReadinessStatus.Invalid)
                    throw new DebugStartPlanningException(
                        "debug_host_readiness_invalid",
                        "The debug host returned an invalid readiness handshake.");
                if (observation is
                    {
                        Status: DebugHostReadinessStatus.Ready,
                        Ready: { } ready
                    })
                {
                    await PublishAsync(context, new DebugHostReadyEvent
                    {
                        DebugTreeId = context.Ownership.DebugTreeId,
                        DebugSessionId = context.DebugSessionId,
                        AdapterId = plan.Attach.AdapterId,
                        SafeProcessRole = ready.SafeProcessRole ?? plan.Host.Role
                    }).ConfigureAwait(false);
                    return ready;
                }
            }
            var result = await process.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            await PublishAsync(context, new DebugHostProcessExitedEvent
            {
                DebugTreeId = context.Ownership.DebugTreeId,
                DebugSessionId = context.DebugSessionId,
                AdapterId = plan.Attach.AdapterId,
                SafeProcessRole = plan.Host.Role,
                ExitCode = result.ExitCode
            }).ConfigureAwait(false);
            throw new DebugStartPlanningException(
                "debug_host_exited_before_ready",
                $"The debug host exited before readiness ({result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}).");
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new DebugStartPlanningException(
                "debug_host_readiness_timeout",
                "The debug host did not report readiness before the timeout.");
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw new DebugStartPlanningException(
                "debug_activation_cancelled",
                "Debug execution activation was cancelled.");
        }
    }

    private static void ValidateRuntime(DebugExecutionPlan plan, DebugRuntimeBinding runtime)
    {
        runtime.State.ThrowIfUnavailable();
        var process = runtime.ProcessExecution ??
            throw new InvalidOperationException("The debug runtime has no process binding.");
        if (!string.Equals(plan.EnvironmentId, process.EnvironmentId, StringComparison.Ordinal) ||
            plan.EnvironmentRevision != process.EnvironmentRevision)
            throw new UnauthorizedAccessException(
                "The execution plan is stale or belongs to another environment.");
    }

    private static void ValidatePermission(
        DebugExecutionPlan plan,
        DebugExecutionActivationContext context)
    {
        var expected = context.IsRestart
            ? DebugPermissionClass.Lifecycle
            : plan.SemanticStartKind == DebugSemanticStartKind.ExplicitAttach
                ? DebugPermissionClass.Attach
                : DebugPermissionClass.Launch;
        if (context.Permission.PermissionClass != expected)
            throw new UnauthorizedAccessException(
                "The activation permission does not authorize the semantic start.");
        if (plan is DirectAdapterDebugExecutionPlan direct &&
            ((plan.SemanticStartKind == DebugSemanticStartKind.ExplicitAttach &&
              direct.Adapter.Method != DebugAdapterStartMethod.Attach) ||
             (plan.SemanticStartKind == DebugSemanticStartKind.DirectLaunch &&
              direct.Adapter.Method != DebugAdapterStartMethod.Launch)))
            throw new DebugStartPlanningException(
                "debug_adapter_method_mismatch",
                "The adapter method does not match the semantic execution plan.");
        if (plan is HostedAttachDebugExecutionPlan &&
            plan.SemanticStartKind != DebugSemanticStartKind.HostedLaunchAttach)
            throw new DebugStartPlanningException(
                "debug_adapter_method_mismatch",
                "The hosted execution plan must activate through adapter attach.");
    }

    private async ValueTask RevalidateAdapterAsync(
        DebugExecutionPlan plan,
        DebugRuntimeBinding runtime,
        CancellationToken cancellationToken)
    {
        var (adapterId, resolution, plannedProvenance) = plan switch
        {
            DirectAdapterDebugExecutionPlan direct => (
                direct.Adapter.AdapterId,
                ResolutionFrom(direct.Adapter, runtime),
                direct.Adapter.PackageProvenance),
            HostedAttachDebugExecutionPlan hosted => (
                hosted.Attach.AdapterId,
                hosted.Attach.Resolution,
                catalog.TryGet(hosted.Attach.AdapterId, out var hostedEntry)
                    ? hostedEntry.Descriptor.Provenance
                    : null),
            PreparedAdapterDebugExecutionPlan prepared => (
                prepared.Launch.AdapterId,
                prepared.Launch.Resolution,
                catalog.TryGet(prepared.Launch.AdapterId, out var preparedEntry)
                    ? preparedEntry.Descriptor.Provenance
                    : null),
            _ => throw new DebugStartPlanningException(
                "debug_target_unsupported",
                "The execution-plan shape is not implemented.")
        };
        if (!catalog.TryGet(adapterId, out var entry) || plannedProvenance is null)
            throw new DebugStartPlanningException(
                "adapter_not_registered",
                "The planned debug adapter is no longer registered.");
        if (entry.Descriptor.Provenance != plannedProvenance)
            throw new DebugStartPlanningException(
                "adapter_provenance_changed",
                "The planned debug adapter provenance changed before activation.");
        var trust = trustPolicy.Evaluate(entry.Descriptor);
        if (trust.TrustLevel != DebugAdapterTrustLevel.Trusted ||
            trust != resolution.TrustDecision)
            throw new DebugStartPlanningException(
                "adapter_trust_changed",
                "The debug adapter trust decision changed before activation.");
        ValidateProcessBinding(resolution.ProcessExecution, runtime.ProcessExecution);
        var currentResolution = resolution with
        {
            ProcessExecution = resolution.ProcessExecution is null
                ? null
                : runtime.ProcessExecution,
            TrustDecision = trust
        };
        DebugAdapterAvailability availability;
        try
        {
            availability = await catalog.GetFactory(adapterId).ProbeAsync(
                entry.Descriptor,
                currentResolution,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw new DebugStartPlanningException(
                "debug_activation_cancelled",
                "Debug execution activation was cancelled.");
        }
        catch (DebugStartPlanningException)
        {
            throw;
        }
        catch
        {
            throw new DebugStartPlanningException(
                "adapter_probe_failed",
                "The debug adapter could not be revalidated before activation.");
        }
        if (availability.Kind != DebugAdapterAvailabilityKind.Available)
            throw new DebugStartPlanningException(
                "adapter_unavailable",
                "The planned debug adapter became unavailable before activation.");
    }

    private static DebugAdapterResolutionContext ResolutionFrom(
        DebugAdapterStartPlan adapter,
        DebugRuntimeBinding runtime)
        => new()
        {
            WorkspaceRoot = adapter.CanonicalWorkingDirectory,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            TargetPlatform = RuntimeInformation.RuntimeIdentifier,
            PolicyRevision = adapter.PolicyRevision,
            EndpointCatalogRevision = adapter.EndpointCatalogRevision,
            AuthorizationScope = adapter.AuthorizationScope,
            FilteredEnvironment = adapter.FilteredEnvironment,
            ProcessExecution = adapter.ProcessExecution,
            ProcessSandbox = runtime.ProcessSandbox,
            TrustDecision = adapter.TrustDecision
        };

    private static void ValidateProcessBinding(
        RuntimeProcessExecutionBinding? planned,
        RuntimeProcessExecutionBinding? current)
    {
        if (planned is null)
            return;
        if (current is null ||
            planned.EnvironmentId != current.EnvironmentId ||
            planned.EnvironmentRevision != current.EnvironmentRevision ||
            planned.ExecutionTarget != current.ExecutionTarget ||
            planned.ProcessProvider.ProviderId != current.ProcessProvider.ProviderId)
            throw new UnauthorizedAccessException(
                "The execution-plan process provider or target changed before activation.");
    }

    private static async ValueTask PublishAsync(
        DebugExecutionActivationContext context,
        DebugLifecycleEvent @event)
    {
        if (context.EventPublisher is null)
            return;
        try
        {
            await context.EventPublisher.PublishAsync(
                @event,
                durable: true,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Event publication must not change activation semantics.
        }
    }
}
