using HPD.Agent.Authority;

namespace HPD.Agent.Runtime;

/// <summary>Describes the closed operational state of one runtime participant coordinator.</summary>
public enum RuntimeParticipantCoordinatorStateV1 : ushort
{
    /// <summary>No participant has been prepared.</summary>
    Created = 1,
    /// <summary>Every participant has been prepared but none is currently being started.</summary>
    Prepared = 2,
    /// <summary>Every participant has started in dependency order.</summary>
    Started = 3,
    /// <summary>New admission is closed and participants are draining.</summary>
    Draining = 4,
    /// <summary>Participants are converging and releasing resources.</summary>
    Terminating = 5,
    /// <summary>All participants have been terminated and disposed.</summary>
    Completed = 6,
    /// <summary>A cancelled or timed-out operation is still converging and cannot safely overlap cleanup.</summary>
    Quarantined = 7,
}

/// <summary>Binds one plan descriptor to its S1-allocated participant context.</summary>
public readonly record struct RuntimeParticipantAdmissionV1
{
    /// <summary>Initializes a validated participant admission.</summary>
    /// <param name="descriptorId">The plan-local descriptor identity.</param>
    /// <param name="context">The S1-allocated participant identity and authority fences.</param>
    /// <exception cref="ArgumentException">The descriptor identity or context is invalid.</exception>
    public RuntimeParticipantAdmissionV1(BoundedAscii descriptorId, RuntimeParticipantContextV1 context)
    {
        if (!descriptorId.IsValid) throw new ArgumentException("A participant descriptor identity is required.", nameof(descriptorId));
        if (!context.IsValid) throw new ArgumentException("A valid participant context is required.", nameof(context));
        DescriptorId = descriptorId;
        Context = context;
    }

    /// <summary>Gets the plan-local descriptor identity.</summary>
    public BoundedAscii DescriptorId { get; }

    /// <summary>Gets the S1-allocated participant identity and authority fences.</summary>
    public RuntimeParticipantContextV1 Context { get; }

    /// <summary>Gets whether both the descriptor identity and context are valid.</summary>
    public bool IsValid => DescriptorId.IsValid && Context.IsValid;
}

/// <summary>
/// Coordinates a bounded set of neutral runtime participants without becoming an authority or effect owner.
/// </summary>
/// <remarks>
/// The coordinator preserves plan order, enforces each descriptor's lifecycle bound, and unwinds in reverse
/// dependency order. Its state is operational and cannot replace S1 admission facts or owner receipts.
/// </remarks>
public sealed class RuntimeParticipantCoordinatorV1 : IAsyncDisposable
{
    private static readonly RuntimeParticipantResultV1 CompletedResult =
        new(RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("Completed"));

    private readonly RuntimeParticipantPlanV1 _plan;
    private readonly IReadOnlyDictionary<string, IRuntimeParticipantV1> _participants;
    private readonly Dictionary<string, RuntimePreparedHandleV1> _handles = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RuntimeParticipantCoordinatorStateV1 _state = RuntimeParticipantCoordinatorStateV1.Created;
    private Task? _quarantineCompletion;
    private bool _disposeRequested;

