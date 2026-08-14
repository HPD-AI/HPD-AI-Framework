using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.MeasurementGeneration;

/// <summary>Names a closed aggregation family; custom implementations remain revisioned and separately certified.</summary>
public enum MeasurementAlgebraKind
{
    /// <summary>Invalid default algebra.</summary>
    None = 0,
    /// <summary>Counts distinct admitted fact identities.</summary>
    Count,
    /// <summary>Sums admitted quantities of one unit.</summary>
    Sum,
    /// <summary>Selects the greatest admitted quantity.</summary>
    Maximum,
    /// <summary>Counts distinct declared dimension values.</summary>
    UniqueCount,
    /// <summary>Sums products under a named weighting revision.</summary>
    WeightedSum,
    /// <summary>Selects the fact latest by the declared time rule.</summary>
    Latest,
    /// <summary>Uses a named, revisioned closed implementation requiring separate evidence.</summary>
    Custom
}

/// <summary>States algebra properties explicitly so correction never assumes a nonexistent inverse.</summary>
public readonly record struct MeasurementAlgebraContract
{
    /// <summary>Gets the algebra family.</summary>
    public MeasurementAlgebraKind Kind { get; }
    /// <summary>Gets the semantic algorithm revision.</summary>
    public Revision Revision { get; }
    /// <summary>Gets whether partitioned partial results may be merged under the same source cut.</summary>
    public bool SupportsPartitionMerge { get; }
    /// <summary>Gets whether member order changes the exact result.</summary>
    public bool IsOrderSensitive { get; }
    /// <summary>Gets whether the exact original contribution admits a declared inverse.</summary>
    public bool HasDeclaredInverse { get; }
    /// <summary>Gets whether member removal requires complete recomputation.</summary>
    public bool RequiresRecomputeOnRemoval { get; }
    /// <summary>Gets whether the contract is valid; the default value is invalid.</summary>
    public bool IsValid => Kind != MeasurementAlgebraKind.None && Enum.IsDefined(Kind) && Revision.IsValid && !(HasDeclaredInverse && RequiresRecomputeOnRemoval);

    /// <summary>Creates an explicit aggregation-law declaration.</summary>
    /// <param name="kind">The closed algebra family.</param>
    /// <param name="revision">The semantic algorithm revision.</param>
    /// <param name="supportsPartitionMerge">Whether same-cut partial results admit merge.</param>
    /// <param name="isOrderSensitive">Whether membership order changes the exact result.</param>
    /// <param name="hasDeclaredInverse">Whether an exact original contribution has a declared inverse.</param>
    /// <param name="requiresRecomputeOnRemoval">Whether removal requires full recomputation.</param>
    /// <exception cref="ArgumentException">The kind/revision is invalid or inverse and mandatory-recompute claims conflict.</exception>
    public MeasurementAlgebraContract(MeasurementAlgebraKind kind, Revision revision, bool supportsPartitionMerge, bool isOrderSensitive, bool hasDeclaredInverse, bool requiresRecomputeOnRemoval)
    {
        (Kind, Revision, SupportsPartitionMerge, IsOrderSensitive, HasDeclaredInverse, RequiresRecomputeOnRemoval) =
            (kind, revision, supportsPartitionMerge, isOrderSensitive, hasDeclaredInverse, requiresRecomputeOnRemoval);
        if (!IsValid) throw new ArgumentException("Invalid or contradictory measurement algebra contract.");
    }
}

/// <summary>Classifies whether the declared source set can support final consequences.</summary>
public enum GenerationCompleteness
{
    /// <summary>Invalid default completeness.</summary>
    None = 0,
    /// <summary>All required source branches have acknowledged the named cut.</summary>
    Complete,
    /// <summary>One or more named branches are missing; the result is provisional only.</summary>
    Incomplete,
    /// <summary>Evidence cannot establish whether the source set is complete.</summary>
    Unverifiable
}

/// <summary>Requests creation of one immutable measurement generation over an exact bounded membership set.</summary>
public sealed record CreateMeasurementGenerationCommand
{
    /// <summary>Maximum number of atomic fact identities retained by this contract instance.</summary>
    public const int MaximumMembers = 4096;
    private readonly SemanticId[] _members;
    /// <summary>Gets the Measurement Generation authority identity.</summary>
    public SemanticId GenerationId { get; }
    /// <summary>Gets the measured subject.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the inclusive effective window start.</summary>
    public NamedTime WindowFrom { get; }
    /// <summary>Gets the exclusive effective window end.</summary>
    public NamedTime WindowUntil { get; }
    /// <summary>Gets the immutable source and knowledge cuts used for the calculation.</summary>
    public HistoricalCut SourceCut { get; }
    /// <summary>Gets the declared algebra contract.</summary>
    public MeasurementAlgebraContract Algebra { get; }
    /// <summary>Gets an owned, duplicate-free copy of the exact included fact identities.</summary>
    public IReadOnlyList<SemanticId> Members => Array.AsReadOnly(_members);
    /// <summary>Gets the asserted completeness state; incomplete never means zero.</summary>
    public GenerationCompleteness Completeness { get; }
    /// <summary>Gets the expected owner generation for authority-local compare-bind.</summary>
    public OwnerGeneration ExpectedGeneration { get; }

