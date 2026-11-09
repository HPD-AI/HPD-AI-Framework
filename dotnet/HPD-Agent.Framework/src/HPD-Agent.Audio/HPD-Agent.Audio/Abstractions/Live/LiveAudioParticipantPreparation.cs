using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Supplies immutable context to one locally prepared live-Audio participant.</summary>
public sealed record LiveAudioParticipantPreparationContext
{
    /// <summary>Initializes a preparation context from an admitted inert start request and one requested specification.</summary>
    /// <param name="request">The admitted inert request whose proofs were revalidated at the reservation cut.</param>
    /// <param name="specification">The exact participant specification being prepared.</param>
    /// <exception cref="ArgumentNullException">The request or specification is null.</exception>
    /// <exception cref="ArgumentException">The specification is not part of the request.</exception>
    public LiveAudioParticipantPreparationContext(
        LiveAudioSessionStartRequest request,
        LiveAudioParticipantSpec specification)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        if (!request.Participants.Any(item => item == specification))
            throw new ArgumentException("The specification must belong to the request.", nameof(specification));
    }

    /// <summary>Gets the deeply owned inert request.</summary>
    public LiveAudioSessionStartRequest Request { get; }

    /// <summary>Gets the exact participant specification.</summary>
    public LiveAudioParticipantSpec Specification { get; }
}

/// <summary>Creates a local prepared handle without starting domain or external effects.</summary>
/// <remarks>Implementations may validate configuration and allocate bounded local state only. Provider, device, network, media, output and transport effects are forbidden.</remarks>
public interface ILiveAudioParticipantFactory
{
    /// <summary>Gets the immutable generated descriptor for this application factory.</summary>
    LiveAudioParticipantDescriptor Descriptor { get; }

