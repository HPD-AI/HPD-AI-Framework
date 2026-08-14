using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.ScopedIdentity;

/// <summary>Names every canonical route whose frozen owner is Scoped Identity.</summary>
/// <remarks>The value identifies conformance routing only. It does not grant authority or imply support in a provider, lane, profile, or adapter.</remarks>
public enum ScopedIdentityRoute
{
    /// <summary>Invalid default route.</summary>
    None = 0,
    /// <summary>PAYM-001 payment-method reference lifecycle.</summary>
    Paym001,
    /// <summary>PAYM-002 vault/tokenization representation identity.</summary>
    Paym002,
    /// <summary>PAYM-003 mandate identity and nonreuse.</summary>
    Paym003,
    /// <summary>PAYM-004 network-token generation identity.</summary>
    Paym004,
    /// <summary>PAYM-005 provider representation binding.</summary>
    Paym005,
    /// <summary>PLAT-001 explicit tenant and organization scope.</summary>
    Plat001,
    /// <summary>PLAT-002 delegated actor scope.</summary>
    Plat002,
    /// <summary>PLAT-003 credential-generation identity.</summary>
    Plat003,
    /// <summary>PLAT-004 constrained-client identity.</summary>
    Plat004,
    /// <summary>PLAT-005 environment collision scope.</summary>
    Plat005,
    /// <summary>PLAT-006 configuration revision identity.</summary>
    Plat006,
    /// <summary>PLAT-007 typed metadata identity.</summary>
    Plat007,
    /// <summary>PLAT-008 immutable audit-context identity.</summary>
    Plat008,
    /// <summary>PLAT-009 account timeline identity.</summary>
    Plat009,
    /// <summary>PLAT-010 bounded bulk branch identity.</summary>
    Plat010,
    /// <summary>PLAT-011 health observation identity.</summary>
    Plat011,
    /// <summary>PLAT-012 extension governance identity.</summary>
    Plat012,
    /// <summary>PLAT-013 effective configuration observation identity.</summary>
    Plat013,
    /// <summary>PLAT-014 extension invocation identity.</summary>
    Plat014,
    /// <summary>PLAT-015 deployment acknowledgement identity.</summary>
    Plat015,
    /// <summary>PLAT-016 extension resource authority identity.</summary>
    Plat016,
    /// <summary>PLAT-017 descriptor graph identity.</summary>
    Plat017,
    /// <summary>PLAT-018 delivery-semantic identity.</summary>
    Plat018,
    /// <summary>PLAT-019 generated-manifest identity.</summary>
    Plat019,
    /// <summary>PLAT-020 cross-profile semantic identity.</summary>
    Plat020,
    /// <summary>PLAT-021 classified observation identity.</summary>
    Plat021,
    /// <summary>PLAT-022 deployment provenance identity.</summary>
    Plat022,
    /// <summary>PLAT-023 operator-action identity.</summary>
    Plat023,
    /// <summary>PLAT-024 shared-mechanism conformance identity.</summary>
    Plat024,
}

/// <summary>Specifies the only mutations owned by Scoped Identity.</summary>
public enum ScopedIdentityOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Reserves a previously unoccupied semantic identity.</summary>
    Reserve,
    /// <summary>Compares a proposed semantic binding with the retained binding.</summary>
    CompareBind,
    /// <summary>Appends a nonreuse tombstone without releasing the identity.</summary>
    Retire,
}

/// <summary>Requests one bounded reservation, comparison, or retirement in the identity authority.</summary>
public sealed class ScopedIdentityCommand
{
    /// <summary>Gets the canonical route whose semantic identity is being protected.</summary>
    public ScopedIdentityRoute Route { get; }
    /// <summary>Gets the requested identity-authority operation.</summary>
    public ScopedIdentityOperation Operation { get; }
    /// <summary>Gets the complete tenant, environment, authority, namespace, kind, and optional provider/account identity.</summary>
    public SemanticId Identity { get; }
    /// <summary>Gets the owner-defined canonical semantic binding; representation or wire hashes are not accepted as substitutes.</summary>
    public CanonicalDigest Binding { get; }
    /// <summary>Gets the expected owner generation. A first reservation requires generation one.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the current action-specific authorization revision supplied by the caller.</summary>
    public Revision AuthorizationRevision { get; }
    /// <summary>Gets the explicit UTC request time.</summary>
    public NamedTime RequestedAt { get; }

    /// <summary>Creates an immutable command and rejects default, unknown, or incorrectly scoped coordinates.</summary>
    /// <param name="route">A known Scoped Identity canonical route.</param>
    /// <param name="operation">The requested identity mutation.</param>
    /// <param name="identity">The complete semantic identity.</param>
    /// <param name="binding">The canonical semantic digest to bind or compare.</param>
    /// <param name="expectedGeneration">The generation the caller observed.</param>
    /// <param name="authorizationRevision">The action-time authorization revision.</param>
    /// <param name="requestedAt">A <see cref="TimeKind.Requested"/> time.</param>
    /// <exception cref="ArgumentException">Any value is invalid, unknown, or belongs to a different authority scope.</exception>
    public ScopedIdentityCommand(
        ScopedIdentityRoute route,
        ScopedIdentityOperation operation,
        SemanticId identity,
        CanonicalDigest binding,
        OwnerGeneration expectedGeneration,
        Revision authorizationRevision,
        NamedTime requestedAt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (route == ScopedIdentityRoute.None || !Enum.IsDefined(route) ||
            operation == ScopedIdentityOperation.None || !Enum.IsDefined(operation) ||
            !identity.IsValid || !expectedGeneration.IsValid || !authorizationRevision.IsValid ||
            !requestedAt.IsValid || requestedAt.Kind != TimeKind.Requested)
            throw new ArgumentException("A scoped identity command requires known, valid, explicitly timed coordinates.");

        Route = route;
        Operation = operation;
        Identity = identity;
        Binding = binding;
        ExpectedGeneration = expectedGeneration;
        AuthorizationRevision = authorizationRevision;
        RequestedAt = requestedAt;
    }
}

