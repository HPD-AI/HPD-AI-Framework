using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.recoveryCodeByDigest.v1", typeof(AuthBaseTokenReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.secret.twoFactor", Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = ["auth.recoveryCodes"])]
internal sealed partial record AuthRecoveryCodeByDigestReadV1
{
    [BaseReadParameter("auth.read.recoveryCodeByDigest.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.recoveryCodeByDigest.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseReadParameter("auth.read.recoveryCodeByDigest.v1.parameter.digestKeyVersion")] public required int DigestKeyVersion { get; init; }
    [BaseReadParameter("auth.read.recoveryCodeByDigest.v1.parameter.codeDigest", MaximumBytes = 32)] public required BaseBinary CodeDigest { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.recoveryCodeByDigest.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.recoveryCodeByDigest.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.recoveryCodeByDigest.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRecoveryCodeByDigestReadV1, Row> read)
    {
        read.From(AuthRecoveryCodeRecordV1.Collection, "code", out BaseReadSource<AuthRecoveryCodeRecordV1> code)
            .Where(code.Field(AuthRecoveryCodeRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(code.Field(AuthRecoveryCodeRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId)))
                .And(code.Field(AuthRecoveryCodeRecordV1.Fields.DigestKeyVersion).Equal(read.Parameter(Parameters.DigestKeyVersion)))
                .And(code.Field(AuthRecoveryCodeRecordV1.Fields.CodeDigest).Equal(read.Parameter(Parameters.CodeDigest))))
            .Project(Row.Fields.Id, code.Field(AuthRecoveryCodeRecordV1.Fields.Id))
            .Project(Row.Fields.UserId, code.Field(AuthRecoveryCodeRecordV1.Fields.UserId))
            .Project(Row.Fields.Revision, code.Revision)
            .OrderBy(code.Field(AuthRecoveryCodeRecordV1.Fields.Id))
            .Limits(1, 8_192, 10, 250);
    }
}

[BaseRead("auth.read.refreshByDigest.v1", typeof(AuthBaseTokenReadJsonSerializerContext),
    RequiredGrantId = "auth.token.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.refreshByDigest.v1.row.tenantId", "auth.read.refreshByDigest.v1.row.tokenDigest", "auth.read.refreshByDigest.v1.row.jwtId", "auth.read.refreshByDigest.v1.row.securityStampDigest"],
    SystemSourceIds = ["auth.refreshTokens"])]
internal sealed partial record AuthRefreshByDigestReadV1
{
    [BaseReadParameter("auth.read.refreshByDigest.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.refreshByDigest.v1.parameter.digestAlgorithm")] public required AuthRefreshDigestAlgorithmV1 DigestAlgorithm { get; init; }
    [BaseReadParameter("auth.read.refreshByDigest.v1.parameter.digestKeyVersion")] public int? DigestKeyVersion { get; init; }
    [BaseReadParameter("auth.read.refreshByDigest.v1.parameter.tokenDigest", MaximumBytes = 32)] public required BaseBinary TokenDigest { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.refreshByDigest.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.digestAlgorithm")] public required AuthRefreshDigestAlgorithmV1 DigestAlgorithm { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.digestKeyVersion")] public int? DigestKeyVersion { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.tokenDigest", MaximumBytes = 32)] public required BaseBinary TokenDigest { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.jwtId")] public required string JwtId { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.securityStampDigest", MaximumBytes = 32)] public required BaseBinary SecurityStampDigest { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.securityGeneration")] public BaseModuleGeneration? SecurityGeneration { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.used")] public required bool Used { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.revoked")] public required bool Revoked { get; init; }
        [BaseReadField("auth.read.refreshByDigest.v1.row.replacementId")] public BaseRecordId<AuthRefreshTokenRecordV1>? ReplacementId { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRefreshByDigestReadV1, Row> read)
    {
        BaseReadOperand<int> keyVersion = read.OptionalParameter(Parameters.DigestKeyVersion);
        read.From(AuthRefreshTokenRecordV1.Collection, "token", out BaseReadSource<AuthRefreshTokenRecordV1> token)
            .Where(token.Field(AuthRefreshTokenRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(token.Field(AuthRefreshTokenRecordV1.Fields.DigestAlgorithm).Equal(read.Parameter(Parameters.DigestAlgorithm)))
                .And(keyVersion.IsNull().And(token.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion).IsNull())
                    .Or(token.OptionalField(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion).Equal(keyVersion)))
                .And(token.Field(AuthRefreshTokenRecordV1.Fields.TokenDigest).Equal(read.Parameter(Parameters.TokenDigest))))
            .Project(Row.Fields.Id, token.Field(AuthRefreshTokenRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, token.Field(AuthRefreshTokenRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, token.Field(AuthRefreshTokenRecordV1.Fields.UserId))
            .Project(Row.Fields.DigestAlgorithm, token.Field(AuthRefreshTokenRecordV1.Fields.DigestAlgorithm))
            .Project(Row.Fields.DigestKeyVersion, token.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion))
            .Project(Row.Fields.TokenDigest, token.Field(AuthRefreshTokenRecordV1.Fields.TokenDigest))
            .Project(Row.Fields.JwtId, token.Field(AuthRefreshTokenRecordV1.Fields.JwtId))
            .Project(Row.Fields.SecurityStampDigest, token.Field(AuthRefreshTokenRecordV1.Fields.SecurityStampDigest))
            .Project(Row.Fields.SecurityGeneration, token.Field(AuthRefreshTokenRecordV1.Fields.SecurityGeneration))
            .Project(Row.Fields.ExpiresAt, token.Field(AuthRefreshTokenRecordV1.Fields.ExpiresAt))
            .Project(Row.Fields.Used, token.Field(AuthRefreshTokenRecordV1.Fields.Used))
            .Project(Row.Fields.Revoked, token.Field(AuthRefreshTokenRecordV1.Fields.Revoked))
            .Project(Row.Fields.ReplacementId, token.Field(AuthRefreshTokenRecordV1.Fields.ReplacementId))
            .OrderBy(token.Field(AuthRefreshTokenRecordV1.Fields.Id))
            .Limits(1, 32_768, 10, 250);
    }
}

