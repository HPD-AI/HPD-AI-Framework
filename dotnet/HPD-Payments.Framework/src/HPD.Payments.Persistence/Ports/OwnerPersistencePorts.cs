using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Persistence.Ports;

/// <summary>Supplies all guards and the immutable authority fact for one owner-local compare-bind/append.</summary>
/// <typeparam name="TFact">Exact authority fact type; Persistence does not interpret or create it.</typeparam>
public sealed record OwnerAppendRequest<TFact> where TFact : notnull
{
    /// <summary>Gets the authority owner and expected generation.</summary>
    public OwnerReference ExpectedOwner { get; }
    /// <summary>Gets the semantic digest used for replay/conflict comparison.</summary>
    public CanonicalDigest SemanticDigest { get; }
    /// <summary>Gets the atomic domain requested by the caller.</summary>
    public AtomicDomain Domain { get; }
    /// <summary>Gets the immutable fact supplied by the authority implementation.</summary>
    public TFact Fact { get; }

    /// <summary>Creates an owner-local append request; construction does not authorize an append.</summary>
    /// <exception cref="ArgumentException">Owner, digest, domain, scope, or fact is invalid.</exception>
    public OwnerAppendRequest(OwnerReference expectedOwner, CanonicalDigest semanticDigest, AtomicDomain domain, TFact fact)
    {
        ArgumentNullException.ThrowIfNull(semanticDigest); ArgumentNullException.ThrowIfNull(fact);
        if (!expectedOwner.IsValid || !domain.IsValid || expectedOwner.SubjectId.Scope != domain.DomainId.Scope)
            throw new ArgumentException("Owner and atomic domain must be valid and share a scope.");
        ExpectedOwner = expectedOwner; SemanticDigest = semanticDigest; Domain = domain; Fact = fact;
    }
}

/// <summary>Requests a bounded immutable owner history at an explicit historical cut.</summary>
public sealed record OwnerHistoryRequest
{
    /// <summary>Gets the exact authority owner whose history is requested.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the immutable historical interpretation and cut.</summary>
    public HistoricalFrame Frame { get; }
    /// <summary>Gets the maximum number of facts to return.</summary>
    public int MaximumFacts { get; }

    /// <summary>Creates a bounded owner-history request.</summary>
    /// <exception cref="ArgumentException">Owner, frame membership, or bound is invalid.</exception>
    public OwnerHistoryRequest(OwnerReference owner, HistoricalFrame frame, int maximumFacts)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!owner.IsValid || maximumFacts is < 1 or > 4096 || !frame.OwnerCuts.Any(x => x.Owner == owner))
            throw new ArgumentException("History request requires a valid owner present in the frame and a bounded fact count.");
        Owner = owner; Frame = frame; MaximumFacts = maximumFacts;
    }
}

/// <summary>Returns an owned bounded sequence of authority facts and an explicit continuation marker.</summary>
/// <typeparam name="TFact">Exact authority fact type.</typeparam>
public sealed record OwnerHistoryPage<TFact> where TFact : notnull
{
    private readonly TFact[] _facts;
    /// <summary>Gets an immutable view over the page-owned fact references.</summary>
    public IReadOnlyList<TFact> Facts => _facts;
    /// <summary>Gets the last generation represented by this page.</summary>
    public OwnerGeneration ThroughGeneration { get; }
    /// <summary>Gets a caller-owned continuation token, or empty memory when the history is complete.</summary>
    public ReadOnlyMemory<byte> Continuation { get; }

    /// <summary>Copies a bounded page and continuation so borrowed input cannot escape.</summary>
    /// <exception cref="ArgumentException">The page is empty, over-bound, contains null, or has an invalid generation/token.</exception>
    public OwnerHistoryPage(ReadOnlySpan<TFact> facts, OwnerGeneration throughGeneration, ReadOnlySpan<byte> continuation = default)
    {
        if (facts.Length is < 1 or > 4096 || !throughGeneration.IsValid || continuation.Length > 1024)
            throw new ArgumentException("History page or continuation is invalid or over-bound.");
        _facts = facts.ToArray();
        if (_facts.Any(static fact => fact is null)) throw new ArgumentException("History facts cannot contain null.", nameof(facts));
        ThroughGeneration = throughGeneration; Continuation = continuation.ToArray();
    }
}

/// <summary>Defines the inward adapter-neutral owner fact port.</summary>
/// <typeparam name="TFact">Exact authority fact type persisted by this closed port instance.</typeparam>
/// <remarks>Implementations provide storage mechanics only; the authority supplies facts and decides their semantic admission.</remarks>
public interface IOwnerPersistencePort<TFact> where TFact : notnull
{
    /// <summary>Attempts one compare-bind/append and returns its bounded durable observation.</summary>
    /// <param name="request">Owner, digest, domain, and authority-created fact.</param>
    /// <param name="cancellationToken">Cooperative cancellation; cancellation does not prove non-commit.</param>
    /// <returns>A receipt that keeps replay, conflict, unsupported, and indeterminate outcomes visible.</returns>
    ValueTask<OwnerAppendReceipt<TFact>> CompareBindAppendAsync(OwnerAppendRequest<TFact> request, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded immutable historical page at the explicit frame.</summary>
    /// <param name="request">Owner, frame, and bound.</param>
    /// <param name="continuation">Opaque continuation returned by the same adapter and exact request shape.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>An owned history page; absence and unsupported behavior are reported by the implementation's documented result path.</returns>
    ValueTask<OwnerHistoryPage<TFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default);
}
