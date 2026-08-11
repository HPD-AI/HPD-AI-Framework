using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Describes one generated live-Audio participant and its bounded dependency contract.</summary>
public sealed record LiveAudioParticipantDescriptorV1
{
    /// <summary>The maximum direct dependencies of one participant.</summary>
    public const int MaximumDependencies = 16;
    /// <summary>The maximum capacity dimensions declared by one participant.</summary>
    public const int MaximumCapacityDimensions = 16;
    private readonly BoundedAscii[] _dependencies;
    private readonly CapacityDimensionId[] _capacityDimensions;

    /// <summary>Initializes one immutable generated participant descriptor.</summary>
    /// <param name="factoryKey">The application-scoped factory key.</param>
    /// <param name="owner">The sole S2-S9 or S11 owner.</param>
    /// <param name="generationFence">The exact owner generation axis.</param>
    /// <param name="dependencies">Zero to 16 direct factory-key dependencies.</param>
    /// <param name="capacityDimensions">One to 16 registered physical charge dimensions.</param>
    /// <param name="maximumPrepareDuration">The positive local preparation deadline.</param>
    /// <param name="maximumDrainDuration">The positive drain deadline.</param>
    /// <param name="maximumTerminateDuration">The positive termination deadline.</param>
    public LiveAudioParticipantDescriptorV1(BoundedAscii factoryKey, OwnerSliceId owner,
        AuthorityAxisId generationFence, IEnumerable<BoundedAscii> dependencies,
        IEnumerable<CapacityDimensionId> capacityDimensions, DurationNs maximumPrepareDuration,
        DurationNs maximumDrainDuration, DurationNs maximumTerminateDuration)
    {
        if (!factoryKey.IsValid) throw new ArgumentException("A factory key is required.", nameof(factoryKey));
        if (!TryAxis(owner, out var expectedAxis) || generationFence != expectedAxis)
            throw new ArgumentException("The generation fence must be the registered axis of the owner.", nameof(generationFence));
        _dependencies = OwnDependencies(factoryKey, dependencies);
        _capacityDimensions = OwnDimensions(capacityDimensions);
        RequireDuration(maximumPrepareDuration, nameof(maximumPrepareDuration));
        RequireDuration(maximumDrainDuration, nameof(maximumDrainDuration));
        RequireDuration(maximumTerminateDuration, nameof(maximumTerminateDuration));
        FactoryKey = factoryKey; Owner = owner; GenerationFence = generationFence;
        Dependencies = Array.AsReadOnly(_dependencies); CapacityDimensions = Array.AsReadOnly(_capacityDimensions);
        MaximumPrepareDuration = maximumPrepareDuration; MaximumDrainDuration = maximumDrainDuration;
        MaximumTerminateDuration = maximumTerminateDuration;
    }

    /// <summary>Gets the application-scoped factory key.</summary>
    public BoundedAscii FactoryKey { get; }
    /// <summary>Gets the sole owner.</summary>
    public OwnerSliceId Owner { get; }
    /// <summary>Gets the exact owner generation fence.</summary>
    public AuthorityAxisId GenerationFence { get; }
    /// <summary>Gets canonical direct dependency keys.</summary>
    public IReadOnlyList<BoundedAscii> Dependencies { get; }
    /// <summary>Gets canonical registered capacity dimensions.</summary>
    public IReadOnlyList<CapacityDimensionId> CapacityDimensions { get; }
    /// <summary>Gets the local preparation deadline.</summary>
    public DurationNs MaximumPrepareDuration { get; }
    /// <summary>Gets the drain deadline.</summary>
    public DurationNs MaximumDrainDuration { get; }
    /// <summary>Gets the termination deadline.</summary>
    public DurationNs MaximumTerminateDuration { get; }

    private static BoundedAscii[] OwnDependencies(BoundedAscii self, IEnumerable<BoundedAscii> values)
    {
        ArgumentNullException.ThrowIfNull(values); var result = values.ToArray();
        if (result.Length > MaximumDependencies || result.Any(value => !value.IsValid || value == self))
            throw new ArgumentException("Dependencies must be bounded, valid, and cannot name the participant itself.", nameof(values));
        Array.Sort(result); if (result.Distinct().Count() != result.Length) throw new ArgumentException("Dependencies must be unique.", nameof(values));
        return result;
    }

