using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Supplies immutable context to one locally prepared live-Audio participant.</summary>
public sealed record LiveAudioParticipantPreparationContextV1
{
    /// <summary>Initializes a preparation context from an admitted inert start request and one requested specification.</summary>
    /// <param name="request">The admitted inert request whose proofs were revalidated at the reservation cut.</param>
    /// <param name="specification">The exact participant specification being prepared.</param>
    /// <exception cref="ArgumentNullException">The request or specification is null.</exception>
    /// <exception cref="ArgumentException">The specification is not part of the request.</exception>
    public LiveAudioParticipantPreparationContextV1(
        LiveAudioSessionStartRequestV1 request,
        LiveAudioParticipantSpecV1 specification)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        if (!request.Participants.Any(item => item == specification))
            throw new ArgumentException("The specification must belong to the request.", nameof(specification));
    }

    /// <summary>Gets the deeply owned inert request.</summary>
    public LiveAudioSessionStartRequestV1 Request { get; }

    /// <summary>Gets the exact participant specification.</summary>
    public LiveAudioParticipantSpecV1 Specification { get; }
}

/// <summary>Creates a local prepared handle without starting domain or external effects.</summary>
/// <remarks>Implementations may validate configuration and allocate bounded local state only. Provider, device, network, media, output and transport effects are forbidden.</remarks>
public interface ILiveAudioParticipantFactoryV1
{
    /// <summary>Gets the immutable generated descriptor for this application factory.</summary>
    LiveAudioParticipantDescriptorV1 Descriptor { get; }

    /// <summary>Prepares bounded local state without starting an effect or publishing readiness.</summary>
    /// <param name="context">The exact request and participant specification.</param>
    /// <param name="cancellationToken">Requests cancellation of local preparation.</param>
    /// <returns>A local handle that can only be unwound or disposed.</returns>
    ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
        LiveAudioParticipantPreparationContextV1 context,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents the closed local-only result returned by a participant factory.</summary>
public abstract record LiveAudioParticipantFactoryResultV1
{
    private LiveAudioParticipantFactoryResultV1() { }

    /// <summary>Contains one successfully prepared local handle.</summary>
    public sealed record Prepared : LiveAudioParticipantFactoryResultV1
    {
        /// <summary>Initializes a successful local preparation result.</summary>
        /// <param name="participant">The bounded local handle.</param>
        /// <exception cref="ArgumentNullException">The participant is null.</exception>
        public Prepared(ILiveAudioPreparedParticipantV1 participant) =>
            Participant = participant ?? throw new ArgumentNullException(nameof(participant));

        /// <summary>Gets the prepared local handle.</summary>
        public ILiveAudioPreparedParticipantV1 Participant { get; }
    }

    /// <summary>Reports a bounded, nonsecret pre-effect refusal.</summary>
    public sealed record Refused : LiveAudioParticipantFactoryResultV1
    {
        /// <summary>Initializes a typed local refusal.</summary>
        /// <param name="safeCode">A bounded nonsecret reason.</param>
        /// <exception cref="ArgumentException">The safe code is invalid.</exception>
        public Refused(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid) throw new ArgumentException("A bounded safe code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        /// <summary>Gets the bounded nonsecret reason.</summary>
        public BoundedAscii SafeCode { get; }
    }
}

/// <summary>Represents locally prepared participant state that has no start or effect surface.</summary>
public interface ILiveAudioPreparedParticipantV1 : IAsyncDisposable
{
    /// <summary>Gets the unique participant identity allocated by its owner factory.</summary>
    ParticipantId ParticipantId { get; }

    /// <summary>Gets the exact generated factory key.</summary>
    BoundedAscii FactoryKey { get; }

    /// <summary>Gets the sole domain owner.</summary>
    OwnerSliceId Owner { get; }
}

/// <summary>Contains an explicit immutable application-scoped participant factory catalog.</summary>
/// <remarks>The catalog uses no reflection, module initializer, process-global registry or fallback discovery.</remarks>
public sealed class LiveAudioParticipantFactoryCatalogV1
{
    /// <summary>The maximum number of factories in one application catalog.</summary>
    public const int MaximumFactories = 64;

    private readonly IReadOnlyDictionary<string, FactoryEntry> _factories;

