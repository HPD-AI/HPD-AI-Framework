namespace HPD.Agent.Authority;

/// <summary>Identifies the closed wire arm of a capacity-grant expiry.</summary>
public enum CapacityGrantExpiryKindV1 : ushort
{
    /// <summary>The grant has no time-based recovery eligibility.</summary>
    NoExpiry = 1,
    /// <summary>The grant has one explicit monotonic recovery-eligibility instant.</summary>
    At = 2,
}

/// <summary>Represents the closed expiry policy of an admitted capacity grant.</summary>
public abstract record CapacityGrantExpiryV1
{
    private CapacityGrantExpiryV1() { }

    /// <summary>Gets the exact closed wire arm.</summary>
    public abstract CapacityGrantExpiryKindV1 Kind { get; }

    /// <summary>Represents a grant with no time-based recovery eligibility.</summary>
    public sealed record NoExpiry : CapacityGrantExpiryV1
    {
        /// <inheritdoc />
        public override CapacityGrantExpiryKindV1 Kind => CapacityGrantExpiryKindV1.NoExpiry;
    }

    /// <summary>Represents a grant that becomes recovery-eligible at one comparable monotonic instant.</summary>
    public sealed record At : CapacityGrantExpiryV1
    {
        /// <summary>Initializes a bounded expiry instant.</summary>
        /// <param name="value">The required monotonic instant.</param>
        /// <exception cref="ArgumentException">The instant is invalid.</exception>
        public At(MonotonicStampV1 value)
        {
            if (!value.IsValid) throw new ArgumentException("A monotonic expiry is required.", nameof(value));
            Value = value;
        }

        /// <summary>Gets the monotonic expiry instant.</summary>
        public MonotonicStampV1 Value { get; }

        /// <inheritdoc />
        public override CapacityGrantExpiryKindV1 Kind => CapacityGrantExpiryKindV1.At;
    }
}

/// <summary>Identifies one closed kind of capacity settlement evidence.</summary>
public enum CapacitySettlementKindV1 : ushort
{
    /// <summary>Moves listed reserved quantities into active use.</summary>
    Activated = 1,
    /// <summary>Releases listed resident or exclusive quantities.</summary>
    Released = 2,
    /// <summary>Settles listed consumable or rate-window quantities as used.</summary>
    Consumed = 3,
    /// <summary>Marks the disposition of listed quantities explicitly unknown.</summary>
    MarkedUnknown = 4,
    /// <summary>Revokes listed remaining quantities without claiming use or release.</summary>
    Revoked = 5,
}

/// <summary>Names one positive quantity in a capacity settlement fact.</summary>
public sealed record CapacitySettlementChargeV1
{
    /// <summary>Initializes a validated settlement quantity.</summary>
    /// <param name="dimensionId">The registered dimension.</param>
    /// <param name="scope">The exact granted scope.</param>
    /// <param name="purpose">The exact granted purpose.</param>
    /// <param name="amount">The positive settled amount.</param>
    /// <exception cref="ArgumentException">A scope, purpose, or dimension/scope pairing is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The amount is outside the dimension's per-charge bound.</exception>
    public CapacitySettlementChargeV1(CapacityDimensionId dimensionId, CapacityScopeV1 scope, CapacityPurposeId purpose, long amount)
    {
        var validated = new CapacityChargeV1(dimensionId, scope, amount, purpose);
        DimensionId = validated.DimensionId;
        Scope = validated.Scope;
        Purpose = validated.Purpose;
        Amount = validated.Amount;
    }

    /// <summary>Gets the registered dimension.</summary>
    public CapacityDimensionId DimensionId { get; }
    /// <summary>Gets the exact granted scope.</summary>
    public CapacityScopeV1 Scope { get; }
    /// <summary>Gets the exact granted purpose.</summary>
    public CapacityPurposeId Purpose { get; }
    /// <summary>Gets the positive quantity.</summary>
    public long Amount { get; }
}