    private static CapacityDimensionId[] OwnDimensions(IEnumerable<CapacityDimensionId> values)
    {
        ArgumentNullException.ThrowIfNull(values); var result = values.ToArray();
        if (result.Length is 0 or > MaximumCapacityDimensions || result.Any(value => !value.IsValid))
            throw new ArgumentException("Capacity dimensions must be nonempty, bounded, and registered.", nameof(values));
        Array.Sort(result, static (left, right) => left.Value.CompareTo(right.Value));
        if (result.Distinct().Count() != result.Length) throw new ArgumentException("Capacity dimensions must be unique.", nameof(values));
        return result;
    }

    private static void RequireDuration(DurationNs value, string name)
    {
        if (value.Nanoseconds <= 0 || value.Nanoseconds > 60_000_000_000)
            throw new ArgumentOutOfRangeException(name, "Participant deadlines must be in (0, 60s].");
    }

    private static bool TryAxis(OwnerSliceId owner, out AuthorityAxisId axis)
    {
        axis = owner switch
        {
            OwnerSliceId.S2 => AuthorityAxisId.Graph, OwnerSliceId.S3 => AuthorityAxisId.Activity,
            OwnerSliceId.S4 => AuthorityAxisId.Turn, OwnerSliceId.S5 => AuthorityAxisId.Provider,
            OwnerSliceId.S6 => AuthorityAxisId.Output, OwnerSliceId.S7 => AuthorityAxisId.Tool,
            OwnerSliceId.S8 => AuthorityAxisId.Route, OwnerSliceId.S9 => AuthorityAxisId.Privacy,
            OwnerSliceId.S11 => AuthorityAxisId.Transport, _ => 0,
        };
        return axis != 0;
    }
}

/// <summary>Contains a deterministic dependency-complete participant plan without runtime handles.</summary>
public sealed class LiveAudioParticipantPlanV1
{
    internal LiveAudioParticipantPlanV1(LiveAudioPlanId planId, IReadOnlyList<LiveAudioParticipantDescriptorV1> descriptors,
        IReadOnlyList<BoundedAscii> skippedOptionalFactories, Hash256 fingerprint)
    { PlanId = planId; Descriptors = descriptors; SkippedOptionalFactories = skippedOptionalFactories; Fingerprint = fingerprint; }
    /// <summary>Gets the request's immutable plan identity.</summary>
    public LiveAudioPlanId PlanId { get; }
    /// <summary>Gets descriptors in deterministic dependency order.</summary>
    public IReadOnlyList<LiveAudioParticipantDescriptorV1> Descriptors { get; }
    /// <summary>Gets optional requested factories absent from the application catalog.</summary>
    public IReadOnlyList<BoundedAscii> SkippedOptionalFactories { get; }
    /// <summary>Gets the schema-separated canonical plan fingerprint.</summary>
    public Hash256 Fingerprint { get; }
}