    /// <summary>Initializes a deeply owned, duplicate-free application catalog.</summary>
    /// <param name="factories">One to 64 explicitly supplied factories.</param>
    /// <exception cref="ArgumentNullException">The collection or a factory is null.</exception>
    /// <exception cref="ArgumentException">A key is duplicated or a descriptor is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The catalog is empty or contains more than 64 factories.</exception>
    public LiveAudioParticipantFactoryCatalogV1(IEnumerable<ILiveAudioParticipantFactoryV1> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var values = new Dictionary<string, FactoryEntry>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (values.Count == MaximumFactories)
                throw new ArgumentOutOfRangeException(nameof(factories));
            var descriptor = factory.Descriptor ?? throw new ArgumentException("Each factory needs a descriptor.", nameof(factories));
            if (!values.TryAdd(descriptor.FactoryKey.ToString(), new FactoryEntry(factory, descriptor)))
                throw new ArgumentException("Factory keys must be unique within an application catalog.", nameof(factories));
        }
        if (values.Count == 0) throw new ArgumentOutOfRangeException(nameof(factories));
        _factories = values;
    }

    /// <summary>Gets the number of explicitly registered factories.</summary>
    public int Count => _factories.Count;

    internal bool TryResolve(LiveAudioParticipantSpecV1 specification, out ILiveAudioParticipantFactoryV1 factory)
    {
        var found = TryResolve(specification, out factory, out _);
        return found;
    }

    internal bool TryResolve(LiveAudioParticipantSpecV1 specification, out ILiveAudioParticipantFactoryV1 factory,
        out LiveAudioParticipantDescriptorV1 descriptor)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (_factories.TryGetValue(specification.FactoryKey.ToString(), out var entry) && entry.Descriptor.Owner == specification.Owner)
        {
            factory = entry.Factory; descriptor = entry.Descriptor; return true;
        }
        factory = null!; descriptor = null!; return false;
    }

    private sealed record FactoryEntry(ILiveAudioParticipantFactoryV1 Factory, LiveAudioParticipantDescriptorV1 Descriptor);
}

internal abstract record LiveAudioParticipantPreparationResultV1
{
    private LiveAudioParticipantPreparationResultV1() { }
    internal sealed record Prepared(IReadOnlyList<ILiveAudioPreparedParticipantV1> Participants,
        IReadOnlyList<BoundedAscii> SkippedOptionalFactories, Hash256 EffectiveFingerprint) : LiveAudioParticipantPreparationResultV1;
    internal sealed record Unavailable(BoundedAscii FactoryKey) : LiveAudioParticipantPreparationResultV1;
    internal sealed record Failed(BoundedAscii FactoryKey, BoundedAscii SafeCode) : LiveAudioParticipantPreparationResultV1;
    internal sealed record Cancelled : LiveAudioParticipantPreparationResultV1;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : LiveAudioParticipantPreparationResultV1;
}

