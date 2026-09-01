using HPD.Auth.Base;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>Persists external-provider profile metadata through HPD Base.</summary>
internal sealed class AuthBaseExternalIdentityProfileStore(AuthBaseRuntime runtime)
    : IAuthExternalIdentityProfileStore
{
    /// <inheritdoc />
    public async Task UpsertAsync(
        AuthExternalIdentityProfileUpdate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.ProviderId)
            || request.Provider.Length > 128
            || request.ProviderId.Length > 256
            || request.SignedInAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The external identity profile update is invalid.", nameof(request));

        BaseCanonicalJson identityData;
        try
        {
            identityData = BaseCanonicalJson.ParseAndValidate(
                System.Text.Encoding.UTF8.GetBytes(request.CanonicalIdentityJson),
                IdentityDataLimits());
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            throw new ArgumentException("The external identity profile JSON is invalid.", nameof(request), exception);
        }

        BaseSession session = runtime.OpenServiceSession();
        BaseResult<AuthExternalIdentityReadV1.Row?> read = await session.Reads.FirstAsync(
            AuthExternalIdentityReadV1.Handle,
            new AuthExternalIdentityReadV1
            {
                TenantId = runtime.TenantId,
                Provider = request.Provider,
                ProviderId = request.ProviderId,
            }, cancellationToken).ConfigureAwait(false);
        if (read is BaseFailure<AuthExternalIdentityReadV1.Row?> readFailure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(readFailure.Error));

        AuthExternalIdentityReadV1.Row? existing = read.RequireValue();
        if (existing is not null && existing.UserId.Value.Value != request.UserId.ToString("D"))
            throw new AuthBasePersistenceException("auth.externalIdentity.subjectMismatch");

        Guid recordId = existing?.Id ?? Guid.NewGuid();
        DateTimeOffset createdAt = existing?.CreatedAt ?? request.SignedInAt;
        var createRecord = new AuthUserIdentityRecordV1
        {
            Id = recordId,
            TenantId = runtime.TenantId,
            UserId = BaseRecordId<AuthUserRecordV1>.Create(request.UserId.ToString("D")),
            Provider = request.Provider,
            ProviderId = request.ProviderId,
            IdentityData = identityData,
            LastSignInAt = request.SignedInAt,
            FederationSourceId = null,
            LastSyncAt = null,
            ProviderTokens = null,
            CreatedAt = createdAt,
            UpdatedAt = null,
        };
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.external-identity.upsert.v1",
            runtime.TenantId,
            recordId.ToString("D"),
            request.UserId.ToString("D"),
            request.Provider,
            request.ProviderId,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(identityData.Utf8.Span)),
            request.SignedInAt.ToString("O"));
        BaseBatchBuilder batch = session.Atomic(identity);
        batch.UpsertPatch(
            AuthUserIdentityRecordV1.Collection,
            RecordId.Create(recordId.ToString("D")),
            createRecord,
            new AuthExternalIdentityProfilePatch
            {
                IdentityData = identityData,
                LastSignInAt = request.SignedInAt,
                UpdatedAt = request.SignedInAt,
            },
            Serialization.HPDAuthInfrastructureJsonSerializerContext.Default.AuthExternalIdentityProfilePatch,
            expectedRevision: existing?.Revision);
        BaseResult<BaseBatchResult> committed = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (committed is BaseFailure<BaseBatchResult> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
        committed.RequireValue().RequireCommitted();
    }

    private static BaseCanonicalJsonLimits IdentityDataLimits() => new()
    {
        MaximumCanonicalBytes = 65_536,
        MaximumDepth = 16,
        MaximumTotalNodes = 4_096,
        MaximumTotalStringUtf8Bytes = 65_536,
        MaximumTotalNameUtf8Bytes = 65_536,
        MaximumArrayItemsPerContainer = 1_024,
        MaximumObjectPropertiesPerContainer = 1_024,
    };
}

/// <summary>Owns the non-secret fields updated after a successful external sign-in.</summary>
internal sealed record AuthExternalIdentityProfilePatch
{
    /// <summary>Gets the canonical external identity profile.</summary>
    public required BaseCanonicalJson IdentityData { get; init; }

    /// <summary>Gets the successful sign-in instant.</summary>
    public required DateTimeOffset LastSignInAt { get; init; }

    /// <summary>Gets the record update instant.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
