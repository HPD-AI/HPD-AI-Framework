using System.Collections.Immutable;
using System.Text;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Immutable evidence and authority supplied to an execution-target planner.</summary>
internal sealed record DebugExecutionPlanningContext
{
    public required LaunchDebugOperation Operation { get; init; }
    public required DebugTarget Target { get; init; }
    public required DebugRuntimeBinding Runtime { get; init; }
    public required AgentWorkspace Workspace { get; init; }
    public required string CanonicalWorkspacePath { get; init; }
    public required string CanonicalTargetPath { get; init; }
    public required WorkspaceRootMarkerResolution Evidence { get; init; }
    public string? ExplicitAdapterId { get; init; }
    public string? LanguageHint { get; init; }
}

/// <summary>Indicates whether an execution planner owns a semantic target.</summary>
internal enum DebugPlannerMatchKind
{
    NotApplicable,
    Applicable
}

/// <summary>Result of evaluating one execution planner.</summary>
internal sealed record DebugExecutionPlanningResult
{
    public required DebugPlannerMatchKind MatchKind { get; init; }
    public DebugExecutionPlan? Plan { get; init; }

    public static DebugExecutionPlanningResult NotApplicable { get; } = new()
    {
        MatchKind = DebugPlannerMatchKind.NotApplicable
    };

    public static DebugExecutionPlanningResult Applicable(DebugExecutionPlan plan) => new()
    {
        MatchKind = DebugPlannerMatchKind.Applicable,
        Plan = plan ?? throw new ArgumentNullException(nameof(plan))
    };
}

/// <summary>Produces an inert execution plan for one closed semantic target shape.</summary>
internal interface IDebugExecutionTargetPlanner
{
    string Id { get; }
    int Priority { get; }

    ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken);
}

/// <summary>Selects exactly one deterministic execution planner.</summary>
internal sealed class DebugExecutionTargetPlannerRegistry(
    IEnumerable<IDebugExecutionTargetPlanner> planners)
{
    private readonly IReadOnlyList<IDebugExecutionTargetPlanner> _planners =
        (planners ?? throw new ArgumentNullException(nameof(planners)))
        .OrderByDescending(planner => planner.Priority)
        .ThenBy(planner => planner.Id, StringComparer.Ordinal)
        .ToArray();

    public async ValueTask<DebugExecutionPlan> PlanAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var matches = new List<(IDebugExecutionTargetPlanner Planner, DebugExecutionPlan Plan)>();
        foreach (var planner in _planners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await planner.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.MatchKind == DebugPlannerMatchKind.Applicable)
                matches.Add((planner, result.Plan ??
                    throw new InvalidOperationException($"Planner '{planner.Id}' returned no plan.")));
        }

        if (matches.Count == 0)
            throw new DebugStartPlanningException(
                "debug_target_unsupported",
                "No trusted execution planner supports the requested target.");
        var winningPriority = matches.Max(match => match.Planner.Priority);
        var winners = matches.Where(match => match.Planner.Priority == winningPriority).ToArray();
        if (winners.Length != 1)
            throw new DebugStartPlanningException(
                "debug_planner_ambiguous",
                "Multiple execution planners claim the requested target.");
        return winners[0].Plan;
    }
}