/// <summary>Compiles explicit catalog descriptors into a pure deterministic participant plan.</summary>
public static class LiveAudioParticipantPlanCompilerV1
{
    /// <summary>Compiles and fingerprints the exact requested dependency closure.</summary>
    /// <param name="request">The inert start request.</param>
    /// <param name="catalog">The explicit application factory catalog.</param>
    /// <returns>A dependency-complete plan.</returns>
    /// <exception cref="ArgumentException">A requested factory/dependency is absent, an owner differs, or the catalog contains a cycle.</exception>
    public static LiveAudioParticipantPlanV1 Compile(LiveAudioSessionStartRequestV1 request, LiveAudioParticipantFactoryCatalogV1 catalog)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(catalog);
        var descriptors = new Dictionary<string, LiveAudioParticipantDescriptorV1>(StringComparer.Ordinal);
        var skipped = new List<BoundedAscii>();
        foreach (var specification in request.Participants)
        {
            if (!catalog.TryResolve(specification, out var factory))
            {
                if (!specification.Required) { skipped.Add(specification.FactoryKey); continue; }
                throw new ArgumentException($"Factory '{specification.FactoryKey}' is unavailable.", nameof(catalog));
            }
            descriptors.Add(specification.FactoryKey.ToString(), factory.Descriptor);
        }
        foreach (var descriptor in descriptors.Values)
        {
            if (!request.ExpectedAuthority.Axes.Any(entry => entry.AxisId == descriptor.GenerationFence))
                throw new ArgumentException($"Generation fence '{descriptor.GenerationFence}' is absent for '{descriptor.FactoryKey}'.", nameof(request));
            foreach (var dependency in descriptor.Dependencies)
                if (!descriptors.ContainsKey(dependency.ToString()))
                    throw new ArgumentException($"Dependency '{dependency}' of '{descriptor.FactoryKey}' is not requested.", nameof(request));
        }
        var ordered = Topological(descriptors);
        var bytes = Encode(request, ordered, skipped); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.live-audio-participant-plan.v1@1.0\0"u8); hash.AppendData(bytes);
        if (!Hash256.TryCreate(hash.GetHashAndReset(), out var fingerprint)) throw new InvalidOperationException("SHA-256 returned an invalid digest.");
        return new LiveAudioParticipantPlanV1(request.PlanId, Array.AsReadOnly(ordered), skipped.AsReadOnly(), fingerprint);
    }

    private static LiveAudioParticipantDescriptorV1[] Topological(IReadOnlyDictionary<string, LiveAudioParticipantDescriptorV1> descriptors)
    {
        var remaining = descriptors.ToDictionary(pair => pair.Key,
            pair => pair.Value.Dependencies.Select(value => value.ToString()).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var result = new List<LiveAudioParticipantDescriptorV1>(descriptors.Count);
        while (remaining.Count != 0)
        {
            var ready = remaining.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            if (ready.Length == 0) throw new ArgumentException("The requested participant dependency graph contains a cycle.", nameof(descriptors));
            foreach (var key in ready) { result.Add(descriptors[key]); remaining.Remove(key); }
            foreach (var dependencies in remaining.Values) foreach (var key in ready) dependencies.Remove(key);
        }
        return result.ToArray();
    }

    private static byte[] Encode(LiveAudioSessionStartRequestV1 request, IReadOnlyList<LiveAudioParticipantDescriptorV1> descriptors,
        IReadOnlyList<BoundedAscii> skipped)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(4);
        writer.WriteUInt64(1); WriteId(writer, request.PlanId); writer.WriteUInt64(2); writer.WriteByteString(request.FingerprintBytes());
        writer.WriteUInt64(3); writer.WriteStartArray(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            writer.WriteStartMap(8); writer.WriteUInt64(1); writer.WriteTextString(descriptor.FactoryKey.ToString());
            writer.WriteUInt64(2); writer.WriteUInt64((ushort)descriptor.Owner); writer.WriteUInt64(3); writer.WriteUInt64((ushort)descriptor.GenerationFence);
            writer.WriteUInt64(4); writer.WriteStartArray(descriptor.Dependencies.Count); foreach (var value in descriptor.Dependencies) writer.WriteTextString(value.ToString()); writer.WriteEndArray();
            writer.WriteUInt64(5); writer.WriteStartArray(descriptor.CapacityDimensions.Count); foreach (var value in descriptor.CapacityDimensions) writer.WriteUInt64(value.Value); writer.WriteEndArray();
            writer.WriteUInt64(6); writer.WriteInt64(descriptor.MaximumPrepareDuration.Nanoseconds);
            writer.WriteUInt64(7); writer.WriteInt64(descriptor.MaximumDrainDuration.Nanoseconds);
            writer.WriteUInt64(8); writer.WriteInt64(descriptor.MaximumTerminateDuration.Nanoseconds); writer.WriteEndMap();
        }
        writer.WriteEndArray(); writer.WriteUInt64(4); writer.WriteStartArray(skipped.Count);
        foreach (var key in skipped) writer.WriteTextString(key.ToString());
        writer.WriteEndArray(); writer.WriteEndMap(); return writer.Encode();
    }

    private static void WriteId(CborWriter writer, LiveAudioPlanId value)
    { Span<byte> bytes = stackalloc byte[16]; if (!value.TryWriteBytes(bytes)) throw new ArgumentException("A plan ID is required."); writer.WriteByteString(bytes); }
}

internal static class LiveAudioParticipantPlanRequestExtensionsV1
{
    internal static byte[] FingerprintBytes(this LiveAudioSessionStartRequestV1 request)
    { Span<byte> bytes = stackalloc byte[32]; if (!request.Fingerprint.TryWriteBytes(bytes)) throw new ArgumentException("A request fingerprint is required."); return bytes.ToArray(); }
}
