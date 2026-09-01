using HPD.Auth.Core.Models;

namespace HPD.Auth.Core.Interfaces;

/// <summary>Owns atomic refresh-token issuance, rotation, delivery recovery, and revocation.</summary>
public interface IRefreshTokenStore
{
    /// <summary>Issues one identified refresh credential without persisting its bearer value.</summary>
    /// <param name="request">Bounded issuance request and idempotency authority.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The committed or receipt-recovered credential.</returns>
    Task<RefreshTokenPersistenceResult> IssueAsync(RefreshTokenIssueRequest request, CancellationToken ct = default);

    /// <summary>Finds safe predecessor authority without consuming the credential.</summary>
    /// <param name="token">Opaque predecessor bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Safe authority, or <see langword="null"/> for every invalid-token state.</returns>
    Task<RefreshTokenInspection?> InspectAsync(string token, CancellationToken ct = default);

    /// <summary>Atomically consumes one predecessor and creates its replacement.</summary>
    /// <param name="request">Rotation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The committed or receipt-recovered replacement; otherwise <see langword="null"/>.</returns>
    Task<RefreshTokenPersistenceResult?> RotateAsync(RefreshTokenRotateRequest request, CancellationToken ct = default);

    /// <summary>Revokes one exact credential under its current revision.</summary>
    /// <param name="token">Opaque bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> only when the credential exists or its identified revocation resolves.</returns>
    Task<bool> RevokeAsync(string token, CancellationToken ct = default);

    /// <summary>Revokes one bounded cohort of active credentials for a user.</summary>
    /// <param name="userId">Tenant-bound user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