/// <summary>Inert semantic execution plan created before any long-lived resource starts.</summary>
internal abstract record DebugExecutionPlan
{
    public required string PlannerId { get; init; }
    public required DebugSemanticStartKind SemanticStartKind { get; init; }
    public required string EnvironmentId { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required string CanonicalWorkingDirectory { get; init; }
    public required DebugInitialConfiguration InitialConfiguration { get; init; }
    public DebugProjectEvaluationMetadata? ProjectEvaluation { get; init; }
}

/// <summary>Bounded safe metadata describing an inert semantic execution plan.</summary>
/// <param name="PlannerId">Trusted planner identity.</param>
/// <param name="SemanticStartKind">Public semantic execution shape.</param>
/// <param name="TargetKind">Bound public target record name.</param>
/// <param name="WorkingDirectoryIdentity">Safe working-directory leaf identity.</param>
public sealed record DebugExecutionPlanMetadata(
    string PlannerId,
    DebugSemanticStartKind SemanticStartKind,
    string TargetKind,
    string WorkingDirectoryIdentity);

/// <summary>Bounded safe metadata describing an activated adapter execution.</summary>
/// <param name="SemanticStartKind">Public semantic execution shape.</param>
/// <param name="AdapterStartMethod">Actual adapter protocol method.</param>
/// <param name="AdapterId">Trusted adapter identity.</param>
/// <param name="OwnedResourceCount">Resources transferred to the debug tree.</param>
public sealed record DebugExecutionActivationMetadata(
    DebugSemanticStartKind SemanticStartKind,
    DebugAdapterStartMethod AdapterStartMethod,
    string AdapterId,
    int OwnedResourceCount);

/// <summary>Bounded safe metadata from trusted project evaluation.</summary>
/// <param name="ProjectKind">Evaluated project classification.</param>
/// <param name="TestPlatform">Evaluated test platform classification.</param>
/// <param name="SelectedTargetFramework">Exact selected target framework.</param>
/// <param name="ArtifactIdentity">Safe artifact file identity.</param>
/// <param name="EvaluationFingerprint">Stable evaluation fingerprint.</param>
/// <param name="ArtifactIsCurrent">Whether the exact artifact and dependencies are current.</param>
public sealed record DebugProjectEvaluationMetadata(
    string ProjectKind,
    string TestPlatform,
    string SelectedTargetFramework,
    string ArtifactIdentity,
    string EvaluationFingerprint,
    bool ArtifactIsCurrent);

/// <summary>Execution plan whose adapter can directly launch the target.</summary>
internal sealed record DirectAdapterDebugExecutionPlan : DebugExecutionPlan
{
    public required DebugAdapterStartPlan Adapter { get; init; }
}

/// <summary>Trusted specification for a tree-owned prerequisite host process.</summary>
internal sealed record DebugHostProcessPlan
{
    public required string Role { get; init; }
    public required ProcessInvocationSpec Invocation { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaximumStdoutBytes { get; init; } = 16 * 1024;
    public int MaximumStderrBytes { get; init; } = 16 * 1024;
    public bool StopProcessTree { get; init; } = true;
}

/// <summary>Controls how many readiness identities a host strategy may accept.</summary>
internal enum DebugReadinessMultiplicity
{
    ExactlyOne,
    FirstOwnedMatch
}

/// <summary>Trusted readiness grammar for a hosted execution strategy.</summary>
internal sealed record DebugHostReadinessPlan
{
    public required string ProtocolId { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumObservationBytes { get; init; } = 32 * 1024;
    public DebugReadinessMultiplicity Multiplicity { get; init; } =
        DebugReadinessMultiplicity.ExactlyOne;
}

/// <summary>Data needed to create a trusted adapter attach plan after host readiness.</summary>
internal sealed record DebugDeferredAttachPlan
{
    public required string AdapterId { get; init; }
    public required DebugAdapterResolutionContext Resolution { get; init; }
    public required string WorkingDirectory { get; init; }
}

/// <summary>Execution plan that owns a host and attaches the adapter to its reported debuggee.</summary>
internal sealed record HostedAttachDebugExecutionPlan : DebugExecutionPlan
{
    public required DebugHostProcessPlan Host { get; init; }
    public required DebugHostReadinessPlan Readiness { get; init; }
    public required DebugDeferredAttachPlan Attach { get; init; }
}

/// <summary>Trusted bounded foreground work required before an adapter launch.</summary>
internal sealed record DebugPreparationPlan
{
    public required string Role { get; init; }
    public required ProcessInvocationSpec Invocation { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public int MaximumStdoutBytes { get; init; } = 16 * 1024;
    public int MaximumStderrBytes { get; init; } = 16 * 1024;
    public required string ExpectedOutputPath { get; init; }
    public bool OwnsExpectedOutput { get; init; }
}

/// <summary>Data needed to produce an adapter launch after preparation succeeds.</summary>
internal sealed record DebugDeferredLaunchPlan
{
    public required string AdapterId { get; init; }
    public required DebugAdapterResolutionContext Resolution { get; init; }
    public required string WorkingDirectory { get; init; }
    public DebugTargetKind TargetKind { get; init; } = DebugTargetKind.Executable;
    public DebugAdapterProgramKind ProgramKind { get; init; } =
        DebugAdapterProgramKind.ExecutableFile;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public bool StopOnEntry { get; init; }
}

/// <summary>Execution plan that prepares one exact artifact before launching it.</summary>
internal sealed record PreparedAdapterDebugExecutionPlan : DebugExecutionPlan
{
    public required DebugPreparationPlan Preparation { get; init; }
    public required DebugDeferredLaunchPlan Launch { get; init; }
}

/// <summary>A resource whose lifetime is owned by a debug tree.</summary>
internal interface IDebugOwnedResource : IAsyncDisposable
{
    string Kind { get; }
    string SafeIdentity { get; }
    ValueTask StopAsync(string reason, CancellationToken cancellationToken);
}

/// <summary>Bounded non-secret output retained from an owned host process.</summary>
internal sealed record DebugOwnedProcessOutputSnapshot(
    string Stdout,
    string Stderr,
    long RetainedBytes,
    long DroppedBytes);

/// <summary>Owns one runner or prerequisite process without exposing its command to the model.</summary>
internal sealed class DebugOwnedProcessResource(
    IProcessInvocationHandle process,
    string role,
    TimeSpan stopTimeout,
    Func<ValueTask>? cleanupFailure = null) : IDebugOwnedResource
{
    private readonly IProcessInvocationHandle _process =
        process ?? throw new ArgumentNullException(nameof(process));
    private readonly TimeSpan _stopTimeout = stopTimeout;
    private int _stopped;
    private int _disposed;
    private int _cleanupFailureReported;
    private readonly object _outputGate = new();
    private Task? _observation;
    private byte[] _stdout = [];
    private byte[] _stderr = [];
    private long _droppedBytes;

    public string Kind => "process";
    public string SafeIdentity { get; } =
        string.IsNullOrWhiteSpace(role) ? "debug-host" : role;
    internal IProcessInvocationHandle Process => _process;

    internal DebugOwnedProcessOutputSnapshot OutputSnapshot
    {
        get
        {
            lock (_outputGate)
                return new(
                    Encoding.UTF8.GetString(_stdout),
                    Encoding.UTF8.GetString(_stderr),
                    _stdout.Length + _stderr.Length,
                    _droppedBytes);
        }
    }

    internal void BeginObservation(
        int maximumStdoutBytes,
        int maximumStderrBytes,
        Func<ProcessInvocationResult, ValueTask>? exited = null)
    {
        if (maximumStdoutBytes <= 0 || maximumStderrBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStdoutBytes));
        lock (_outputGate)
        {
            if (_observation is not null)
                throw new InvalidOperationException(
                    "Owned process observation has already started.");
            _observation = ObserveAsync(
                maximumStdoutBytes,
                maximumStderrBytes,
                exited);
        }
    }

