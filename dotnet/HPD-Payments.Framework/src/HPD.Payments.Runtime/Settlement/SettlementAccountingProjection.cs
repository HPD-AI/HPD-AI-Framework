using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Runtime.Settlement;

/// <summary>Names an immutable settlement/accounting observation without claiming external truth.</summary>
public enum SettlementAccountingObservationKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Records the locally expected payout or transfer.</summary>
    Expected,
    /// <summary>Records authenticated provider settlement inclusion.</summary>
    Included,
    /// <summary>Records authenticated provider settlement exclusion.</summary>
    Excluded,
    /// <summary>Records a conflicting external settlement observation.</summary>
    Conflicted,
    /// <summary>Records acknowledgement of an exact accounting export.</summary>
    AccountingAcknowledged,
    /// <summary>Retains an unresolved external or accounting residue.</summary>
    Residual,
}

/// <summary>Immutable generation-fenced projection for one expected payout/transfer and its distinct external consequences.</summary>
public sealed record SettlementAccountingState
{
    /// <summary>Gets the local movement identity.</summary>
    public SemanticId MovementId { get; }
    /// <summary>Gets the exact positive expected magnitude.</summary>
    public decimal ExpectedMagnitude { get; }
    /// <summary>Gets the bounded exact unit.</summary>
    public string Unit { get; }
    /// <summary>Gets the externally observed included magnitude, if any.</summary>
    public decimal? IncludedMagnitude { get; }
    /// <summary>Gets whether exclusion was observed.</summary>
    public bool Excluded { get; }
    /// <summary>Gets whether incompatible external evidence remains.</summary>
    public bool Conflicted { get; }
    /// <summary>Gets whether the exact accounting export was acknowledged.</summary>
    public bool AccountingAcknowledged { get; }
    /// <summary>Gets whether residue remains.</summary>
    public bool Residual { get; }
    /// <summary>Gets the exact projection generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the last immutable evidence identity.</summary>
    public SemanticId LastEvidenceId { get; }

    private SettlementAccountingState(SemanticId movementId, decimal expectedMagnitude, string unit, decimal? includedMagnitude,
        bool excluded, bool conflicted, bool accountingAcknowledged, bool residual, OwnerGeneration generation, SemanticId lastEvidenceId)
    {
        if (!movementId.IsValid || expectedMagnitude <= 0 || !ScopeId.TryCreate("unit", "settlement", unit, out _) ||
            includedMagnitude is <= 0 || !generation.IsValid || !lastEvidenceId.IsValid || lastEvidenceId.Scope != movementId.Scope ||
            accountingAcknowledged && includedMagnitude is null || excluded && includedMagnitude is not null && !conflicted)
            throw new ArgumentException("Settlement/accounting projection is invalid.");
        MovementId = movementId; ExpectedMagnitude = expectedMagnitude; Unit = unit; IncludedMagnitude = includedMagnitude;
        Excluded = excluded; Conflicted = conflicted; AccountingAcknowledged = accountingAcknowledged; Residual = residual;
        Generation = generation; LastEvidenceId = lastEvidenceId;
    }

    /// <summary>Creates the local expected movement without implying settlement.</summary>
    public static SettlementAccountingState Create(SemanticId movementId, decimal expectedMagnitude, string unit,
        OwnerGeneration generation, SemanticId evidenceId) => new(movementId, expectedMagnitude, unit, null, false, false, false, false, generation, evidenceId);

    /// <summary>Rehydrates an exact previously stored projection.</summary>
    public static SettlementAccountingState Restore(SemanticId movementId, decimal expectedMagnitude, string unit,
        decimal? includedMagnitude, bool excluded, bool conflicted, bool accountingAcknowledged, bool residual,
        OwnerGeneration generation, SemanticId lastEvidenceId) => new(movementId, expectedMagnitude, unit, includedMagnitude,
            excluded, conflicted, accountingAcknowledged, residual, generation, lastEvidenceId);

    /// <summary>Applies one authenticated, same-movement successor observation.</summary>
    public SettlementAccountingState Observe(SettlementAccountingObservationKind kind, SemanticId evidenceId,
        decimal? magnitude = null, bool authenticated = true)
    {
        if (kind is SettlementAccountingObservationKind.None or SettlementAccountingObservationKind.Expected || !Enum.IsDefined(kind) ||
            !evidenceId.IsValid || evidenceId.Scope != MovementId.Scope || !Generation.TryNext(out var next))
            throw new ArgumentException("Invalid settlement/accounting observation.");
        if (!authenticated) throw new InvalidOperationException("Unauthenticated settlement/accounting evidence is quarantined.");
        return kind switch
        {
            SettlementAccountingObservationKind.Included when magnitude > 0 && !Excluded => Copy(magnitude, false, Conflicted, false, magnitude != ExpectedMagnitude),
            SettlementAccountingObservationKind.Excluded when IncludedMagnitude is null => Copy(null, true, Conflicted, false, true),
            SettlementAccountingObservationKind.Conflicted => Copy(IncludedMagnitude, Excluded, true, false, true),
            SettlementAccountingObservationKind.AccountingAcknowledged when IncludedMagnitude == ExpectedMagnitude && !Conflicted && !Excluded =>
                Copy(IncludedMagnitude, false, false, true, Residual),
            SettlementAccountingObservationKind.Residual => Copy(IncludedMagnitude, Excluded, Conflicted, AccountingAcknowledged, true),
            _ => throw new InvalidOperationException("Observation contradicts settlement/accounting authority boundaries."),
        };

        SettlementAccountingState Copy(decimal? included, bool excluded, bool conflicted, bool acknowledged, bool residual) =>
            new(MovementId, ExpectedMagnitude, Unit, included, excluded, conflicted, acknowledged, residual, next, evidenceId);
    }
}
