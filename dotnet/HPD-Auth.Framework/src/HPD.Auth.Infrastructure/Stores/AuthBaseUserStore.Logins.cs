using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore : IUserLoginStore<ApplicationUser>
{
    /// <inheritdoc />
    public async Task AddLoginAsync(ApplicationUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentNullException.ThrowIfNull(login); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        string loginId = LoginId(login.LoginProvider, login.ProviderKey);
        Guid identityId = Guid.NewGuid();
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthLoginLinkV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            LoginId = loginId,
            IdentityId = identityId,
            ExpectedUserRevision = authority.Revision,
            LoginProvider = login.LoginProvider,
            ProviderKey = login.ProviderKey,
            ProviderDisplayName = login.ProviderDisplayName,
            ProviderId = login.ProviderKey,
            IdentityData = EmptyObject(),
            FederationSourceId = null,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthLoginLinkV1, AuthLoginLinkResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthLoginLinkOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(request, $"login:{loginId}:link");
        RequireMutation(await operation.ExecuteAsync(
            request, identity, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task RemoveLoginAsync(
        ApplicationUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider); ArgumentException.ThrowIfNullOrWhiteSpace(providerKey); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        AuthUserLoginsReadV1.Row? login = (await ReadLoginsAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.LoginProvider, loginProvider, StringComparison.Ordinal)
                && string.Equals(item.ProviderKey, providerKey, StringComparison.Ordinal));
        if (login is null)
            return;
        AuthExternalIdentityReadV1.Row? external = await ReadExternalIdentityAsync(
            loginProvider, providerKey, cancellationToken).ConfigureAwait(false);
        if (external is null || external.UserId != BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")))
            throw new AuthBasePersistenceException("auth.persistence.authorityChanged");
        var request = new AuthLoginUnlinkV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            LoginId = login.Id,
            IdentityId = external.Id,
            ExpectedUserRevision = authority.Revision,
            ExpectedLoginRevision = login.Revision,
            ExpectedIdentityRevision = external.Revision,
            OperationTime = runtime.GetUtcNow(),
        };
        BaseInstalledModuleMutationHandle<AuthLoginUnlinkV1, AuthLoginUnlinkResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthLoginUnlinkOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(request, $"login:{login.Id}:unlink");
        RequireMutation(await operation.ExecuteAsync(
            request, identity, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        return (await ReadLoginsAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .Select(static login => new UserLoginInfo(
                login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)).ToList();
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider); ArgumentException.ThrowIfNullOrWhiteSpace(providerKey); cancellationToken.ThrowIfCancellationRequested();
        AuthExternalIdentityReadV1.Row? identity = await ReadExternalIdentityAsync(
            loginProvider, providerKey, cancellationToken).ConfigureAwait(false);
        return identity is null
            ? null
            : await FindByIdAsync(identity.UserId.Value.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthUserLoginsReadV1.Row[]> ReadLoginsAsync(Guid userId, CancellationToken cancellationToken)
    {
        BaseResult<AuthUserLoginsReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUserLoginsReadV1.Handle,
            new AuthUserLoginsReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserLoginsReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue();
    }

    private async Task<AuthExternalIdentityReadV1.Row?> ReadExternalIdentityAsync(
        string provider,
        string providerId,
        CancellationToken cancellationToken)
    {
        BaseResult<AuthExternalIdentityReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthExternalIdentityReadV1.Handle,
            new AuthExternalIdentityReadV1
            {
                TenantId = runtime.TenantId,
                Provider = provider,
                ProviderId = providerId,
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthExternalIdentityReadV1.Row?> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue();
    }

    private string LoginId(string provider, string providerKey) => AuthBaseDeterministicId.Create(
        runtime.TenantId.ToString("D"), provider, providerKey);

    private static BaseCanonicalJson EmptyObject() => BaseCanonicalJson.ParseAndValidate("{}"u8,
        new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 65_536,
            MaximumDepth = 16,
            MaximumArrayItemsPerContainer = 1_024,
            MaximumObjectPropertiesPerContainer = 1_024,
            MaximumTotalNodes = 4_096,
            MaximumTotalStringUtf8Bytes = 65_536,
            MaximumTotalNameUtf8Bytes = 65_536,
        });
}
