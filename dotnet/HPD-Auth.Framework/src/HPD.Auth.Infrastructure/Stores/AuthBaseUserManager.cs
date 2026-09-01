using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// Routes Identity password resets through the installed atomic Auth reset command.
/// </summary>
internal sealed class AuthBaseUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> options,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger)
    : UserManager<ApplicationUser>(
        store,
        options,
        passwordHasher,
        userValidators,
        passwordValidators,
        keyNormalizer,
        errors,
        services,
        logger),
      IAuthPasswordResetCommand
{
    /// <inheritdoc />
    public override Task<IdentityResult> ResetPasswordAsync(
        ApplicationUser user,
        string token,
        string newPassword) =>
        ResetWithTokenAsync(user, token, newPassword);

    /// <inheritdoc />
    public async Task<IdentityResult> ResetWithTokenAsync(
        ApplicationUser user,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        bool valid = await VerifyUserTokenAsync(
            user,
            Options.Tokens.PasswordResetTokenProvider,
            ResetPasswordTokenPurpose,
            token).ConfigureAwait(false);
        if (!valid)
            return IdentityResult.Failed(ErrorDescriber.InvalidToken());

        return await ResetCoreAsync(user, newPassword, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IdentityResult> ResetByAuthorityAsync(
        ApplicationUser user,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();
        return ResetCoreAsync(user, newPassword, cancellationToken);
    }

    private async Task<IdentityResult> ResetCoreAsync(
        ApplicationUser user,
        string newPassword,
        CancellationToken cancellationToken)
    {
        List<IdentityError>? validationErrors = null;
        foreach (IPasswordValidator<ApplicationUser> validator in PasswordValidators)
        {
            IdentityResult validation = await validator
                .ValidateAsync(this, user, newPassword)
                .ConfigureAwait(false);
            if (!validation.Succeeded)
                (validationErrors ??= []).AddRange(validation.Errors);
        }

        if (validationErrors is { Count: > 0 })
            return IdentityResult.Failed(validationErrors.ToArray());

        if (Store is not AuthBaseUserStore authStore)
            throw new InvalidOperationException("The HPD Auth Base user store is not installed.");

        string passwordHash = PasswordHasher.HashPassword(user, newPassword);
        string securityStamp = Guid.NewGuid().ToString("N");
        string concurrencyStamp = Guid.NewGuid().ToString("N");
        return await authStore.ResetPasswordAsync(
            user,
            passwordHash,
            securityStamp,
            concurrencyStamp,
            cancellationToken).ConfigureAwait(false);
    }
}
