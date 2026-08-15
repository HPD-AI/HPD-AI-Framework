using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

namespace HPD.Payments.Persistence.Ports;

/// <summary>Persists a supporting relation only after both immutable endpoint generations are guarded.</summary>
public interface IRelationPersistencePort
{
    /// <summary>Attempts to persist a relation without mutating either endpoint.</summary>
    /// <param name="relation">Supporting relation with exact endpoint generations.</param>
    /// <param name="domain">Requested local or distributed relation domain.</param>
    /// <param name="cancellationToken">Cooperative cancellation; cancellation does not establish rollback.</param>
    /// <returns>A scoped observation that makes unsupported or indeterminate relation outcomes visible.</returns>
    ValueTask<PersistenceReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default);
}

/// <summary>Describes one authority-created continuation that must become durably discoverable.</summary>
public sealed record ContinuationDeclaration
{
    /// <summary>Gets the owner fact that requires the continuation.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the Work Requirement or Publication Obligation identity.</summary>
    public SemanticId ContinuationId { get; }
    /// <summary>Gets the exact continuation fact digest.</summary>
    public CanonicalDigest Digest { get; }

    /// <summary>Creates a same-scope continuation declaration.</summary>
    /// <exception cref="ArgumentException">Owner, continuation identity, scope, or digest is invalid.</exception>
    public ContinuationDeclaration(OwnerReference owner, SemanticId continuationId, CanonicalDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (!owner.IsValid || !continuationId.IsValid || owner.SubjectId.Scope != continuationId.Scope)
            throw new ArgumentException("Continuation and owner must be valid and share a scope.");
        Owner = owner; ContinuationId = continuationId; Digest = digest;
    }
}

/// <summary>Persists or discovers authority-created continuation obligations without treating queue state as authority truth.</summary>
public interface IContinuationPersistencePort
{
    /// <summary>Makes the continuation discoverable in the requested certified domain or returns explicit residue.</summary>
    ValueTask<PersistenceReceipt> CommitDiscoverableAsync(ContinuationDeclaration continuation, AtomicDomain domain, CancellationToken cancellationToken = default);
    /// <summary>Discovers a bounded page of continuation declarations after an opaque adapter token.</summary>
    ValueTask<ContinuationDiscoveryPage> DiscoverAsync(AtomicDomain domain, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default);
}

/// <summary>Returns owned continuation declarations and an opaque next-page token.</summary>
public sealed record ContinuationDiscoveryPage
{
    private readonly ContinuationDeclaration[] _items;
    /// <summary>Gets an immutable view over page-owned declarations.</summary>
    public IReadOnlyList<ContinuationDeclaration> Items => _items;
    /// <summary>Gets an owned opaque continuation token, or empty memory at the end.</summary>
    public ReadOnlyMemory<byte> Continuation { get; }

    /// <summary>Copies and validates one bounded discovery page.</summary>
    /// <exception cref="ArgumentException">The item/token bounds or item uniqueness are invalid.</exception>
    public ContinuationDiscoveryPage(ReadOnlySpan<ContinuationDeclaration> items, ReadOnlySpan<byte> continuation = default)
    {
        if (items.Length > 1024 || continuation.Length > 1024) throw new ArgumentException("Discovery page exceeds its bound.");
        _items = items.ToArray();
        if (_items.Any(static x => x is null) || _items.Select(static x => x.ContinuationId).Distinct().Count() != _items.Length)
            throw new ArgumentException("Discovery items must be non-null and unique.", nameof(items));
        Continuation = continuation.ToArray();
    }
}

/// <summary>Records per-instance custody and residue observations without implying global deletion.</summary>
public interface ICustodyPersistencePort
{
    /// <summary>Persists one authority-routed custody instance observation.</summary>
    ValueTask<PersistenceReceipt> RecordCustodyAsync(CustodyInstance custody, AtomicDomain domain, CancellationToken cancellationToken = default);
    /// <summary>Reads bounded custody observations for the exact owner at a declared inventory generation.</summary>
    ValueTask<CustodyPage> ReadCustodyAsync(OwnerReference owner, OwnerGeneration throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default);
}

/// <summary>Returns an owned bounded page of per-controller custody observations.</summary>
public sealed record CustodyPage
{
    private readonly CustodyInstance[] _items;
    /// <summary>Gets an immutable view over page-owned custody instances.</summary>
    public IReadOnlyList<CustodyInstance> Items => _items;
    /// <summary>Gets an owned opaque continuation token.</summary>
    public ReadOnlyMemory<byte> Continuation { get; }

    /// <summary>Copies and validates one custody page while preserving each named residue.</summary>
    /// <exception cref="ArgumentException">Bounds, null items, or duplicate instance identities are invalid.</exception>
    public CustodyPage(ReadOnlySpan<CustodyInstance> items, ReadOnlySpan<byte> continuation = default)
    {
        if (items.Length > 1024 || continuation.Length > 1024) throw new ArgumentException("Custody page exceeds its bound.");
        _items = items.ToArray();
        if (_items.Any(static x => x is null) || _items.Select(static x => x.InstanceId).Distinct().Count() != _items.Length)
            throw new ArgumentException("Custody items must be non-null and unique.", nameof(items));
        Continuation = continuation.ToArray();
    }
}
