using System.Security.Cryptography;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Auth.Infrastructure.Serialization;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

internal sealed partial class AuthBaseUserStore
{
    /// <inheritdoc />
    public async Task AddOrUpdatePasskeyAsync(
        ApplicationUser user,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(passkey);
        cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        ValidatePasskey(passkey);

        byte[] digest = SHA256.HashData(passkey.CredentialId);
        string passkeyId = Convert.ToHexStringLower(digest);
        AuthPasskeyByDigestReadV1.Row? existing = await FindPasskeyRowAsync(
            passkey.CredentialId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.UserId.Value.Value, user.Id.ToString("D"), StringComparison.Ordinal))
                throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");
            if (IsAssertionUpdate(existing, passkey))
            {
                await RecordPasskeyAssertionAsync(
                    user, authority, existing, passkey, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        string nextSecurityStamp = Guid.NewGuid().ToString("N");
        string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthPasskeyRegisterV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            PasskeyId = passkeyId,
            ExpectedUserRevision = authority.Revision,
            CredentialDigest = BaseBinary.From(digest),
            CredentialId = BaseBinary.From(passkey.CredentialId),
            PublicKey = BaseBinary.From(passkey.PublicKey),
            SignatureCounter = passkey.SignCount,
            AaGuid = null,
            Name = passkey.Name,
            Transports = CanonicalTransports(passkey.Transports),
            UserVerified = passkey.IsUserVerified,
            BackupEligible = passkey.IsBackupEligible,
            BackedUp = passkey.IsBackedUp,
            IsDiscoverable = true,
            AttestationObject = BaseBinary.From(passkey.AttestationObject),
            ClientDataJson = BaseBinary.From(passkey.ClientDataJson),
            SecurityStamp = nextSecurityStamp,
            ConcurrencyStamp = nextConcurrencyStamp,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthPasskeyRegisterV1, AuthPasskeyRegisterResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthPasskeyRegisterOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:revision:{authority.Revision.Value}:passkey:{passkeyId}:add-or-update");
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));

