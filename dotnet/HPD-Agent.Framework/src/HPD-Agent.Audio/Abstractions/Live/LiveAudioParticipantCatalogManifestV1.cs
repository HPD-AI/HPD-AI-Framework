using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Declares one live-Audio participant factory for application catalog generation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdLiveAudioParticipantFactoryAttribute : Attribute
{
    /// <summary>Initializes one bounded participant declaration.</summary>
    /// <param name="factoryKey">The application-unique factory key.</param>
    /// <param name="owner">The sole participant owner.</param>
    /// <param name="generationFence">The owner's registered generation axis.</param>
    /// <param name="maximumPrepareNanoseconds">The positive preparation deadline.</param>
    /// <param name="maximumDrainNanoseconds">The positive drain deadline.</param>
    /// <param name="maximumTerminateNanoseconds">The positive termination deadline.</param>
    /// <param name="capacityDimensions">One to sixteen registered capacity dimension values.</param>
    public HpdLiveAudioParticipantFactoryAttribute(string factoryKey, OwnerSliceId owner,
        AuthorityAxisId generationFence, long maximumPrepareNanoseconds, long maximumDrainNanoseconds,
        long maximumTerminateNanoseconds, params ushort[] capacityDimensions)
    {
        FactoryKey = factoryKey; Owner = owner; GenerationFence = generationFence;
        MaximumPrepareNanoseconds = maximumPrepareNanoseconds; MaximumDrainNanoseconds = maximumDrainNanoseconds;
        MaximumTerminateNanoseconds = maximumTerminateNanoseconds;
        CapacityDimensions = capacityDimensions ?? [];
    }

    /// <summary>Gets the declared factory key.</summary>
    public string FactoryKey { get; }
    /// <summary>Gets the declared owner.</summary>
    public OwnerSliceId Owner { get; }
    /// <summary>Gets the declared generation axis.</summary>
    public AuthorityAxisId GenerationFence { get; }
    /// <summary>Gets the preparation deadline in nanoseconds.</summary>
    public long MaximumPrepareNanoseconds { get; }
    /// <summary>Gets the drain deadline in nanoseconds.</summary>
    public long MaximumDrainNanoseconds { get; }
    /// <summary>Gets the termination deadline in nanoseconds.</summary>
    public long MaximumTerminateNanoseconds { get; }
    /// <summary>Gets the registered capacity dimension values.</summary>
    public ushort[] CapacityDimensions { get; }
    /// <summary>Gets or sets the direct participant factory-key dependencies.</summary>
    public string[] Dependencies { get; set; } = [];
}

/// <summary>Carries one generated participant declaration across an assembly boundary.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HpdLiveAudioParticipantManifestAttribute : Attribute
{
    /// <summary>Initializes one generated assembly manifest entry.</summary>
    /// <param name="factoryType">The exact declared factory type.</param>
    /// <param name="factoryKey">The application-unique factory key.</param>
    /// <param name="owner">The numeric sole-owner value.</param>
    /// <param name="generationFence">The numeric owner-axis value.</param>
    /// <param name="maximumPrepareNanoseconds">The positive preparation deadline.</param>
    /// <param name="maximumDrainNanoseconds">The positive drain deadline.</param>
    /// <param name="maximumTerminateNanoseconds">The positive termination deadline.</param>
    /// <param name="capacityDimensions">The registered capacity dimension values.</param>
    /// <param name="dependencies">The direct factory-key dependencies.</param>
    public HpdLiveAudioParticipantManifestAttribute(Type factoryType, string factoryKey, ushort owner,
        ushort generationFence, long maximumPrepareNanoseconds, long maximumDrainNanoseconds,
        long maximumTerminateNanoseconds, ushort[] capacityDimensions, string[] dependencies)
    {
        FactoryType = factoryType; FactoryKey = factoryKey; Owner = owner; GenerationFence = generationFence;
        MaximumPrepareNanoseconds = maximumPrepareNanoseconds; MaximumDrainNanoseconds = maximumDrainNanoseconds;
        MaximumTerminateNanoseconds = maximumTerminateNanoseconds; CapacityDimensions = capacityDimensions;
        Dependencies = dependencies;
    }

    /// <summary>Gets the declared factory type.</summary>
    public Type FactoryType { get; }
    /// <summary>Gets the factory key.</summary>
    public string FactoryKey { get; }
    /// <summary>Gets the numeric owner value.</summary>
    public ushort Owner { get; }
    /// <summary>Gets the numeric generation-axis value.</summary>
    public ushort GenerationFence { get; }
    /// <summary>Gets the preparation deadline in nanoseconds.</summary>
    public long MaximumPrepareNanoseconds { get; }
    /// <summary>Gets the drain deadline in nanoseconds.</summary>
    public long MaximumDrainNanoseconds { get; }
    /// <summary>Gets the termination deadline in nanoseconds.</summary>
    public long MaximumTerminateNanoseconds { get; }
    /// <summary>Gets the capacity dimension values.</summary>
    public ushort[] CapacityDimensions { get; }
    /// <summary>Gets the direct dependency keys.</summary>
    public string[] Dependencies { get; }
}

