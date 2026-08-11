namespace HPD.Agent.Authority;

/// <summary>Identifies the folded lifecycle of one admitted capacity grant.</summary>
public enum CapacityGrantStateV1 : ushort
{
    /// <summary>No quantity has been activated or terminalized.</summary>
    Reserved = 1,
    /// <summary>Some quantity is active and none has terminal evidence.</summary>
    Active = 2,
    /// <summary>Some quantity is terminal while another quantity remains.</summary>
    Settling = 3,
    /// <summary>Every quantity was released or consumed.</summary>
    Settled = 4,
    /// <summary>Every quantity is terminal and at least one was revoked, with none unknown.</summary>
    Revoked = 5,
    /// <summary>Every quantity is terminal and at least one has unknown disposition.</summary>
    Unknown = 6,
}

/// <summary>Configures one immutable per-identity scope limit and its bounded emergency reserve.</summary>
public sealed record CapacityScopeLimitV1
{
    /// <summary>Initializes one registered dimension/scope limit.</summary>
    /// <param name="dimensionId">The registered capacity dimension.</param>
    /// <param name="scopeKind">One scope kind allowed by the dimension.</param>
    /// <param name="normalLimit">The positive ordinary capacity.</param>
    /// <param name="emergencyReserve">The nonnegative reserve restricted to the dimension's emergency class.</param>
    /// <exception cref="ArgumentException">The dimension does not allow the scope kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is invalid or their sum overflows.</exception>
    internal CapacityScopeLimitV1(CapacityDimensionId dimensionId, CapacityScopeKindV1 scopeKind, long normalLimit, long emergencyReserve)
    {
        var descriptor = CapacityDimensionRegistryV1.Get(dimensionId);
        if (!descriptor.ScopeKinds.Contains(scopeKind)) throw new ArgumentException("The scope kind is not registered for the dimension.", nameof(scopeKind));
        if (normalLimit <= 0) throw new ArgumentOutOfRangeException(nameof(normalLimit));
        if (emergencyReserve < 0 || normalLimit > long.MaxValue - emergencyReserve) throw new ArgumentOutOfRangeException(nameof(emergencyReserve));
        DimensionId = dimensionId; ScopeKind = scopeKind; NormalLimit = normalLimit; EmergencyReserve = emergencyReserve;
    }

    /// <summary>Gets the registered dimension.</summary>
    public CapacityDimensionId DimensionId { get; }
    /// <summary>Gets the canonical scope kind.</summary>
    public CapacityScopeKindV1 ScopeKind { get; }
    /// <summary>Gets the positive ordinary capacity.</summary>
    public long NormalLimit { get; }
    /// <summary>Gets the bounded emergency reserve.</summary>
    public long EmergencyReserve { get; }
}

/// <summary>Projects the conserved quantities of one granted charge.</summary>
public sealed record CapacityChargeBalanceV1
{
    internal CapacityChargeBalanceV1(CapacityChargeV1 charge, long normal, long reserve, long unactivated, long active,
        long released, long consumed, long agedOut, long revoked, long unknown, long encumberedNormal, long encumberedReserve)
    {
        Charge = charge; NormalAllocation = normal; ReserveAllocation = reserve; Unactivated = unactivated; Active = active;
        Released = released; Consumed = consumed; AgedOut = agedOut; Revoked = revoked; ExplicitlyUnknown = unknown;
        EncumberedNormal = encumberedNormal; EncumberedReserve = encumberedReserve;
    }
    /// <summary>Gets the original granted charge.</summary>
    public CapacityChargeV1 Charge { get; }
    /// <summary>Gets the original amount allocated from ordinary capacity.</summary>
    public long NormalAllocation { get; }
    /// <summary>Gets the original amount allocated from emergency reserve.</summary>
    public long ReserveAllocation { get; }
    /// <summary>Gets remaining quantity not yet activated.</summary>
    public long Unactivated { get; }
    /// <summary>Gets currently active quantity.</summary>
    public long Active { get; }
    /// <summary>Gets released quantity.</summary>
    public long Released { get; }
    /// <summary>Gets consumed quantity.</summary>
    public long Consumed { get; }
    /// <summary>Gets rate-window quantity released only by admitted aging evidence.</summary>
    public long AgedOut { get; }
    /// <summary>Gets revoked quantity.</summary>
    public long Revoked { get; }
    /// <summary>Gets quantity whose disposition is explicitly unknown.</summary>
    public long ExplicitlyUnknown { get; }
    /// <summary>Gets quantity that still encumbers ordinary capacity.</summary>
    public long EncumberedNormal { get; }
    /// <summary>Gets quantity that still encumbers the emergency reserve.</summary>
    public long EncumberedReserve { get; }
}

