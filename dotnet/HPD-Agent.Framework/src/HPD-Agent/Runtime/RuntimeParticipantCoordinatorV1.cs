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
                var result = await InvokePrepareAsync(participant, contexts[descriptor.Id.ToString()], descriptor.MaxPrepare, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await UnwindAsync(attempted, CauseFor(result.Disposition, RuntimeTerminationCauseV1.PrepareFailed)).ConfigureAwait(false);
                    return new RuntimeParticipantResultV1(result.Disposition, result.Code);
                }
                var handle = result.Handle!;
                if (handle.DescriptorId != descriptor.Id || handle.Context.ParticipantId != contexts[descriptor.Id.ToString()].ParticipantId)
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
                var result = await InvokeAsync(
                    token => participant.StartAsync(_handles[descriptor.Id.ToString()], token), descriptor.MaxStart, cancellationToken).ConfigureAwait(false);
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
                var result = await InvokeAsync(token => participant.DrainAsync(intent, token), descriptor.MaxDrain, cancellationToken).ConfigureAwait(false);
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
        try
        {
            if (_state == RuntimeParticipantCoordinatorStateV1.Completed)
                return CompletedResult;
            if (_state == RuntimeParticipantCoordinatorStateV1.Created)
                throw new InvalidOperationException("An unprepared coordinator has no admitted participant resources to terminate.");
            return await UnwindAsync(ParticipantsInPlanOrder(), cause).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Terminates admitted resources, if any, and disposes the coordinator gate.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_state is not RuntimeParticipantCoordinatorStateV1.Created and not RuntimeParticipantCoordinatorStateV1.Completed)
            await TerminateAsync(RuntimeTerminationCauseV1.HostFault).ConfigureAwait(false);
        if (_state == RuntimeParticipantCoordinatorStateV1.Created)
        {
            foreach (var participant in ParticipantsInPlanOrder().Reverse())
                await participant.DisposeAsync().ConfigureAwait(false);
            _state = RuntimeParticipantCoordinatorStateV1.Completed;
        }
        _gate.Dispose();
    }

    private Dictionary<string, RuntimeParticipantContextV1> ValidateAdmissions(IEnumerable<RuntimeParticipantAdmissionV1> admissions)
    {
        var contexts = new Dictionary<string, RuntimeParticipantContextV1>(StringComparer.Ordinal);
        foreach (var admission in admissions)
        {
            if (!admission.IsValid || !contexts.TryAdd(admission.DescriptorId.ToString(), admission.Context))
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
        RuntimeParticipantResultV1? firstFailure = null;
        foreach (var participant in participants.Reverse())
        {
            var result = await InvokeAsync(
                token => participant.TerminateAsync(cause, token), participant.Descriptor.MaxTerminate, CancellationToken.None).ConfigureAwait(false);
            if (!result.IsSuccess && firstFailure is null) firstFailure = result;
        }
        foreach (var participant in ParticipantsInPlanOrder().Reverse())
        {
            try
            {
                await participant.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                firstFailure ??= new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("DisposeFault"));
            }
        }
        _handles.Clear();
        _state = RuntimeParticipantCoordinatorStateV1.Completed;
        return firstFailure ?? CompletedResult;
    }

    private static async ValueTask<RuntimeParticipantPrepareResultV1> InvokePrepareAsync(
        IRuntimeParticipantV1 participant,
        RuntimeParticipantContextV1 context,
        DurationNs duration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToTimeSpan(duration));
        try
        {
            return await participant.PrepareAsync(context, timeout.Token).AsTask()
                .WaitAsync(ToTimeSpan(duration), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("PrepareTimedOut"), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.Cancelled, new BoundedAscii("PrepareCancelled"), null);
        }
        catch (OperationCanceledException)
        {
            return new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("PrepareTimedOut"), null);
        }
        catch (Exception)
        {
            return new RuntimeParticipantPrepareResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("PrepareFault"), null);
        }
    }

    private static async ValueTask<RuntimeParticipantResultV1> InvokeAsync(
        Func<CancellationToken, ValueTask<RuntimeParticipantResultV1>> operation,
        DurationNs duration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToTimeSpan(duration));
        try
        {
            return await operation(timeout.Token).AsTask()
                .WaitAsync(ToTimeSpan(duration), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("ParticipantTimedOut"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Cancelled, new BoundedAscii("ParticipantCancelled"));
        }
        catch (OperationCanceledException)
        {
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.TimedOut, new BoundedAscii("ParticipantTimedOut"));
        }
        catch (Exception)
        {
            return new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Failed, new BoundedAscii("ParticipantFault"));
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
}