/// <summary>Binds one generated participant descriptor to its exact factory implementation type.</summary>
/// <remarks>The canonical identity is <c>assembly-simple-name:global::fully-qualified-source-type</c>, emitted directly from Roslyn symbols.</remarks>
public sealed record LiveAudioParticipantFactoryRegistrationV1
{
    /// <summary>Initializes one generated factory registration.</summary>
    /// <param name="factoryType">The exact generated factory implementation type.</param>
    /// <param name="factoryIdentity">The Roslyn-derived assembly and metadata-name identity.</param>
    /// <param name="descriptor">The generated immutable descriptor.</param>
    /// <exception cref="ArgumentNullException">A value is null.</exception>
    /// <exception cref="ArgumentException">The canonical identity is empty, non-ASCII, or longer than 512 bytes.</exception>
    public LiveAudioParticipantFactoryRegistrationV1(Type factoryType, string factoryIdentity,
        LiveAudioParticipantDescriptorV1 descriptor)
    {
        FactoryType = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
        ArgumentNullException.ThrowIfNull(factoryIdentity);
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (factoryIdentity.Length is < 1 or > 512 || factoryIdentity.Any(static value => value is < (char)0x21 or > (char)0x7e))
            throw new ArgumentException("A bounded printable-ASCII factory identity is required.", nameof(factoryIdentity));
        FactoryIdentity = factoryIdentity;
    }

    /// <summary>Gets the exact factory implementation type.</summary>
    public Type FactoryType { get; }
    /// <summary>Gets the canonical Roslyn-derived assembly and fully-qualified source-type identity.</summary>
    public string FactoryIdentity { get; }
    /// <summary>Gets the generated descriptor.</summary>
    public LiveAudioParticipantDescriptorV1 Descriptor { get; }
}

/// <summary>Contains the generated, canonical participant descriptor exact set and fingerprint.</summary>
public sealed class LiveAudioParticipantCatalogManifestV1
{
    private readonly LiveAudioParticipantFactoryRegistrationV1[] _registrations;

    private LiveAudioParticipantCatalogManifestV1(LiveAudioParticipantFactoryRegistrationV1[] registrations, Hash256 fingerprint)
    { _registrations = registrations; Descriptors = Array.AsReadOnly(registrations.Select(static value => value.Descriptor).ToArray()); Fingerprint = fingerprint; }

    /// <summary>Gets the descriptors in canonical factory-key order.</summary>
    public IReadOnlyList<LiveAudioParticipantDescriptorV1> Descriptors { get; }
    /// <summary>Gets the domain-separated fingerprint of the exact descriptor set.</summary>
    public Hash256 Fingerprint { get; }

    internal static LiveAudioParticipantCatalogManifestV1 Create(
        IEnumerable<LiveAudioParticipantFactoryRegistrationV1> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var values = new List<LiveAudioParticipantFactoryRegistrationV1>(LiveAudioParticipantFactoryCatalogV1.MaximumFactories);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (values.Count == LiveAudioParticipantFactoryCatalogV1.MaximumFactories)
                throw new ArgumentOutOfRangeException(nameof(registrations));
            values.Add(registration);
        }
        if (values.Count == 0) throw new ArgumentOutOfRangeException(nameof(registrations));
        var ordered = values.OrderBy(static value => value.Descriptor.FactoryKey).ToArray();
        if (ordered.Select(static value => value.Descriptor.FactoryKey).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Generated factory keys must be unique.", nameof(registrations));
        var known = ordered.Select(static value => value.Descriptor.FactoryKey.ToString()).ToHashSet(StringComparer.Ordinal);
        if (ordered.SelectMany(static value => value.Descriptor.Dependencies).Any(value => !known.Contains(value.ToString())))
            throw new ArgumentException("Every generated dependency must be present in the same application manifest.", nameof(registrations));
        return new LiveAudioParticipantCatalogManifestV1(ordered, Compute(ordered));
    }

    internal bool TryGet(BoundedAscii key, out LiveAudioParticipantFactoryRegistrationV1 registration)
    {
        registration = _registrations.FirstOrDefault(value => value.Descriptor.FactoryKey == key)!;
        return registration is not null;
    }

    private static Hash256 Compute(IReadOnlyList<LiveAudioParticipantFactoryRegistrationV1> registrations)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(registrations.Count);
        foreach (var registration in registrations)
        {
            var value = registration.Descriptor;
            writer.WriteStartMap(9); writer.WriteUInt64(1); writer.WriteTextString(value.FactoryKey.ToString());
            writer.WriteUInt64(2); writer.WriteUInt64((ushort)value.Owner); writer.WriteUInt64(3); writer.WriteUInt64((ushort)value.GenerationFence);
            writer.WriteUInt64(4); writer.WriteStartArray(value.Dependencies.Count); foreach (var item in value.Dependencies) writer.WriteTextString(item.ToString()); writer.WriteEndArray();
            writer.WriteUInt64(5); writer.WriteStartArray(value.CapacityDimensions.Count); foreach (var item in value.CapacityDimensions) writer.WriteUInt64(item.Value); writer.WriteEndArray();
            writer.WriteUInt64(6); writer.WriteInt64(value.MaximumPrepareDuration.Nanoseconds);
            writer.WriteUInt64(7); writer.WriteInt64(value.MaximumDrainDuration.Nanoseconds);
            writer.WriteUInt64(8); writer.WriteInt64(value.MaximumTerminateDuration.Nanoseconds);
            writer.WriteUInt64(9); writer.WriteTextString(registration.FactoryIdentity);
            writer.WriteEndMap();
        }
        writer.WriteEndArray(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.live-audio-participant-catalog.v1@1.0\0"u8); hash.AppendData(writer.Encode());
        return Hash256.FromBytes(hash.GetHashAndReset());
    }
}