/// <summary>Projects one grant reconstructed solely from admitted capacity facts.</summary>
public sealed record CapacityGrantSnapshotV1
{
    internal CapacityGrantSnapshotV1(CapacityGrantId grantId, OperationId operationId, ExpectedAuthorityVectorV1 authority,
        JournalPositionV1 grantedAt, JournalPositionV1 currentFact, CapacityGrantExpiryV1 expiry,
        CapacityGrantStateV1 state, IReadOnlyList<CapacityChargeBalanceV1> balances)
    {
        GrantId = grantId; OperationId = operationId; Authority = authority; GrantedAt = grantedAt; CurrentFact = currentFact;
        ExpiresAt = expiry; State = state; Balances = balances;
    }
    /// <summary>Gets the deterministic grant identity.</summary>
    public CapacityGrantId GrantId { get; }
    /// <summary>Gets the original reservation operation.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the historical authority fence bound to the grant.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the reservation fact position.</summary>
    public JournalPositionV1 GrantedAt { get; }
    /// <summary>Gets the latest admitted transition fact.</summary>
    public JournalPositionV1 CurrentFact { get; }
    /// <summary>Gets the explicit expiry policy.</summary>
    public CapacityGrantExpiryV1 ExpiresAt { get; }
    /// <summary>Gets the derived aggregate lifecycle.</summary>
    public CapacityGrantStateV1 State { get; }
    /// <summary>Gets immutable balances in original canonical charge order.</summary>
    public IReadOnlyList<CapacityChargeBalanceV1> Balances { get; }
}

internal abstract record CapacityLedgerEntryV1
{
    private CapacityLedgerEntryV1() { }
    internal sealed record Reservation(JournalPositionV1 Position, SessionAuthorityStampV1 OuterSession,
        ExpectedAuthorityVectorV1 OuterAuthority, CapacityReservationFactBodyV1 Body) : CapacityLedgerEntryV1;
    internal sealed record Settlement(JournalPositionV1 Position, SessionAuthorityStampV1 OuterSession,
        ExpectedAuthorityVectorV1 OuterAuthority, CapacitySettlementFactBodyV1 Body) : CapacityLedgerEntryV1;
}

internal abstract record CapacityLedgerFoldResultV1
{
    private CapacityLedgerFoldResultV1() { }
    internal sealed record Current(long LastPosition, IReadOnlyList<CapacityGrantSnapshotV1> Grants) : CapacityLedgerFoldResultV1;
    internal sealed record InvalidHistory(string SafeCode, long LastVerifiedPosition) : CapacityLedgerFoldResultV1;
}

internal static class CapacityLedgerReducerV1
{
    internal static CapacityLedgerFoldResultV1 Fold(IEnumerable<CapacityLedgerEntryV1> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var limitMap = CapacityGovernanceRegistryV1.All.ToDictionary(limit => (limit.DimensionId, limit.ScopeKind));
        var grants = new Dictionary<CapacityGrantId, MutableGrant>();
        SessionAuthorityStampV1? session = null; long previous = 0;
        foreach (var entry in entries)
        {
            if (entry is null) return Invalid("null-entry", previous);
            var position = entry switch { CapacityLedgerEntryV1.Reservation x => x.Position, CapacityLedgerEntryV1.Settlement x => x.Position, _ => default };
            if (!position.IsValid || position.Sequence <= previous || session is { } pinned && position.Session != pinned)
                return Invalid("position-order", previous);
            session ??= position.Session;
            string? error;
            try
            {
                error = entry switch
                {
                    CapacityLedgerEntryV1.Reservation reservation => ApplyReservation(reservation, limitMap, grants),
                    CapacityLedgerEntryV1.Settlement settlement => ApplySettlement(settlement, grants),
                    _ => "unknown-entry",
                };
            }
            catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
            {
                return Invalid("capacity-arithmetic", previous);
            }
            if (error is not null) return Invalid(error, previous);
            previous = position.Sequence;
        }
        try
        {
            var snapshots = grants.Values.OrderBy(x => x.GrantedAt.Sequence).Select(x => x.Snapshot()).ToArray();
            return new CapacityLedgerFoldResultV1.Current(previous, Array.AsReadOnly(snapshots));
        }
        catch (OverflowException)
        {
            return Invalid("capacity-arithmetic", previous);
        }
    }