    /// <summary>Prepares bounded local state without starting an effect or publishing readiness.</summary>
    /// <param name="context">The exact request and participant specification.</param>
    /// <param name="cancellationToken">Requests cancellation of local preparation.</param>
    /// <returns>A local handle that can only be unwound or disposed.</returns>
    ValueTask<LiveAudioParticipantFactoryResult> PrepareAsync(
        LiveAudioParticipantPreparationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents the closed local-only result returned by a participant factory.</summary>
public abstract record LiveAudioParticipantFactoryResult
{
    private LiveAudioParticipantFactoryResult() { }

    /// <summary>Contains one successfully prepared local handle.</summary>
    public sealed record Prepared : LiveAudioParticipantFactoryResult
    {
        /// <summary>Initializes a successful local preparation result.</summary>
        /// <param name="participant">The bounded local handle.</param>
        /// <exception cref="ArgumentNullException">The participant is null.</exception>
        public Prepared(ILiveAudioPreparedParticipant participant) =>
            Participant = participant ?? throw new ArgumentNullException(nameof(participant));

        /// <summary>Gets the prepared local handle.</summary>
        public ILiveAudioPreparedParticipant Participant { get; }
    }

    /// <summary>Reports a bounded, nonsecret pre-effect refusal.</summary>
    public sealed record Refused : LiveAudioParticipantFactoryResult
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
public interface ILiveAudioPreparedParticipant : IAsyncDisposable
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
public abstract class LiveAudioParticipantFactoryCatalog
{
    /// <summary>The maximum number of factories in one application catalog.</summary>
    public const int MaximumFactories = 64;

    private readonly IReadOnlyDictionary<string, FactoryEntry> _factories;

    /// <summary>Initializes a deeply owned, duplicate-free application catalog.</summary>
    /// <param name="factories">One to 64 explicitly supplied factories.</param>
    /// <exception cref="ArgumentNullException">The collection or a factory is null.</exception>
    /// <exception cref="ArgumentException">A key is duplicated or a descriptor is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The catalog is empty or contains more than 64 factories.</exception>
    internal static LiveAudioParticipantFactoryCatalog CreateExplicit(
        IEnumerable<ILiveAudioParticipantFactory> factories) => new ExplicitCatalog(BuildExplicit(factories));

    /// <summary>Initializes a catalog from the exact registrations emitted into a generated application subclass.</summary>
    /// <param name="registrations">The generated exact factory-type and descriptor registrations.</param>
    /// <param name="factories">The explicitly constructed factory instances.</param>
    /// <exception cref="ArgumentNullException">A collection or value is null.</exception>
    /// <exception cref="ArgumentException">The instances do not exactly match the generated registrations.</exception>
    protected LiveAudioParticipantFactoryCatalog(
        IEnumerable<LiveAudioParticipantFactoryRegistration> registrations,
        IEnumerable<ILiveAudioParticipantFactory> factories)
        : this(BuildGenerated(registrations, factories))
    { }

    private LiveAudioParticipantFactoryCatalog(CatalogBuild build)
    {
        _factories = build.Factories; Manifest = build.Manifest;
    }

    /// <summary>Gets the number of explicitly registered factories.</summary>
    public int Count => _factories.Count;

    /// <summary>Gets the generated exact-set manifest bound to this catalog.</summary>
    public LiveAudioParticipantCatalogManifest Manifest { get; }

    internal bool TryResolve(LiveAudioParticipantSpec specification, out ILiveAudioParticipantFactory factory)
    {
        var found = TryResolve(specification, out factory, out _);
        return found;
    }

    internal bool TryResolve(LiveAudioParticipantSpec specification, out ILiveAudioParticipantFactory factory,
        out LiveAudioParticipantDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (_factories.TryGetValue(specification.FactoryKey.ToString(), out var entry) && entry.Descriptor.Owner == specification.Owner)
        {
            factory = entry.Factory; descriptor = entry.Descriptor; return true;
        }
        factory = null!; descriptor = null!; return false;
    }

    private sealed record FactoryEntry(ILiveAudioParticipantFactory Factory, LiveAudioParticipantDescriptor Descriptor);
    private sealed record CatalogBuild(IReadOnlyDictionary<string, FactoryEntry> Factories,
        LiveAudioParticipantCatalogManifest Manifest);
    private sealed class ExplicitCatalog(CatalogBuild build) : LiveAudioParticipantFactoryCatalog(build);

    private static CatalogBuild BuildExplicit(IEnumerable<ILiveAudioParticipantFactory> factories)
    {
        var values = OwnFactories(factories);
        var registrations = values.Values.Select(static value => new LiveAudioParticipantFactoryRegistration(
            value.Factory.GetType(), "explicit-test:" + value.Descriptor.FactoryKey, value.Descriptor));
        return new CatalogBuild(values, LiveAudioParticipantCatalogManifest.Create(registrations));
    }

    private static CatalogBuild BuildGenerated(IEnumerable<LiveAudioParticipantFactoryRegistration> registrations,
        IEnumerable<ILiveAudioParticipantFactory> factories)
    {
        var manifest = LiveAudioParticipantCatalogManifest.Create(registrations);
        var values = OwnFactories(factories);
        foreach (var entry in values.Values)
        {
            if (!manifest.TryGet(entry.Descriptor.FactoryKey, out var expected) ||
                expected.FactoryType != entry.Factory.GetType() || !DescriptorEquals(expected.Descriptor, entry.Descriptor))
                throw new ArgumentException("Every instance and descriptor must exactly match the generated application registration.", nameof(factories));
        }
        if (values.Count != manifest.Descriptors.Count)
            throw new ArgumentException("Every generated participant factory must be supplied exactly once.", nameof(factories));
        return new CatalogBuild(values, manifest);
    }

    private static Dictionary<string, FactoryEntry> OwnFactories(IEnumerable<ILiveAudioParticipantFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var values = new Dictionary<string, FactoryEntry>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (values.Count == MaximumFactories) throw new ArgumentOutOfRangeException(nameof(factories));
            var descriptor = factory.Descriptor ?? throw new ArgumentException("Each factory needs a descriptor.", nameof(factories));
            if (!values.TryAdd(descriptor.FactoryKey.ToString(), new FactoryEntry(factory, descriptor)))
                throw new ArgumentException("Factory keys must be unique within an application catalog.", nameof(factories));
        }
        if (values.Count == 0) throw new ArgumentOutOfRangeException(nameof(factories));
        return values;
    }

    private static bool DescriptorEquals(LiveAudioParticipantDescriptor left, LiveAudioParticipantDescriptor right) =>
        left.FactoryKey == right.FactoryKey && left.Owner == right.Owner && left.GenerationFence == right.GenerationFence &&
        left.Dependencies.SequenceEqual(right.Dependencies) && left.CapacityDimensions.SequenceEqual(right.CapacityDimensions) &&
        left.MaximumPrepareDuration == right.MaximumPrepareDuration && left.MaximumDrainDuration == right.MaximumDrainDuration &&
        left.MaximumTerminateDuration == right.MaximumTerminateDuration;
}

internal abstract record LiveAudioParticipantPreparationResult
{
    private LiveAudioParticipantPreparationResult() { }
    internal sealed record Prepared(IReadOnlyList<ILiveAudioPreparedParticipant> Participants,
        IReadOnlyList<BoundedAscii> SkippedOptionalFactories, Hash256 EffectiveFingerprint) : LiveAudioParticipantPreparationResult;
    internal sealed record Unavailable(BoundedAscii FactoryKey) : LiveAudioParticipantPreparationResult;
    internal sealed record Failed(BoundedAscii FactoryKey, BoundedAscii SafeCode) : LiveAudioParticipantPreparationResult;
    internal sealed record Cancelled : LiveAudioParticipantPreparationResult;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : LiveAudioParticipantPreparationResult;
}

internal static class LiveAudioParticipantPreparationCoordinator
{
    internal static async ValueTask<LiveAudioParticipantPreparationResult> PrepareAsync(
        LiveAudioSessionStartRequest request,
        LiveAudioParticipantFactoryCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        if (cancellationToken.IsCancellationRequested) return new LiveAudioParticipantPreparationResult.Cancelled();
        var prepared = new List<PreparedEntry>(request.Participants.Count);
        var participantIds = new HashSet<ParticipantId>();
        foreach (var specification in request.Participants.Where(value => value.Required))
            if (!catalog.TryResolve(specification, out _))
                return new LiveAudioParticipantPreparationResult.Unavailable(specification.FactoryKey);
        LiveAudioParticipantPlan plan;
        try { plan = LiveAudioParticipantPlanCompiler.Compile(request, catalog); }
        catch (ArgumentException)
        {
            return new LiveAudioParticipantPreparationResult.Failed(
                new BoundedAscii("participant-plan"), new BoundedAscii("participant-plan-invalid"));
        }
        var specifications = request.Participants.ToDictionary(value => value.FactoryKey.ToString(), StringComparer.Ordinal);
        var skipped = plan.SkippedOptionalFactories.ToList();
        var skippedKeys = skipped.Select(value => value.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in plan.Descriptors)
        {
            if (cancellationToken.IsCancellationRequested)
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResult.Cancelled()).ConfigureAwait(false);
            var specification = specifications[descriptor.FactoryKey.ToString()];
            if (descriptor.Dependencies.Any(value => skippedKeys.Contains(value.ToString())))
            {
                if (!specification.Required) { skipped.Add(specification.FactoryKey); skippedKeys.Add(specification.FactoryKey.ToString()); continue; }
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResult.Failed(
                    specification.FactoryKey, new BoundedAscii("participant-dependency-unavailable"))).ConfigureAwait(false);
            }
            if (!catalog.TryResolve(specification, out var factory)) throw new InvalidOperationException("A compiled factory disappeared from an immutable catalog.");
            Task<LiveAudioParticipantFactoryResult>? pending = null;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(ToTimeSpan(descriptor.MaximumPrepareDuration));
                pending = factory.PrepareAsync(
                    new LiveAudioParticipantPreparationContext(request, specification), deadline.Token).AsTask();
                var result = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (result is LiveAudioParticipantFactoryResult.Prepared cancelledPrepared)
                        prepared.Add(new PreparedEntry(cancelledPrepared.Participant, descriptor));
                    return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResult.Cancelled()).ConfigureAwait(false);
                }
                if (result is LiveAudioParticipantFactoryResult.Refused refused)
                {
                    if (!specification.Required)
                    { skipped.Add(specification.FactoryKey); skippedKeys.Add(specification.FactoryKey.ToString()); continue; }
                    return await UnwindOrAsync(prepared,
                        new LiveAudioParticipantPreparationResult.Failed(specification.FactoryKey, refused.SafeCode)).ConfigureAwait(false);
                }
                var participant = (result as LiveAudioParticipantFactoryResult.Prepared)?.Participant;
                if (participant is null || !participant.ParticipantId.IsValid || participant.FactoryKey != specification.FactoryKey ||
                    participant.Owner != specification.Owner)
                    throw new InvalidOperationException("The factory returned a handle outside its exact registration.");
                prepared.Add(new PreparedEntry(participant, descriptor));
                if (!participantIds.Add(participant.ParticipantId))
                    return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResult.Failed(
                        specification.FactoryKey, new BoundedAscii("participant-identity-duplicate"))).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (pending is { IsCompleted: false }) _ = ObserveLatePreparationAsync(pending, descriptor);
                LiveAudioParticipantPreparationResult cancelled = cancellationToken.IsCancellationRequested && pending is not { IsCompleted: false }
                    ? new LiveAudioParticipantPreparationResult.Cancelled()
                    : new LiveAudioParticipantPreparationResult.OutcomeUnknown(
                        new BoundedAscii(cancellationToken.IsCancellationRequested ? "participant-cancel-late" : "participant-prepare-timeout"));
                return await UnwindOrAsync(prepared, cancelled).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResult.Failed(
                    specification.FactoryKey, new BoundedAscii("participant-prepare-failed"))).ConfigureAwait(false);
            }
        }
        skipped.Sort();
        return new LiveAudioParticipantPreparationResult.Prepared(
            Array.AsReadOnly(prepared.Select(value => value.Participant).ToArray()), skipped.AsReadOnly(),
            LiveAudioParticipantEffectiveFingerprint.Compute(plan.Fingerprint, skipped));
    }

    private static async ValueTask<LiveAudioParticipantPreparationResult> UnwindOrAsync(
        IReadOnlyList<PreparedEntry> prepared,
        LiveAudioParticipantPreparationResult result)
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
            ? new LiveAudioParticipantPreparationResult.OutcomeUnknown(new BoundedAscii("participant-unwind-unknown"))
            : result;
    }

    private static async Task ObserveLatePreparationAsync(Task<LiveAudioParticipantFactoryResult> pending,
        LiveAudioParticipantDescriptor descriptor)
    {
        try
        {
            if (await pending.ConfigureAwait(false) is LiveAudioParticipantFactoryResult.Prepared prepared)
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

    private sealed record PreparedEntry(ILiveAudioPreparedParticipant Participant, LiveAudioParticipantDescriptor Descriptor);
}
