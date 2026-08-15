namespace HPD.Agent.Authority;

/// <summary>Identifies the physical unit conserved by a registered capacity dimension.</summary>
public enum CapacityUnitV1 : ushort
{
    /// <summary>Bytes of resident or accounted data.</summary>
    Bytes = 1,
    /// <summary>Discrete resident or accounted items.</summary>
    Items = 2,
    /// <summary>Nanoseconds of buffered media duration.</summary>
    Nanoseconds = 3,
    /// <summary>Discrete audio samples.</summary>
    Samples = 4,
    /// <summary>Consumable logical tokens.</summary>
    Tokens = 5,
    /// <summary>Exclusive concurrent-operation slots.</summary>
    Slots = 6,
}

/// <summary>Identifies how an admitted capacity amount is conserved and settled.</summary>
public enum CapacityConservationV1 : ushort
{
    /// <summary>The amount remains charged while the resource is resident.</summary>
    Resident = 1,
    /// <summary>The used amount is consumed and any unused remainder is released.</summary>
    Consumable = 2,
    /// <summary>The amount remains accounted through a registered time window.</summary>
    RateWindow = 3,
    /// <summary>The amount represents an exclusive slot with explicit ownership.</summary>
    Exclusive = 4,
}

/// <summary>Identifies the bounded emergency reserve available to a capacity dimension.</summary>
public enum CapacityEmergencyClassV1 : ushort
{
    /// <summary>No emergency reserve is available.</summary>
    None = 0,
    /// <summary>The reserve is restricted to mandatory authority work.</summary>
    Authority = 1,
    /// <summary>The reserve is restricted to mandatory privacy work.</summary>
    Privacy = 2,
    /// <summary>The reserve is restricted to bounded recovery work.</summary>
    Recovery = 3,
}

/// <summary>Identifies the canonical scope family to which a capacity charge applies.</summary>
public enum CapacityScopeKindV1 : ushort
{
    /// <summary>A tenant scope identified by <see cref="TenantId"/>.</summary>
    Tenant = 1,
    /// <summary>A durable session scope identified by <see cref="SessionId"/>.</summary>
    Session = 2,
    /// <summary>A live participant scope identified by <see cref="ParticipantId"/>.</summary>
    Participant = 3,
    /// <summary>An operation scope identified by <see cref="OperationId"/>.</summary>
    Operation = 4,
    /// <summary>A provider scope identified by <see cref="ProviderId"/>.</summary>
    Provider = 5,
    /// <summary>An output sink scope identified by <see cref="SinkGenerationId"/>.</summary>
    Sink = 6,
    /// <summary>A subscriber scope identified by <see cref="SubscriberId"/>.</summary>
    Subscriber = 7,
    /// <summary>A custodian scope identified by <see cref="CustodianDescriptorId"/>.</summary>
    Custodian = 8,
    /// <summary>A schema scope identified by <see cref="SchemaId"/>.</summary>
    Schema = 9,
    /// <summary>An exporter scope identified by <see cref="ExportId"/>.</summary>
    Exporter = 10,
    /// <summary>An owner-slice scope identified by <see cref="OwnerSliceId"/>.</summary>
    Owner = 11,
}

/// <summary>Identifies one entry in the generated S2 capacity-dimension registry.</summary>
public readonly record struct CapacityDimensionId
{
    private const ushort MaximumRegisteredValue = 14;

    /// <summary>Initializes a registered capacity-dimension identifier.</summary>
    /// <param name="value">The positive generated registry value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not registered by version 1.</exception>
    public CapacityDimensionId(ushort value)
    {
        if (value is 0 or > MaximumRegisteredValue)
            throw new ArgumentOutOfRangeException(nameof(value), "The capacity dimension is not registered by version 1.");
        Value = value;
    }

    /// <summary>Gets the generated positive numeric value.</summary>
    public ushort Value { get; }

    /// <summary>Gets whether this value names a registered version-1 dimension.</summary>
    public bool IsValid => Value is > 0 and <= MaximumRegisteredValue;

    /// <summary>Returns the invariant decimal registry value.</summary>
    public override string ToString() => IsValid ? Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
}

/// <summary>Describes one immutable generated S2 capacity dimension.</summary>
public sealed record CapacityDimensionDescriptorV1
{
    internal CapacityDimensionDescriptorV1(
        CapacityDimensionId id,
        string token,
        CapacityUnitV1 unit,
        CapacityConservationV1 conservation,
        IReadOnlyList<CapacityScopeKindV1> scopeKinds,
        CapacityEmergencyClassV1 emergencyClass,
        long maximumPerCharge,
        ushort schemaVersion,
        string settlementEvidence)
    {
        Id = id;
        Token = token;
        Unit = unit;
        Conservation = conservation;
        ScopeKinds = scopeKinds;
        EmergencyClass = emergencyClass;
        MaximumPerCharge = maximumPerCharge;
        SchemaVersion = schemaVersion;
        SettlementEvidence = settlementEvidence;
    }

    /// <summary>Gets the immutable numeric dimension identity.</summary>
    public CapacityDimensionId Id { get; }
    /// <summary>Gets the sole semantic owner of the dimension registry entry.</summary>
    public OwnerSliceId Owner => OwnerSliceId.S2;
    /// <summary>Gets the stable lowercase ASCII token.</summary>
    public string Token { get; }
    /// <summary>Gets the conserved unit.</summary>
    public CapacityUnitV1 Unit { get; }
    /// <summary>Gets the conservation law.</summary>
    public CapacityConservationV1 Conservation { get; }
    /// <summary>Gets the canonical allowed scope kinds in registry order.</summary>
    public IReadOnlyList<CapacityScopeKindV1> ScopeKinds { get; }
    /// <summary>Gets the restricted emergency-reserve class.</summary>
    public CapacityEmergencyClassV1 EmergencyClass { get; }
    /// <summary>Gets the maximum amount permitted in one charge.</summary>
    public long MaximumPerCharge { get; }
    /// <summary>Gets the positive descriptor schema version.</summary>
    public ushort SchemaVersion { get; }
    /// <summary>Gets the stable settlement-evidence discriminator.</summary>
    public string SettlementEvidence { get; }
}
