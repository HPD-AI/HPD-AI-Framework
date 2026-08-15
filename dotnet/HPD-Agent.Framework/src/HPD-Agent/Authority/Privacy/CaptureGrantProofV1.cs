namespace HPD.Agent.Authority;

/// <summary>Identifies the closed folded disposition of an S9 capture grant.</summary>
public enum CaptureGrantStateV1 : ushort
{
    /// <summary>The admitted grant may authorize capture within its exact scope, limits, and expiry.</summary>
    Active = 1,
    /// <summary>A later admitted privacy or consent fence revoked the grant.</summary>
    Revoked = 2,
    /// <summary>The grant passed its permission cap.</summary>
    Expired = 3,
    /// <summary>The admitted item, byte, range, or time limit was exhausted.</summary>
    Exhausted = 4,
    /// <summary>The fold cannot currently establish whether the grant remains usable.</summary>
    Unknown = 5,
}

/// <summary>Projects one S9-admitted capture authorization for neutral pre-effect validation.</summary>
/// <remarks>Construction is internal to trusted S9 folds. This projection cannot grant, extend, revoke, or settle authorization.</remarks>
public sealed record CaptureGrantProofV1
{
    internal CaptureGrantProofV1(
        CaptureGrantId grantId,
        AuthorizationId authorizationId,
        JournalPositionV1 grantedAt,
        ExpectedAuthorityVectorV1 authority,
        Hash256 scopeHash,
        Hash256 limitsHash,
        CaptureGrantStateV1 state,
        UtcInstant expiresAt)
    {
        if (!grantId.IsValid) throw new ArgumentException("A capture grant is required.", nameof(grantId));
        if (!authorizationId.IsValid) throw new ArgumentException("An authorization identity is required.", nameof(authorizationId));
        if (!grantedAt.IsValid) throw new ArgumentException("An admitted grant position is required.", nameof(grantedAt));
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Session != grantedAt.Session) throw new ArgumentException("The grant and authority sessions must match.", nameof(authority));
        Span<byte> hash = stackalloc byte[32];
        if (!scopeHash.TryWriteBytes(hash)) throw new ArgumentException("A capture scope hash is required.", nameof(scopeHash));
        if (!limitsHash.TryWriteBytes(hash)) throw new ArgumentException("A capture limits hash is required.", nameof(limitsHash));
        if (!Enum.IsDefined(state)) throw new ArgumentException("The capture grant state is outside the closed registry.", nameof(state));
        GrantId = grantId;
        AuthorizationId = authorizationId;
        GrantedAt = grantedAt;
        Authority = authority;
        ScopeHash = scopeHash;
        LimitsHash = limitsHash;
        State = state;
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the S9-allocated capture grant identity.</summary>
    public CaptureGrantId GrantId { get; }
    /// <summary>Gets the authorization identity governing the grant.</summary>
    public AuthorizationId AuthorizationId { get; }
    /// <summary>Gets the exact admitted capture-grant fact position.</summary>
    public JournalPositionV1 GrantedAt { get; }
    /// <summary>Gets the authority vector validated when the grant was admitted.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the canonical subject/purpose/audience/classification scope hash.</summary>
    public Hash256 ScopeHash { get; }
    /// <summary>Gets the canonical item/byte/range/time limit hash.</summary>
    public Hash256 LimitsHash { get; }
    /// <summary>Gets the folded lifecycle disposition at <see cref="GrantedAt"/> or its latest S9 revision.</summary>
    public CaptureGrantStateV1 State { get; }
    /// <summary>Gets the UTC permission cap; journal order still decides revisions and revocation.</summary>
    public UtcInstant ExpiresAt { get; }
}
