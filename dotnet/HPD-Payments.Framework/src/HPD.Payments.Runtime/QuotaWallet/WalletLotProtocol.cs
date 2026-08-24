using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Runtime.QuotaWallet;

/// <summary>Names an append-only wallet lot transition under the accepted RES-009 policy.</summary>
public enum WalletLotChangeKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Moves available quantity into a named reservation.</summary>
    Reserve,
    /// <summary>Consumes reserved quantity.</summary>
    Consume,
    /// <summary>Returns definitely unused reserved quantity.</summary>
    Release,
    /// <summary>Expires available quantity at or after the lot expiry.</summary>
    Expire,
    /// <summary>Moves available quantity to a separately guarded destination lot.</summary>
    TransferOut,
    /// <summary>Applies an additive negative correction to available quantity.</summary>
    CorrectDebit,
    /// <summary>Applies an additive positive correction with retained provenance.</summary>
    CorrectCredit,
    /// <summary>Recredits quantity from a separately evidenced predecessor consequence.</summary>
    Recredit,
    /// <summary>Retains ambiguous reserved quantity as explicit residue.</summary>
    RetainResidue,
}

/// <summary>Immutable conserved projection of one provenance-bearing wallet lot.</summary>
public sealed record WalletLotState
{
    /// <summary>Gets immutable lot provenance.</summary>
    public WalletLot Lot { get; }
    /// <summary>Gets all quantity ever credited into this lineage, including additive corrections/recredits.</summary>
    public long TotalCredited { get; }
    /// <summary>Gets currently available quantity.</summary>
    public long Available { get; }
    /// <summary>Gets definitely reserved quantity.</summary>
    public long Reserved { get; }
    /// <summary>Gets consumed quantity.</summary>
    public long Consumed { get; }
    /// <summary>Gets expired quantity.</summary>
    public long Expired { get; }
    /// <summary>Gets quantity transferred to another guarded lot.</summary>
    public long TransferredOut { get; }
    /// <summary>Gets owner-addressable ambiguous residue.</summary>
    public long Residual { get; }
    /// <summary>Gets the exact projection generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the operation producing this projection.</summary>
    public SemanticId LastOperationId { get; }

    private WalletLotState(WalletLot lot, long totalCredited, long available, long reserved, long consumed,
        long expired, long transferredOut, long residual, OwnerGeneration generation, SemanticId lastOperationId)
    {
        Lot = lot; TotalCredited = totalCredited; Available = available; Reserved = reserved; Consumed = consumed;
        Expired = expired; TransferredOut = transferredOut; Residual = residual; Generation = generation; LastOperationId = lastOperationId;
        Validate();
    }

    /// <summary>Creates the initial lot projection.</summary>
    public static WalletLotState Create(WalletLot lot, SemanticId operationId) =>
        new(lot ?? throw new ArgumentNullException(nameof(lot)), lot.Remaining, lot.Remaining, 0, 0, 0, 0, 0, lot.Generation, operationId);

    /// <summary>Rehydrates an exact previously admitted projection.</summary>
    public static WalletLotState Restore(WalletLot lot, long totalCredited, long available, long reserved, long consumed,
        long expired, long transferredOut, long residual, OwnerGeneration generation, SemanticId lastOperationId) =>
        new(lot, totalCredited, available, reserved, consumed, expired, transferredOut, residual, generation, lastOperationId);

    /// <summary>Applies one checked successor transition at an explicit effective time.</summary>
    public WalletLotState Apply(WalletLotChangeKind kind, long quantity, SemanticId operationId, DateTimeOffset effectiveAt,
        bool nonOccurrenceProven = false)
    {
        if (kind == WalletLotChangeKind.None || !Enum.IsDefined(kind) || quantity <= 0 || !operationId.IsValid || operationId.Scope != Lot.LotId.Scope ||
            !Generation.TryNext(out var next)) throw new ArgumentException("Invalid wallet lot transition.");
        var expiredNow = Lot.ExpiresAt is { } expiry && effectiveAt >= expiry;
        return kind switch
        {
            WalletLotChangeKind.Reserve when !expiredNow && quantity <= Available => Copy(available: Available - quantity, reserved: checked(Reserved + quantity)),
            WalletLotChangeKind.Consume when quantity <= Reserved => Copy(reserved: Reserved - quantity, consumed: checked(Consumed + quantity)),
            WalletLotChangeKind.Release when nonOccurrenceProven && quantity <= Reserved && !expiredNow => Copy(available: checked(Available + quantity), reserved: Reserved - quantity),
            WalletLotChangeKind.Expire when expiredNow && quantity <= Available => Copy(available: Available - quantity, expired: checked(Expired + quantity)),
            WalletLotChangeKind.TransferOut when !expiredNow && quantity <= Available => Copy(available: Available - quantity, transferredOut: checked(TransferredOut + quantity)),
            WalletLotChangeKind.CorrectDebit when quantity <= Available => Copy(totalCredited: TotalCredited - quantity, available: Available - quantity),
            WalletLotChangeKind.CorrectCredit => Copy(totalCredited: checked(TotalCredited + quantity), available: checked(Available + quantity)),
            WalletLotChangeKind.Recredit => Copy(totalCredited: checked(TotalCredited + quantity), available: checked(Available + quantity)),
            WalletLotChangeKind.RetainResidue when quantity <= Reserved => Copy(reserved: Reserved - quantity, residual: checked(Residual + quantity)),
            _ => throw new InvalidOperationException("Wallet lot transition violates expiry, evidence or conservation boundaries."),
        };

        WalletLotState Copy(long? totalCredited = null, long? available = null, long? reserved = null, long? consumed = null,
            long? expired = null, long? transferredOut = null, long? residual = null) => new(Lot, totalCredited ?? TotalCredited,
            available ?? Available, reserved ?? Reserved, consumed ?? Consumed, expired ?? Expired, transferredOut ?? TransferredOut,
            residual ?? Residual, next, operationId);
    }

    /// <summary>Transfers same-unit quantity between two independently generation-fenced lots.</summary>
    public static (WalletLotState Source, WalletLotState Destination) Transfer(WalletLotState source, WalletLotState destination,
        long quantity, SemanticId sourceOperationId, SemanticId destinationOperationId, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(destination);
        if (WalletTransferGuard.Validate(source.Lot.Unit, destination.Lot.Unit, source.Generation.IsValid, destination.Generation.IsValid) != QuotaAdmissionKind.Accepted)
            throw new InvalidOperationException("Wallet transfer endpoints are not compatibly fenced.");
        return (source.Apply(WalletLotChangeKind.TransferOut, quantity, sourceOperationId, effectiveAt),
            destination.Apply(WalletLotChangeKind.Recredit, quantity, destinationOperationId, effectiveAt));
    }

    private void Validate()
    {
        if (Lot is null || !Generation.IsValid || !LastOperationId.IsValid || LastOperationId.Scope != Lot.LotId.Scope ||
            TotalCredited < 0 || Available < 0 || Reserved < 0 || Consumed < 0 || Expired < 0 || TransferredOut < 0 || Residual < 0 ||
            checked(Available + Reserved + Consumed + Expired + TransferredOut + Residual) != TotalCredited)
            throw new ArgumentException("Wallet lot projection violates provenance, generation or conservation invariants.");
    }
}