[BaseRead("auth.read.refreshDigestKeyVersions.v1", typeof(AuthBaseTokenReadJsonSerializerContext),
    RequiredGrantId = "auth.token.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.refreshDigestKeyVersions.v1.row.digestKeyVersion"],
    SystemSourceIds = ["auth.refreshTokens"])]
internal sealed partial record AuthRefreshDigestKeyVersionsReadV1
{
    [BaseReadParameter("auth.read.refreshDigestKeyVersions.v1.parameter.now"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset Now { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.refreshDigestKeyVersions.v1.row.digestKeyVersion")]
        public required int DigestKeyVersion { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRefreshDigestKeyVersionsReadV1, Row> read)
    {
        read.From(AuthRefreshTokenRecordV1.Collection, "token", out BaseReadSource<AuthRefreshTokenRecordV1> token)
            .Where(token.Field(AuthRefreshTokenRecordV1.Fields.DigestAlgorithm).Equal(read.ClosedEnumLiteral(AuthRefreshDigestAlgorithmV1.HmacSha256V1))
                .And(token.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion).IsDefined())
                .And(token.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion).IsNull().Not())
                .And(token.Field(AuthRefreshTokenRecordV1.Fields.ExpiresAt).GreaterThan(read.Parameter(Parameters.Now))))
            .GroupBy(token.OptionalField(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion))
            .Project(Row.Fields.DigestKeyVersion, token.OptionalField(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion))
            .OrderBy(token.OptionalField(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion))
            .Limits(128, 8_192, 8, 500);
    }
}

[BaseRead("auth.read.refreshDelivery.v1", typeof(AuthBaseTokenReadJsonSerializerContext),
    RequiredGrantId = "auth.token.delivery", Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.refreshDelivery.v1.row.requestScopeDigest"],
    SecretOutputFieldIds = ["auth.read.refreshDelivery.v1.row.protectedToken"],
    SystemSourceIds = ["auth.refreshTokenDeliveries"])]
internal sealed partial record AuthRefreshDeliveryReadV1
{
    [BaseReadParameter("auth.read.refreshDelivery.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.refreshDelivery.v1.parameter.requestScopeDigest", MaximumBytes = 32)] public required BaseBinary RequestScopeDigest { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.refreshDelivery.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.replacementId")] public required BaseRecordId<AuthRefreshTokenRecordV1> ReplacementId { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.requestScopeDigest", MaximumBytes = 32)] public required BaseBinary RequestScopeDigest { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.protectedToken", MaximumBytes = 4096)] public required BaseBinary ProtectedToken { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.protectorVersion")] public required int ProtectorVersion { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
        [BaseReadField("auth.read.refreshDelivery.v1.row.state")] public required AuthRefreshDeliveryStateV1 State { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRefreshDeliveryReadV1, Row> read)
    {
        read.From(AuthRefreshTokenDeliveryRecordV1.Collection, "delivery", out BaseReadSource<AuthRefreshTokenDeliveryRecordV1> delivery)
            .Where(delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.RequestScopeDigest).Equal(read.Parameter(Parameters.RequestScopeDigest))))
            .Project(Row.Fields.Id, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.Id))
            .Project(Row.Fields.UserId, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.UserId))
            .Project(Row.Fields.ReplacementId, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ReplacementId))
            .Project(Row.Fields.RequestScopeDigest, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.RequestScopeDigest))
            .Project(Row.Fields.ProtectedToken, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectedToken))
            .Project(Row.Fields.ProtectorVersion, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectorVersion))
            .Project(Row.Fields.SecurityGeneration, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.SecurityGeneration))
            .Project(Row.Fields.CreatedAt, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.ExpiresAt, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ExpiresAt))
            .Project(Row.Fields.State, delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.State))
            .OrderBy(delivery.Field(AuthRefreshTokenDeliveryRecordV1.Fields.Id))
            .Limits(1, 16_384, 8, 250);
    }
}

[JsonSerializable(typeof(AuthRecoveryCodeByDigestReadV1), TypeInfoPropertyName = "AuthRecoveryCodeByDigestReadV1")]
[JsonSerializable(typeof(AuthRecoveryCodeByDigestReadV1.Row), TypeInfoPropertyName = "AuthRecoveryCodeByDigestReadV1Row")]
[JsonSerializable(typeof(AuthRefreshByDigestReadV1), TypeInfoPropertyName = "AuthRefreshByDigestReadV1")]
[JsonSerializable(typeof(AuthRefreshByDigestReadV1.Row), TypeInfoPropertyName = "AuthRefreshByDigestReadV1Row")]
[JsonSerializable(typeof(AuthRefreshDigestKeyVersionsReadV1), TypeInfoPropertyName = "AuthRefreshDigestKeyVersionsReadV1")]
[JsonSerializable(typeof(AuthRefreshDigestKeyVersionsReadV1.Row), TypeInfoPropertyName = "AuthRefreshDigestKeyVersionsReadV1Row")]
[JsonSerializable(typeof(AuthRefreshDeliveryReadV1), TypeInfoPropertyName = "AuthRefreshDeliveryReadV1")]
[JsonSerializable(typeof(AuthRefreshDeliveryReadV1.Row), TypeInfoPropertyName = "AuthRefreshDeliveryReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseTokenReadJsonSerializerContext : JsonSerializerContext;