    private static string? ApplyReservation(CapacityLedgerEntryV1.Reservation entry,
        IReadOnlyDictionary<(CapacityDimensionId, CapacityScopeKindV1), CapacityScopeLimitV1> limits,
        IDictionary<CapacityGrantId, MutableGrant> grants)
    {
        var body = entry.Body;
        if (body is null || entry.OuterAuthority is null || entry.Position.Session != entry.OuterSession ||
            entry.OuterSession != body.Request.Authority.Session || entry.OuterAuthority != body.Request.Authority || grants.ContainsKey(body.GrantId))
            return "reservation-binding";
        var allocations = new List<MutableBalance>(body.Request.Charges.Count);
        var staged = new Dictionary<BalanceKey, (long Normal, long Reserve)>();
        foreach (var charge in body.Request.Charges)
        {
            if (!limits.TryGetValue((charge.DimensionId, charge.Scope.Kind), out var limit)) return "missing-limit";
            var key = BalanceKey.Create(charge.DimensionId, charge.Scope);
            var usage = Usage(grants.Values, key);
            if (staged.TryGetValue(key, out var local)) usage = (checked(usage.Normal + local.Normal), checked(usage.Reserve + local.Reserve));
            var normal = Math.Min(charge.Amount, limit.NormalLimit - usage.Normal);
            if (normal < 0) normal = 0;
            var reserveNeeded = charge.Amount - normal;
            var reserveAllowed = ReserveAllowed(body.Request.Priority, CapacityDimensionRegistryV1.Get(charge.DimensionId).EmergencyClass);
            if (reserveNeeded > 0 && (!reserveAllowed || reserveNeeded > limit.EmergencyReserve - usage.Reserve)) return "capacity-exceeded";
            staged[key] = staged.TryGetValue(key, out local) ? (checked(local.Normal + normal), checked(local.Reserve + reserveNeeded)) : (normal, reserveNeeded);
            allocations.Add(new MutableBalance(charge, normal, reserveNeeded));
        }
        grants.Add(body.GrantId, new MutableGrant(body, entry.Position, allocations));
        return null;
    }

    private static string? ApplySettlement(CapacityLedgerEntryV1.Settlement entry, IDictionary<CapacityGrantId, MutableGrant> grants)
    {
        var body = entry.Body;
        if (body is null || !grants.TryGetValue(body.GrantId, out var grant) || entry.Position.Session != entry.OuterSession ||
            entry.OuterSession != grant.Authority.Session || entry.OuterAuthority != grant.Authority || body.ExpectedFact != grant.CurrentFact ||
            body.ExpectedFact.Session != entry.Position.Session || body.EvidenceAt.CompareTo(grant.Deadline) == ClockComparison.Incomparable ||
            !grant.SettlementOperations.Add(body.OperationId))
            return "settlement-binding";
        var staged = grant.Balances.Select(x => x.Clone()).ToArray();
        foreach (var evidence in body.Charges)
        {
            var index = Array.FindIndex(staged, x => x.Matches(evidence));
            if (index < 0 || !staged[index].Apply(body.Kind, evidence.Amount, body.EvidenceAt)) return "settlement-contradiction";
        }
        grant.Balances = staged; grant.CurrentFact = entry.Position;
        return null;
    }