    public async ValueTask StopAsync(string reason, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_stopTimeout);
        await _process.StopAsync(
            new ProcessStopRequest(StopKind.GracefulThenKill, reason, _stopTimeout),
            timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            await StopAsync("debug tree disposed", CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await ReportCleanupFailureAsync().ConfigureAwait(false);
        }
        Task? observation;
        lock (_outputGate)
            observation = _observation;
        if (observation is not null)
        {
            try
            {
                await observation.WaitAsync(_stopTimeout).ConfigureAwait(false);
            }
            catch
            {
                await ReportCleanupFailureAsync().ConfigureAwait(false);
            }
        }
        try
        {
            await _process.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            await ReportCleanupFailureAsync().ConfigureAwait(false);
        }
    }

    private async Task ObserveAsync(
        int maximumStdoutBytes,
        int maximumStderrBytes,
        Func<ProcessInvocationResult, ValueTask>? exited)
    {
        try
        {
            await foreach (var chunk in _process.ReadOutputAsync(CancellationToken.None)
                               .ConfigureAwait(false))
            {
                lock (_outputGate)
                {
                    if (chunk.Stream == ProcessOutputStream.Stdout)
                        AppendTail(ref _stdout, chunk.Bytes.Span, maximumStdoutBytes);
                    else if (chunk.Stream == ProcessOutputStream.Stderr)
                        AppendTail(ref _stderr, chunk.Bytes.Span, maximumStderrBytes);
                }
            }
            var result = await _process.WaitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (exited is not null)
                try { await exited(result).ConfigureAwait(false); } catch { }
        }
        catch
        {
            // Cleanup and the protocol tree remain authoritative. Observation
            // failures are reported through cleanup diagnostics on disposal.
        }
    }

    private void AppendTail(
        ref byte[] retained,
        ReadOnlySpan<byte> incoming,
        int maximumBytes)
    {
        if (incoming.IsEmpty)
            return;
        var combinedLength = retained.Length + incoming.Length;
        if (combinedLength <= maximumBytes)
        {
            var combined = new byte[combinedLength];
            retained.CopyTo(combined, 0);
            incoming.CopyTo(combined.AsSpan(retained.Length));
            retained = combined;
            return;
        }
        var dropped = combinedLength - maximumBytes;
        _droppedBytes += dropped;
        var tail = new byte[maximumBytes];
        if (incoming.Length >= maximumBytes)
            incoming[^maximumBytes..].CopyTo(tail);
        else
        {
            var retainedCount = maximumBytes - incoming.Length;
            retained.AsSpan(retained.Length - retainedCount).CopyTo(tail);
            incoming.CopyTo(tail.AsSpan(retainedCount));
        }
        retained = tail;
    }

    private async ValueTask ReportCleanupFailureAsync()
    {
        if (cleanupFailure is null ||
            Interlocked.Exchange(ref _cleanupFailureReported, 1) != 0)
            return;
        try { await cleanupFailure().ConfigureAwait(false); } catch { }
    }
}

/// <summary>Owns one trusted preparation output explicitly marked for cleanup.</summary>
internal sealed class DebugOwnedFileResource(string path) : IDebugOwnedResource
{
    private readonly string _path = Path.GetFullPath(
        string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("An owned file path is required.", nameof(path))
            : path);
    private int _disposed;

    public string Kind => "file";
    public string SafeIdentity => Path.GetFileName(_path);

    public ValueTask StopAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && File.Exists(_path))
            File.Delete(_path);
        return ValueTask.CompletedTask;
    }
}
