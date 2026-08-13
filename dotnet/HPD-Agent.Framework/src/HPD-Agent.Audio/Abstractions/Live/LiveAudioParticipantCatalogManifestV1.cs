using System.Formats.Cbor;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

/// <summary>Declares the generated allocation policy for one aggregate graph participant.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdGraphParticipantAllocationAttribute : Attribute
{
    private readonly string[] _orderedNodeKeys;
    private readonly ushort[] _dimensions;
    private readonly string[] _purposeIdHex;
    private readonly ulong[] _amounts;
    private readonly byte[] _windowPolicies;

    /// <summary>Initializes one symbolic aggregate graph-participant allocation declaration.</summary>
    /// <param name="orderedNodeKeys">The complete graph node keys in declared graph order.</param>
    /// <param name="dimensions">The charge dimensions.</param>
    /// <param name="purposeIdHex">The lowercase hexadecimal 16-byte purpose identifiers.</param>
    /// <param name="amounts">The positive requested amounts.</param>
    /// <param name="windowPolicies">The symbolic window policies.</param>
    public HpdGraphParticipantAllocationAttribute(string[] orderedNodeKeys, ushort[] dimensions,
        string[] purposeIdHex, ulong[] amounts, byte[] windowPolicies)
    {
        _orderedNodeKeys = (orderedNodeKeys ?? throw new ArgumentNullException(nameof(orderedNodeKeys))).ToArray();
        _dimensions = (dimensions ?? throw new ArgumentNullException(nameof(dimensions))).ToArray();
        _purposeIdHex = (purposeIdHex ?? throw new ArgumentNullException(nameof(purposeIdHex))).ToArray();
        _amounts = (amounts ?? throw new ArgumentNullException(nameof(amounts))).ToArray();
        _windowPolicies = (windowPolicies ?? throw new ArgumentNullException(nameof(windowPolicies))).ToArray();
    }

    /// <summary>Gets a fresh copy of the complete node-key sequence.</summary>
    public string[] OrderedNodeKeys => _orderedNodeKeys.ToArray(); // HpdGraphParticipantAllocation
    /// <summary>Gets a fresh copy of the dimensions.</summary>
    public ushort[] Dimensions => _dimensions.ToArray(); // HpdGraphParticipantAllocation
    /// <summary>Gets a fresh copy of the purpose identifiers.</summary>
    public string[] PurposeIdHex => _purposeIdHex.ToArray(); // HpdGraphParticipantAllocation
    /// <summary>Gets a fresh copy of the amounts.</summary>
    public ulong[] Amounts => _amounts.ToArray(); // HpdGraphParticipantAllocation
    /// <summary>Gets a fresh copy of the window policies.</summary>
    public byte[] WindowPolicies => _windowPolicies.ToArray(); // HpdGraphParticipantAllocation
}

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
    private readonly byte[] _graphParticipantAllocationDeclarationBytes;
    private readonly byte[] _graphParticipantAllocationDeclarationFingerprintBytes;
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
        : this(factoryType, factoryKey, owner, generationFence, maximumPrepareNanoseconds,
            maximumDrainNanoseconds, maximumTerminateNanoseconds, capacityDimensions, dependencies, [], [])
    { }

    /// <summary>Initializes one generated assembly manifest entry with an authenticated allocation carrier.</summary>
    /// <param name="factoryType">The exact declared factory type.</param>
    /// <param name="factoryKey">The application-unique factory key.</param>
    /// <param name="owner">The numeric sole-owner value.</param>
    /// <param name="generationFence">The numeric owner-axis value.</param>
    /// <param name="maximumPrepareNanoseconds">The positive preparation deadline.</param>
    /// <param name="maximumDrainNanoseconds">The positive drain deadline.</param>
    /// <param name="maximumTerminateNanoseconds">The positive termination deadline.</param>
    /// <param name="capacityDimensions">The registered capacity dimension values.</param>
    /// <param name="dependencies">The direct factory-key dependencies.</param>
    /// <param name="graphParticipantAllocationDeclarationBytes">The exact canonical allocation carrier.</param>
    /// <param name="graphParticipantAllocationDeclarationFingerprintBytes">The separate 32-byte allocation fingerprint.</param>
    public HpdLiveAudioParticipantManifestAttribute(Type factoryType, string factoryKey, ushort owner,
        ushort generationFence, long maximumPrepareNanoseconds, long maximumDrainNanoseconds,
        long maximumTerminateNanoseconds, ushort[] capacityDimensions, string[] dependencies,
        byte[] graphParticipantAllocationDeclarationBytes,
        byte[] graphParticipantAllocationDeclarationFingerprintBytes)
    {
        FactoryType = factoryType; FactoryKey = factoryKey; Owner = owner; GenerationFence = generationFence;
        MaximumPrepareNanoseconds = maximumPrepareNanoseconds; MaximumDrainNanoseconds = maximumDrainNanoseconds;
        MaximumTerminateNanoseconds = maximumTerminateNanoseconds; CapacityDimensions = capacityDimensions;
        Dependencies = dependencies;
        _graphParticipantAllocationDeclarationBytes = (graphParticipantAllocationDeclarationBytes ?? throw new ArgumentNullException(nameof(graphParticipantAllocationDeclarationBytes))).ToArray();
        _graphParticipantAllocationDeclarationFingerprintBytes = (graphParticipantAllocationDeclarationFingerprintBytes ?? throw new ArgumentNullException(nameof(graphParticipantAllocationDeclarationFingerprintBytes))).ToArray();
        ValidateCarrier(factoryKey, capacityDimensions, _graphParticipantAllocationDeclarationBytes,
            _graphParticipantAllocationDeclarationFingerprintBytes);
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
    /// <summary>Gets a fresh copy of the exact canonical graph-participant allocation carrier.</summary>
    public byte[] GraphParticipantAllocationDeclarationBytes => _graphParticipantAllocationDeclarationBytes.ToArray();
    /// <summary>Gets a fresh copy of the separate graph-participant allocation fingerprint bytes.</summary>
    public byte[] GraphParticipantAllocationDeclarationFingerprintBytes => _graphParticipantAllocationDeclarationFingerprintBytes.ToArray();

    private static void ValidateCarrier(string factoryKey, ushort[] capacityDimensions, byte[] bytes, byte[] fingerprintBytes)
    {
        if (bytes.Length == 0 && fingerprintBytes.Length == 0) return;
        if (bytes.Length is 0 or > 16384 ||
            fingerprintBytes.Length != 32)
            throw new ArgumentException("Allocation carrier and fingerprint must be completely present and bounded.");
        var fingerprint = Hash256.FromBytes(fingerprintBytes);
        if (!LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(bytes,
                new BoundedAscii(factoryKey), capacityDimensions.Select(static value => new CapacityDimensionId(value)).ToArray(), fingerprint))
            throw new ArgumentException("Allocation carrier is invalid.", nameof(bytes));
    }
}