/// <summary>Records an immutable semantic binding admitted by Scoped Identity.</summary>
public sealed class ScopedIdentityReservation
{
    /// <summary>Gets the protected semantic identity.</summary>
    public SemanticId Identity { get; }
    /// <summary>Gets the owner-defined canonical semantic digest bound to the identity.</summary>
    public CanonicalDigest Binding { get; }
    /// <summary>Gets the admitted owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets when the reservation was accepted by the authority.</summary>
    public NamedTime AcceptedAt { get; }
    /// <summary>Gets when this immutable record was made durable.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates a reservation fact. The instance owns its digest bytes through <see cref="CanonicalDigest"/>.</summary>
    /// <param name="identity">The complete protected identity.</param>
    /// <param name="binding">Its canonical semantic binding.</param>
    /// <param name="generation">The admitted owner generation.</param>
    /// <param name="acceptedAt">A UTC accepted time.</param>
    /// <param name="recordedAt">A UTC record time no earlier than acceptance.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid or the record precedes acceptance.</exception>
    public ScopedIdentityReservation(SemanticId identity, CanonicalDigest binding, OwnerGeneration generation, NamedTime acceptedAt, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!identity.IsValid || !generation.IsValid || acceptedAt.Kind != TimeKind.Accepted || recordedAt.Kind != TimeKind.Record ||
            !acceptedAt.IsValid || !recordedAt.IsValid || recordedAt.Value < acceptedAt.Value)
            throw new ArgumentException("A reservation requires valid identity, generation, and ordered accepted/record times.");
        Identity = identity;
        Binding = binding;
        Generation = generation;
        AcceptedAt = acceptedAt;
        RecordedAt = recordedAt;
    }
}

/// <summary>Records that an occupied semantic identity can never be reused, even after expiry, deletion, or retention loss.</summary>
public sealed class ScopedIdentityTombstone
{
    /// <summary>Gets the retired identity.</summary>
    public SemanticId Identity { get; }
    /// <summary>Gets the last admitted canonical binding, preventing another meaning from taking the key.</summary>
    public CanonicalDigest LastBinding { get; }
    /// <summary>Gets the successor generation that admitted retirement.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets when retirement was durably recorded.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an immutable nonreuse tombstone.</summary>
    /// <param name="identity">The occupied identity.</param>
    /// <param name="lastBinding">Its last admitted semantic binding.</param>
    /// <param name="generation">The retirement generation.</param>
    /// <param name="recordedAt">A UTC record time.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid.</exception>
    public ScopedIdentityTombstone(SemanticId identity, CanonicalDigest lastBinding, OwnerGeneration generation, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(lastBinding);
        if (!identity.IsValid || !generation.IsValid || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("A tombstone requires valid identity, generation, binding, and record time.");
        Identity = identity;
        LastBinding = lastBinding;
        Generation = generation;
        RecordedAt = recordedAt;
    }
}

/// <summary>Classifies a compare-bind result without treating retention, deletion, or expiration as identity release.</summary>
public static class ScopedIdentityComparison
{
    /// <summary>Compares a proposed binding with a retained reservation or tombstone.</summary>
    /// <param name="proposedIdentity">The proposed complete identity.</param>
    /// <param name="proposedBinding">The proposed canonical semantic binding.</param>
    /// <param name="reservation">The retained reservation, when available.</param>
    /// <param name="tombstone">The retained tombstone, when the identity was retired.</param>
    /// <returns><see cref="ResultKind.Success"/> for semantic replay, <see cref="ResultKind.Conflict"/> for another binding, <see cref="ResultKind.Superseded"/> for a retired identity, or <see cref="ResultKind.Indeterminate"/> when retained evidence is absent.</returns>
    /// <exception cref="ArgumentException">The proposed identity is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="proposedBinding"/> is null.</exception>
    public static PrimitiveResult<ScopedIdentityReservation> Compare(
        SemanticId proposedIdentity,
        CanonicalDigest proposedBinding,
        ScopedIdentityReservation? reservation,
        ScopedIdentityTombstone? tombstone)
    {
        ArgumentNullException.ThrowIfNull(proposedBinding);
        if (!proposedIdentity.IsValid) throw new ArgumentException("The proposed identity is invalid.", nameof(proposedIdentity));
        if (tombstone is not null && tombstone.Identity == proposedIdentity)
            return PrimitiveResults.NonSuccess<ScopedIdentityReservation>(ResultKind.Superseded, "identity-retired");
        if (reservation is null || reservation.Identity != proposedIdentity)
            return PrimitiveResults.NonSuccess<ScopedIdentityReservation>(ResultKind.Indeterminate, "binding-evidence-absent");
        return reservation.Binding.Equals(proposedBinding)
            ? PrimitiveResults.Success(reservation)
            : PrimitiveResults.NonSuccess<ScopedIdentityReservation>(ResultKind.Conflict, "semantic-binding-conflict");
    }
}
