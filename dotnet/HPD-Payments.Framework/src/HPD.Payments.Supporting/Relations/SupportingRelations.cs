using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.Relations;

/// <summary>Names a closed supporting relation without promoting the relation into an authority.</summary>
public enum SupportingRelationKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>An obligation is satisfied in part by a movement or position fact.</summary>
    Application,
    /// <summary>Two observations match under an explicit comparison.</summary>
    Match,
    /// <summary>An external claim is bound to an owner-local subject.</summary>
    Binding,
    /// <summary>One immutable subject corrects an earlier subject.</summary>
    Corrects,
    /// <summary>One immutable subject supersedes an earlier representation.</summary>
    Supersedes,
    /// <summary>A representation derives from a declared source.</summary>
    DerivedFrom,
}

/// <summary>Declares an immutable typed relation between two owner-routed endpoints.</summary>
/// <remarks>The relation owns no endpoint and cannot admit, revise, or delete authority state.</remarks>
public sealed record SupportingRelation
{
    /// <summary>Gets the stable relation identity.</summary>
    public SemanticId RelationId { get; }
    /// <summary>Gets the closed relation meaning.</summary>
    public SupportingRelationKind Kind { get; }
    /// <summary>Gets the source endpoint and its exact authority generation.</summary>
    public OwnerReference Source { get; }
    /// <summary>Gets the target endpoint and its exact authority generation.</summary>
    public OwnerReference Target { get; }
    /// <summary>Gets the revision governing interpretation of the relation.</summary>
    public Revision RelationRevision { get; }

    /// <summary>Creates a same-scope, explicitly owned supporting relation.</summary>
    /// <exception cref="ArgumentException">Identity, kind, endpoint, scope, or revision is invalid.</exception>
    public SupportingRelation(SemanticId relationId, SupportingRelationKind kind, OwnerReference source, OwnerReference target, Revision relationRevision)
    {
        if (!relationId.IsValid || kind == SupportingRelationKind.None || !Enum.IsDefined(kind) || !source.IsValid || !target.IsValid ||
            source.SubjectId.Scope != target.SubjectId.Scope || relationId.Scope != source.SubjectId.Scope || !relationRevision.IsValid)
            throw new ArgumentException("A relation requires valid same-scope endpoints, a closed kind, and a revision.");
        RelationId = relationId; Kind = kind; Source = source; Target = target; RelationRevision = relationRevision;
    }
}

/// <summary>Records a positive application quantity without claiming obligation or movement mutation.</summary>
public sealed record ApplicationRelation
{
    /// <summary>Gets the underlying typed relation, whose kind is Application.</summary>
    public SupportingRelation Relation { get; }
    /// <summary>Gets the strictly positive applied magnitude.</summary>
    public decimal Magnitude { get; }
    /// <summary>Gets the explicit bounded unit shared by both authority facts.</summary>
    public string Unit { get; }

    /// <summary>Creates a descriptive application relation.</summary>
    /// <exception cref="ArgumentException">The relation is not Application, the magnitude is non-positive, or the unit is invalid.</exception>
    public ApplicationRelation(SupportingRelation relation, decimal magnitude, string unit)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (relation.Kind != SupportingRelationKind.Application || magnitude <= 0 || !ScopeId.TryCreate("unit", "unit", unit, out _))
            throw new ArgumentException("Application requires an Application relation, positive magnitude, and bounded unit.");
        Relation = relation; Magnitude = magnitude; Unit = unit;
    }
}
