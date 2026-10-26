using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Application;

public sealed class AgentTuiSessionState
{
    private const string ProjectionFaultStatusMarker = "projection faults: ";
    private readonly HpdAgentTuiRegistry _registry;
    private readonly AgentTuiStateBag _state = new();
    private readonly Action? _requestRender;
    private readonly HashSet<string> _faultedContributions = new(StringComparer.Ordinal);
    private readonly List<AgentTuiProjectionFailure> _projectionFailures = [];
    private readonly List<AgentTuiProjectionInfrastructureFailure> _projectionInfrastructureFailures = [];
    private readonly Queue<DeferredProjection> _deferredProjections = [];
    private readonly object _deferredProjectionGate = new();
    private readonly SemaphoreSlim _projectionGate = new(1, 1);
    private Task? _deferredProjectionDrain;

    public AgentTuiSessionState(
        AgentTuiRuntimeScope scope,
        HpdAgentTuiRegistry registry,
        Action? requestRender = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _requestRender = requestRender;
        Shell = new ChatShellModel(scope);
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiStateBag State => _state;

    /// <summary>Gets contribution failures contained during this TUI session.</summary>
    public IReadOnlyList<AgentTuiProjectionFailure> ProjectionFailures => _projectionFailures;

    /// <summary>Gets commit or render failures from background deferred projections.</summary>
    public IReadOnlyList<AgentTuiProjectionInfrastructureFailure> ProjectionInfrastructureFailures
    {
        get
        {
            lock (_deferredProjectionGate)
                return _projectionInfrastructureFailures.ToArray();
        }
    }

    public async ValueTask ApplyEventAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default,
        AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live)
    {
        await _projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyEventCoreAsync(evt, cancellationToken, deliveryMode).ConfigureAwait(false);
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    private async ValueTask ApplyEventCoreAsync(
        AgentEvent evt,
        CancellationToken cancellationToken,
        AgentTuiEventDeliveryMode deliveryMode)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();

        var toolArguments = CreateToolArguments(evt);
        foreach (var contribution in _registry.EventHandlers)
        {
            if (_faultedContributions.Contains(contribution.Key) ||
                !contribution.Scope.Includes(evt, Scope))
            {
                continue;
            }

            bool canHandle;
            try
            {
                canHandle = contribution.Value.CanHandle(evt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ContainProjectionFailure(contribution.Key, evt, ex);
                continue;
            }

            if (!canHandle)
                continue;

            await ExecuteProjectionAsync(
                contribution.Key,
                evt,
                deliveryMode,
                toolArguments,
                context => contribution.Value.ProjectAsync(evt, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        UpdateProjectionFaultStatus();
    }

    private async ValueTask ExecuteProjectionAsync(
        string contributionKey,
        AgentEvent sourceEvent,
        AgentTuiEventDeliveryMode deliveryMode,
        ToolCallArguments? toolArguments,
        Func<AgentTuiProjectionContext, ValueTask> projection,
        CancellationToken cancellationToken)
    {
        var context = new AgentTuiProjectionContext(
            Scope,
            Shell,
            _registry,
            _state,
            deliveryMode,
            _requestRender,
            toolArguments,
            key => !_faultedContributions.Contains(key),
            (key, exception) => ContainProjectionFailure(key, sourceEvent, exception),
            next => EnqueueDeferredProjection(
                contributionKey,
                sourceEvent,
                next,
                cancellationToken));
        try
        {
            await projection(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ContainProjectionFailure(contributionKey, sourceEvent, ex);
            return;
        }

        try
        {
            context.Commit();
        }
        catch (Exception ex)
        {
            RecordProjectionInfrastructureFailure(contributionKey, sourceEvent, "commit", ex);
            throw;
        }

        try
        {
            context.FlushDeferredRenderRequest();
        }
        catch (Exception ex)
        {
            RecordProjectionInfrastructureFailure(contributionKey, sourceEvent, "render", ex);
            throw;
        }
    }

    private void EnqueueDeferredProjection(
        string contributionKey,
        AgentEvent sourceEvent,
        Func<AgentTuiProjectionContext, ValueTask> projection,
        CancellationToken cancellationToken)
    {
        lock (_deferredProjectionGate)
        {
            if (_faultedContributions.Contains(contributionKey))
                return;

            _deferredProjections.Enqueue(new(
                contributionKey,
                sourceEvent,
                projection,
                cancellationToken));
            _deferredProjectionDrain ??= Task.Run(DrainDeferredProjectionsAsync);
        }
    }

    private async Task DrainDeferredProjectionsAsync()
    {
        while (true)
        {
            DeferredProjection deferred;
            lock (_deferredProjectionGate)
            {
                if (_deferredProjections.Count == 0)
                {
                    _deferredProjectionDrain = null;
                    return;
                }

                deferred = _deferredProjections.Dequeue();
            }

            try
            {
                await _projectionGate.WaitAsync(deferred.CancellationToken).ConfigureAwait(false);
                try
                {
                    if (_faultedContributions.Contains(deferred.ContributionKey))
                        continue;

                    await ExecuteProjectionAsync(
                        deferred.ContributionKey,
                        deferred.SourceEvent,
                        AgentTuiEventDeliveryMode.Live,
                        CreateToolArguments(deferred.SourceEvent),
                        deferred.Projection,
                        deferred.CancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _projectionGate.Release();
                }
            }
            catch (OperationCanceledException) when (deferred.CancellationToken.IsCancellationRequested)
            {
                // A deferred presentation update has no caller waiting for cancellation.
            }
            catch (Exception)
            {
                // Commit, render, and unexpected infrastructure failures are recorded by
                // ExecuteProjectionAsync; keep the queue running for later projections.
            }
        }
    }

    private void RecordProjectionInfrastructureFailure(
        string contributionKey,
        AgentEvent evt,
        string stage,
        Exception exception)
    {
        lock (_deferredProjectionGate)
        {
            _projectionInfrastructureFailures.Add(new(
                contributionKey,
                evt.GetType().Name,
                stage,
                exception.GetType().Name,
                BoundDiagnostic(exception.Message)));
        }
    }

    private static ToolCallArguments? CreateToolArguments(AgentEvent evt)
        => evt switch
        {
            ToolCallArgsEvent args => ToolCallArguments.Parse(args.ArgsJson),
            ToolCallEndEvent end => ToolCallArguments.Parse(end.ArgsJson),
            _ => null
        };

    private void ContainProjectionFailure(string contributionKey, AgentEvent evt, Exception exception)
    {
        if (!_faultedContributions.Add(contributionKey))
            return;

        var message = BoundDiagnostic(exception.Message);
        var failure = new AgentTuiProjectionFailure(
            contributionKey,
            evt.GetType().Name,
            "handler_exception",
            exception.GetType().Name,
            message);
        _projectionFailures.Add(failure);
        UpdateProjectionFaultStatus();
        Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"projection-contribution-failed-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell(
                "TUI projection contribution failed",
                new Text($"{contributionKey} · {evt.GetType().Name} · {message}"),
                TranscriptSeverity.Error),
            Metadata: new TranscriptEntryMetadata(
                AgentId: Scope.AgentId,
                AgentName: "tui",
                AgentChain: ["tui"])));
    }

    private static string BoundDiagnostic(string message)
    {
        const int maximumLength = 512;
        var normalized = string.Join(
            ' ',
            message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "...";
    }

    private void UpdateProjectionFaultStatus()
    {
        if (_projectionFailures.Count == 0)
            return;

        var current = Shell.PromptStatusText;
        var markerIndex = current.LastIndexOf(ProjectionFaultStatusMarker, StringComparison.Ordinal);
        var baseStatus = markerIndex < 0 ? current : current[..markerIndex].TrimEnd();
        var keys = _projectionFailures
            .Select(static failure => failure.ContributionKey)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var keySummary = string.Join(", ", keys);
        if (_projectionFailures.Select(static failure => failure.ContributionKey)
            .Distinct(StringComparer.Ordinal).Skip(3).Any())
        {
            keySummary += ", ...";
        }

        var status = $"{ProjectionFaultStatusMarker}{_projectionFailures.Count}";
        if (keySummary.Length > 0)
            status += $" ({keySummary})";
        Shell.PromptStatusText = string.IsNullOrWhiteSpace(baseStatus)
            ? status
            : $"{baseStatus} | {status}";
    }
}

/// <summary>Describes one contained TUI contribution failure.</summary>
public sealed record AgentTuiProjectionFailure(
    string ContributionKey,
    string EventType,
    string DiagnosticCode,
    string ExceptionType,
    string Message);

/// <summary>Describes a commit or render failure outside the handler failure boundary.</summary>
public sealed record AgentTuiProjectionInfrastructureFailure(
    string ContributionKey,
    string EventType,
    string Stage,
    string ExceptionType,
    string Message);

internal sealed record DeferredProjection(
    string ContributionKey,
    AgentEvent SourceEvent,
    Func<AgentTuiProjectionContext, ValueTask> Projection,
    CancellationToken CancellationToken);
