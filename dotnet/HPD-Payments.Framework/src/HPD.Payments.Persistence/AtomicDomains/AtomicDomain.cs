using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Persistence.AtomicDomains;

/// <summary>Names the four frozen persistence execution domains without implying that an adapter has certified one.</summary>
public enum AtomicDomainKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>One locally certified atomic domain (<c>E-LOCAL</c>).</summary>
    Local,
    /// <summary>One distributed owner conflict domain (<c>D-OWNER</c>).</summary>
    DistributedOwner,
    /// <summary>One distributed guarded-relation domain (<c>D-REL</c>).</summary>
    DistributedRelation,
    /// <summary>One distributed continuation-discovery domain (<c>D-CONT</c>).</summary>
    DistributedContinuation,
}

/// <summary>Identifies one concrete, bounded atomic-domain instance and its frozen kind.</summary>
/// <remarks>The value is a routing input only. Constructing it neither certifies atomicity nor grants mutation authority.</remarks>
public readonly record struct AtomicDomain
{
    /// <summary>Gets the semantic identity of the concrete domain instance.</summary>
    public SemanticId DomainId { get; }
    /// <summary>Gets the frozen execution-domain kind.</summary>
    public AtomicDomainKind Kind { get; }
    /// <summary>Gets the adapter configuration or topology revision under which the domain is interpreted.</summary>
    public Revision TopologyRevision { get; }
    /// <summary>Gets whether all components are valid.</summary>
    public bool IsValid => DomainId.IsValid && Kind != AtomicDomainKind.None && Enum.IsDefined(Kind) && TopologyRevision.IsValid;

    /// <summary>Creates an adapter-neutral atomic-domain identity.</summary>
    /// <param name="domainId">Concrete domain identity.</param>
    /// <param name="kind">Frozen domain kind.</param>
    /// <param name="topologyRevision">Exact topology/configuration revision.</param>
    /// <exception cref="ArgumentException">A component is invalid or the kind is unknown.</exception>
    public AtomicDomain(SemanticId domainId, AtomicDomainKind kind, Revision topologyRevision)
    {
        if (!domainId.IsValid || kind == AtomicDomainKind.None || !Enum.IsDefined(kind) || !topologyRevision.IsValid)
            throw new ArgumentException("A valid domain identity, frozen kind, and topology revision are required.");
        DomainId = domainId;
        Kind = kind;
        TopologyRevision = topologyRevision;
    }
}