internal static class LiveAudioParticipantPreparationCoordinatorV1
{
    internal static async ValueTask<LiveAudioParticipantPreparationResultV1> PrepareAsync(
        LiveAudioSessionStartRequestV1 request,
        LiveAudioParticipantFactoryCatalogV1 catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        if (cancellationToken.IsCancellationRequested) return new LiveAudioParticipantPreparationResultV1.Cancelled();
        var prepared = new List<PreparedEntry>(request.Participants.Count);
        var participantIds = new HashSet<ParticipantId>();
        foreach (var specification in request.Participants.Where(value => value.Required))
            if (!catalog.TryResolve(specification, out _))
                return new LiveAudioParticipantPreparationResultV1.Unavailable(specification.FactoryKey);
        LiveAudioParticipantPlanV1 plan;
        try { plan = LiveAudioParticipantPlanCompilerV1.Compile(request, catalog); }
        catch (ArgumentException)
        {
            return new LiveAudioParticipantPreparationResultV1.Failed(
                new BoundedAscii("participant-plan"), new BoundedAscii("participant-plan-invalid"));
        }
        var specifications = request.Participants.ToDictionary(value => value.FactoryKey.ToString(), StringComparer.Ordinal);
        var skipped = plan.SkippedOptionalFactories.ToList();
        var skippedKeys = skipped.Select(value => value.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in plan.Descriptors)
        {
            if (cancellationToken.IsCancellationRequested)
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Cancelled()).ConfigureAwait(false);
            var specification = specifications[descriptor.FactoryKey.ToString()];
            if (descriptor.Dependencies.Any(value => skippedKeys.Contains(value.ToString())))
            {
                if (!specification.Required) { skipped.Add(specification.FactoryKey); skippedKeys.Add(specification.FactoryKey.ToString()); continue; }
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Failed(
                    specification.FactoryKey, new BoundedAscii("participant-dependency-unavailable"))).ConfigureAwait(false);
            }
            if (!catalog.TryResolve(specification, out var factory)) throw new InvalidOperationException("A compiled factory disappeared from an immutable catalog.");
            Task<LiveAudioParticipantFactoryResultV1>? pending = null;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(ToTimeSpan(descriptor.MaximumPrepareDuration));
                pending = factory.PrepareAsync(
                    new LiveAudioParticipantPreparationContextV1(request, specification), deadline.Token).AsTask();
                var result = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (result is LiveAudioParticipantFactoryResultV1.Prepared cancelledPrepared)
                        prepared.Add(new PreparedEntry(cancelledPrepared.Participant, descriptor));
                    return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Cancelled()).ConfigureAwait(false);
                }
                if (result is LiveAudioParticipantFactoryResultV1.Refused refused)
                {
                    if (!specification.Required)
                    { skipped.Add(specification.FactoryKey); skippedKeys.Add(specification.FactoryKey.ToString()); continue; }
                    return await UnwindOrAsync(prepared,
                        new LiveAudioParticipantPreparationResultV1.Failed(specification.FactoryKey, refused.SafeCode)).ConfigureAwait(false);
                }
                var participant = (result as LiveAudioParticipantFactoryResultV1.Prepared)?.Participant;
                if (participant is null || !participant.ParticipantId.IsValid || participant.FactoryKey != specification.FactoryKey ||
                    participant.Owner != specification.Owner)
                    throw new InvalidOperationException("The factory returned a handle outside its exact registration.");
                prepared.Add(new PreparedEntry(participant, descriptor));
                if (!participantIds.Add(participant.ParticipantId))
                    return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Failed(
                        specification.FactoryKey, new BoundedAscii("participant-identity-duplicate"))).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (pending is { IsCompleted: false }) _ = ObserveLatePreparationAsync(pending, descriptor);
                LiveAudioParticipantPreparationResultV1 cancelled = cancellationToken.IsCancellationRequested && pending is not { IsCompleted: false }
                    ? new LiveAudioParticipantPreparationResultV1.Cancelled()
                    : new LiveAudioParticipantPreparationResultV1.OutcomeUnknown(
                        new BoundedAscii(cancellationToken.IsCancellationRequested ? "participant-cancel-late" : "participant-prepare-timeout"));
                return await UnwindOrAsync(prepared, cancelled).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Failed(
                    specification.FactoryKey, new BoundedAscii("participant-prepare-failed"))).ConfigureAwait(false);
            }
        }
        skipped.Sort();
        return new LiveAudioParticipantPreparationResultV1.Prepared(
            Array.AsReadOnly(prepared.Select(value => value.Participant).ToArray()), skipped.AsReadOnly(),
            LiveAudioParticipantEffectiveFingerprintV1.Compute(plan.Fingerprint, skipped));
    }

    private static async ValueTask<LiveAudioParticipantPreparationResultV1> UnwindOrAsync(
        IReadOnlyList<PreparedEntry> prepared,
        LiveAudioParticipantPreparationResultV1 result)
    {
        var failed = false;
        for (var index = prepared.Count - 1; index >= 0; index--)
        {
            Task? pending = null;
            try
            {
                pending = prepared[index].Participant.DisposeAsync().AsTask();
                await pending.WaitAsync(ToTimeSpan(prepared[index].Descriptor.MaximumTerminateDuration)).ConfigureAwait(false);
            }
            catch
            {
                failed = true;
                if (pending is { IsCompleted: false }) _ = ObserveLateDisposalAsync(pending);
            }
        }
        return failed
            ? new LiveAudioParticipantPreparationResultV1.OutcomeUnknown(new BoundedAscii("participant-unwind-unknown"))
            : result;
    }

    private static async Task ObserveLatePreparationAsync(Task<LiveAudioParticipantFactoryResultV1> pending,
        LiveAudioParticipantDescriptorV1 descriptor)
    {
        try
        {
            if (await pending.ConfigureAwait(false) is LiveAudioParticipantFactoryResultV1.Prepared prepared)
            {
                var disposal = prepared.Participant.DisposeAsync().AsTask();
                await disposal.WaitAsync(ToTimeSpan(descriptor.MaximumTerminateDuration)).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private static async Task ObserveLateDisposalAsync(Task pending)
    { try { await pending.ConfigureAwait(false); } catch { } }

    private static TimeSpan ToTimeSpan(DurationNs duration) =>
        TimeSpan.FromTicks(checked((duration.Nanoseconds + 99) / 100));

    private sealed record PreparedEntry(ILiveAudioPreparedParticipantV1 Participant, LiveAudioParticipantDescriptorV1 Descriptor);
}