    /// <summary>Creates a bounded generation command and defensively copies its membership.</summary>
    /// <param name="generationId">The new authority identity.</param>
    /// <param name="subjectId">The measured subject identity.</param>
    /// <param name="windowFrom">The inclusive effective window start.</param>
    /// <param name="windowUntil">The exclusive effective window end.</param>
    /// <param name="sourceCut">The immutable historical source cut.</param>
    /// <param name="algebra">The explicit aggregation-law declaration.</param>
    /// <param name="members">The bounded exact included fact identities; the sequence is copied.</param>
    /// <param name="completeness">The source completeness classification.</param>
    /// <param name="expectedGeneration">The authority generation expected during compare-bind.</param>
    /// <exception cref="ArgumentException">Metadata, interval, membership, scope, or completeness is invalid.</exception>
    public CreateMeasurementGenerationCommand(SemanticId generationId, SemanticId subjectId, NamedTime windowFrom, NamedTime windowUntil,
        HistoricalCut sourceCut, MeasurementAlgebraContract algebra, IEnumerable<SemanticId> members, GenerationCompleteness completeness, OwnerGeneration expectedGeneration)
    {
        ArgumentNullException.ThrowIfNull(sourceCut); ArgumentNullException.ThrowIfNull(members);
        _members = members.ToArray();
        if (!generationId.IsValid || generationId.Scope.Authority != "measurement-generation" || !subjectId.IsValid ||
            windowFrom.Kind != TimeKind.Effective || windowUntil.Kind != TimeKind.Effective || windowUntil.Value <= windowFrom.Value ||
            !algebra.IsValid || completeness == GenerationCompleteness.None || !Enum.IsDefined(completeness) || !expectedGeneration.IsValid ||
            _members.Length > MaximumMembers || _members.Any(static x => !x.IsValid) || _members.Distinct().Count() != _members.Length)
            throw new ArgumentException("Invalid measurement generation command.");
        (GenerationId, SubjectId, WindowFrom, WindowUntil, SourceCut, Algebra, Completeness, ExpectedGeneration) =
            (generationId, subjectId, windowFrom, windowUntil, sourceCut, algebra, completeness, expectedGeneration);
    }
}

/// <summary>Records a calculated generation result while preserving cut, membership, algebra, and completeness.</summary>
public sealed record MeasurementGenerationFact
{
    /// <summary>Gets the originating command.</summary>
    public CreateMeasurementGenerationCommand Command { get; }
    /// <summary>Gets the exact calculated quantity.</summary>
    public decimal Result { get; }
    /// <summary>Gets the result unit token.</summary>
    public string Unit { get; }
    /// <summary>Gets the accepted authority generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the named calculation time.</summary>
    public NamedTime CalculatedAt { get; }

    /// <summary>Creates a result fact; it makes no valuation or source-admission claim.</summary>
    /// <param name="command">The generation command that fixed cut, membership, and algebra.</param>
    /// <param name="result">The exact calculated decimal result.</param>
    /// <param name="unit">The bounded semantic result unit.</param>
    /// <param name="generation">The accepted authority generation.</param>
    /// <param name="calculatedAt">The UTC calculation time.</param>
    /// <exception cref="ArgumentException">The unit, generation, or calculation time is invalid.</exception>
    public MeasurementGenerationFact(CreateMeasurementGenerationCommand command, decimal result, string unit, OwnerGeneration generation, NamedTime calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ContractToken.TryValidate(unit, out var stable) || !generation.IsValid || calculatedAt.Kind != TimeKind.Calculated) throw new ArgumentException("Invalid measurement generation result.");
        (Command, Result, Unit, Generation, CalculatedAt) = (command, result, stable, generation, calculatedAt);
    }
}

/// <summary>Requests append-only supersession of a generation after source, algebra, or completeness changes.</summary>
public sealed record SupersedeMeasurementGenerationCommand
{
    /// <summary>Gets the predecessor generation identity.</summary>
    public SemanticId PredecessorId { get; }
    /// <summary>Gets the predecessor authority generation that must match.</summary>
    public OwnerGeneration ExpectedPredecessorGeneration { get; }
    /// <summary>Gets the complete successor command.</summary>
    public CreateMeasurementGenerationCommand Successor { get; }
    /// <summary>Gets the stable reason token.</summary>
    public string Reason { get; }

    /// <summary>Creates an immutable successor request without mutating the predecessor.</summary>
    /// <param name="predecessorId">The prior immutable generation identity.</param>
    /// <param name="expectedPredecessorGeneration">The prior authority generation that must match.</param>
    /// <param name="successor">The complete replacement generation command.</param>
    /// <param name="reason">A stable bounded supersession reason.</param>
    /// <exception cref="ArgumentException">Lineage, generation, or reason is invalid.</exception>
    public SupersedeMeasurementGenerationCommand(SemanticId predecessorId, OwnerGeneration expectedPredecessorGeneration, CreateMeasurementGenerationCommand successor, string reason)
    {
        ArgumentNullException.ThrowIfNull(successor);
        if (!predecessorId.IsValid || predecessorId == successor.GenerationId || !expectedPredecessorGeneration.IsValid || !ContractToken.TryValidate(reason, out var stable))
            throw new ArgumentException("Invalid measurement generation supersession.");
        (PredecessorId, ExpectedPredecessorGeneration, Successor, Reason) = (predecessorId, expectedPredecessorGeneration, successor, stable);
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
