using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Requests exclusive in-process ownership of one durable thread execution.</summary>
public sealed record ThreadExecutionStartRequest(
    ThreadKey Thread,
    string ThreadExecutionId,
    Agent Agent);

/// <summary>Represents an acquired exclusive thread execution.</summary>
public sealed record ThreadExecutionLease(
    ThreadKey Thread,
    string ThreadExecutionId,
    Agent Agent,
    long Fence);

/// <summary>Reports whether execution ownership was acquired.</summary>
public sealed record ThreadExecutionLeaseResult(
    bool Acquired,
    ThreadExecutionLease? Lease,
    string? ActiveThreadExecutionId);

/// <summary>Describes the active execution for one thread.</summary>
public sealed record ActiveThreadExecutionLookup(
    bool IsActive,
    string? ThreadExecutionId);

/// <summary>Reports exact-execution steering.</summary>
public sealed record ThreadExecutionSteerResult(
    bool Accepted,
    string? ActiveThreadExecutionId,
    AgentInputDisposition Disposition);

/// <summary>Reports exact-execution cancellation.</summary>
public sealed record ThreadExecutionCancelResult(
    bool Accepted,
    string? ActiveThreadExecutionId,
    AgentInputDisposition Disposition);

/// <summary>Describes the terminal state used to release execution authority.</summary>
public sealed record ThreadExecutionTerminalResult(
    ThreadExecutionOutcome Outcome,
    string? ErrorType = null,
    string? ErrorMessage = null);

/// <summary>One cursor-addressed execution observation.</summary>
public sealed record ThreadExecutionObservation(
    ThreadJournalCursor Cursor,
    string ThreadExecutionId,
    string Status,
    AgentEvent Event);

/// <summary>Owns one active execution per durable <see cref="ThreadKey"/>.</summary>
public interface IThreadExecutionController
{
    /// <summary>Attempts to acquire exclusive execution authority.</summary>
    ValueTask<ThreadExecutionLeaseResult> TryAcquireAsync(
        ThreadExecutionStartRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Finds the active execution without acquiring it.</summary>
    ValueTask<ActiveThreadExecutionLookup> FindActiveAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);
    /// <summary>Steers only the exact active execution.</summary>
    ValueTask<ThreadExecutionSteerResult> SteerAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        UserMessagesInputEvent input,
        CancellationToken cancellationToken = default);
    /// <summary>Cancels only the exact active execution.</summary>
    ValueTask<ThreadExecutionCancelResult> CancelAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        string? reason,
        CancellationToken cancellationToken = default);
    /// <summary>Idempotently releases an acquired execution.</summary>
    ValueTask ReleaseAsync(
        ThreadExecutionLease lease,
        ThreadExecutionTerminalResult terminal,
        CancellationToken cancellationToken = default);
    /// <summary>Observes the exact execution from a journal cursor.</summary>
    IAsyncEnumerable<ThreadExecutionObservation> ObserveAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        ThreadJournalCursor from,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves one shared controller for every agent using the same store instance.</summary>
internal static class ThreadExecutionControllerRegistry
{
    private static readonly ConditionalWeakTable<ISessionStore, InProcessThreadExecutionController> Controllers = new();

    internal static IThreadExecutionController For(ISessionStore store) =>
        Controllers.GetValue(store, static authority => new InProcessThreadExecutionController(authority));
}

internal sealed class InProcessThreadExecutionController(ISessionStore store) : IThreadExecutionController
{
    private readonly ISessionStore _store = store;
    private readonly ConcurrentDictionary<ThreadKey, ThreadExecutionLease> _active = new();
    private long _fence;

    public ValueTask<ThreadExecutionLeaseResult> TryAcquireAsync(
        ThreadExecutionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var lease = new ThreadExecutionLease(
            request.Thread,
            request.ThreadExecutionId,
            request.Agent,
            Interlocked.Increment(ref _fence));
        if (_active.TryAdd(request.Thread, lease))
            return ValueTask.FromResult(new ThreadExecutionLeaseResult(true, lease, null));
        var current = _active.GetValueOrDefault(request.Thread);
        return ValueTask.FromResult(new ThreadExecutionLeaseResult(
            false,
            null,
            current?.ThreadExecutionId));
    }

