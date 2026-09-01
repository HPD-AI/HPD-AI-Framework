using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthSessionCreateV1
{
    [BaseField("auth.operation.session.create.sessionId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SessionId { get; init; }
    [BaseField("auth.operation.session.create.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.session.create.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.session.create.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.session.create.aal", AllowedEnumLiterals = ["aal1", "aal2", "aal3"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthSessionAssuranceLevelV1>))] public required AuthSessionAssuranceLevelV1 Aal { get; init; }
    [BaseField("auth.operation.session.create.brokerSessionId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? BrokerSessionId { get; init; }
    [BaseField("auth.operation.session.create.brokerUserId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? BrokerUserId { get; init; }
    [BaseField("auth.operation.session.create.ssoProviderId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public BaseRecordId<AuthSsoProviderRecordV1>? SsoProviderId { get; init; }
    [BaseField("auth.operation.session.create.notBefore", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotBefore { get; init; }
    [BaseField("auth.operation.session.create.notAfter", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotAfter { get; init; }
    [BaseField("auth.operation.session.create.oauthClientId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? OauthClientId { get; init; }
    [BaseField("auth.operation.session.create.scopes", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2000, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Scopes { get; init; }
    [BaseField("auth.operation.session.create.clientSessions", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumCanonicalJsonBytes = 32_768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1_024, MaximumJsonObjectProperties = 64, MaximumJsonTotalNodes = 4_096, MaximumJsonTotalStringUtf8Bytes = 32_768, MaximumJsonTotalNameUtf8Bytes = 32_768)] public BaseCanonicalJson? ClientSessions { get; init; }
    [BaseField("auth.operation.session.create.state", AllowedEnumLiterals = ["active", "loggedOut", "loggingOut"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthSessionStateV1>))] public required AuthSessionStateV1 State { get; init; }
    [BaseField("auth.operation.session.create.ipAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? IpAddress { get; init; }
    [BaseField("auth.operation.session.create.userAgent", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 1024, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserAgent { get; init; }
    [BaseField("auth.operation.session.create.deviceInfo", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? DeviceInfo { get; init; }
    [BaseField("auth.operation.session.create.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.operation.session.create.lastActiveAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastActiveAt { get; init; }
    [BaseField("auth.operation.session.create.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
    [BaseField("auth.operation.session.create.revoked")] public required bool Revoked { get; init; }
    [BaseField("auth.operation.session.create.revokedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RevokedAt { get; init; }
    [BaseField("auth.operation.session.create.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
    [BaseField("auth.operation.session.create.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthSessionCreateResultV1
{
    [BaseField("auth.operation.session.create.result.sessionId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SessionId { get; init; }
    [BaseField("auth.operation.session.create.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.session.create.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}

internal sealed record AuthSessionTouchV1
{
    [BaseField("auth.operation.session.touch.sessionId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SessionId { get; init; }
    [BaseField("auth.operation.session.touch.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.session.touch.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.session.touch.ssoProviderId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public BaseRecordId<AuthSsoProviderRecordV1>? SsoProviderId { get; init; }
    [BaseField("auth.operation.session.touch.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.session.touch.expectedSessionRevision")] public required RevisionToken ExpectedSessionRevision { get; init; }
    [BaseField("auth.operation.session.touch.lastActiveAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastActiveAt { get; init; }
    [BaseField("auth.operation.session.touch.ipAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? IpAddress { get; init; }
    [BaseField("auth.operation.session.touch.userAgent", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 1024, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserAgent { get; init; }
    [BaseField("auth.operation.session.touch.deviceInfo", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? DeviceInfo { get; init; }
    [BaseField("auth.operation.session.touch.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthSessionTouchResultV1
{
    [BaseField("auth.operation.session.touch.result.sessionId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SessionId { get; init; }
    [BaseField("auth.operation.session.touch.result.revision")] public required RevisionToken Revision { get; init; }
}
