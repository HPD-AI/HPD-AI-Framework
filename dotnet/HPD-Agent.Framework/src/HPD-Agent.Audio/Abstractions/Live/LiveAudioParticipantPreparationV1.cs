using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Supplies immutable context to one locally prepared live-Audio participant.</summary>
public sealed record LiveAudioParticipantPreparationContextV1
{
    /// <summary>Initializes a preparation context from an admitted inert start request and one requested specification.</summary>
    /// <param name="request">The admitted inert request whose proofs were revalidated at the reservation cut.</param>
    /// <param name="specification">The exact participant specification being prepared.</param>
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

    private readonly IReadOnlyDictionary<string, ILiveAudioParticipantFactoryV1> _factories;

    /// <summary>Initializes a deeply owned, duplicate-free application catalog.</summary>
    /// <param name="factories">One to 64 explicitly supplied factories.</param>
    public LiveAudioParticipantFactoryCatalogV1(IEnumerable<ILiveAudioParticipantFactoryV1> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var values = new Dictionary<string, ILiveAudioParticipantFactoryV1>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (values.Count == MaximumFactories)
                throw new ArgumentOutOfRangeException(nameof(factories));
            var descriptor = factory.Descriptor ?? throw new ArgumentException("Each factory needs a descriptor.", nameof(factories));
            if (!values.TryAdd(descriptor.FactoryKey.ToString(), factory))
                throw new ArgumentException("Factory keys must be unique within an application catalog.", nameof(factories));
        }
        if (values.Count == 0) throw new ArgumentOutOfRangeException(nameof(factories));
        _factories = values;
    }

    /// <summary>Gets the number of explicitly registered factories.</summary>
    public int Count => _factories.Count;

    internal bool TryResolve(LiveAudioParticipantSpecV1 specification, out ILiveAudioParticipantFactoryV1 factory)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return _factories.TryGetValue(specification.FactoryKey.ToString(), out factory!) && factory.Descriptor.Owner == specification.Owner;
    }
}

internal abstract record LiveAudioParticipantPreparationResultV1
{
    private LiveAudioParticipantPreparationResultV1() { }
    internal sealed record Prepared(IReadOnlyList<ILiveAudioPreparedParticipantV1> Participants,
        IReadOnlyList<BoundedAscii> SkippedOptionalFactories) : LiveAudioParticipantPreparationResultV1;
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
        var prepared = new List<ILiveAudioPreparedParticipantV1>(request.Participants.Count);
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
        foreach (var descriptor in plan.Descriptors)
        {
            var specification = specifications[descriptor.FactoryKey.ToString()];
            if (!catalog.TryResolve(specification, out var factory)) throw new InvalidOperationException("A compiled factory disappeared from an immutable catalog.");
            try
            {
                var result = await factory.PrepareAsync(
                    new LiveAudioParticipantPreparationContextV1(request, specification), cancellationToken).ConfigureAwait(false);
                if (result is LiveAudioParticipantFactoryResultV1.Refused refused)
                {
                    if (!specification.Required) { skipped.Add(specification.FactoryKey); continue; }
                    return await UnwindOrAsync(prepared,
                        new LiveAudioParticipantPreparationResultV1.Failed(specification.FactoryKey, refused.SafeCode)).ConfigureAwait(false);
                }
                var participant = (result as LiveAudioParticipantFactoryResultV1.Prepared)?.Participant;
                if (participant is null || !participant.ParticipantId.IsValid || participant.FactoryKey != specification.FactoryKey ||
                    participant.Owner != specification.Owner)
                    throw new InvalidOperationException("The factory returned a handle outside its exact registration.");
                prepared.Add(participant);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Cancelled()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return await UnwindOrAsync(prepared, new LiveAudioParticipantPreparationResultV1.Failed(
                    specification.FactoryKey, new BoundedAscii("participant-prepare-failed"))).ConfigureAwait(false);
            }
        }
        skipped.Sort();
        return new LiveAudioParticipantPreparationResultV1.Prepared(prepared.AsReadOnly(), skipped.AsReadOnly());
    }

    private static async ValueTask<LiveAudioParticipantPreparationResultV1> UnwindOrAsync(
        IReadOnlyList<ILiveAudioPreparedParticipantV1> prepared,
        LiveAudioParticipantPreparationResultV1 result)
    {
        var failed = false;
        for (var index = prepared.Count - 1; index >= 0; index--)
        {
            try { await prepared[index].DisposeAsync().ConfigureAwait(false); }
            catch { failed = true; }
        }
        return failed
            ? new LiveAudioParticipantPreparationResultV1.OutcomeUnknown(new BoundedAscii("participant-unwind-unknown"))
            : result;
    }
}
