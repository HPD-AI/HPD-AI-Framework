using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>Persists refresh-token authority through HPD Base without storing bearer values.</summary>
internal sealed class AuthBaseRefreshTokenStore(
    AuthBaseRuntime runtime,
    IAuthRefreshTokenDigestKeyRing digestKeys,
    IAuthTokenDeliveryProtector deliveryProtector) : IRefreshTokenStore
{
    private static readonly byte[] RefreshPurpose = "hpd.auth.refresh.v1"u8.ToArray();
    private static readonly byte[] StampPurpose = "hpd.auth.security-stamp.v1"u8.ToArray();

    /// <inheritdoc />
    public async Task<RefreshTokenPersistenceResult> IssueAsync(RefreshTokenIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIssue(request);
        byte[] scopeDigest = ScopeDigest(request.RequestScope, "issue", request.IdempotencyKey);
        byte[] requestFingerprint = RequestFingerprint("issue", request.RequestScope, request.IdempotencyKey,
            request.UserId, null, request.SecurityStamp, request.ExpiresAt);
        try
        {
            RefreshTokenPersistenceResult? replay = await TryRecoverDeliveryAsync(scopeDigest, requestFingerprint, ct).ConfigureAwait(false);
            if (replay is not null)
                return replay;

            BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row> user = await RequireUserAsync(request.UserId, ct).ConfigureAwait(false);
            if (user.Item is not { IsActive: true, IsDeleted: false } row)
                throw new AuthBasePersistenceException("auth.user.inactive");
            return await CreateAsync(row, user.Authority, request.SecurityStamp, request.ExpiresAt,
                request.RequestScope, request.IdempotencyKey, "issue", null, scopeDigest,
                requestFingerprint, ct).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(requestFingerprint); }
    }

    /// <inheritdoc />
    public async Task<RefreshTokenInspection?> InspectAsync(string token, CancellationToken ct = default)
    {
        AuthRefreshByDigestReadV1.Row? row = await FindAsync(token, ct).ConfigureAwait(false);
        return row is null || row.Used || row.Revoked || row.ExpiresAt <= runtime.GetUtcNow()
            ? null
            : new RefreshTokenInspection
            {
                UserId = Guid.ParseExact(row.UserId.Value.Value, "D"),
                ExpiresAt = row.ExpiresAt,
            };
    }

    /// <inheritdoc />
    public async Task<RefreshTokenPersistenceResult?> RotateAsync(RefreshTokenRotateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PredecessorToken);
        if (request.ExpiresAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Refresh expiry must be UTC.", nameof(request));
        AuthRefreshByDigestReadV1.Row? predecessor = await FindAsync(request.PredecessorToken, ct).ConfigureAwait(false);
        if (predecessor is null)
            return null;
        byte[] scopeDigest = ScopeDigest(predecessor.Id, "rotate", predecessor.Id);
        Guid userId = Guid.ParseExact(predecessor.UserId.Value.Value, "D");
        byte[] requestFingerprint = RequestFingerprint("rotate", predecessor.Id, predecessor.Id,
            userId, predecessor.Id, request.SecurityStamp, request.ExpiresAt);
        try
        {
            RefreshTokenPersistenceResult? replay = await TryRecoverDeliveryAsync(scopeDigest, requestFingerprint, ct).ConfigureAwait(false);
            if (replay is not null)
                return replay;
            DateTimeOffset now = runtime.GetUtcNow();
            if (predecessor.Used || predecessor.Revoked || predecessor.ExpiresAt <= now)
                return null;

            BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row> user = await RequireUserAsync(userId, ct).ConfigureAwait(false);
            if (user.Item is not { IsActive: true, IsDeleted: false } row)
                return null;
            byte[] stampDigest = SecurityStampDigest(request.SecurityStamp);
            if (!CryptographicOperations.FixedTimeEquals(stampDigest, predecessor.SecurityStampDigest.ToArray()))
                return null;
            return await CreateAsync(row, user.Authority, request.SecurityStamp, request.ExpiresAt,
                predecessor.Id, predecessor.Id, "rotate", predecessor, scopeDigest,
                requestFingerprint, ct).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(requestFingerprint); }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        AuthRefreshByDigestReadV1.Row? row = await FindAsync(token, ct).ConfigureAwait(false);
        if (row is null)
            return false;
        if (row.Revoked)
            return true;
        DateTimeOffset now = runtime.GetUtcNow();
        BaseCollectionSession<AuthRefreshTokenRecordV1> tokens = runtime.OpenServiceSession().Collection(AuthRefreshTokenRecordV1.Collection);
        BaseMergePatchSelectionProfile<AuthRefreshTokenRecordV1> profile =
            tokens.GetMergePatchSelectionProfile(AuthSelectionProfiles.RefreshTokensRevokeUser);
        BaseQuery<AuthRefreshTokenRecordV1> query = tokens.Query()
            .Where(AuthRefreshTokenRecordV1.Fields.TenantId.Equal(runtime.TenantId))
            .Where(AuthRefreshTokenRecordV1.Fields.Id.Equal(row.Id))
            .Where(AuthRefreshTokenRecordV1.Fields.Revoked.Equal(false))
            .OrderBy(AuthRefreshTokenRecordV1.Fields.Id).ThenByRecordId().Take(1);
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "auth.refreshTokens.revoke-user.v1", runtime.TenantId, row.Id, row.Revision.Value, now.ToString("O"));
        BaseResult<BaseSelectionMutationResult> result = await query.PatchSelectedAsync(
            profile, RevocationPatch(now, row.ExpiresAt), BasePreviousStateRequirement.None, identity,
            cancellationToken: ct).ConfigureAwait(false);
        if (result is BaseFailure<BaseSelectionMutationResult> failure)
        {
            if (failure.Error.Category is ErrorCategory.NotFound or ErrorCategory.Conflict)
                return false;
            throw Failure(failure.Error);
        }
        return result.RequireValue().SelectedCount == 1;
    }

    /// <inheritdoc />
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        DateTimeOffset now = runtime.GetUtcNow();
        BaseCollectionSession<AuthRefreshTokenRecordV1> tokens = runtime.OpenServiceSession().Collection(AuthRefreshTokenRecordV1.Collection);
        BaseMergePatchSelectionProfile<AuthRefreshTokenRecordV1> profile =
            tokens.GetMergePatchSelectionProfile(AuthSelectionProfiles.RefreshTokensRevokeUser);
        for (int chunk = 0; ; chunk++)
        {
            BaseQuery<AuthRefreshTokenRecordV1> query = tokens.Query()
                .Where(AuthRefreshTokenRecordV1.Fields.TenantId.Equal(runtime.TenantId))
                .Where(AuthRefreshTokenRecordV1.Fields.UserId.Equal(BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D"))))
                .Where(AuthRefreshTokenRecordV1.Fields.Revoked.Equal(false))
                .OrderBy(AuthRefreshTokenRecordV1.Fields.ExpiresAt).ThenByRecordId().Take(200);
            BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
                "auth.refreshTokens.revoke-user.v1", runtime.TenantId, userId.ToString("D"),
                chunk.ToString(CultureInfo.InvariantCulture), now.ToString("O"));
            BaseResult<BaseSelectionMutationResult> result = await query.PatchSelectedAsync(
                profile, CohortRevocationPatch(now), BasePreviousStateRequirement.None, identity,
                cancellationToken: ct).ConfigureAwait(false);
            if (result is BaseFailure<BaseSelectionMutationResult> failure)
                throw Failure(failure.Error);
            if (result.RequireValue().SelectedCount == 0)
                return;
        }
    }

    private async Task<RefreshTokenPersistenceResult> CreateAsync(
        AuthUserByIdReadV1.Row user,
        BaseRegisteredReadSnapshotAuthority authority,
        string securityStamp,
        DateTimeOffset expiresAt,
        string requestScope,
        string idempotencyKey,
        string operationKind,
        AuthRefreshByDigestReadV1.Row? predecessor,
        byte[] scopeDigest,
        byte[] requestFingerprint,
        CancellationToken ct)
    {
        ValidateDigestCapability();
        ValidateProtectorCapability();
        AuthAuthorityResult<AuthRefreshDigestKey> keyResult = digestKeys.GetActiveIssuanceKey();
        if (!keyResult.IsAvailable)
            throw new AuthBasePersistenceException("auth.refresh.digestKeyUnavailable");
        using AuthRefreshDigestKey key = keyResult.Value!;
        if (key.Version != digestKeys.Capability.ActiveIssuanceVersion || key.KeyMaterial.Length is not (32 or 64))
            throw new AuthBasePersistenceException("auth.refresh.digestKeyUnavailable");

        DateTimeOffset now = runtime.GetUtcNow();
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = CurrentToken(key.Version, tokenBytes);
        byte[] tokenDigest = Digest(key, tokenBytes);
        string refreshId = RefreshId(key.Version, tokenDigest);
        string jwtId = Convert.ToHexStringLower(HashParts(scopeDigest, "jwt"u8));
        byte[] stampDigest = SecurityStampDigest(securityStamp);
        string semanticFingerprint = Convert.ToHexStringLower(SHA256.HashData(BuildSemanticFingerprint(
            operationKind, requestScope, idempotencyKey, user.Id, predecessor?.Id, refreshId, jwtId, expiresAt)));
        string deliveryId = AuthBaseDeterministicId.Create(runtime.TenantId.ToString("D"), requestScope,
            operationKind, idempotencyKey, semanticFingerprint);
        byte[] associatedData = AssociatedData(authority, operationKind, runtime.TenantId, user.Id,
            predecessor?.Id, refreshId, deliveryId, scopeDigest, requestFingerprint, now, expiresAt);
        AuthProtectedTokenEnvelope envelope;
        using (AuthOwnedSecretBytes plaintext = AuthOwnedSecretBytes.From(Encoding.UTF8.GetBytes(token)))
        {
            AuthAuthorityResult<AuthProtectedTokenEnvelope> protectedResult = deliveryProtector.Protect(
                plaintext, AuthOwnedEnvelopeBytes.From(associatedData));
            if (!protectedResult.IsAvailable)
                throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");
            envelope = protectedResult.Value!;
        }
        if (envelope.ProtectorVersion != deliveryProtector.Capability.ActiveVersion
            || envelope.Ciphertext.Length is < 1 or > 4096
            || associatedData.Length > 4096)
            throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");

        try
        {
            if (predecessor is null)
            {
                var request = new AuthRefreshIssueV1
                {
                    CreatedAt = now, DeliveryExpiresAt = now.AddMinutes(5), DeliveryId = deliveryId,
                    DigestAlgorithm = AuthRefreshDigestAlgorithmV1.HmacSha256V1, DigestKeyVersion = key.Version,
                    ExpectedUserRevision = user.Revision, ExpiresAt = expiresAt, JwtId = jwtId,
                    ProtectedToken = BaseBinary.From(envelope.Ciphertext.ToArray()),
                    ProtectionAssociatedData = BaseBinary.From(associatedData), ProtectorVersion = envelope.ProtectorVersion,
                    RefreshTokenId = refreshId, RequestScopeDigest = BaseBinary.From(scopeDigest),
                    RequestFingerprint = BaseBinary.From(requestFingerprint),
                    RetentionEligibleAt = expiresAt.AddDays(30),
                    SecurityStampDigest = BaseBinary.From(stampDigest), TenantId = runtime.TenantId,
                    TokenDigest = BaseBinary.From(tokenDigest), UserId = user.Id,
                };
                BaseInstalledModuleMutationHandle<AuthRefreshIssueV1, AuthRefreshIssueResultV1> handle = runtime
                    .OpenServiceSession().ModuleMutations.Get(AuthRefreshIssueOperationV1.Identity);
                BaseMutationRequestIdentity identity = handle.CreateRequestIdentity(request, $"refresh:{deliveryId}:issue");
                BaseResult<BaseModuleMutationExecutionResult<AuthRefreshIssueResultV1>> result = await handle
                    .ExecuteAsync(request, identity, cancellationToken: ct).ConfigureAwait(false);
                if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRefreshIssueResultV1>> failure)
                    return await RecoverAfterFailureAsync(scopeDigest, requestFingerprint, failure.Error).ConfigureAwait(false);
            }
            else
            {
                var request = new AuthRefreshRotateV1
                {
                    CreatedAt = now, DeliveryExpiresAt = now.AddMinutes(5), DeliveryId = deliveryId,
                    DigestAlgorithm = AuthRefreshDigestAlgorithmV1.HmacSha256V1, DigestKeyVersion = key.Version,
                    ExpectedPredecessorRevision = predecessor.Revision,
                    ExpectedSecurityStampDigest = predecessor.SecurityStampDigest,
                    ExpectedUserRevision = user.Revision, ExpiresAt = expiresAt, JwtId = jwtId,
                    OperationTime = now, PredecessorId = predecessor.Id,
                    ProtectedToken = BaseBinary.From(envelope.Ciphertext.ToArray()),
                    ProtectionAssociatedData = BaseBinary.From(associatedData), ProtectorVersion = envelope.ProtectorVersion,
                    RefreshTokenId = refreshId, RequestScopeDigest = BaseBinary.From(scopeDigest),
                    RequestFingerprint = BaseBinary.From(requestFingerprint),
                    ReplacementRetentionEligibleAt = expiresAt.AddDays(30),
                    RetentionEligibleAt = (predecessor.ExpiresAt > now ? predecessor.ExpiresAt : now).AddDays(30),
                    SecurityStampDigest = BaseBinary.From(stampDigest), TenantId = runtime.TenantId,
                    TokenDigest = BaseBinary.From(tokenDigest), UserId = user.Id,
                };
                BaseInstalledModuleMutationHandle<AuthRefreshRotateV1, AuthRefreshRotateResultV1> handle = runtime
                    .OpenServiceSession().ModuleMutations.Get(AuthRefreshRotateOperationV1.Identity);
                BaseMutationRequestIdentity identity = handle.CreateRequestIdentity(request, $"refresh:{predecessor.Id}:rotate");
                BaseResult<BaseModuleMutationExecutionResult<AuthRefreshRotateResultV1>> result = await handle
                    .ExecuteAsync(request, identity, cancellationToken: ct).ConfigureAwait(false);
                if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRefreshRotateResultV1>> failure)
                    return await RecoverAfterFailureAsync(scopeDigest, requestFingerprint, failure.Error).ConfigureAwait(false);
            }
            return new RefreshTokenPersistenceResult { Token = token, UserId = user.Id, JwtId = jwtId, ExpiresAt = expiresAt };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            CryptographicOperations.ZeroMemory(tokenDigest);
            CryptographicOperations.ZeroMemory(stampDigest);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private async Task<RefreshTokenPersistenceResult> RecoverAfterFailureAsync(
        byte[] scopeDigest,
        byte[] requestFingerprint,
        BaseError error)
    {
        using var resolution = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                RefreshTokenPersistenceResult? recovered = await TryRecoverDeliveryAsync(
                    scopeDigest, requestFingerprint, resolution.Token).ConfigureAwait(false);
                if (recovered is not null)
                    return recovered;
                await Task.Delay(TimeSpan.FromMilliseconds(50), resolution.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (resolution.IsCancellationRequested)
        {
            throw Failure(error);
        }
    }

    private async Task<RefreshTokenPersistenceResult?> TryRecoverDeliveryAsync(
        byte[] scopeDigest,
        byte[] requestFingerprint,
        CancellationToken ct)
    {
        BaseResult<AuthRefreshDeliveryReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthRefreshDeliveryReadV1.Handle,
            new AuthRefreshDeliveryReadV1 { TenantId = runtime.TenantId, RequestScopeDigest = BaseBinary.From(scopeDigest) }, ct).ConfigureAwait(false);
        if (result is BaseFailure<AuthRefreshDeliveryReadV1.Row?> failure)
            throw Failure(failure.Error);
        AuthRefreshDeliveryReadV1.Row? row = result.RequireValue();
        if (row is null || row.State != AuthRefreshDeliveryStateV1.available || row.ExpiresAt <= runtime.GetUtcNow())
            return null;
        if (!CryptographicOperations.FixedTimeEquals(requestFingerprint, row.RequestFingerprint.ToArray()))
            throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");
        ValidateProtectorCapability();
        AuthAuthorityResult<AuthOwnedSecretBytes> plaintextResult = deliveryProtector.Unprotect(
            row.ProtectorVersion, AuthOwnedEnvelopeBytes.From(row.ProtectedToken.ToArray()),
            AuthOwnedEnvelopeBytes.From(row.ProtectionAssociatedData.ToArray()));
        if (!plaintextResult.IsAvailable)
            throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");
        using AuthOwnedSecretBytes plaintext = plaintextResult.Value!;
        string token = Encoding.UTF8.GetString(plaintext.DangerousReadOnlySpan);
        if (!TryDecodeCurrentToken(token, out _, out byte[] decoded))
            throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");
        CryptographicOperations.ZeroMemory(decoded);
        return new RefreshTokenPersistenceResult
        {
            Token = token,
            UserId = Guid.ParseExact(row.UserId.Value.Value, "D"),
            JwtId = row.JwtId,
            ExpiresAt = row.RefreshExpiresAt,
        };
    }

    private async Task<AuthRefreshByDigestReadV1.Row?> FindAsync(string token, CancellationToken ct)
    {
        if (!TryDecodeCurrentToken(token, out int keyVersion, out byte[] currentBytes))
            return null;
        try
        {
            ValidateDigestCapability();
            if (!digestKeys.Capability.ValidationVersions.Contains(keyVersion))
                return null;
            AuthAuthorityResult<AuthRefreshDigestKey> keyResult = digestKeys.GetValidationKey(keyVersion);
            if (!keyResult.IsAvailable)
                return null;
            using AuthRefreshDigestKey key = keyResult.Value!;
            if (key.Version != keyVersion || key.KeyMaterial.Length is not (32 or 64))
                return null;
            byte[] digest = Digest(key, currentBytes);
            try
            {
                return await ReadDigestAsync(
                    AuthRefreshDigestAlgorithmV1.HmacSha256V1, keyVersion, digest, ct).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(digest); }
        }
        finally { CryptographicOperations.ZeroMemory(currentBytes); }
    }

    private async Task<AuthRefreshByDigestReadV1.Row?> ReadDigestAsync(
        AuthRefreshDigestAlgorithmV1 algorithm, int version, byte[] digest, CancellationToken ct)
    {
        BaseResult<AuthRefreshByDigestReadV1.Row?> result = await runtime.OpenServiceSession().Reads.FirstAsync(
            AuthRefreshByDigestReadV1.Handle,
            new AuthRefreshByDigestReadV1
            {
                TenantId = runtime.TenantId, DigestAlgorithm = algorithm,
                DigestKeyVersion = version, TokenDigest = BaseBinary.From(digest),
            }, ct).ConfigureAwait(false);
        if (result is BaseFailure<AuthRefreshByDigestReadV1.Row?> failure)
            throw Failure(failure.Error);
        return result.RequireValue();
    }

    private async Task<BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row>> RequireUserAsync(Guid userId, CancellationToken ct)
    {
        BaseResult<BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthUserByIdReadV1.Handle, new AuthUserByIdReadV1 { TenantId = runtime.TenantId, UserId = userId }, ct).ConfigureAwait(false);
        if (result is BaseFailure<BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row>> failure)
            throw Failure(failure.Error);
        return result.RequireValue();
    }

    private void ValidateDigestCapability()
    {
        AuthRefreshDigestKeyRingCapability capability = digestKeys.Capability;
        if (!capability.IsReady || capability.ActiveIssuanceVersion <= 0
            || capability.ValidationVersions.IsDefaultOrEmpty || capability.ValidationVersions.Length > 128
            || !capability.ValidationVersions.SequenceEqual(capability.ValidationVersions.Order())
            || capability.ValidationVersions.Distinct().Count() != capability.ValidationVersions.Length
            || !capability.ValidationVersions.Contains(capability.ActiveIssuanceVersion)
            || capability.LastVerifiedAt.Offset != TimeSpan.Zero || !Enum.IsDefined(capability.Ownership))
            throw new AuthBasePersistenceException("auth.refresh.digestKeyUnavailable");
    }

    private void ValidateProtectorCapability()
    {
        AuthTokenDeliveryProtectorCapability capability = deliveryProtector.Capability;
        if (!capability.IsReady || !capability.AuthenticatedEncryption || !capability.SupportsRotation
            || capability.ActiveVersion <= 0 || capability.ValidationVersions.IsDefaultOrEmpty
            || !capability.ValidationVersions.Contains(capability.ActiveVersion)
            || !capability.ValidationVersions.SequenceEqual(capability.ValidationVersions.Order())
            || capability.ValidationVersions.Distinct().Count() != capability.ValidationVersions.Length
            || capability.LastVerifiedAt.Offset != TimeSpan.Zero || !Enum.IsDefined(capability.Ownership))
            throw new AuthBasePersistenceException("auth.refresh.deliveryUnavailable");
    }

    private static byte[] Digest(AuthRefreshDigestKey key, byte[] tokenBytes)
    {
        byte[] keyCopy = new byte[key.KeyMaterial.Length];
        key.KeyMaterial.CopyTo(keyCopy);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keyCopy);
            hash.AppendData(RefreshPurpose);
            hash.AppendData(tokenBytes);
            return hash.GetHashAndReset();
        }
        finally { CryptographicOperations.ZeroMemory(keyCopy); }
    }

    private static byte[] SecurityStampDigest(string stamp)
    {
        byte[] value = Encoding.UTF8.GetBytes(stamp.Normalize(NormalizationForm.FormC));
        try { return HashParts(StampPurpose, value); }
        finally { CryptographicOperations.ZeroMemory(value); }
    }

    private static byte[] HashParts(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(first);
        hash.AppendData(second);
        return hash.GetHashAndReset();
    }

    private static byte[] ScopeDigest(string scope, string operation, string idempotencyKey) => SHA256.HashData(
        BuildSemanticFingerprint("scope", scope, operation, Guid.Empty, null, idempotencyKey, string.Empty, DateTimeOffset.UnixEpoch));

    private static byte[] RequestFingerprint(string operation, string scope, string idempotencyKey,
        Guid userId, string? predecessorId, string securityStamp, DateTimeOffset expiresAt)
    {
        byte[] stampDigest = SecurityStampDigest(securityStamp);
        try
        {
            return SHA256.HashData(BuildSemanticFingerprint(operation, scope, idempotencyKey,
                userId, predecessorId, Convert.ToHexStringLower(stampDigest), string.Empty, expiresAt));
        }
        finally { CryptographicOperations.ZeroMemory(stampDigest); }
    }

    private static byte[] BuildSemanticFingerprint(string operation, string scope, string key, Guid userId,
        string? predecessor, string replacement, string jwtId, DateTimeOffset expiry)
    {
        using var stream = new MemoryStream();
        Write(stream, "hpd.auth.refresh.semantic.v1"); Write(stream, operation); Write(stream, scope); Write(stream, key);
        Write(stream, userId.ToString("D")); Write(stream, predecessor ?? string.Empty); Write(stream, replacement);
        Write(stream, jwtId); Write(stream, expiry.ToString("O")); return stream.ToArray();
    }

    private static byte[] AssociatedData(BaseRegisteredReadSnapshotAuthority authority, string operation,
        Guid tenantId, Guid userId, string? predecessorId, string replacementId, string deliveryId,
        byte[] scopeDigest, byte[] requestFingerprint, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        using var stream = new MemoryStream();
        Write(stream, "hpd.auth.refresh.delivery-ad.v1"); Write(stream, operation); Write(stream, tenantId.ToString("D"));
        Write(stream, userId.ToString("D")); Write(stream, predecessorId ?? string.Empty); Write(stream, replacementId);
        Write(stream, deliveryId); WriteBytes(stream, scopeDigest); WriteBytes(stream, requestFingerprint);
        Write(stream, issuedAt.ToString("O")); Write(stream, expiresAt.ToString("O"));
        Write(stream, authority.ApplicationId); Write(stream, authority.LogicalStoreId); Write(stream, authority.StoreInstanceId);
        Write(stream, authority.RestoreEpoch.ToString(CultureInfo.InvariantCulture)); Write(stream, authority.SchemaGeneration.ToString(CultureInfo.InvariantCulture));
        WriteBytes(stream, authority.LogicalSchemaChecksum.AsSpan()); WriteBytes(stream, authority.AuthorityChecksum.AsSpan());
        foreach (BaseRegisteredReadCollectionAuthority collection in authority.Collections)
        { Write(stream, collection.CollectionId); Write(stream, collection.CollectionGeneration.ToString(CultureInfo.InvariantCulture)); }
        return stream.ToArray();
    }

    private static string RefreshId(int version, byte[] digest)
    {
        using var stream = new MemoryStream();
        Write(stream, "hpd.auth.refresh-id.v1"); Write(stream, "hmac-sha256-v1");
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, version); stream.Write(number); stream.Write(digest);
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static RecordPatchRequest RevocationPatch(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        DateTimeOffset retention = (expiresAt > now ? expiresAt : now).AddDays(30);
        return new RecordPatchRequest
        {
            Patch = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [Wire(AuthRefreshTokenRecordV1.Fields.Revoked)] = JsonSerializer.SerializeToElement(
                        true, AuthBaseJsonSerializerContext.Default.Boolean),
                    [Wire(AuthRefreshTokenRecordV1.Fields.RevokedAt)] = UtcDateTime(now),
                    [Wire(AuthRefreshTokenRecordV1.Fields.RetentionEligibleAt)] = UtcDateTime(retention),
                },
            },
            RemovedFieldIds = ImmutableArray<string>.Empty,
        };
    }

    private static RecordPatchRequest CohortRevocationPatch(DateTimeOffset now) => new()
    {
        Patch = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [Wire(AuthRefreshTokenRecordV1.Fields.Revoked)] = JsonSerializer.SerializeToElement(
                    true, AuthBaseJsonSerializerContext.Default.Boolean),
                [Wire(AuthRefreshTokenRecordV1.Fields.RevokedAt)] = UtcDateTime(now),
            },
        },
        RemovedFieldIds = ImmutableArray<string>.Empty,
    };

    private static string Wire<T>(BaseField<AuthRefreshTokenRecordV1, T> field) =>
        AuthRefreshTokenRecordV1.Collection.Definition.Fields!.Single(candidate => candidate.Id == field.Id).WireName;

    private static JsonElement UtcDateTime(DateTimeOffset value) => JsonSerializer.SerializeToElement(
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        AuthBaseJsonSerializerContext.Default.String);

    private static bool TryDecodeCurrentToken(string token, out int keyVersion, out byte[] bytes)
    {
        keyVersion = 0;
        bytes = [];
        if (string.IsNullOrEmpty(token) || !token.StartsWith("hpd1.", StringComparison.Ordinal))
            return false;
        int separator = token.IndexOf('.', 5);
        if (separator is < 6 or > 15)
            return false;
        ReadOnlySpan<char> versionText = token.AsSpan(5, separator - 5);
        if ((versionText.Length > 1 && versionText[0] == '0')
            || !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out keyVersion)
            || keyVersion <= 0)
            return false;
        string encoded = token[(separator + 1)..];
        if (encoded.Length != 43 || encoded.Any(static value =>
            !(char.IsAsciiLetterOrDigit(value) || value is '-' or '_')))
            return false;
        try { bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/') + "="); }
        catch (FormatException) { return false; }
        if (bytes.Length != 32 || !string.Equals(Base64Url(bytes), encoded, StringComparison.Ordinal))
        { CryptographicOperations.ZeroMemory(bytes); bytes = []; return false; }
        return true;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string CurrentToken(int keyVersion, byte[] bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"hpd1.{keyVersion}.{Base64Url(bytes)}");
    private static void Write(Stream stream, string value) => WriteBytes(stream, Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC)));
    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    { Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, value.Length); stream.Write(length); stream.Write(value); }
    private static void ValidateIssue(RefreshTokenIssueRequest request)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.SecurityStamp)
            || string.IsNullOrWhiteSpace(request.RequestScope) || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.RequestScope.Length > 256 || request.IdempotencyKey.Length > 256 || request.ExpiresAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The refresh issuance request is invalid.", nameof(request));
    }
    private static AuthBasePersistenceException Failure(BaseError error) => new(AuthBaseIdentityErrorMapper.SafeWriteCode(error));
}