/// <summary>Contains the canonical body of one admitted S2 capacity reservation.</summary>
public sealed record CapacityReservationFactBodyV1
{
    /// <summary>Initializes a reservation body whose identity is derived from its operation.</summary>
    /// <param name="grantId">The deterministic S2 grant identity.</param>
    /// <param name="request">The deeply owned reservation request.</param>
    /// <param name="expiresAt">The explicit grant expiry policy.</param>
    /// <exception cref="ArgumentNullException">The request or expiry policy is missing.</exception>
    /// <exception cref="ArgumentException">The grant, request, derived identity, or expiry is invalid.</exception>
    public CapacityReservationFactBodyV1(CapacityGrantId grantId, CapacityRequestV1 request, CapacityGrantExpiryV1 expiresAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(expiresAt);
        if (!grantId.IsValid || grantId != CapacityGrantIdDerivationV1.Derive(request.OperationId))
            throw new ArgumentException("The grant identity does not match the request operation.", nameof(grantId));
        if (expiresAt is CapacityGrantExpiryV1.At at &&
            (at.Value.ClockDomainId != request.Deadline.ClockDomainId || at.Value.BootId != request.Deadline.BootId ||
             at.Value.CompareTo(request.Deadline) != ClockComparison.Later))
            throw new ArgumentException("A finite grant expiry must follow the request deadline on the same clock and boot.", nameof(expiresAt));
        GrantId = grantId;
        Request = request;
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the deterministic grant identity.</summary>
    public CapacityGrantId GrantId { get; }
    /// <summary>Gets the immutable reservation request.</summary>
    public CapacityRequestV1 Request { get; }
    /// <summary>Gets the explicit expiry policy.</summary>
    public CapacityGrantExpiryV1 ExpiresAt { get; }
}

/// <summary>Contains the canonical body of one predecessor-fenced S2 settlement fact.</summary>
public sealed record CapacitySettlementFactBodyV1
{
    /// <summary>Initializes a validated settlement body and deeply owns its sorted charge set.</summary>
    /// <param name="grantId">The grant being settled.</param>
    /// <param name="operationId">The settlement-effect idempotency identity.</param>
    /// <param name="expectedFact">The exact prior grant transition fact.</param>
    /// <param name="kind">The closed settlement kind.</param>
    /// <param name="charges">One to 256 distinct settlement quantities.</param>
    /// <exception cref="ArgumentNullException">The charge collection is missing.</exception>
    /// <exception cref="ArgumentException">An identity, predecessor, kind, or charge is invalid or duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The charge count is outside 1..256.</exception>
    public CapacitySettlementFactBodyV1(CapacityGrantId grantId, OperationId operationId, JournalPositionV1 expectedFact,
        CapacitySettlementKindV1 kind, IEnumerable<CapacitySettlementChargeV1> charges)
    {
        if (!grantId.IsValid) throw new ArgumentException("A grant identity is required.", nameof(grantId));
        if (!operationId.IsValid) throw new ArgumentException("A settlement operation identity is required.", nameof(operationId));
        if (!expectedFact.IsValid) throw new ArgumentException("A predecessor fact is required.", nameof(expectedFact));
        if (!Enum.IsDefined(kind)) throw new ArgumentException("A registered settlement kind is required.", nameof(kind));
        ArgumentNullException.ThrowIfNull(charges);
        var owned = new List<CapacitySettlementChargeV1>(CapacityRequestV1.MaximumCharges);
        foreach (var charge in charges)
        {
            if (owned.Count == CapacityRequestV1.MaximumCharges) throw new ArgumentOutOfRangeException(nameof(charges));
            owned.Add(charge ?? throw new ArgumentException("A settlement charge cannot be null.", nameof(charges)));
        }
        if (owned.Count == 0) throw new ArgumentOutOfRangeException(nameof(charges));
        owned.Sort(CapacitySettlementChargeComparerV1.Instance);
        for (var index = 1; index < owned.Count; index++)
            if (CapacitySettlementChargeComparerV1.Instance.Compare(owned[index - 1], owned[index]) == 0)
                throw new ArgumentException("Duplicate settlement balance keys are forbidden.", nameof(charges));
        GrantId = grantId;
        OperationId = operationId;
        ExpectedFact = expectedFact;
        Kind = kind;
        Charges = Array.AsReadOnly(owned.ToArray());
    }

    /// <summary>Gets the settled grant.</summary>
    public CapacityGrantId GrantId { get; }
    /// <summary>Gets the settlement-effect idempotency identity.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the exact prior transition fact.</summary>
    public JournalPositionV1 ExpectedFact { get; }
    /// <summary>Gets the closed evidence kind.</summary>
    public CapacitySettlementKindV1 Kind { get; }
    /// <summary>Gets the canonical sorted, deeply owned settlement quantities.</summary>
    public IReadOnlyList<CapacitySettlementChargeV1> Charges { get; }
}

internal sealed class CapacitySettlementChargeComparerV1 : IComparer<CapacitySettlementChargeV1>
{
    internal static CapacitySettlementChargeComparerV1 Instance { get; } = new();

    public int Compare(CapacitySettlementChargeV1? left, CapacitySettlementChargeV1? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return CapacityChargeComparerV1.Instance.Compare(
            new CapacityChargeV1(left.DimensionId, left.Scope, left.Amount, left.Purpose),
            new CapacityChargeV1(right.DimensionId, right.Scope, right.Amount, right.Purpose));
    }
}
