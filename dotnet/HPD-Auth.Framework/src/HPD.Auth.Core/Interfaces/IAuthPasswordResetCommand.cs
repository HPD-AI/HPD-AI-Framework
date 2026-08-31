using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Validates and commits password-reset commands through HPD Auth's authoritative
/// persistence operation.
/// </summary>
public interface IAuthPasswordResetCommand
{
    /// <summary>
    /// Validates a public password-reset token and atomically resets the credential
    /// and lockout state.
    /// </summary>
    /// <param name="user">The detached user whose credential is reset.</param>
    /// <param name="token">The public password-reset token.</param>
    /// <param name="newPassword">The new plaintext password to validate and hash.</param>
    /// <param name="cancellationToken">Cancels the command before commit.</param>
    /// <returns>The Identity-compatible command result.</returns>
    Task<IdentityResult> ResetWithTokenAsync(
        ApplicationUser user,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically resets a credential and lockout state after the caller has already
    /// established privileged administrative authority.
    /// </summary>
    /// <param name="user">The detached user whose credential is reset.</param>
    /// <param name="newPassword">The new plaintext password to validate and hash.</param>
    /// <param name="cancellationToken">Cancels the command before commit.</param>
    /// <returns>The Identity-compatible command result.</returns>
    Task<IdentityResult> ResetByAuthorityAsync(
        ApplicationUser user,
        string newPassword,
        CancellationToken cancellationToken = default);
}