    private static (long Normal, long Reserve) Usage(IEnumerable<MutableGrant> grants, BalanceKey key)
    {
        long normal = 0, reserve = 0;
        foreach (var balance in grants.SelectMany(x => x.Balances).Where(x => BalanceKey.Create(x.Charge.DimensionId, x.Charge.Scope) == key))
        {
            normal = checked(normal + balance.EncumberedNormal);
            reserve = checked(reserve + balance.EncumberedReserve);
        }
        return (normal, reserve);
    }

    private static bool ReserveAllowed(CapacityPriorityV1 priority, CapacityEmergencyClassV1 emergency) =>
        (priority, emergency) is (CapacityPriorityV1.Authority, CapacityEmergencyClassV1.Authority) or
            (CapacityPriorityV1.Privacy, CapacityEmergencyClassV1.Privacy) or
            (CapacityPriorityV1.Recovery, CapacityEmergencyClassV1.Recovery);

    private static CapacityLedgerFoldResultV1.InvalidHistory Invalid(string code, long position) => new(code, position);

    private sealed class MutableGrant
    {
        internal MutableGrant(CapacityReservationFactBodyV1 body, JournalPositionV1 position, List<MutableBalance> balances)
        { GrantId = body.GrantId; OperationId = body.Request.OperationId; Authority = body.Request.Authority; Deadline = body.Request.Deadline; ExpiresAt = body.ExpiresAt; GrantedAt = position; CurrentFact = position; Balances = balances.ToArray(); }
        internal CapacityGrantId GrantId; internal OperationId OperationId; internal ExpectedAuthorityVectorV1 Authority;
        internal MonotonicStampV1 Deadline; internal CapacityGrantExpiryV1 ExpiresAt; internal JournalPositionV1 GrantedAt; internal JournalPositionV1 CurrentFact;
        internal MutableBalance[] Balances; internal HashSet<OperationId> SettlementOperations = [];
        internal CapacityGrantSnapshotV1 Snapshot()
        {
            var projected = Balances.Select(x => x.Snapshot()).ToArray();
            var remaining = projected.Sum(x => x.Unactivated + x.Active); var terminal = projected.Sum(x => x.Released + x.Consumed + x.AgedOut + x.Revoked + x.ExplicitlyUnknown);
            var state = terminal == 0 ? (projected.Sum(x => x.Active) == 0 ? CapacityGrantStateV1.Reserved : CapacityGrantStateV1.Active)
                : remaining > 0 ? CapacityGrantStateV1.Settling
                : projected.Sum(x => x.ExplicitlyUnknown) > 0 ? CapacityGrantStateV1.Unknown
                : projected.Sum(x => x.Revoked) > 0 ? CapacityGrantStateV1.Revoked : CapacityGrantStateV1.Settled;
            return new(GrantId, OperationId, Authority, GrantedAt, CurrentFact, ExpiresAt, state, Array.AsReadOnly(projected));
        }
    }

