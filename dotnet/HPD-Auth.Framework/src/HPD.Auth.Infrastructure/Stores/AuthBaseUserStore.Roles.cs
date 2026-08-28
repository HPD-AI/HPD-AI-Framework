using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore : IUserRoleStore<ApplicationUser>
{
    /// <inheritdoc />
    public async Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        AuthRoleByNormalizedNameReadV1.Row role = await RequireRoleAsync(normalizedRoleName, cancellationToken).ConfigureAwait(false);
        string membershipId = MembershipId(user.Id, role.Id);
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthMembershipAddV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            RoleId = role.Id,
            MembershipId = membershipId,
            ExpectedUserRevision = authority.Revision,
            ExpectedRoleRevision = role.Revision,
            CreatedAt = now,
        };
        BaseInstalledModuleMutationHandle<AuthMembershipAddV1, AuthMembershipAddResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthMembershipAddOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(request, $"membership:{membershipId}:add");
        BaseResult<BaseModuleMutationExecutionResult<AuthMembershipAddResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        RequireMutation(result);
    }

    /// <inheritdoc />
    public async Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName); cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        AuthRoleByNormalizedNameReadV1.Row role = await RequireRoleAsync(normalizedRoleName, cancellationToken).ConfigureAwait(false);
        AuthUserRolesReadV1.Row? membership = (await ReadRolesAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.RoleId == BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")));
        if (membership is null)
            return;
        var request = new AuthMembershipRemoveV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            RoleId = role.Id,
            MembershipId = membership.Id,
            ExpectedUserRevision = authority.Revision,
            ExpectedRoleRevision = role.Revision,
            ExpectedMembershipRevision = membership.Revision,
            OperationTime = runtime.GetUtcNow(),
        };
        BaseInstalledModuleMutationHandle<AuthMembershipRemoveV1, AuthMembershipRemoveResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthMembershipRemoveOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(request, $"membership:{membership.Id}:remove");
        BaseResult<BaseModuleMutationExecutionResult<AuthMembershipRemoveResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        RequireMutation(result);
    }

    /// <inheritdoc />
    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        return (await ReadRolesAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .Select(static role => role.Name ?? string.Empty).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName); cancellationToken.ThrowIfCancellationRequested();
        return (await ReadRolesAsync(user.Id, cancellationToken).ConfigureAwait(false)).Any(
            role => string.Equals(role.NormalizedName, normalizedRoleName, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName); cancellationToken.ThrowIfCancellationRequested();
        AuthRoleByNormalizedNameReadV1.Row role = await RequireRoleAsync(normalizedRoleName, cancellationToken).ConfigureAwait(false);
        BaseResult<AuthUsersInRoleReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUsersInRoleReadV1.Handle,
            new AuthUsersInRoleReadV1
            {
                TenantId = runtime.TenantId,
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUsersInRoleReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        var users = new List<ApplicationUser>(result.RequireValue().Length);
        foreach (AuthUsersInRoleReadV1.Row row in result.RequireValue())
        {
            ApplicationUser? user = await FindByIdAsync(row.Id.ToString("D"), cancellationToken).ConfigureAwait(false);
            if (user is not null)
                users.Add(user);
        }
        return users;
    }

    private async Task<AuthUserRolesReadV1.Row[]> ReadRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        BaseResult<AuthUserRolesReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUserRolesReadV1.Handle,
            new AuthUserRolesReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserRolesReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue();
    }

    private async Task<AuthRoleByNormalizedNameReadV1.Row> RequireRoleAsync(
        string normalizedRoleName,
        CancellationToken cancellationToken)
    {
        BaseResult<AuthRoleByNormalizedNameReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthRoleByNormalizedNameReadV1.Handle,
            new AuthRoleByNormalizedNameReadV1
            {
                TenantId = runtime.TenantId,
                NormalizedName = normalizedRoleName,
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthRoleByNormalizedNameReadV1.Row?> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue() ?? throw new InvalidOperationException("The requested role does not exist.");
    }

    private string MembershipId(Guid userId, Guid roleId) => AuthBaseDeterministicId.Create(
        runtime.TenantId.ToString("D"), userId.ToString("D"), roleId.ToString("D"));

    private static void RequireMutation<T>(BaseResult<BaseModuleMutationExecutionResult<T>> result)
    {
        if (result is BaseFailure<BaseModuleMutationExecutionResult<T>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
        _ = result.RequireValue();
    }
}
