using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore : IUserAuthenticationTokenStore<ApplicationUser>
{
    private const string IdentityInternalLoginProvider = "[AspNetUserStore]";
    private const string IdentityAuthenticatorKeyToken = "AuthenticatorKey";

    /// <inheritdoc />
    public async Task SetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        string? value,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider); ArgumentException.ThrowIfNullOrWhiteSpace(name); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        if (IsAuthenticatorKey(loginProvider, name))
        {
            authority.AuthenticatorKey = value;
            authority.DirtyFields |= AuthUserDirtyFields.AuthenticatorKey;
            return;
        }
        string id = TokenId(user.Id, loginProvider, name);
        DateTimeOffset now = runtime.GetUtcNow();
        var record = new AuthUserTokenRecordV1
        {
            Id = id,
            TenantId = runtime.TenantId,
            UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
            LoginProvider = loginProvider,
            Name = name,
            Value = value,
            UpdatedAt = now,
        };
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-token.set.v1", runtime.TenantId, id, user.Id.ToString("D"),
            loginProvider, name, value, now.ToString("O"));
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        batch.Upsert(AuthUserTokenRecordV1.Collection, RecordId.Create(id), record, record);
        RequireBatch(await batch.CommitAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task RemoveTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider); ArgumentException.ThrowIfNullOrWhiteSpace(name); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        if (IsAuthenticatorKey(loginProvider, name))
        {
            authority.AuthenticatorKey = null;
            authority.DirtyFields |= AuthUserDirtyFields.AuthenticatorKey;
            return;
        }
        string id = TokenId(user.Id, loginProvider, name);
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-token.remove.v1", runtime.TenantId, id, user.Id.ToString("D"), loginProvider, name);
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        batch.Delete(AuthUserTokenRecordV1.Collection, RecordId.Create(id));
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure && failure.Error.Category == ErrorCategory.NotFound)
            return;
        RequireBatch(result);
    }

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider); ArgumentException.ThrowIfNullOrWhiteSpace(name); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        if (IsAuthenticatorKey(loginProvider, name))
            return authority.AuthenticatorKey;
        BaseResult<AuthUserTokenSecretReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthUserTokenSecretReadV1.Handle,
            new AuthUserTokenSecretReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
                Provider = loginProvider,
                Name = name,
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserTokenSecretReadV1.Row?> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue()?.Value;
    }

    private string TokenId(Guid userId, string provider, string name) => AuthBaseDeterministicId.Create(
        runtime.TenantId.ToString("D"), userId.ToString("D"), provider, name);

    private static bool IsAuthenticatorKey(string loginProvider, string name) =>
        string.Equals(loginProvider, IdentityInternalLoginProvider, StringComparison.Ordinal)
        && string.Equals(name, IdentityAuthenticatorKeyToken, StringComparison.Ordinal);

    private static void RequireBatch(BaseResult<BaseBatchResult> result)
    {
        if (result is BaseFailure<BaseBatchResult> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
        result.RequireValue().RequireCommitted();
    }
}
