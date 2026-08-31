using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal enum AuthRefreshDigestAlgorithmV1
{
    [JsonStringEnumMemberName("hmac-sha256-v1")] HmacSha256V1,
}

internal enum AuthRefreshDeliveryStateV1 { available, deleted, expired }
internal enum AuthSessionAssuranceLevelV1 { aal1, aal2, aal3 }
internal enum AuthSessionStateV1 { active, loggedOut, loggingOut }

[BaseCollection("auth.recoveryCodes", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
internal sealed partial record AuthRecoveryCodeRecordV1
{
    [BaseField("auth.recoveryCodes.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.recoveryCodes.tenantId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.recoveryCodes.userId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.recoveryCode.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.recoveryCodes.codeDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary CodeDigest { get; init; }
    [BaseField("auth.recoveryCodes.digestKeyVersion", MinimumInt32 = 1, HasMinimumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int DigestKeyVersion { get; init; }
    [BaseField("auth.recoveryCodes.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

[BaseCollection("auth.passkeys", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.passkeys.digest", Unique = true)]
[BaseIndexPart("auth.idx.passkeys.digest", 0, nameof(CredentialDigest))]
[BaseIndex("auth.idx.passkeys.user")]
[BaseIndexPart("auth.idx.passkeys.user", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.passkeys.user", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.passkeys.user", 2, nameof(CreatedAt))]
[BaseIndexPart("auth.idx.passkeys.user", 3, nameof(Id))]
internal sealed partial record AuthPasskeyRecordV1
{
    [BaseField("auth.passkeys.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64, Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.passkeys.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.passkeys.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.passkey.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.passkeys.credentialDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary CredentialDigest { get; init; }
    [BaseField("auth.passkeys.credentialId", MaximumBytes = 1024), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary CredentialId { get; init; }
    [BaseField("auth.passkeys.publicKey", MaximumBytes = 16384), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary PublicKey { get; init; }
    [BaseField("auth.passkeys.signatureCounter", MinimumInt64 = 0, HasMinimumInt64 = true, MaximumInt64 = 4_294_967_295, HasMaximumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long SignatureCounter { get; init; }
    [BaseField("auth.passkeys.aaGuid", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public Guid? AaGuid { get; init; }
    [BaseField("auth.passkeys.name", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 200, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? Name { get; init; }
    [BaseField("auth.passkeys.transports", MaximumCanonicalJsonBytes = 2048, JsonShape = BaseJsonShape.Array, MaximumJsonDepth = 2, MaximumJsonArrayItems = 16, MaximumJsonObjectProperties = 1, MaximumJsonTotalNodes = 17, MaximumJsonTotalStringUtf8Bytes = 1024, MaximumJsonTotalNameUtf8Bytes = 1), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseCanonicalJson Transports { get; init; }
    [BaseField("auth.passkeys.userVerified"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool UserVerified { get; init; }
    [BaseField("auth.passkeys.backupEligible"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool BackupEligible { get; init; }
    [BaseField("auth.passkeys.backedUp"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool BackedUp { get; init; }
    [BaseField("auth.passkeys.isDiscoverable"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool IsDiscoverable { get; init; }
    [BaseField("auth.passkeys.attestationObject", MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary AttestationObject { get; init; }
    [BaseField("auth.passkeys.clientDataJson", MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary ClientDataJson { get; init; }
    [BaseField("auth.passkeys.createdAt", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.passkeys.lastUsedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastUsedAt { get; init; }
}

[BaseCollection("auth.refreshTokens", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.refresh.digest", Unique = true)]
[BaseIndexPart("auth.idx.refresh.digest", 0, nameof(DigestAlgorithm))]
[BaseIndexPart("auth.idx.refresh.digest", 1, nameof(DigestKeyVersion))]
[BaseIndexPart("auth.idx.refresh.digest", 2, nameof(TokenDigest))]
[BaseIndex("auth.idx.refresh.userState")]
[BaseIndexPart("auth.idx.refresh.userState", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.refresh.userState", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.refresh.userState", 2, nameof(Revoked))]
[BaseIndexPart("auth.idx.refresh.userState", 3, nameof(ExpiresAt))]
[BaseIndexPart("auth.idx.refresh.userState", 4, nameof(Id))]
internal sealed partial record AuthRefreshTokenRecordV1
{
    [BaseField("auth.refreshTokens.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64, Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.refreshTokens.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.refreshTokens.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.refreshToken.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.refreshTokens.digestAlgorithm", AllowedEnumLiterals = ["hmac-sha256-v1"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthRefreshDigestAlgorithmV1>))] public required AuthRefreshDigestAlgorithmV1 DigestAlgorithm { get; init; }
    [BaseField("auth.refreshTokens.tokenDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary TokenDigest { get; init; }
    [BaseField("auth.refreshTokens.digestKeyVersion", MinimumInt32 = 1, HasMinimumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int DigestKeyVersion { get; init; }
    [BaseField("auth.refreshTokens.jwtId", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string JwtId { get; init; }
    [BaseField("auth.refreshTokens.securityStampDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary SecurityStampDigest { get; init; }
    [BaseField("auth.refreshTokens.securityGeneration"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseModuleGeneration SecurityGeneration { get; init; }
    [BaseField("auth.refreshTokens.expiresAt", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.refreshTokens.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.refreshTokens.used"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Used { get; init; }
    [BaseField("auth.refreshTokens.usedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? UsedAt { get; init; }
    [BaseField("auth.refreshTokens.revoked", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Revoked { get; init; }
    [BaseField("auth.refreshTokens.revokedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RevokedAt { get; init; }
    [BaseField("auth.refreshTokens.replacementId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.refreshToken.replacement", typeof(AuthRefreshTokenRecordV1))] public BaseRecordId<AuthRefreshTokenRecordV1>? ReplacementId { get; init; }
    [BaseField("auth.refreshTokens.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
}

[BaseCollection("auth.refreshTokenDeliveries", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.deliveries.expiry")]
[BaseIndexPart("auth.idx.deliveries.expiry", 0, nameof(State))]
[BaseIndexPart("auth.idx.deliveries.expiry", 1, nameof(ExpiresAt))]
[BaseIndexPart("auth.idx.deliveries.expiry", 2, nameof(Id))]
internal sealed partial record AuthRefreshTokenDeliveryRecordV1
{
    [BaseField("auth.refreshTokenDeliveries.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64, Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.refreshTokenDeliveries.tenantId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.refreshTokenDeliveries.userId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.delivery.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.refreshTokenDeliveries.replacementId"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.delivery.refreshToken", typeof(AuthRefreshTokenRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthRefreshTokenRecordV1> ReplacementId { get; init; }
    [BaseField("auth.refreshTokenDeliveries.requestScopeDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary RequestScopeDigest { get; init; }
    [BaseField("auth.refreshTokenDeliveries.requestFingerprint", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary RequestFingerprint { get; init; }
    [BaseField("auth.refreshTokenDeliveries.protectedToken", MaximumBytes = 4096), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary ProtectedToken { get; init; }
    [BaseField("auth.refreshTokenDeliveries.protectionAssociatedData", MaximumBytes = 4096), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary ProtectionAssociatedData { get; init; }
    [BaseField("auth.refreshTokenDeliveries.protectorVersion", MinimumInt32 = 1, HasMinimumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int ProtectorVersion { get; init; }
    [BaseField("auth.refreshTokenDeliveries.securityGeneration"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseModuleGeneration SecurityGeneration { get; init; }
    [BaseField("auth.refreshTokenDeliveries.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.refreshTokenDeliveries.expiresAt", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.refreshTokenDeliveries.state", AllowedEnumLiterals = ["available", "deleted", "expired"], Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthRefreshDeliveryStateV1>))] public required AuthRefreshDeliveryStateV1 State { get; init; }
}

[BaseCollection("auth.sessions", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.sessions.userState")]
[BaseIndexPart("auth.idx.sessions.userState", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.sessions.userState", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.sessions.userState", 2, nameof(Revoked))]
[BaseIndexPart("auth.idx.sessions.userState", 3, nameof(ExpiresAt))]
[BaseIndexPart("auth.idx.sessions.userState", 4, nameof(Id))]
[BaseIndex("auth.idx.sessions.expiry")]
[BaseIndexPart("auth.idx.sessions.expiry", 0, nameof(Revoked))]
[BaseIndexPart("auth.idx.sessions.expiry", 1, nameof(ExpiresAt))]
[BaseIndexPart("auth.idx.sessions.expiry", 2, nameof(Id))]
internal sealed partial record AuthSessionRecordV1
{
    [BaseField("auth.sessions.id", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.sessions.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.sessions.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.session.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.sessions.aal", AllowedEnumLiterals = ["aal1", "aal2", "aal3"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthSessionAssuranceLevelV1>))] public required AuthSessionAssuranceLevelV1 Aal { get; init; }
    [BaseField("auth.sessions.brokerSessionId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? BrokerSessionId { get; init; }
    [BaseField("auth.sessions.brokerUserId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? BrokerUserId { get; init; }
    [BaseField("auth.sessions.ssoProviderId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.session.ssoProvider", typeof(AuthSsoProviderRecordV1))] public BaseRecordId<AuthSsoProviderRecordV1>? SsoProviderId { get; init; }
    [BaseField("auth.sessions.notBefore", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotBefore { get; init; }
    [BaseField("auth.sessions.notAfter", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotAfter { get; init; }
    [BaseField("auth.sessions.oauthClientId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public Guid? OauthClientId { get; init; }
    [BaseField("auth.sessions.scopes", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2000, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? Scopes { get; init; }
    [BaseField("auth.sessions.clientSessions", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 64, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public BaseCanonicalJson? ClientSessions { get; init; }
    [BaseField("auth.sessions.state", AllowedEnumLiterals = ["active", "loggedOut", "loggingOut"], Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthSessionStateV1>))] public required AuthSessionStateV1 State { get; init; }
    [BaseField("auth.sessions.ipAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? IpAddress { get; init; }
    [BaseField("auth.sessions.userAgent", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 1024, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? UserAgent { get; init; }
    [BaseField("auth.sessions.deviceInfo", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? DeviceInfo { get; init; }
    [BaseField("auth.sessions.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.sessions.lastActiveAt", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastActiveAt { get; init; }
    [BaseField("auth.sessions.expiresAt", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.sessions.revoked", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Revoked { get; init; }
    [BaseField("auth.sessions.revokedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RevokedAt { get; init; }
    [BaseField("auth.sessions.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
    [BaseField("auth.sessions.securityGeneration"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseModuleGeneration SecurityGeneration { get; init; }
}
