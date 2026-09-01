namespace HPD.Auth.Core.Models;

/// <summary>Identifies one logical access/refresh-token issuance attempt.</summary>
public sealed record TokenIssuanceIdentity
{
    /// <summary>Gets the stable authorized request scope.</summary>
    public required string RequestScope { get; init; }

    /// <summary>Gets the stable idempotency key within <see cref="RequestScope"/>.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Creates a new identity for a non-retryable in-process issuance attempt.</summary>
    /// <param name="requestScope">Stable bounded name of the calling flow.</param>
    /// <returns>A fresh logical-attempt identity.</returns>
    public static TokenIssuanceIdentity CreateEphemeral(string requestScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestScope);
        return new TokenIssuanceIdentity
        {
            RequestScope = requestScope,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
        };
    }
}

/// <summary>Describes one identified initial refresh-token issuance attempt.</summary>
public sealed record RefreshTokenIssueRequest
{
    /// <summary>Gets the tenant-bound user receiving the credential.</summary>
    public required Guid UserId { get; init; }
    /// <summary>Gets the current Auth security stamp.</summary>
    public required string SecurityStamp { get; init; }
    /// <summary>Gets the UTC refresh-token expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets the stable authorized request scope.</summary>
    public required string RequestScope { get; init; }
    /// <summary>Gets the stable idempotency key inside the request scope.</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>Describes one atomic consume-and-replace refresh-token attempt.</summary>
public sealed record RefreshTokenRotateRequest
{
    /// <summary>Gets the predecessor bearer token.</summary>
    public required string PredecessorToken { get; init; }
    /// <summary>Gets the user's current Auth security stamp.</summary>
    public required string SecurityStamp { get; init; }
    /// <summary>Gets the UTC replacement expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Returns non-secret authority needed to validate a rotation subject.</summary>
public sealed record RefreshTokenInspection
{
    /// <summary>Gets the user bound to the predecessor.</summary>
    public required Guid UserId { get; init; }
    /// <summary>Gets the predecessor expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Returns one committed or receipt-recovered refresh credential.</summary>
public sealed record RefreshTokenPersistenceResult
{
    /// <summary>Gets the exact opaque bearer token.</summary>
    public required string Token { get; init; }
    /// <summary>Gets the user bound to the token.</summary>
    public required Guid UserId { get; init; }
    /// <summary>Gets the JWT identifier committed with the token.</summary>
    public required string JwtId { get; init; }
    /// <summary>Gets the UTC refresh-token expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
