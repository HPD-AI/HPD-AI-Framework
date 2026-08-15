using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Valuation;

/// <summary>Classifies which oracle may truthfully verify an accepted valuation.</summary>
public enum ReproducibilityKind
{
    /// <summary>Invalid default classification.</summary>
    None = 0,
    /// <summary>Owned inputs and deterministic semantics permit exact recalculation.</summary>
    ExactRecomputable,
    /// <summary>Owned output and evidence permit integrity verification but not recalculation.</summary>
    ExactOutputVerified,
    /// <summary>An external authority can answer a fresh scoped verification question.</summary>
    ExternallyReverifiable,
    /// <summary>A bounded approximation is available but cannot authorize an exact consequence.</summary>
    ApproximateRecomputable,
    /// <summary>Required evidence is missing, conflicting, incompatible, or lawfully unavailable.</summary>
    Unverifiable
}

/// <summary>Names the exact midpoint rule and stage used to turn precise value into postable value.</summary>
public readonly record struct RoundingContract
{
    /// <summary>Gets the number of decimal digits retained in the rounded result.</summary>
    public byte Scale { get; }
    /// <summary>Gets the midpoint rounding rule.</summary>
    public MidpointRounding Mode { get; }
    /// <summary>Gets the stable semantic stage token, such as <c>line</c> or <c>invoice</c>.</summary>
    public string Stage { get; }
    /// <summary>Gets whether the contract is valid; the default value is invalid.</summary>
    public bool IsValid => Stage is not null && Scale <= 28 && Enum.IsDefined(Mode);

    /// <summary>Creates a bounded explicit rounding contract.</summary>
    /// <param name="scale">The decimal digits retained, from zero through 28.</param>
    /// <param name="mode">The explicit midpoint rounding rule.</param>
    /// <param name="stage">The stable semantic rounding-stage token.</param>
    /// <exception cref="ArgumentException">Scale, stage, or midpoint rule is invalid.</exception>
    public RoundingContract(byte scale, MidpointRounding mode, string stage)
    {
        Scale = scale; Mode = mode; Stage = ContractToken.TryValidate(stage, out var stable) ? stable : null!;
        if (!IsValid) throw new ArgumentException("Invalid rounding contract.");
    }

    /// <summary>Applies this exact rounding contract with checked decimal semantics.</summary>
    /// <param name="value">The precise input value.</param>
    /// <returns>The rounded value.</returns>
    /// <exception cref="InvalidOperationException">This is the default invalid contract.</exception>
    public decimal Apply(decimal value) => IsValid ? decimal.Round(value, Scale, Mode) : throw new InvalidOperationException("Default rounding contract is invalid.");
}

/// <summary>Represents exact precise and rounded economic value in one currency without owning an obligation.</summary>
public readonly record struct EconomicValue
{
    /// <summary>Gets the precise calculation output before the named rounding stage.</summary>
    public decimal Precise { get; }
    /// <summary>Gets the postable rounded output.</summary>
    public decimal Rounded { get; }
    /// <summary>Gets the uppercase ISO-style currency token.</summary>
    public string Currency { get; }
    /// <summary>Gets whether currency and rounded result are valid.</summary>
    public bool IsValid => Currency is not null;

    /// <summary>Creates economic value and verifies its rounded component against the supplied contract.</summary>
    /// <param name="precise">The exact pre-rounding value.</param>
    /// <param name="rounded">The claimed post-rounding value.</param>
    /// <param name="currency">A three-letter uppercase currency token.</param>
    /// <param name="rounding">The exact rounding contract used.</param>
    /// <exception cref="ArgumentException">Currency is malformed or rounded output disagrees with the contract.</exception>
    public EconomicValue(decimal precise, decimal rounded, string currency, RoundingContract rounding)
    {
        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || currency.Any(static c => c is < 'A' or > 'Z') || !rounding.IsValid || rounding.Apply(precise) != rounded)
            throw new ArgumentException("Invalid currency or rounded economic value.");
        (Precise, Rounded, Currency) = (precise, rounded, currency);
    }
}