    private sealed class MutableBalance
    {
        internal MutableBalance(CapacityChargeV1 charge, long normal, long reserve)
        {
            Charge = charge; NormalAllocation = normal; ReserveAllocation = reserve; Unactivated = charge.Amount;
            EncumberedNormal = normal; EncumberedReserve = reserve;
        }
        private MutableBalance(MutableBalance x)
        {
            Charge=x.Charge; NormalAllocation=x.NormalAllocation; ReserveAllocation=x.ReserveAllocation; Unactivated=x.Unactivated;
            Active=x.Active; Released=x.Released; Consumed=x.Consumed; AgedOut=x.AgedOut; Revoked=x.Revoked; Unknown=x.Unknown;
            EncumberedNormal=x.EncumberedNormal; EncumberedReserve=x.EncumberedReserve;
        }
        internal CapacityChargeV1 Charge;
        internal long NormalAllocation, ReserveAllocation, Unactivated, Active, Released, Consumed, AgedOut, Revoked, Unknown;
        internal long EncumberedNormal, EncumberedReserve;
        internal MutableBalance Clone() => new(this);
        internal bool Matches(CapacitySettlementChargeV1 x) => Charge.DimensionId == x.DimensionId && Charge.Scope == x.Scope && Charge.Purpose == x.Purpose;
        internal bool Apply(CapacitySettlementKindV1 kind, long amount, MonotonicStampV1 evidenceAt)
        {
            if (amount <= 0) return false;
            if (kind == CapacitySettlementKindV1.Activated) { if (amount > Unactivated) return false; Unactivated -= amount; Active += amount; return true; }
            var conservation = CapacityDimensionRegistryV1.Get(Charge.DimensionId).Conservation;
            if (kind is CapacitySettlementKindV1.Consumed or CapacitySettlementKindV1.RecoveredConsumed &&
                conservation is not (CapacityConservationV1.Consumable or CapacityConservationV1.RateWindow)) return false;
            if (kind == CapacitySettlementKindV1.Consumed)
            { if (amount > Active) return false; Active -= amount; Consumed += amount; return true; }
            if (kind == CapacitySettlementKindV1.RecoveredReleased)
            {
                if (!TakeRepair(amount)) return false;
                Released += amount; Free(amount); return true;
            }
            if (kind == CapacitySettlementKindV1.RecoveredConsumed)
            {
                if (!TakeRepair(amount)) return false;
                Consumed += amount; return true;
            }
            if (kind == CapacitySettlementKindV1.WindowAgedOut)
            {
                if (conservation != CapacityConservationV1.RateWindow || amount > Consumed ||
                    Charge.Window is not CapacityChargeWindowV1.EndsAt at || evidenceAt.CompareTo(at.Value) is ClockComparison.Earlier or ClockComparison.Incomparable)
                    return false;
                Consumed -= amount; AgedOut += amount; Free(amount); return true;
            }
            if (amount > Active + Unactivated) return false;
            var fromActive = Math.Min(Active, amount); Active -= fromActive; Unactivated -= amount - fromActive;
            if (kind == CapacitySettlementKindV1.Released) { Released += amount; Free(amount); }
            else if (kind == CapacitySettlementKindV1.Revoked) Revoked += amount;
            else if (kind == CapacitySettlementKindV1.MarkedUnknown) Unknown += amount;
            else return false;
            return true;
        }
        private bool TakeRepair(long amount)
        {
            if (amount > Unknown + Revoked) return false;
            var fromUnknown = Math.Min(Unknown, amount); Unknown -= fromUnknown; Revoked -= amount - fromUnknown;
            return true;
        }
        private void Free(long amount)
        {
            var fromReserve = Math.Min(EncumberedReserve, amount); EncumberedReserve -= fromReserve;
            EncumberedNormal -= amount - fromReserve;
            if (EncumberedNormal < 0) throw new InvalidOperationException("Capacity encumbrance underflowed.");
        }
        internal CapacityChargeBalanceV1 Snapshot() => new(Charge, NormalAllocation, ReserveAllocation, Unactivated, Active,
            Released, Consumed, AgedOut, Revoked, Unknown, EncumberedNormal, EncumberedReserve);
    }

    private readonly record struct BalanceKey(CapacityDimensionId Dimension, CapacityScopeKindV1 Kind, string Identity)
    {
        internal static BalanceKey Create(CapacityDimensionId dimension, CapacityScopeV1 scope)
        {
            Span<byte> tenant = stackalloc byte[16]; if (!scope.TenantId.TryWriteBytes(tenant)) throw new InvalidOperationException();
            var identity = Convert.ToHexString(tenant);
            if (scope.Kind == CapacityScopeKindV1.Session)
            { Span<byte> session = stackalloc byte[16]; if (!scope.SessionId!.Value.TryWriteBytes(session)) throw new InvalidOperationException(); identity += Convert.ToHexString(session); }
            else if (scope.Subject is { } subject)
            { Span<byte> value = stackalloc byte[16]; if (!subject.TryWriteIdentity(value, out var written)) throw new InvalidOperationException(); identity += ((ushort)subject.Kind).ToString("X4") + Convert.ToHexString(value[..written]); }
            return new(dimension, scope.Kind, identity);
        }
    }
}