/// <summary>Binds one generated participant descriptor to its exact factory implementation type.</summary>
/// <remarks>The canonical identity is <c>assembly-simple-name:global::fully-qualified-source-type</c>, emitted directly from Roslyn symbols.</remarks>
public sealed record LiveAudioParticipantFactoryRegistrationV1
{
    private readonly byte[] _graphParticipantAllocationDeclarationBytes;
    /// <summary>Initializes one generated factory registration.</summary>
    /// <param name="factoryType">The exact generated factory implementation type.</param>
    /// <param name="factoryIdentity">The Roslyn-derived assembly and metadata-name identity.</param>
    /// <param name="descriptor">The generated immutable descriptor.</param>
    /// <exception cref="ArgumentNullException">A value is null.</exception>
    /// <exception cref="ArgumentException">The canonical identity is empty, non-ASCII, or longer than 512 bytes.</exception>
    public LiveAudioParticipantFactoryRegistrationV1(Type factoryType, string factoryIdentity,
        LiveAudioParticipantDescriptorV1 descriptor)
        : this(factoryType, factoryIdentity, descriptor, ReadOnlyMemory<byte>.Empty, null)
    { }

    /// <summary>Initializes one generated factory registration with an authenticated allocation carrier.</summary>
    /// <param name="factoryType">The exact generated factory implementation type.</param>
    /// <param name="factoryIdentity">The Roslyn-derived assembly and metadata-name identity.</param>
    /// <param name="descriptor">The generated immutable descriptor.</param>
    /// <param name="graphParticipantAllocationDeclarationBytes">The exact canonical allocation carrier.</param>
    /// <param name="graphParticipantAllocationDeclarationFingerprint">The separate allocation fingerprint, or null for absence.</param>
    public LiveAudioParticipantFactoryRegistrationV1(Type factoryType, string factoryIdentity,
        LiveAudioParticipantDescriptorV1 descriptor,
        ReadOnlyMemory<byte> graphParticipantAllocationDeclarationBytes,
        Hash256? graphParticipantAllocationDeclarationFingerprint)
    {
        FactoryType = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
        ArgumentNullException.ThrowIfNull(factoryIdentity);
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (factoryIdentity.Length is < 1 or > 512 || factoryIdentity.Any(static value => value is < (char)0x21 or > (char)0x7e))
            throw new ArgumentException("A bounded printable-ASCII factory identity is required.", nameof(factoryIdentity));
        FactoryIdentity = factoryIdentity;
        if (graphParticipantAllocationDeclarationBytes.Length == 0 && graphParticipantAllocationDeclarationFingerprint is null)
            _graphParticipantAllocationDeclarationBytes = [];
        else
        {
            if (graphParticipantAllocationDeclarationBytes.Length is 0 or > 16384 ||
                graphParticipantAllocationDeclarationFingerprint is null)
                throw new ArgumentException("Allocation carrier and fingerprint must be completely present and bounded.");
            _graphParticipantAllocationDeclarationBytes = graphParticipantAllocationDeclarationBytes.ToArray();
            if (!LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(
                    _graphParticipantAllocationDeclarationBytes, descriptor.FactoryKey, descriptor.CapacityDimensions,
                    graphParticipantAllocationDeclarationFingerprint.Value))
                throw new ArgumentException("Allocation carrier is invalid.", nameof(graphParticipantAllocationDeclarationBytes));
        }
        GraphParticipantAllocationDeclarationFingerprint = graphParticipantAllocationDeclarationFingerprint;
    }