    /// <summary>Initializes a coordinator whose participants exactly match a compiled plan.</summary>
    /// <param name="plan">The immutable dependency plan.</param>
    /// <param name="participants">One participant for every descriptor in the plan.</param>
    /// <exception cref="ArgumentNullException">A parameter or participant is null.</exception>
    /// <exception cref="ArgumentException">Participant identifiers are duplicated or do not exactly match the plan.</exception>
    public RuntimeParticipantCoordinatorV1(RuntimeParticipantPlanV1 plan, IEnumerable<IRuntimeParticipantV1> participants)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(participants);
        var byId = new Dictionary<string, IRuntimeParticipantV1>(StringComparer.Ordinal);
        foreach (var participant in participants)
        {
            ArgumentNullException.ThrowIfNull(participant);
            if (byId.Count == plan.OrderedDescriptors.Count)
                throw new ArgumentException("Participants exceed the compiled plan bound.", nameof(participants));
            if (!byId.TryAdd(participant.Descriptor.Id.ToString(), participant))
                throw new ArgumentException("A runtime participant identifier is duplicated.", nameof(participants));
        }
        if (byId.Count != plan.OrderedDescriptors.Count ||
            plan.OrderedDescriptors.Any(descriptor => !byId.TryGetValue(descriptor.Id.ToString(), out var participant) ||
                !ReferenceEquals(descriptor, participant.Descriptor)))
            throw new ArgumentException("Participants must expose the exact descriptors used to compile the plan.", nameof(participants));
        _participants = byId;
    }

    /// <summary>Gets the current operational coordinator state.</summary>
    public RuntimeParticipantCoordinatorStateV1 State => _state;

    /// <summary>Prepares every admitted participant in dependency order.</summary>
    /// <param name="admissions">Exactly one authority-fenced context for every plan descriptor.</param>
    /// <param name="cancellationToken">Cancels bounded preparation and triggers reverse-order unwind.</param>
    /// <returns>The first nonsuccess result, or a successful prepared result.</returns>
    public async ValueTask<RuntimeParticipantResultV1> PrepareAsync(
        IEnumerable<RuntimeParticipantAdmissionV1> admissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureState(RuntimeParticipantCoordinatorStateV1.Created);
            var contexts = ValidateAdmissions(admissions);
            var attempted = new List<IRuntimeParticipantV1>();
            foreach (var descriptor in _plan.OrderedDescriptors)
            {
                var participant = _participants[descriptor.Id.ToString()];
                attempted.Add(participant);
                var invocation = await InvokePrepareAsync(participant, contexts[descriptor.Id.ToString()], descriptor.MaxPrepare, cancellationToken).ConfigureAwait(false);
                var result = invocation.Result;
                if (invocation.Outstanding is not null)
                {
                    EnterQuarantine(
                        invocation.Outstanding,
                        CauseFor(result.Disposition, RuntimeTerminationCauseV1.PrepareFailed),
                        attempted.AsEnumerable().Reverse().ToArray());
                    return new RuntimeParticipantResultV1(result.Disposition, result.Code);
                }
                if (!result.IsValid)
                    result = new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("InvalidPrepareResult"), null);
                if (!result.IsSuccess)
                {
                    await UnwindAsync(attempted, CauseFor(result.Disposition, RuntimeTerminationCauseV1.PrepareFailed)).ConfigureAwait(false);
                    return new RuntimeParticipantResultV1(result.Disposition, result.Code);
                }
                var handle = result.Handle!;
                if (handle.DescriptorId != descriptor.Id || handle.Context != contexts[descriptor.Id.ToString()])
                {
                    await UnwindAsync(attempted, RuntimeTerminationCauseV1.PrepareFailed).ConfigureAwait(false);
                    return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("PreparedHandleMismatch"));
                }
                _handles.Add(descriptor.Id.ToString(), handle);
            }
            _state = RuntimeParticipantCoordinatorStateV1.Prepared;
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("Prepared"));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts every prepared participant in dependency order.</summary>
    /// <param name="cancellationToken">Cancels bounded start and triggers reverse-order unwind.</param>
    /// <returns>The first nonsuccess result, or a successful started result.</returns>
    public async ValueTask<RuntimeParticipantResultV1> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureState(RuntimeParticipantCoordinatorStateV1.Prepared);
            foreach (var descriptor in _plan.OrderedDescriptors)
            {
                var participant = _participants[descriptor.Id.ToString()];
                var invocation = await InvokeAsync(
                    token => participant.StartAsync(_handles[descriptor.Id.ToString()], token), descriptor.MaxStart, cancellationToken).ConfigureAwait(false);
                var result = invocation.Result;
                if (invocation.Outstanding is not null)
                {
                    EnterQuarantine(
                        invocation.Outstanding,
                        CauseFor(result.Disposition, RuntimeTerminationCauseV1.StartFailed),
                        ParticipantsInPlanOrder().Reverse().ToArray());
                    return result;
                }
                if (!result.IsValid)
                    result = new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("InvalidStartResult"));
                if (!result.IsSuccess)
                {
                    await UnwindAsync(ParticipantsInPlanOrder(), CauseFor(result.Disposition, RuntimeTerminationCauseV1.StartFailed)).ConfigureAwait(false);
                    return result;
                }
            }
            _state = RuntimeParticipantCoordinatorStateV1.Started;
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("Started"));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes admission and drains participants in reverse dependency order.</summary>
    /// <param name="intent">The graceful or forced convergence intent.</param>
    /// <param name="cancellationToken">Cancels bounded drain and triggers termination.</param>
    /// <returns>The first nonsuccess result, or a successful drained result.</returns>
    public async ValueTask<RuntimeParticipantResultV1> DrainAsync(
        RuntimeDrainIntentV1 intent,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(intent)) throw new ArgumentException("The drain intent is outside the closed registry.", nameof(intent));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureState(RuntimeParticipantCoordinatorStateV1.Started);
            _state = RuntimeParticipantCoordinatorStateV1.Draining;
            foreach (var descriptor in _plan.OrderedDescriptors.Reverse())
            {
                var participant = _participants[descriptor.Id.ToString()];
                var invocation = await InvokeAsync(token => participant.DrainAsync(intent, token), descriptor.MaxDrain, cancellationToken).ConfigureAwait(false);
                var result = invocation.Result;
                if (invocation.Outstanding is not null)
                {
                    EnterQuarantine(
                        invocation.Outstanding,
                        CauseFor(result.Disposition, RuntimeTerminationCauseV1.DrainFailed),
                        ParticipantsInPlanOrder().Reverse().ToArray());
                    return result;
                }
                if (!result.IsValid)
                    result = new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("InvalidDrainResult"));
                if (!result.IsSuccess)
                {
                    await UnwindAsync(ParticipantsInPlanOrder(), CauseFor(result.Disposition, RuntimeTerminationCauseV1.DrainFailed)).ConfigureAwait(false);
                    return result;
                }
            }
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("Drained"));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Terminates and disposes participants in reverse dependency order.</summary>
    /// <param name="cause">The qualified termination cause.</param>
    /// <param name="cancellationToken">Cancels the caller's wait; participant bounds still constrain cleanup.</param>
    /// <returns>The first nonsuccess result while still attempting every participant.</returns>
    public async ValueTask<RuntimeParticipantResultV1> TerminateAsync(
        RuntimeTerminationCauseV1 cause,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(cause)) throw new ArgumentException("The termination cause is outside the closed registry.", nameof(cause));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task? quarantine = null;
        try
        {
            if (_state == RuntimeParticipantCoordinatorStateV1.Quarantined)
            {
                quarantine = _quarantineCompletion ?? throw new InvalidOperationException("Quarantine completion is unavailable.");
            }
            else
            {
                if (_state == RuntimeParticipantCoordinatorStateV1.Completed)
                    return CompletedResult;
                if (_state == RuntimeParticipantCoordinatorStateV1.Created)
                    throw new InvalidOperationException("An unprepared coordinator has no admitted participant resources to terminate.");
                return await UnwindAsync(ParticipantsInPlanOrder(), cause).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
        await quarantine.WaitAsync(cancellationToken).ConfigureAwait(false);
        return CompletedResult;
    }

    /// <summary>Terminates admitted resources, if any; quarantined work completes cleanup after it converges.</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Task? quarantine = null;
        try
        {
            if (_disposeRequested)
                return;
            _disposeRequested = true;
            if (_state == RuntimeParticipantCoordinatorStateV1.Quarantined)
                quarantine = _quarantineCompletion ?? throw new InvalidOperationException("Quarantine completion is unavailable.");
            else if (_state is not RuntimeParticipantCoordinatorStateV1.Created and not RuntimeParticipantCoordinatorStateV1.Completed)
                await UnwindAsync(ParticipantsInPlanOrder(), RuntimeTerminationCauseV1.HostFault).ConfigureAwait(false);
            else if (_state == RuntimeParticipantCoordinatorStateV1.Created)
            {
                await DisposeParticipantsAsync().ConfigureAwait(false);
                _state = RuntimeParticipantCoordinatorStateV1.Completed;
            }
        }
        finally
        {
            _gate.Release();
        }
        if (quarantine is not null)
            await quarantine.ConfigureAwait(false);
    }

    private Dictionary<string, RuntimeParticipantContextV1> ValidateAdmissions(IEnumerable<RuntimeParticipantAdmissionV1> admissions)
    {
        var contexts = new Dictionary<string, RuntimeParticipantContextV1>(StringComparer.Ordinal);
        var participantIds = new HashSet<ParticipantId>();
        foreach (var admission in admissions)
        {
            if (contexts.Count == _participants.Count)
                throw new ArgumentException("Participant admissions exceed the compiled plan bound.", nameof(admissions));
            if (!admission.IsValid || !contexts.TryAdd(admission.DescriptorId.ToString(), admission.Context) ||
                !participantIds.Add(admission.Context.ParticipantId))
                throw new ArgumentException("Participant admissions must be valid and unique.", nameof(admissions));
        }
        if (contexts.Count != _participants.Count || _plan.OrderedDescriptors.Any(descriptor => !contexts.ContainsKey(descriptor.Id.ToString())))
            throw new ArgumentException("Participant admissions must exactly match the compiled plan.", nameof(admissions));
        return contexts;
    }

    private IReadOnlyList<IRuntimeParticipantV1> ParticipantsInPlanOrder() =>
        _plan.OrderedDescriptors.Select(descriptor => _participants[descriptor.Id.ToString()]).ToArray();

    private async ValueTask<RuntimeParticipantResultV1> UnwindAsync(
        IEnumerable<IRuntimeParticipantV1> participants,
        RuntimeTerminationCauseV1 cause)
    {
        _state = RuntimeParticipantCoordinatorStateV1.Terminating;
        return await ContinueUnwindAsync(participants.Reverse().ToArray(), cause).ConfigureAwait(false);
    }

    private async ValueTask<RuntimeParticipantResultV1> ContinueUnwindAsync(
        IReadOnlyList<IRuntimeParticipantV1> terminationOrder,
        RuntimeTerminationCauseV1 cause)
    {
        RuntimeParticipantResultV1? firstFailure = null;
        for (var index = 0; index < terminationOrder.Count; index++)
        {
            var participant = terminationOrder[index];
            var invocation = await InvokeAsync(
                token => participant.TerminateAsync(cause, token), participant.Descriptor.MaxTerminate, CancellationToken.None).ConfigureAwait(false);
            var result = invocation.Result;
            if (invocation.Outstanding is not null)
            {
                EnterQuarantine(
                    invocation.Outstanding,
                    RuntimeTerminationCauseV1.TimedOut,
                    terminationOrder.Skip(index + 1).ToArray());
                return result;
            }
            if (!result.IsValid)
                result = new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("InvalidTerminateResult"));
            if (!result.IsSuccess && firstFailure is null) firstFailure = result;
        }
        await DisposeParticipantsAsync().ConfigureAwait(false);
        _handles.Clear();
        _state = RuntimeParticipantCoordinatorStateV1.Completed;
        return firstFailure ?? CompletedResult;
    }

    private async ValueTask DisposeParticipantsAsync()
    {
        foreach (var participant in ParticipantsInPlanOrder().Reverse())
        {
            try
            {
                await participant.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception) { }
        }
    }

    private static async ValueTask<BoundedInvocation<RuntimeParticipantPrepareResultV1>> InvokePrepareAsync(
        IRuntimeParticipantV1 participant,
        RuntimeParticipantContextV1 context,
        DurationNs duration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToTimeSpan(duration));
        Task<RuntimeParticipantPrepareResultV1>? task = null;
        try
        {
            task = participant.PrepareAsync(context, timeout.Token).AsTask();
            try
            {
                return new(await task.WaitAsync(ToTimeSpan(duration), cancellationToken).ConfigureAwait(false), null);
            }
            catch (TimeoutException)
            {
                timeout.Cancel();
                return new(new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("PrepareTimedOut"), null), task.IsCompleted ? null : task);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timeout.Cancel();
            return new(new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.Cancelled, new BoundedAscii("PrepareCancelled"), null), task is { IsCompleted: false } ? task : null);
        }
        catch (OperationCanceledException)
        {
            return new(new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("PrepareTimedOut"), null), null);
        }
        catch (Exception)
        {
            return new(new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("PrepareFault"), null), null);
        }
    }

    private static async ValueTask<BoundedInvocation<RuntimeParticipantResultV1>> InvokeAsync(
        Func<CancellationToken, ValueTask<RuntimeParticipantResultV1>> operation,
        DurationNs duration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToTimeSpan(duration));
        Task<RuntimeParticipantResultV1>? task = null;
        try
        {
            task = operation(timeout.Token).AsTask();
            try
            {
                return new(await task.WaitAsync(ToTimeSpan(duration), cancellationToken).ConfigureAwait(false), null);
            }
            catch (TimeoutException)
            {
                timeout.Cancel();
                return new(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("ParticipantTimedOut")), task.IsCompleted ? null : task);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timeout.Cancel();
            return new(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Cancelled, new BoundedAscii("ParticipantCancelled")), task is { IsCompleted: false } ? task : null);
        }
        catch (OperationCanceledException)
        {
            return new(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("ParticipantTimedOut")), null);
        }
        catch (Exception)
        {
            return new(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("ParticipantFault")), null);
        }
    }

    private static TimeSpan ToTimeSpan(DurationNs duration)
    {
        var ticks = duration.Nanoseconds / TimeSpan.NanosecondsPerTick;
        if (duration.Nanoseconds % TimeSpan.NanosecondsPerTick != 0) ticks++;
        ticks = Math.Max(1, ticks);
        return TimeSpan.FromTicks(ticks);
    }

    private static RuntimeTerminationCauseV1 CauseFor(RuntimeParticipantDispositionV1 disposition, RuntimeTerminationCauseV1 fallback) =>
        disposition switch
        {
            RuntimeParticipantDispositionV1.Cancelled => RuntimeTerminationCauseV1.Cancelled,
            RuntimeParticipantDispositionV1.TimedOut => RuntimeTerminationCauseV1.TimedOut,
            _ => fallback,
        };

    private void EnsureState(RuntimeParticipantCoordinatorStateV1 expected)
    {
        if (_state != expected)
            throw new InvalidOperationException($"The coordinator is {_state} and requires {expected}.");
    }

    private void EnterQuarantine(
        Task outstanding,
        RuntimeTerminationCauseV1 cause,
        IReadOnlyList<IRuntimeParticipantV1> remainingTerminationOrder)
    {
        _state = RuntimeParticipantCoordinatorStateV1.Quarantined;
        _quarantineCompletion = CompleteQuarantineAsync(outstanding, cause, remainingTerminationOrder);
    }

    private async Task CompleteQuarantineAsync(
        Task outstanding,
        RuntimeTerminationCauseV1 cause,
        IReadOnlyList<IRuntimeParticipantV1> remainingTerminationOrder)
    {
        try { await outstanding.ConfigureAwait(false); }
        catch (Exception) { }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == RuntimeParticipantCoordinatorStateV1.Quarantined)
            {
                _state = RuntimeParticipantCoordinatorStateV1.Terminating;
                await ContinueUnwindAsync(remainingTerminationOrder, cause).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private readonly record struct BoundedInvocation<T>(T Result, Task? Outstanding);
}