/// <summary>Owns the bounded exact input references and semantic revisions needed to explain a valuation.</summary>
public sealed record ValuationInputManifest
{
    /// <summary>Maximum number of exact input identities retained by one manifest.</summary>
    public const int MaximumInputs = 4096;
    private readonly SemanticId[] _inputs;
    /// <summary>Gets the manifest identity.</summary>
    public SemanticId ManifestId { get; }
    /// <summary>Gets the measurement-generation identity without importing its mutator contract.</summary>
    public SemanticId MeasurementGenerationId { get; }
    /// <summary>Gets the exact historical read frame and owner cuts.</summary>
    public HistoricalCut HistoricalCut { get; }
    /// <summary>Gets the pricing semantic revision.</summary>
    public Revision PricingRevision { get; }
    /// <summary>Gets the arithmetic algorithm semantic revision.</summary>
    public Revision AlgorithmRevision { get; }
    /// <summary>Gets the exact rounding rule and stage.</summary>
    public RoundingContract Rounding { get; }
    /// <summary>Gets the declared reproducibility class; it is not inferred from implementation type.</summary>
    public ReproducibilityKind Reproducibility { get; }
    /// <summary>Gets an owned duplicate-free copy of all exact input identities.</summary>
    public IReadOnlyList<SemanticId> Inputs => Array.AsReadOnly(_inputs);
    /// <summary>Gets the semantic digest of the complete canonical manifest.</summary>
    public CanonicalDigest Digest { get; }

    /// <summary>Creates a bounded exact valuation input manifest.</summary>
    /// <param name="manifestId">The Valuation-scoped manifest identity.</param>
    /// <param name="measurementGenerationId">The immutable input generation identity.</param>
    /// <param name="historicalCut">The exact acted-upon historical frame and cuts.</param>
    /// <param name="pricingRevision">The resolved pricing semantic revision.</param>
    /// <param name="algorithmRevision">The arithmetic semantic revision.</param>
    /// <param name="rounding">The exact rounding rule and stage.</param>
    /// <param name="reproducibility">The declared class whose oracle later applies.</param>
    /// <param name="inputs">The bounded exact input identities; the sequence is copied.</param>
    /// <param name="digest">The canonical digest of all semantic manifest fields.</param>
    /// <exception cref="ArgumentException">Identity, revisions, reproducibility, input set, or authority scope is invalid.</exception>
    public ValuationInputManifest(SemanticId manifestId, SemanticId measurementGenerationId, HistoricalCut historicalCut,
        Revision pricingRevision, Revision algorithmRevision, RoundingContract rounding, ReproducibilityKind reproducibility,
        IEnumerable<SemanticId> inputs, CanonicalDigest digest)
    {
        ArgumentNullException.ThrowIfNull(historicalCut); ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(digest);
        _inputs = inputs.ToArray();
        if (!manifestId.IsValid || manifestId.Scope.Authority != "valuation" || !measurementGenerationId.IsValid || !pricingRevision.IsValid ||
            !algorithmRevision.IsValid || !rounding.IsValid || reproducibility == ReproducibilityKind.None || !Enum.IsDefined(reproducibility) ||
            _inputs.Length > MaximumInputs || _inputs.Any(static x => !x.IsValid) || _inputs.Distinct().Count() != _inputs.Length)
            throw new ArgumentException("Invalid valuation input manifest.");
        (ManifestId, MeasurementGenerationId, HistoricalCut, PricingRevision, AlgorithmRevision, Rounding, Reproducibility, Digest) =
            (manifestId, measurementGenerationId, historicalCut, pricingRevision, algorithmRevision, rounding, reproducibility, digest);
    }
}

/// <summary>Requests authority-local admission of one exact economic determination.</summary>
public sealed record AdmitValuationCommand
{
    /// <summary>Gets the Valuation authority identity.</summary>
    public SemanticId ValuationId { get; }
    /// <summary>Gets the exact input manifest.</summary>
    public ValuationInputManifest Manifest { get; }
    /// <summary>Gets the exact precise and rounded result.</summary>
    public EconomicValue Result { get; }
    /// <summary>Gets the expected authority generation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets when the calculation occurred.</summary>
    public NamedTime CalculatedAt { get; }

