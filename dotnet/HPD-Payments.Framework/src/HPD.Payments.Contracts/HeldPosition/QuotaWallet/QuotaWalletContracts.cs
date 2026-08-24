using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.HeldPosition.QuotaWallet;

/// <summary>Names the accepted RES-009 policy discriminators.</summary>
public static class QuotaWalletPolicyIds
{
    /// <summary>The strict epoch-anchored fixed-window quota default.</summary>
    public const string QuotaFixedWindowStrict = "quota.fixed-window.strict.v1";
    /// <summary>The expiry-first, oldest-origin wallet default.</summary>
    public const string WalletExpiryFirstOriginOldest = "wallet.expiry-first-origin-oldest.v1";
}

/// <summary>Describes the outcome of one guarded quota admission.</summary>
public enum QuotaAdmissionKind
{
    /// <summary>No valid outcome.</summary>
    None = 0,
    /// <summary>The request was not admitted.</summary>
    Rejected = 1,
    /// <summary>The reservation was durably admitted.</summary>
    Accepted = 2,
    /// <summary>The reservation is retained while acknowledgement remains uncertain.</summary>
    Indeterminate = 3,
}

/// <summary>Retains the authoritative quantities after one quota admission decision.</summary>
public readonly record struct QuotaAdmissionResult
{
    /// <summary>Gets the typed outcome.</summary>
    public QuotaAdmissionKind Kind { get; }
    /// <summary>Gets capacity still available to a different request.</summary>
    public long Remaining { get; }
    /// <summary>Gets the admitted or uncertainty-retained reservation.</summary>
    public long RetainedReservation { get; }
    /// <summary>Creates a validated result.</summary>
    public QuotaAdmissionResult(QuotaAdmissionKind kind, long remaining, long retainedReservation)
    {
        if (kind == QuotaAdmissionKind.None || !Enum.IsDefined(kind) || remaining < 0 || retainedReservation < 0 ||
            kind == QuotaAdmissionKind.Rejected && retainedReservation != 0)
            throw new ArgumentException("Quota admission result is invalid.");
        Kind = kind; Remaining = remaining; RetainedReservation = retainedReservation;
    }
}

/// <summary>Executes the accepted fail-closed quota equation without owning persistence.</summary>
public static class StrictFixedWindowQuota
{
    /// <summary>Evaluates reserve-then-consume against an exact capacity generation.</summary>
    public static QuotaAdmissionResult Admit(long capacity, long consumed, long activeReservations,
        long requested, bool policyEvidenceFresh, bool acknowledgementIndeterminate = false)
    {
        if (capacity < 0 || consumed < 0 || activeReservations < 0 || requested <= 0)
            throw new ArgumentOutOfRangeException(nameof(requested));
        long committed = checked(consumed + activeReservations);
        if (committed > capacity) throw new ArgumentException("Quota equation is overcommitted.");
        long remaining = capacity - committed;
        if (!policyEvidenceFresh || requested > remaining)
            return new(QuotaAdmissionKind.Rejected, remaining, 0);
        long after = checked(remaining - requested);
        return new(acknowledgementIndeterminate ? QuotaAdmissionKind.Indeterminate : QuotaAdmissionKind.Accepted,
            after, requested);
    }
}

/// <summary>Identifies wallet lot provenance.</summary>
public enum WalletLotOriginKind
{
    /// <summary>No valid provenance.</summary>
    None = 0,
    /// <summary>Customer-funded value.</summary>
    Paid = 1,
    /// <summary>Promotional value.</summary>
    Promotional = 2,
}

