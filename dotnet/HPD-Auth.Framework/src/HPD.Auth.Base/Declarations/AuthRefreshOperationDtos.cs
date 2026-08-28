using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthRefreshIssueV1
{
    [BaseField("auth.operation.refresh.issue.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.operation.refresh.issue.deliveryExpiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset DeliveryExpiresAt { get; init; }
    [BaseField("auth.operation.refresh.issue.deliveryId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string DeliveryId { get; init; }
    [BaseField("auth.operation.refresh.issue.digestAlgorithm", AllowedEnumLiterals = ["hmac-sha256-v1", "legacy-sha256-v1"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthRefreshDigestAlgorithmV1>))] public required AuthRefreshDigestAlgorithmV1 DigestAlgorithm { get; init; }
    [BaseField("auth.operation.refresh.issue.digestKeyVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int DigestKeyVersion { get; init; }
    [BaseField("auth.operation.refresh.issue.expectedSecurityGeneration")] public required BaseModuleGeneration ExpectedSecurityGeneration { get; init; }
    [BaseField("auth.operation.refresh.issue.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.refresh.issue.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.operation.refresh.issue.jwtId", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string JwtId { get; init; }
    [BaseField("auth.operation.refresh.issue.protectedToken", MaximumBytes = 4096)] public required BaseBinary ProtectedToken { get; init; }
    [BaseField("auth.operation.refresh.issue.protectorVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int ProtectorVersion { get; init; }
    [BaseField("auth.operation.refresh.issue.refreshTokenId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string RefreshTokenId { get; init; }
    [BaseField("auth.operation.refresh.issue.requestScopeDigest", MaximumBytes = 32)] public required BaseBinary RequestScopeDigest { get; init; }
    [BaseField("auth.operation.refresh.issue.securityStampDigest", MaximumBytes = 32)] public required BaseBinary SecurityStampDigest { get; init; }
    [BaseField("auth.operation.refresh.issue.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.refresh.issue.tokenDigest", MaximumBytes = 32)] public required BaseBinary TokenDigest { get; init; }
    [BaseField("auth.operation.refresh.issue.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
}

internal sealed record AuthRefreshIssueResultV1
{
    [BaseField("auth.operation.refresh.issue.result.deliveryId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string DeliveryId { get; init; }
    [BaseField("auth.operation.refresh.issue.result.deliveryRevision")] public required RevisionToken DeliveryRevision { get; init; }
    [BaseField("auth.operation.refresh.issue.result.refreshTokenId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string RefreshTokenId { get; init; }
    [BaseField("auth.operation.refresh.issue.result.refreshTokenRevision")] public required RevisionToken RefreshTokenRevision { get; init; }
    [BaseField("auth.operation.refresh.issue.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}

internal sealed record AuthRefreshRotateV1
{
    [BaseField("auth.operation.refresh.rotate.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.operation.refresh.rotate.deliveryExpiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset DeliveryExpiresAt { get; init; }
    [BaseField("auth.operation.refresh.rotate.deliveryId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string DeliveryId { get; init; }
    [BaseField("auth.operation.refresh.rotate.digestAlgorithm", AllowedEnumLiterals = ["hmac-sha256-v1", "legacy-sha256-v1"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthRefreshDigestAlgorithmV1>))] public required AuthRefreshDigestAlgorithmV1 DigestAlgorithm { get; init; }
    [BaseField("auth.operation.refresh.rotate.digestKeyVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int DigestKeyVersion { get; init; }
    [BaseField("auth.operation.refresh.rotate.expectedPredecessorRevision")] public required RevisionToken ExpectedPredecessorRevision { get; init; }
    [BaseField("auth.operation.refresh.rotate.expectedSecurityGeneration")] public required BaseModuleGeneration ExpectedSecurityGeneration { get; init; }
    [BaseField("auth.operation.refresh.rotate.expectedSecurityStampDigest", MaximumBytes = 32)] public required BaseBinary ExpectedSecurityStampDigest { get; init; }
    [BaseField("auth.operation.refresh.rotate.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.refresh.rotate.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.operation.refresh.rotate.jwtId", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string JwtId { get; init; }
    [BaseField("auth.operation.refresh.rotate.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
    [BaseField("auth.operation.refresh.rotate.predecessorId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PredecessorId { get; init; }
    [BaseField("auth.operation.refresh.rotate.protectedToken", MaximumBytes = 4096)] public required BaseBinary ProtectedToken { get; init; }
    [BaseField("auth.operation.refresh.rotate.protectorVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int ProtectorVersion { get; init; }
    [BaseField("auth.operation.refresh.rotate.refreshTokenId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string RefreshTokenId { get; init; }
    [BaseField("auth.operation.refresh.rotate.requestScopeDigest", MaximumBytes = 32)] public required BaseBinary RequestScopeDigest { get; init; }
    [BaseField("auth.operation.refresh.rotate.retentionEligibleAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset RetentionEligibleAt { get; init; }
    [BaseField("auth.operation.refresh.rotate.securityStampDigest", MaximumBytes = 32)] public required BaseBinary SecurityStampDigest { get; init; }
    [BaseField("auth.operation.refresh.rotate.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.refresh.rotate.tokenDigest", MaximumBytes = 32)] public required BaseBinary TokenDigest { get; init; }
    [BaseField("auth.operation.refresh.rotate.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
}

internal sealed record AuthRefreshRotateResultV1
{
    [BaseField("auth.operation.refresh.rotate.result.deliveryId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string DeliveryId { get; init; }
    [BaseField("auth.operation.refresh.rotate.result.deliveryRevision")] public required RevisionToken DeliveryRevision { get; init; }
    [BaseField("auth.operation.refresh.rotate.result.predecessorRevision")] public required RevisionToken PredecessorRevision { get; init; }
    [BaseField("auth.operation.refresh.rotate.result.refreshTokenId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string RefreshTokenId { get; init; }
    [BaseField("auth.operation.refresh.rotate.result.refreshTokenRevision")] public required RevisionToken RefreshTokenRevision { get; init; }
    [BaseField("auth.operation.refresh.rotate.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}
