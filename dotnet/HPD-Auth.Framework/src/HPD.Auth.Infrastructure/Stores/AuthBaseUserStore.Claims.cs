using System.Security.Claims;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore : IUserClaimStore<ApplicationUser>
{
    /// <inheritdoc />
    public async Task<IList<Claim>> GetClaimsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        return (await ReadClaimsAsync(user.Id, cancellationToken).ConfigureAwait(false)).Select(ToClaim).ToList();
    }

    /// <inheritdoc />
    public async Task AddClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentNullException.ThrowIfNull(claims); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        Claim[] owned = claims.ToArray();
        if (owned.Length == 0)
            return;
        if (owned.Length > 256 || owned.Any(static claim => claim is null))
            throw new ArgumentException("The claim set is invalid.", nameof(claims));
        Guid[] ids = Enumerable.Range(0, owned.Length).Select(static _ => Guid.NewGuid()).ToArray();
        DateTimeOffset now = runtime.GetUtcNow();
        string[] identityValues = new string[owned.Length * 7 + 2];
        identityValues[0] = user.Id.ToString("D");
        identityValues[1] = now.ToString("O");
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-claims.add.v1", runtime.TenantId, user.Id.ToString("D"),
            IdentityParts(owned, ids, identityValues));
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        for (int index = 0; index < owned.Length; index++)
        {
            AuthUserClaimRecordV1 record = Record(user.Id, ids[index], owned[index], now);
            batch.Create(AuthUserClaimRecordV1.Collection, RecordId.Create(ids[index].ToString("D")), record);
        }
        RequireBatch(await batch.CommitAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task ReplaceClaimAsync(
        ApplicationUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(newClaim); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        AuthUserClaimsReadV1.Row[] matches = (await ReadClaimsAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .Where(row => Matches(row, claim)).ToArray();
        if (matches.Length == 0)
            return;
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-claims.replace.v1", runtime.TenantId, user.Id.ToString("D"),
            matches.SelectMany(row => new[]
            {
                row.Id.ToString("D"), row.Revision.Value, newClaim.Type, newClaim.Value,
                newClaim.ValueType, newClaim.Issuer, newClaim.OriginalIssuer,
            }).ToArray());
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        foreach (AuthUserClaimsReadV1.Row row in matches)
        {
            AuthUserClaimRecordV1 replacement = Record(user.Id, row.Id, newClaim, row.CreatedAt);
            batch.Replace(AuthUserClaimRecordV1.Collection, RecordId.Create(row.Id.ToString("D")), replacement, row.Revision);
        }
        RequireBatch(await batch.CommitAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task RemoveClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentNullException.ThrowIfNull(claims); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        Claim[] removals = claims.ToArray();
        if (removals.Length == 0)
            return;
        if (removals.Length > 256 || removals.Any(static claim => claim is null))
            throw new ArgumentException("The claim set is invalid.", nameof(claims));
        AuthUserClaimsReadV1.Row[] matches = (await ReadClaimsAsync(user.Id, cancellationToken).ConfigureAwait(false))
            .Where(row => removals.Any(claim => Matches(row, claim))).ToArray();
        if (matches.Length == 0)
            return;
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-claims.remove.v1", runtime.TenantId, user.Id.ToString("D"),
            matches.SelectMany(static row => new[] { row.Id.ToString("D"), row.Revision.Value }).ToArray());
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        foreach (AuthUserClaimsReadV1.Row row in matches)
            batch.Delete(AuthUserClaimRecordV1.Collection, RecordId.Create(row.Id.ToString("D")), row.Revision);
        RequireBatch(await batch.CommitAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IList<ApplicationUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(claim); cancellationToken.ThrowIfCancellationRequested();
        BaseResult<AuthUsersForClaimReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUsersForClaimReadV1.Handle,
            new AuthUsersForClaimReadV1
            {
                TenantId = runtime.TenantId,
                ClaimType = claim.Type,
                ClaimValue = claim.Value,
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUsersForClaimReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        var users = new List<ApplicationUser>(result.RequireValue().Length);
        foreach (AuthUsersForClaimReadV1.Row row in result.RequireValue())
        {
            ApplicationUser? user = await FindByIdAsync(row.UserId.Value.Value, cancellationToken).ConfigureAwait(false);
            if (user is not null)
                users.Add(user);
        }
        return users;
    }

    private async Task<AuthUserClaimsReadV1.Row[]> ReadClaimsAsync(Guid userId, CancellationToken cancellationToken)
    {
        BaseResult<AuthUserClaimsReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUserClaimsReadV1.Handle,
            new AuthUserClaimsReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserClaimsReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue();
    }

    private AuthUserClaimRecordV1 Record(Guid userId, Guid id, Claim claim, DateTimeOffset createdAt) => new()
    {
        Id = id,
        TenantId = runtime.TenantId,
        UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
        ClaimType = claim.Type,
        ClaimValue = claim.Value,
        Issuer = claim.Issuer,
        OriginalIssuer = claim.OriginalIssuer,
        ValueType = claim.ValueType,
        CreatedAt = createdAt,
    };

    private static Claim ToClaim(AuthUserClaimsReadV1.Row row) => new(
        row.ClaimType ?? string.Empty, row.ClaimValue ?? string.Empty,
        row.ValueType, row.Issuer, row.OriginalIssuer);

    private static bool Matches(AuthUserClaimsReadV1.Row row, Claim claim) =>
        string.Equals(row.ClaimType, claim.Type, StringComparison.Ordinal)
        && string.Equals(row.ClaimValue, claim.Value, StringComparison.Ordinal);

    private static string?[] IdentityParts(Claim[] claims, Guid[] ids, string[] scratch)
    {
        int offset = 0;
        foreach ((Claim claim, Guid id) in claims.Zip(ids))
        {
            scratch[offset++] = id.ToString("D");
            scratch[offset++] = claim.Type;
            scratch[offset++] = claim.Value;
            scratch[offset++] = claim.ValueType;
            scratch[offset++] = claim.Issuer;
            scratch[offset++] = claim.OriginalIssuer;
            scratch[offset++] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return scratch.Take(offset).ToArray();
    }
}
