using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthPasskeyRegisterV1
{
    [BaseField("auth.operation.passkey.register.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.passkey.register.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.passkey.register.passkeyId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PasskeyId { get; init; }
    [BaseField("auth.operation.passkey.register.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.passkey.register.credentialDigest", MaximumBytes = 32)] public required BaseBinary CredentialDigest { get; init; }
    [BaseField("auth.operation.passkey.register.credentialId", MaximumBytes = 1024)] public required BaseBinary CredentialId { get; init; }
    [BaseField("auth.operation.passkey.register.publicKey", MaximumBytes = 16_384)] public required BaseBinary PublicKey { get; init; }
    [BaseField("auth.operation.passkey.register.signatureCounter", MinimumInt64 = 0, HasMinimumInt64 = true, MaximumInt64 = 4_294_967_295, HasMaximumInt64 = true)] public required long SignatureCounter { get; init; }
    [BaseField("auth.operation.passkey.register.aaGuid", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? AaGuid { get; init; }
    [BaseField("auth.operation.passkey.register.name", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 200, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Name { get; init; }
    [BaseField("auth.operation.passkey.register.transports", MaximumCanonicalJsonBytes = 2048, JsonShape = BaseJsonShape.Array, MaximumJsonDepth = 2, MaximumJsonArrayItems = 16, MaximumJsonObjectProperties = 1, MaximumJsonTotalNodes = 17, MaximumJsonTotalStringUtf8Bytes = 1024, MaximumJsonTotalNameUtf8Bytes = 1)] public required BaseCanonicalJson Transports { get; init; }
    [BaseField("auth.operation.passkey.register.userVerified")] public required bool UserVerified { get; init; }
    [BaseField("auth.operation.passkey.register.backupEligible")] public required bool BackupEligible { get; init; }
    [BaseField("auth.operation.passkey.register.backedUp")] public required bool BackedUp { get; init; }
    [BaseField("auth.operation.passkey.register.isDiscoverable")] public required bool IsDiscoverable { get; init; }
    [BaseField("auth.operation.passkey.register.attestationObject", MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary AttestationObject { get; init; }
    [BaseField("auth.operation.passkey.register.clientDataJson", MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary ClientDataJson { get; init; }
    [BaseField("auth.operation.passkey.register.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.passkey.register.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.passkey.register.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthPasskeyRegisterResultV1
{
    [BaseField("auth.operation.passkey.register.result.passkeyId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PasskeyId { get; init; }
    [BaseField("auth.operation.passkey.register.result.passkeyRevision")] public required RevisionToken PasskeyRevision { get; init; }
    [BaseField("auth.operation.passkey.register.result.userRevision")] public required RevisionToken UserRevision { get; init; }
    [BaseField("auth.operation.passkey.register.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
    [BaseField("auth.operation.passkey.register.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}

internal sealed record AuthPasskeyRemoveV1
{
    [BaseField("auth.operation.passkey.remove.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.passkey.remove.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.passkey.remove.passkeyId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PasskeyId { get; init; }
    [BaseField("auth.operation.passkey.remove.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.passkey.remove.expectedPasskeyRevision")] public required RevisionToken ExpectedPasskeyRevision { get; init; }
    [BaseField("auth.operation.passkey.remove.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.passkey.remove.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.passkey.remove.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthPasskeyRemoveResultV1
{
    [BaseField("auth.operation.passkey.remove.result.passkeyId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PasskeyId { get; init; }
    [BaseField("auth.operation.passkey.remove.result.userRevision")] public required RevisionToken UserRevision { get; init; }
    [BaseField("auth.operation.passkey.remove.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
    [BaseField("auth.operation.passkey.remove.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}

internal sealed record AuthPasskeyRecordAssertionV1
{
    [BaseField("auth.operation.passkey.assert.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.passkey.assert.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.passkey.assert.passkeyId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string PasskeyId { get; init; }
    [BaseField("auth.operation.passkey.assert.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.passkey.assert.expectedPasskeyRevision")] public required RevisionToken ExpectedPasskeyRevision { get; init; }
    [BaseField("auth.operation.passkey.assert.presentedCounter", MinimumInt64 = 0, HasMinimumInt64 = true, MaximumInt64 = 4_294_967_295, HasMaximumInt64 = true)] public required long PresentedCounter { get; init; }
    [BaseField("auth.operation.passkey.assert.counterSupported")] public required bool CounterSupported { get; init; }
    [BaseField("auth.operation.passkey.assert.userVerified")] public required bool UserVerified { get; init; }
    [BaseField("auth.operation.passkey.assert.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthPasskeyAssertionResultV1
{
    [BaseField("auth.operation.passkey.assert.result.revision")] public required RevisionToken Revision { get; init; }
}