    public ValueTask<ActiveThreadExecutionLookup> FindActiveAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_active.TryGetValue(thread, out var active)
            ? new ActiveThreadExecutionLookup(true, active.ThreadExecutionId)
            : new ActiveThreadExecutionLookup(false, null));
    }

    public async ValueTask<ThreadExecutionSteerResult> SteerAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        UserMessagesInputEvent input,
        CancellationToken cancellationToken = default)
    {
        if (!_active.TryGetValue(thread, out var active))
            return new(false, null, AgentInputDisposition.NoActiveExecution);
        if (!string.Equals(active.ThreadExecutionId, expectedThreadExecutionId, StringComparison.Ordinal))
            return new(false, active.ThreadExecutionId, AgentInputDisposition.ActiveExecutionMismatch);
        var result = await active.Agent.RunAsync(input with
        {
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId,
            ThreadExecutionId = expectedThreadExecutionId,
            Delivery = AgentInputDelivery.Steer
        }, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            AgentInputResult.Steered => new(true, expectedThreadExecutionId, AgentInputDisposition.Accepted),
            AgentInputResult.Control control => new(false, control.ThreadExecutionId, control.Disposition),
            _ => new(false, expectedThreadExecutionId, AgentInputDisposition.ActiveInputNotSteerable)
        };
    }

    public ValueTask<ThreadExecutionCancelResult> CancelAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_active.TryGetValue(thread, out var active))
            return ValueTask.FromResult(new ThreadExecutionCancelResult(false, null, AgentInputDisposition.NoActiveExecution));
        if (!string.Equals(active.ThreadExecutionId, expectedThreadExecutionId, StringComparison.Ordinal))
            return ValueTask.FromResult(new ThreadExecutionCancelResult(false, active.ThreadExecutionId, AgentInputDisposition.ActiveExecutionMismatch));
        var result = active.Agent.CancelRuntimeExecution(expectedThreadExecutionId);
        return ValueTask.FromResult(new ThreadExecutionCancelResult(
            result.Disposition == AgentInputDisposition.Accepted,
            result.ThreadExecutionId,
            result.Disposition));
    }

    public ValueTask ReleaseAsync(
        ThreadExecutionLease lease,
        ThreadExecutionTerminalResult terminal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_active.TryGetValue(lease.Thread, out var current) && current.Fence == lease.Fence)
            _active.TryRemove(new KeyValuePair<ThreadKey, ThreadExecutionLease>(lease.Thread, current));
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ThreadExecutionObservation> ObserveAsync(
        ThreadKey thread,
        string expectedThreadExecutionId,
        ThreadJournalCursor from,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in _store.ObserveThreadEventsAsync(
            thread,
            from,
            new ThreadObservationOptions(),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events.Where(evt =>
                         string.Equals(evt.ThreadExecutionId, expectedThreadExecutionId, StringComparison.Ordinal) ||
                         evt is ThreadExecutionStartedEvent started && started.ThreadExecutionId == expectedThreadExecutionId ||
                         evt is ThreadExecutionFinishedEvent finished && finished.ThreadExecutionId == expectedThreadExecutionId))
            {
                var status = evt is ThreadExecutionFinishedEvent terminal
                    ? terminal.Outcome switch
                    {
                        ThreadExecutionOutcome.Succeeded => ThreadExecutionStatus.Succeeded,
                        ThreadExecutionOutcome.Cancelled => ThreadExecutionStatus.Cancelled,
                        _ => ThreadExecutionStatus.Failed
                    }
                    : ThreadExecutionStatus.Active;
                yield return new ThreadExecutionObservation(
                    new ThreadJournalCursor(batch.Generation, evt.ThreadSequenceNumber),
                    expectedThreadExecutionId,
                    status,
                    evt);
                if (evt is ThreadExecutionFinishedEvent) yield break;
            }
        }
    }
}
