using System.Collections.Immutable;
using System.Globalization;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore
{
    /// <inheritdoc />
    public async Task ReplaceCodesAsync(
        ApplicationUser user,
        IEnumerable<string> recoveryCodes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        ImmutableArray<int> versions = AuthRecoveryCodeDigestAuthority.ValidateCapability(recoveryCodeKeys);
        _ = versions;

        string[] canonicalCodes = recoveryCodes.Select(CanonicalRecoveryCode).ToArray();
        if (canonicalCodes.Length > 64 || canonicalCodes.Distinct(StringComparer.Ordinal).Count() != canonicalCodes.Length)
            throw new ArgumentException("Recovery codes must be unique and contain at most 64 values.", nameof(recoveryCodes));
        AuthRecoveryCodesForUserReadV1.Row[] prior = await ReadRecoveryCodesAsync(user.Id, cancellationToken).ConfigureAwait(false);

        AuthAuthorityResult<AuthRecoveryCodeDigestKey> keyResult = recoveryCodeKeys.GetActiveIssuanceKey();
        if (!keyResult.IsAvailable)
            throw new AuthBasePersistenceException("auth.recoveryCode.digestKeyUnavailable");
        using AuthRecoveryCodeDigestKey key = keyResult.Value!;
        if (key.Version != recoveryCodeKeys.Capability.ActiveIssuanceVersion)
            throw new AuthBasePersistenceException("auth.recoveryCode.digestKeyUnavailable");

        var activeSlots = new List<AuthRecoveryNewSlotV1>(canonicalCodes.Length);
        foreach (string canonicalCode in canonicalCodes)
        {
            byte[] digest = AuthRecoveryCodeDigestAuthority.Digest(key, canonicalCode);
            try
            {
                string id = AuthBaseDeterministicId.Create(
                    runtime.TenantId.ToString("D"), user.Id.ToString("D"), "recovery-code",
                    key.Version.ToString(CultureInfo.InvariantCulture), Convert.ToHexStringLower(digest));
                activeSlots.Add(new AuthRecoveryNewSlotV1
                {
                    Active = true,
                    CodeDigest = BaseBinary.From(digest),
                    DigestKeyVersion = key.Version,
                    Id = id,
                });
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(digest);
            }
        }
        activeSlots.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

        var newSlots = new AuthRecoveryNewSlotV1[64];
        for (int index = 0; index < newSlots.Length; index++)
        {
            newSlots[index] = index < activeSlots.Count
                ? activeSlots[index]
                : new AuthRecoveryNewSlotV1
                {
                    Active = false,
                    CodeDigest = BaseBinary.From([]),
                    DigestKeyVersion = 1,
                    Id = new string('0', 64),
                };
        }

        var priorSlots = new AuthRecoveryPriorSlotV1[64];
        for (int index = 0; index < priorSlots.Length; index++)
            priorSlots[index] = index < prior.Length
                ? new AuthRecoveryPriorSlotV1 { Active = true, Id = prior[index].Id }
                : new AuthRecoveryPriorSlotV1 { Active = false, Id = new string('0', 64) };

        string nextSecurityStamp = Guid.NewGuid().ToString("N");
        string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        DateTimeOffset now = runtime.GetUtcNow();
        AuthRecoveryCodesReplaceV1 request = AuthRecoveryCodesReplaceRequestFactory.Create(
            runtime.TenantId, user.Id, authority.Revision, priorSlots, newSlots,
            nextSecurityStamp, nextConcurrencyStamp, now);
        BaseInstalledModuleMutationHandle<AuthRecoveryCodesReplaceV1, AuthRecoveryCodeMutationResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthRecoveryCodesReplaceOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:recovery-codes:replace:revision:{authority.Revision.Value}");
        BaseResult<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));

        AuthRecoveryCodeMutationResultV1 committed = result.RequireValue().Result;
        user.SecurityStamp = nextSecurityStamp;
        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, committed.UserRevision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
    }

    /// <inheritdoc />
    public async Task<bool> RedeemCodeAsync(
        ApplicationUser user,
        string code,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        string canonicalCode = CanonicalRecoveryCode(code);
        ImmutableArray<int> versions = AuthRecoveryCodeDigestAuthority.ValidateCapability(recoveryCodeKeys);

        foreach (int version in versions)
        {
            AuthAuthorityResult<AuthRecoveryCodeDigestKey> keyResult = recoveryCodeKeys.GetValidationKey(version);
            if (!keyResult.IsAvailable)
                return false;
            using AuthRecoveryCodeDigestKey key = keyResult.Value!;
            if (key.Version != version)
                return false;
            byte[] digest = AuthRecoveryCodeDigestAuthority.Digest(key, canonicalCode);
            AuthRecoveryCodeByDigestReadV1.Row? row;
            try
            {
                BaseResult<AuthRecoveryCodeByDigestReadV1.Row?> read = await runtime.OpenServiceSession().Reads.FirstAsync(
                    AuthRecoveryCodeByDigestReadV1.Handle,
                    new AuthRecoveryCodeByDigestReadV1
                    {
                        TenantId = runtime.TenantId,
                        UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
                        DigestKeyVersion = version,
                        CodeDigest = BaseBinary.From(digest),
                    }, cancellationToken).ConfigureAwait(false);
                if (read is BaseFailure<AuthRecoveryCodeByDigestReadV1.Row?> readFailure)
                    throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(readFailure.Error));
                row = read.RequireValue();
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(digest);
            }
            if (row is null)
                continue;

            string nextSecurityStamp = Guid.NewGuid().ToString("N");
            string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
            DateTimeOffset now = runtime.GetUtcNow();
            byte[] committedDigest = AuthRecoveryCodeDigestAuthority.Digest(key, canonicalCode);
            var request = new AuthRecoveryCodeConsumeV1
            {
                TenantId = runtime.TenantId,
                UserId = user.Id,
                CodeId = row.Id,
                CodeDigest = BaseBinary.From(committedDigest),
                ExpectedCodeRevision = row.Revision,
                ExpectedUserRevision = authority.Revision,
                SecurityStamp = nextSecurityStamp,
                ConcurrencyStamp = nextConcurrencyStamp,
                OperationTime = now,
            };
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(committedDigest);
            BaseInstalledModuleMutationHandle<AuthRecoveryCodeConsumeV1, AuthRecoveryCodeMutationResultV1> operation = runtime
                .OpenServiceSession().ModuleMutations.Get(AuthRecoveryCodeConsumeOperationV1.Identity);
            BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
                request, $"user:{user.Id:D}:recovery-code:{row.Id}:consume");
            BaseResult<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> result = await operation
                .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> failure)
            {
                if (failure.Error.Category is ErrorCategory.NotFound or ErrorCategory.Conflict)
                    return false;
                throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
            }

            AuthRecoveryCodeMutationResultV1 committed = result.RequireValue().Result;
            user.SecurityStamp = nextSecurityStamp;
            user.ConcurrencyStamp = nextConcurrencyStamp;
            user.Updated = now.UtcDateTime;
            if (!await RefreshAuthorityAsync(user, committed.UserRevision, cancellationToken).ConfigureAwait(false))
                throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<int> CountCodesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        return (await ReadRecoveryCodesAsync(user.Id, cancellationToken).ConfigureAwait(false)).Length;
    }

    private async Task<AuthRecoveryCodesForUserReadV1.Row[]> ReadRecoveryCodesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        BaseResult<AuthRecoveryCodesForUserReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthRecoveryCodesForUserReadV1.Handle,
            new AuthRecoveryCodesForUserReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthRecoveryCodesForUserReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue();
    }

    private static string CanonicalRecoveryCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        string canonical = code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (canonical.Length is < 8 or > 128 || canonical.Any(static value => !char.IsAsciiLetterOrDigit(value)))
            throw new ArgumentException("The recovery code is not canonical.", nameof(code));
        return canonical;
    }
}