/// <summary>Defines one immutable provenance-bearing wallet lot at an exact generation.</summary>
public sealed record WalletLot
{
    /// <summary>Gets the lot identity.</summary>
    public SemanticId LotId { get; }
    /// <summary>Gets the remaining positive quantity.</summary>
    public long Remaining { get; }
    /// <summary>Gets the unit or ISO currency discriminator.</summary>
    public string Unit { get; }
    /// <summary>Gets paid or promotional provenance.</summary>
    public WalletLotOriginKind OriginKind { get; }
    /// <summary>Gets the origin effective time.</summary>
    public DateTimeOffset OriginEffectiveAt { get; }
    /// <summary>Gets expiry; null sorts after every expiring lot.</summary>
    public DateTimeOffset? ExpiresAt { get; }
    /// <summary>Gets the exact lot generation.</summary>
    public OwnerGeneration Generation { get; }

    /// <summary>Creates one validated wallet lot.</summary>
    public WalletLot(SemanticId lotId, long remaining, string unit, WalletLotOriginKind originKind,
        DateTimeOffset originEffectiveAt, DateTimeOffset? expiresAt, OwnerGeneration generation)
    {
        if (!lotId.IsValid || remaining <= 0 || !ScopeId.TryCreate("unit", "wallet", unit, out _) ||
            originKind == WalletLotOriginKind.None || !Enum.IsDefined(originKind) || !generation.IsValid || expiresAt <= originEffectiveAt)
            throw new ArgumentException("Wallet lot is invalid.");
        LotId = lotId; Remaining = remaining; Unit = unit; OriginKind = originKind;
        OriginEffectiveAt = originEffectiveAt; ExpiresAt = expiresAt; Generation = generation;
    }
}

/// <summary>Names one deterministic positive source slice in a wallet plan.</summary>
public readonly record struct WalletSourceSlice
{
    /// <summary>Gets the source lot.</summary>
    public SemanticId LotId { get; }
    /// <summary>Gets the positive quantity.</summary>
    public long Quantity { get; }
    /// <summary>Gets the pinned lot generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Creates one guarded source slice.</summary>
    public WalletSourceSlice(SemanticId lotId, long quantity, OwnerGeneration generation)
    {
        if (!lotId.IsValid || quantity <= 0 || !generation.IsValid) throw new ArgumentException("Wallet source slice is invalid.");
        LotId = lotId; Quantity = quantity; Generation = generation;
    }
}

/// <summary>Builds the accepted expiry-first wallet source plan without mutating lots.</summary>
public static class ExpiryFirstWalletPlanner
{
    /// <summary>Returns exact guarded slices, or an empty list when eligible quantity is insufficient.</summary>
    public static IReadOnlyList<WalletSourceSlice> Plan(IEnumerable<WalletLot> lots, string unit,
        long requested, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(lots);
        if (requested <= 0 || !ScopeId.TryCreate("unit", "wallet", unit, out _)) throw new ArgumentException("Wallet request is invalid.");
        long needed = requested;
        var slices = new List<WalletSourceSlice>();
        foreach (WalletLot lot in lots.Where(x => x.Unit == unit && (x.ExpiresAt is null || x.ExpiresAt > effectiveAt))
            .OrderBy(x => x.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(x => x.OriginEffectiveAt)
            .ThenBy(x => x.LotId.LocalId, StringComparer.Ordinal))
        {
            long take = Math.Min(needed, lot.Remaining);
            if (take > 0) slices.Add(new(lot.LotId, take, lot.Generation));
            needed -= take;
            if (needed == 0) return slices.ToArray();
        }
        return Array.Empty<WalletSourceSlice>();
    }
}

/// <summary>Validates the accepted same-unit, dual-guard wallet transfer boundary.</summary>
public static class WalletTransferGuard
{
    /// <summary>Returns rejected for unit conversion and indeterminate when either endpoint is unfenced.</summary>
    public static QuotaAdmissionKind Validate(string sourceUnit, string destinationUnit, bool sourceGuarded, bool destinationGuarded) =>
        !string.Equals(sourceUnit, destinationUnit, StringComparison.Ordinal) ? QuotaAdmissionKind.Rejected :
        sourceGuarded && destinationGuarded ? QuotaAdmissionKind.Accepted : QuotaAdmissionKind.Indeterminate;
}
