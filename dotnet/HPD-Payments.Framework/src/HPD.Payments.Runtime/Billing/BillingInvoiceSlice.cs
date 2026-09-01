using HPD.Payments.Contracts.IssuanceFact;
using HPD.Payments.Contracts.Obligation;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.Billing;

/// <summary>Distinguishes a progressive statement from a final closed-period invoice.</summary>
public enum BillingClosureKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The artifact covers a declared cut while later additive facts remain possible.</summary>
    Progressive = 1,
    /// <summary>The artifact closes the declared source cut under the named policy revision.</summary>
    Final = 2,
}

/// <summary>Binds the exact additive obligation facts and policy evidence used for one invoice artifact.</summary>
public sealed record BillingManifest
{
    /// <summary>Gets the manifest identity in Issuance Fact scope.</summary>
    public SemanticId ManifestId { get; }
    /// <summary>Gets the exact ordered obligation fact identities.</summary>
    public IReadOnlyList<SemanticId> ObligationFactIds { get; }
    /// <summary>Gets the source cut fixed for reproducibility.</summary>
    public HistoricalCut SourceCut { get; }
    /// <summary>Gets the tax evidence revision; this does not claim tax correctness.</summary>
    public Revision TaxRevision { get; }
    /// <summary>Gets the FX evidence revision; this does not claim an external rate as true.</summary>
    public Revision FxRevision { get; }
    /// <summary>Gets the rounding contract revision.</summary>
    public Revision RoundingRevision { get; }
    /// <summary>Gets progressive or final closure semantics.</summary>
    public BillingClosureKind Closure { get; }
    /// <summary>Gets the canonical digest of the complete externalized manifest.</summary>
    public CanonicalDigest Digest { get; }

    /// <summary>Creates a fully revision-bound immutable invoice manifest.</summary>
    public BillingManifest(SemanticId manifestId, IEnumerable<SemanticId> obligationFactIds, HistoricalCut sourceCut,
        Revision taxRevision, Revision fxRevision, Revision roundingRevision, BillingClosureKind closure, CanonicalDigest digest)
    {
        ArgumentNullException.ThrowIfNull(obligationFactIds); ArgumentNullException.ThrowIfNull(sourceCut); ArgumentNullException.ThrowIfNull(digest);
        var ids = obligationFactIds.ToArray();
        if (!manifestId.IsValid || manifestId.Scope.Authority != "issuance-fact" || ids.Length == 0 || ids.Any(static id => !id.IsValid || id.Scope.Authority != "obligation") ||
            ids.Distinct().Count() != ids.Length || !taxRevision.IsValid || !fxRevision.IsValid || !roundingRevision.IsValid ||
            closure == BillingClosureKind.None || !Enum.IsDefined(closure))
            throw new ArgumentException("Billing manifest requires unique obligation facts and exact policy revisions.");
        ManifestId = manifestId; ObligationFactIds = Array.AsReadOnly(ids); SourceCut = sourceCut;
        TaxRevision = taxRevision; FxRevision = fxRevision; RoundingRevision = roundingRevision; Closure = closure; Digest = digest;
    }
}

/// <summary>Calculates invoice totals without mutating obligation or issuance authority.</summary>
public static class BillingInvoicePlanner
{
    /// <summary>Returns the net due-minus-credit magnitude for an exact immutable fact set.</summary>
    public static decimal CalculateNet(IReadOnlyCollection<ObligationFact> facts, string unit)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Count == 0 || string.IsNullOrEmpty(unit) || facts.Any(f => !StringComparer.Ordinal.Equals(f.Command.Quantity.Unit, unit)))
            throw new ArgumentException("Invoice calculation requires a non-empty, single-unit obligation set.");
        return facts.Sum(static fact => fact.Command.Direction == ObligationDirection.Due
            ? fact.Command.Quantity.Magnitude : -fact.Command.Quantity.Magnitude);
    }

    /// <summary>Builds an additive correction; prior history is referenced and never overwritten.</summary>
    public static AdmitObligationCommand Correct(SemanticId factId, SemanticId obligationId, SemanticId sourceManifestId,
        CanonicalDigest sourceManifestDigest, ObligationDirection direction, ObligationQuantity quantity,
        NamedTime effectiveAt, NamedTime sourceAt, OwnerGeneration expectedGeneration, CanonicalDigest expectedDigest,
        SemanticId predecessorFactId) => new(factId, obligationId, sourceManifestId, sourceManifestDigest,
            ObligationFactKind.Correction, direction, quantity, effectiveAt, sourceAt,
            new ObligationGuard(expectedGeneration, expectedDigest), predecessorFactId);

    /// <summary>Creates an issuance command bound to the exact manifest and artifact bytes.</summary>
    public static RecordIssuanceCommand Issue(SemanticId factId, SemanticId artifactId, BillingManifest manifest,
        IssuanceNumberClaim numberClaim, ReadOnlySpan<byte> artifactBytes, ClassificationMark classification,
        CanonicalDigest artifactDigest, NamedTime issuedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new(factId, artifactId, manifest.ManifestId, manifest.Digest, IssuanceFactKind.Issued, numberClaim,
            new OwnedClassifiedBytes(artifactBytes, classification), artifactDigest, issuedAt);
    }
}