    /// <summary>Creates a valuation admission request without creating an obligation or movement.</summary>
    /// <param name="valuationId">The new Valuation authority identity.</param>
    /// <param name="manifest">The complete input and calculation manifest.</param>
    /// <param name="result">The precise and rounded economic result.</param>
    /// <param name="expectedGeneration">The authority generation expected during compare-bind.</param>
    /// <param name="calculatedAt">The UTC calculation time.</param>
    /// <exception cref="ArgumentException">Identity, scope, generation, or calculation time is invalid.</exception>
    public AdmitValuationCommand(SemanticId valuationId, ValuationInputManifest manifest, EconomicValue result, OwnerGeneration expectedGeneration, NamedTime calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!valuationId.IsValid || valuationId.Scope.Authority != "valuation" || valuationId.Scope != manifest.ManifestId.Scope || !result.IsValid ||
            !expectedGeneration.IsValid || calculatedAt.Kind != TimeKind.Calculated) throw new ArgumentException("Invalid valuation admission command.");
        (ValuationId, Manifest, Result, ExpectedGeneration, CalculatedAt) = (valuationId, manifest, result, expectedGeneration, calculatedAt);
    }
}

/// <summary>Records an admitted immutable valuation and its authority generation.</summary>
public sealed record ValuationFact
{
    /// <summary>Gets the admitted valuation command.</summary>
    public AdmitValuationCommand Admission { get; }
    /// <summary>Gets the accepted authority generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets when the authority admitted the valuation.</summary>
    public NamedTime AcceptedAt { get; }

    /// <summary>Creates an admitted valuation fact.</summary>
    /// <param name="admission">The validated valuation admission.</param>
    /// <param name="generation">The accepted authority generation.</param>
    /// <param name="acceptedAt">The UTC authority acceptance time.</param>
    /// <exception cref="ArgumentException">Generation or acceptance time is invalid.</exception>
    public ValuationFact(AdmitValuationCommand admission, OwnerGeneration generation, NamedTime acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!generation.IsValid || acceptedAt.Kind != TimeKind.Accepted) throw new ArgumentException("Invalid valuation fact metadata.");
        (Admission, Generation, AcceptedAt) = (admission, generation, acceptedAt);
    }
}

/// <summary>Records the result of applying the oracle appropriate to a valuation's declared reproducibility class.</summary>
public sealed record ValuationVerification
{
    /// <summary>Gets the valuation identity being verified.</summary>
    public SemanticId ValuationId { get; }
    /// <summary>Gets the generation that was verified.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the reproducibility class whose oracle was applied.</summary>
    public ReproducibilityKind Reproducibility { get; }
    /// <summary>Gets whether the class-specific oracle passed.</summary>
    public bool Passed { get; }
    /// <summary>Gets a bounded exact outcome or missing-evidence code.</summary>
    public string Code { get; }
    /// <summary>Gets when verification occurred.</summary>
    public NamedTime VerifiedAt { get; }

    /// <summary>Creates a verification receipt; passing never upgrades the original reproducibility class.</summary>
    /// <param name="valuationId">The valuation identity verified.</param>
    /// <param name="generation">The exact authority generation verified.</param>
    /// <param name="reproducibility">The class-specific oracle applied.</param>
    /// <param name="passed">Whether that oracle passed.</param>
    /// <param name="code">A stable result or missing-evidence code.</param>
    /// <param name="verifiedAt">The UTC verification time.</param>
    /// <exception cref="ArgumentException">Identity, generation, class, code, or verification time is invalid.</exception>
    public ValuationVerification(SemanticId valuationId, OwnerGeneration generation, ReproducibilityKind reproducibility, bool passed, string code, NamedTime verifiedAt)
    {
        if (!valuationId.IsValid || !generation.IsValid || reproducibility == ReproducibilityKind.None || !Enum.IsDefined(reproducibility) ||
            !ContractToken.TryValidate(code, out var stable) || verifiedAt.Kind != TimeKind.Verify) throw new ArgumentException("Invalid valuation verification receipt.");
        (ValuationId, Generation, Reproducibility, Passed, Code, VerifiedAt) = (valuationId, generation, reproducibility, passed, stable, verifiedAt);
    }
}

internal static class ContractToken
{
    internal static bool TryValidate(string? candidate, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(candidate) || candidate.Length > ScopeId.MaximumComponentUtf8Bytes) return false;
        foreach (var c in candidate) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_')) return false;
        value = candidate;
        return true;
    }
}
