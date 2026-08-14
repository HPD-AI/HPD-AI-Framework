using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Persistence.Receipts;

/// <summary>Names a closed result of an owner compare-bind and append attempt.</summary>
public enum OwnerAppendDisposition
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The exact fact was appended and the returned generation is current for this receipt.</summary>
    Appended,
    /// <summary>The same semantic identity and digest had already been admitted.</summary>
    Replay,
    /// <summary>The expected generation, semantic identity, or digest conflicted.</summary>
    Conflict,
    /// <summary>The request was rejected without an append.</summary>
    Rejected,
    /// <summary>The adapter cannot perform the operation in the requested domain.</summary>
    Unsupported,
    /// <summary>The durable outcome cannot currently be established.</summary>
    Indeterminate,
}

/// <summary>Reports an adapter's bounded observation without certifying a domain guarantee by type existence.</summary>
public enum PersistenceObservation
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The requested operation completed with the scoped postcondition described by the receipt.</summary>
    Observed,
    /// <summary>The requested operation is unsupported in the named domain.</summary>
    Unsupported,
    /// <summary>The requested operation has not been tested for the named domain.</summary>
    Untested,
    /// <summary>The requested operation failed and the failure remains evidence.</summary>
    Failed,
    /// <summary>The operation outcome cannot currently be established.</summary>
    Indeterminate,
}

/// <summary>Returns the bounded result of one owner compare-bind/append operation.</summary>
/// <typeparam name="TFact">The exact authority fact type supplied by the caller.</typeparam>
public sealed record OwnerAppendReceipt<TFact> where TFact : notnull
{
    /// <summary>Gets the owner against which the operation was compared.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the closed append disposition.</summary>
    public OwnerAppendDisposition Disposition { get; }
    /// <summary>Gets the resulting or conflicting owner generation.</summary>
    public OwnerGeneration ObservedGeneration { get; }
    /// <summary>Gets the admitted or existing fact when the disposition is Appended or Replay.</summary>
    public TFact? Fact { get; }
    /// <summary>Gets a bounded diagnostic code; it is not mutation truth.</summary>
    public string Code { get; }

    /// <summary>Creates a typed owner append receipt with internally consistent fact presence.</summary>
    /// <exception cref="ArgumentException">The receipt is invalid or inconsistent.</exception>
    public OwnerAppendReceipt(OwnerReference owner, OwnerAppendDisposition disposition, OwnerGeneration observedGeneration, TFact? fact, string code)
    {
        if (!owner.IsValid || disposition == OwnerAppendDisposition.None || !Enum.IsDefined(disposition) || !observedGeneration.IsValid ||
            !ScopeId.TryCreate("token", "code", code, out _) || (disposition is OwnerAppendDisposition.Appended or OwnerAppendDisposition.Replay) != (fact is not null))
            throw new ArgumentException("Owner append receipt components or fact presence are inconsistent.");
        Owner = owner; Disposition = disposition; ObservedGeneration = observedGeneration; Fact = fact; Code = code;
    }
}

/// <summary>Describes an exact adapter observation for one frozen atomic domain.</summary>
public sealed record AtomicDomainReceipt
{
    /// <summary>Gets the exact domain instance tested or invoked.</summary>
    public AtomicDomain Domain { get; }
    /// <summary>Gets the stable operation identifier.</summary>
    public string Operation { get; }
    /// <summary>Gets the scoped observation.</summary>
    public PersistenceObservation Observation { get; }
    /// <summary>Gets when the observation was recorded.</summary>
    public NamedTime ObservedAt { get; }
    /// <summary>Gets the exact implementation/configuration evidence digest.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets the bounded limitation code, including <c>none</c> when no limitation is declared.</summary>
    public string Limitation { get; }

    /// <summary>Creates evidence about an operation; it never upgrades an untested domain.</summary>
    /// <exception cref="ArgumentException">A component is invalid.</exception>
    public AtomicDomainReceipt(AtomicDomain domain, string operation, PersistenceObservation observation, NamedTime observedAt, CanonicalDigest evidenceDigest, string limitation)
    {
        ArgumentNullException.ThrowIfNull(evidenceDigest);
        if (!domain.IsValid || !ScopeId.TryCreate("token", "operation", operation, out _) || observation == PersistenceObservation.None || !Enum.IsDefined(observation) ||
            !observedAt.IsValid || observedAt.Kind is not (TimeKind.Observed or TimeKind.Verify) || !ScopeId.TryCreate("token", "limitation", limitation, out _))
            throw new ArgumentException("Atomic-domain receipt components are invalid.");
        Domain = domain; Operation = operation; Observation = observation; ObservedAt = observedAt; EvidenceDigest = evidenceDigest; Limitation = limitation;
    }
}

/// <summary>Returns one scoped relation, continuation, custody, or residue persistence observation.</summary>
public sealed record PersistenceReceipt
{
    /// <summary>Gets the subject to which this receipt is scoped.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the closed persistence observation.</summary>
    public PersistenceObservation Observation { get; }
    /// <summary>Gets the atomic-domain evidence for the operation.</summary>
    public AtomicDomainReceipt DomainReceipt { get; }

    /// <summary>Creates a subject-scoped persistence receipt.</summary>
    /// <exception cref="ArgumentException">The subject or scope is invalid.</exception>
    public PersistenceReceipt(SemanticId subjectId, PersistenceObservation observation, AtomicDomainReceipt domainReceipt)
    {
        ArgumentNullException.ThrowIfNull(domainReceipt);
        if (!subjectId.IsValid || observation == PersistenceObservation.None || !Enum.IsDefined(observation) || subjectId.Scope != domainReceipt.Domain.DomainId.Scope)
            throw new ArgumentException("Persistence receipt requires a valid subject in the domain scope.");
        SubjectId = subjectId; Observation = observation; DomainReceipt = domainReceipt;
    }
}