        AuthPasskeyRegisterResultV1 committed = result.RequireValue().Result;
        user.SecurityStamp = nextSecurityStamp;
        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, committed.UserRevision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
    }

    /// <inheritdoc />
    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        BaseResult<AuthUserPasskeysReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthUserPasskeysReadV1.Handle,
            new AuthUserPasskeysReadV1
            {
                TenantId = runtime.TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthUserPasskeysReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue().Select(ToPasskeyInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<UserPasskeyInfo?> FindPasskeyAsync(
        ApplicationUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(user);
        AuthPasskeyByDigestReadV1.Row? row = await FindPasskeyRowAsync(credentialId, cancellationToken).ConfigureAwait(false);
        return row is not null && string.Equals(row.UserId.Value.Value, user.Id.ToString("D"), StringComparison.Ordinal)
            ? ToPasskeyInfo(row)
            : null;
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> FindByPasskeyIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(credentialId);
        cancellationToken.ThrowIfCancellationRequested();
        AuthPasskeyByDigestReadV1.Row? row = await FindPasskeyRowAsync(credentialId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : await FindByIdAsync(row.UserId.Value.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemovePasskeyAsync(
        ApplicationUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);
        cancellationToken.ThrowIfCancellationRequested();
        AuthUserAuthorityLease authority = RequireAttachedAuthority(user);
        AuthPasskeyByDigestReadV1.Row? row = await FindPasskeyRowAsync(credentialId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return;
        if (!string.Equals(row.UserId.Value.Value, user.Id.ToString("D"), StringComparison.Ordinal))
            throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");

        string nextSecurityStamp = Guid.NewGuid().ToString("N");
        string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthPasskeyRemoveV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            PasskeyId = row.Id,
            ExpectedUserRevision = authority.Revision,
            ExpectedPasskeyRevision = row.Revision,
            SecurityStamp = nextSecurityStamp,
            ConcurrencyStamp = nextConcurrencyStamp,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthPasskeyRemoveV1, AuthPasskeyRemoveResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthPasskeyRemoveOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:passkey:{row.Id}:remove");
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyRemoveResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyRemoveResultV1>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));

        AuthPasskeyRemoveResultV1 committed = result.RequireValue().Result;
        user.SecurityStamp = nextSecurityStamp;
        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, committed.UserRevision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
    }

    private async Task<AuthPasskeyByDigestReadV1.Row?> FindPasskeyRowAsync(
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        if (credentialId.Length is < 1 or > 1024)
            return null;
        byte[] digest = SHA256.HashData(credentialId);
        BaseResult<AuthPasskeyByDigestReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthPasskeyByDigestReadV1.Handle,
            new AuthPasskeyByDigestReadV1
            {
                CredentialDigest = BaseBinary.From(digest),
                TenantHint = runtime.TenantId,
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthPasskeyByDigestReadV1.Row?> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        AuthPasskeyByDigestReadV1.Row? row = result.RequireValue();
        return row is not null && CryptographicOperations.FixedTimeEquals(row.CredentialId.ToArray(), credentialId)
            ? row
            : null;
    }

    private async Task RecordPasskeyAssertionAsync(
        ApplicationUser user,
        AuthUserAuthorityLease authority,
        AuthPasskeyByDigestReadV1.Row existing,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthPasskeyRecordAssertionV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            PasskeyId = existing.Id,
            ExpectedUserRevision = authority.Revision,
            ExpectedPasskeyRevision = existing.Revision,
            PresentedCounter = passkey.SignCount,
            BackedUp = passkey.IsBackedUp,
            CounterSupported = existing.SignatureCounter != 0 || passkey.SignCount != 0,
            UserVerified = passkey.IsUserVerified,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthPasskeyRecordAssertionV1, AuthPasskeyAssertionResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthPasskeyRecordAssertionOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request,
            $"user:{user.Id:D}:revision:{authority.Revision.Value}:passkey:{existing.Id}:revision:{existing.Revision.Value}:assertion");
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
    }

    private static bool IsAssertionUpdate(AuthPasskeyByDigestReadV1.Row existing, UserPasskeyInfo passkey) =>
        existing.CreatedAt == passkey.CreatedAt
        && string.Equals(existing.Name, passkey.Name, StringComparison.Ordinal)
        && existing.BackupEligible == passkey.IsBackupEligible
        && CryptographicOperations.FixedTimeEquals(existing.PublicKey.ToArray(), passkey.PublicKey)
        && CryptographicOperations.FixedTimeEquals(existing.AttestationObject.ToArray(), passkey.AttestationObject)
        && CryptographicOperations.FixedTimeEquals(existing.ClientDataJson.ToArray(), passkey.ClientDataJson)
        && ParseTransports(existing.Transports).SequenceEqual(passkey.Transports ?? [], StringComparer.Ordinal);

    private static UserPasskeyInfo ToPasskeyInfo(AuthUserPasskeysReadV1.Row row) => new(
        row.CredentialId.ToArray(), row.PublicKey.ToArray(), row.CreatedAt, checked((uint)row.SignatureCounter),
        ParseTransports(row.Transports), row.UserVerified, row.BackupEligible, row.BackedUp,
        row.AttestationObject.ToArray(), row.ClientDataJson.ToArray()) { Name = row.Name };

    private static UserPasskeyInfo ToPasskeyInfo(AuthPasskeyByDigestReadV1.Row row) => new(
        row.CredentialId.ToArray(), row.PublicKey.ToArray(), row.CreatedAt, checked((uint)row.SignatureCounter),
        ParseTransports(row.Transports), row.UserVerified, row.BackupEligible, row.BackedUp,
        row.AttestationObject.ToArray(), row.ClientDataJson.ToArray()) { Name = row.Name };

    private static BaseCanonicalJson CanonicalTransports(string[]? transports)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(
            transports ?? [], HPDAuthInfrastructureJsonSerializerContext.Default.StringArray);
        return BaseCanonicalJson.ParseAndValidate(utf8, PasskeyTransportLimits());
    }

    private static string[] ParseTransports(BaseCanonicalJson transports) => JsonSerializer.Deserialize(
        transports.Utf8.Span, HPDAuthInfrastructureJsonSerializerContext.Default.StringArray) ?? [];

    private static BaseCanonicalJsonLimits PasskeyTransportLimits() => new()
    {
        MaximumCanonicalBytes = 2_048,
        MaximumDepth = 2,
        MaximumArrayItemsPerContainer = 16,
        MaximumObjectPropertiesPerContainer = 1,
        MaximumTotalNodes = 17,
        MaximumTotalStringUtf8Bytes = 1_024,
        MaximumTotalNameUtf8Bytes = 1,
    };

    private static void ValidatePasskey(UserPasskeyInfo passkey)
    {
        if (passkey.CredentialId is not { Length: > 0 and <= 1024 }
            || passkey.PublicKey is not { Length: > 0 and <= 16_384 }
            || passkey.AttestationObject is not { Length: <= 65_536 }
            || passkey.ClientDataJson is not { Length: <= 65_536 }
            || passkey.Name is { } name && System.Text.Encoding.UTF8.GetByteCount(name) > 200)
            throw new ArgumentException("The passkey exceeds the installed HPD Auth contract.", nameof(passkey));
    }
}