    /// <summary>Gets the exact factory implementation type.</summary>
    public Type FactoryType { get; }
    /// <summary>Gets the canonical Roslyn-derived assembly and fully-qualified source-type identity.</summary>
    public string FactoryIdentity { get; }
    /// <summary>Gets the generated descriptor.</summary>
    public LiveAudioParticipantDescriptorV1 Descriptor { get; }
    /// <summary>Gets fresh read-only memory containing the exact canonical allocation carrier.</summary>
    public ReadOnlyMemory<byte> GraphParticipantAllocationDeclarationBytes => new(_graphParticipantAllocationDeclarationBytes.ToArray());
    /// <summary>Gets the separate allocation fingerprint, or null when no allocation is declared.</summary>
    public Hash256? GraphParticipantAllocationDeclarationFingerprint { get; }
}

/// <summary>Contains the generated, canonical participant descriptor exact set and fingerprint.</summary>
public sealed class LiveAudioParticipantCatalogManifestV1
{
    private const int MaximumGraphParticipantAllocationNodes = 64;
    private const int MaximumGraphParticipantAllocationTemplates = 14;
    private const int MaximumGraphParticipantAllocationNodeKeyUtf8Bytes = 64;
    private const int MaximumGraphParticipantAllocationCarrierBytes = 16384;
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
        if (ordered.Count(static value => !value.GraphParticipantAllocationDeclarationBytes.IsEmpty) > 1)
            throw new ArgumentException("More than one aggregate graph-participant allocation is forbidden.", nameof(registrations));
        if (ordered.Any(static value => !value.GraphParticipantAllocationDeclarationBytes.IsEmpty &&
                (value.Descriptor.Owner != OwnerSliceId.S2 || value.Descriptor.GenerationFence != AuthorityAxisId.Graph)))
            throw new ArgumentException("The aggregate allocation factory must be owned by S2 with the Graph fence.", nameof(registrations));
        return new LiveAudioParticipantCatalogManifestV1(ordered, Compute(ordered));
    }

    internal bool TryGet(BoundedAscii key, out LiveAudioParticipantFactoryRegistrationV1 registration)
    {
        registration = _registrations.FirstOrDefault(value => value.Descriptor.FactoryKey == key)!;
        return registration is not null;
    }

    internal static byte[] EncodeGraphParticipantAllocationDeclaration(string factoryKey,
        IReadOnlyList<string> orderedNodeKeys, IReadOnlyList<ushort> dimensions,
        IReadOnlyList<string> purposeIdHex, IReadOnlyList<ulong> amounts,
        IReadOnlyList<byte> windowPolicies)
    {
        ArgumentNullException.ThrowIfNull(factoryKey); ArgumentNullException.ThrowIfNull(orderedNodeKeys);
        ArgumentNullException.ThrowIfNull(dimensions); ArgumentNullException.ThrowIfNull(purposeIdHex);
        ArgumentNullException.ThrowIfNull(amounts); ArgumentNullException.ThrowIfNull(windowPolicies);
        if (orderedNodeKeys.Count is < 1 or > MaximumGraphParticipantAllocationNodes ||
            dimensions.Count is < 1 or > MaximumGraphParticipantAllocationTemplates ||
            purposeIdHex.Count != dimensions.Count || amounts.Count != dimensions.Count || windowPolicies.Count != dimensions.Count)
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        var nodes = orderedNodeKeys.ToArray();
        if (nodes.Distinct(StringComparer.Ordinal).Count() != nodes.Length || nodes.Any(static value =>
                value is null || System.Text.Encoding.UTF8.GetByteCount(value) is < 1 or > MaximumGraphParticipantAllocationNodeKeyUtf8Bytes ||
                value.Any(static character => character is < (char)0x21 or > (char)0x7e)))
            throw new ArgumentException("Node keys are invalid.", nameof(orderedNodeKeys));
        var charges = dimensions.Select((dimension,index) => (Dimension:dimension, Purpose:purposeIdHex[index], Amount:amounts[index], Policy:windowPolicies[index]))
            .OrderBy(static value => value.Dimension).ToArray();
        if (charges.Select(static value => value.Dimension).Distinct().Count() != charges.Length ||
            charges.Any(static value => value.Dimension is < 1 or > 14 || value.Amount is 0 or > long.MaxValue || value.Policy is < 1 or > 2 || !TryPurpose(value.Purpose, out _)))
            throw new ArgumentException("Charge templates are invalid.", nameof(dimensions));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(4);
        writer.WriteUInt64(0); writer.WriteUInt64(1); writer.WriteUInt64(1); writer.WriteTextString(factoryKey);
        writer.WriteUInt64(2); writer.WriteStartArray(nodes.Length); foreach (var node in nodes) writer.WriteTextString(node); writer.WriteEndArray();
        writer.WriteUInt64(3); writer.WriteStartArray(charges.Length);
        foreach (var charge in charges)
        {
            TryPurpose(charge.Purpose, out var purposeIdBytes); writer.WriteStartMap(4);
            writer.WriteUInt64(0); writer.WriteUInt64(charge.Dimension); writer.WriteUInt64(1); writer.WriteByteString(purposeIdBytes);
            writer.WriteUInt64(2); writer.WriteUInt64(charge.Amount); writer.WriteUInt64(3); writer.WriteUInt64(charge.Policy); writer.WriteEndMap();
        }
        writer.WriteEndArray(); writer.WriteEndMap(); var encoded = writer.Encode();
        if (encoded.Length > MaximumGraphParticipantAllocationCarrierBytes) throw new ArgumentOutOfRangeException(nameof(orderedNodeKeys));
        return encoded;
    }

    internal static bool TryValidateGraphParticipantAllocationDeclaration(ReadOnlySpan<byte> bytes,
        BoundedAscii expectedFactoryKey, IReadOnlyList<CapacityDimensionId> expectedDimensions, Hash256 expectedFingerprint)
    {
        if (bytes.Length is 0 or > MaximumGraphParticipantAllocationCarrierBytes || expectedDimensions is null) return false;
        try
        {
            var reader = new CborReader(bytes.ToArray(), CborConformanceMode.Ctap2Canonical);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 0 || reader.ReadUInt64() != 1 || reader.ReadUInt64() != 1) return false;
            var factoryKey = reader.ReadTextString(); if (!StringComparer.Ordinal.Equals(factoryKey, expectedFactoryKey.ToString()) || reader.ReadUInt64() != 2) return false;
            var nodeCount = reader.ReadStartArray(); if (nodeCount is null or < 1 or > MaximumGraphParticipantAllocationNodes) return false;
            var nodes = new string[nodeCount.Value]; for (var i=0;i<nodes.Length;i++) nodes[i]=reader.ReadTextString(); reader.ReadEndArray();
            if (reader.ReadUInt64()!=3) return false; var chargeCount=reader.ReadStartArray(); if (chargeCount is null or < 1 or > MaximumGraphParticipantAllocationTemplates) return false;
            var dimensions=new ushort[chargeCount.Value]; var purposes=new string[chargeCount.Value]; var amounts=new ulong[chargeCount.Value]; var policies=new byte[chargeCount.Value];
            for (var i=0;i<dimensions.Length;i++)
            {
                if (reader.ReadStartMap()!=4 || reader.ReadUInt64()!=0) return false; dimensions[i]=checked((ushort)reader.ReadUInt64());
                if (reader.ReadUInt64()!=1) return false; purposes[i]=Convert.ToHexString(reader.ReadByteString()).ToLowerInvariant();
                if (reader.ReadUInt64()!=2) return false; amounts[i]=reader.ReadUInt64(); if (reader.ReadUInt64()!=3) return false; policies[i]=checked((byte)reader.ReadUInt64()); reader.ReadEndMap();
            }
            reader.ReadEndArray(); reader.ReadEndMap(); if (reader.BytesRemaining!=0) return false;
            if (!dimensions.SequenceEqual(expectedDimensions.Select(static value => value.Value).OrderBy(static value => value))) return false;
            var canonical=EncodeGraphParticipantAllocationDeclaration(factoryKey,nodes,dimensions,purposes,amounts,policies); if (!bytes.SequenceEqual(canonical)) return false;
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData("hpd-graph-participant-allocation-declaration-v1\0"u8); hash.AppendData(canonical);
            return Hash256.FromBytes(hash.GetHashAndReset())==expectedFingerprint;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and
            not AccessViolationException and not ThreadAbortException) { return false; }
    }

    private static bool TryPurpose(string value, out byte[] purposeIdBytes)
    {
        purposeIdBytes=[]; if (value is null || value.Length!=32 || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) return false;
        purposeIdBytes=Convert.FromHexString(value); return purposeIdBytes.Any(static item => item!=0);
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
