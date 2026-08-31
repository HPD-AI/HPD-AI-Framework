using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.activeSessions.v1", typeof(AuthSessionReadJsonContext),
    RequiredGrantId = "auth.session.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.activeSessions.v1.row.tenantId", "auth.read.activeSessions.v1.row.brokerSessionId", "auth.read.activeSessions.v1.row.brokerUserId", "auth.read.activeSessions.v1.row.scopes", "auth.read.activeSessions.v1.row.clientSessions", "auth.read.activeSessions.v1.row.ipAddress", "auth.read.activeSessions.v1.row.userAgent", "auth.read.activeSessions.v1.row.deviceInfo", "auth.read.activeSessions.v1.row.lastActiveAt"],
    SystemSourceIds = ["auth.sessions"])]
internal sealed partial record AuthActiveSessionsReadV1
{
    [BaseReadParameter("auth.read.activeSessions.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.activeSessions.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseReadParameter("auth.read.activeSessions.v1.parameter.now"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset Now { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.activeSessions.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.aal")] public required AuthSessionAssuranceLevelV1 Aal { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.brokerSessionId")] public string? BrokerSessionId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.brokerUserId")] public string? BrokerUserId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.ssoProviderId")] public BaseRecordId<AuthSsoProviderRecordV1>? SsoProviderId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.notBefore"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotBefore { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.notAfter"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? NotAfter { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.oauthClientId")] public Guid? OauthClientId { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.scopes")] public string? Scopes { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.clientSessions")] public BaseCanonicalJson? ClientSessions { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.state")] public required AuthSessionStateV1 State { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.ipAddress")] public string? IpAddress { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.userAgent")] public string? UserAgent { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.deviceInfo")] public string? DeviceInfo { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.lastActiveAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastActiveAt { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.expiresAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset ExpiresAt { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
        [BaseReadField("auth.read.activeSessions.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthActiveSessionsReadV1, Row> read)
    {
        read.From(AuthSessionRecordV1.Collection, "session", out BaseReadSource<AuthSessionRecordV1> session)
            .Where(session.Field(AuthSessionRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(session.Field(AuthSessionRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId)))
                .And(session.Field(AuthSessionRecordV1.Fields.Revoked).Equal(read.Literal(false)))
                .And(session.Field(AuthSessionRecordV1.Fields.State).Equal(read.ClosedEnumLiteral(AuthSessionStateV1.active)))
                .And(session.Field(AuthSessionRecordV1.Fields.ExpiresAt).GreaterThan(read.Parameter(Parameters.Now))))
            .Project(Row.Fields.Id, session.Field(AuthSessionRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, session.Field(AuthSessionRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, session.Field(AuthSessionRecordV1.Fields.UserId))
            .Project(Row.Fields.Aal, session.Field(AuthSessionRecordV1.Fields.Aal))
            .Project(Row.Fields.BrokerSessionId, session.Field(AuthSessionRecordV1.Fields.BrokerSessionId))
            .Project(Row.Fields.BrokerUserId, session.Field(AuthSessionRecordV1.Fields.BrokerUserId))
            .Project(Row.Fields.SsoProviderId, session.Field(AuthSessionRecordV1.Fields.SsoProviderId))
            .Project(Row.Fields.NotBefore, session.Field(AuthSessionRecordV1.Fields.NotBefore))
            .Project(Row.Fields.NotAfter, session.Field(AuthSessionRecordV1.Fields.NotAfter))
            .Project(Row.Fields.OauthClientId, session.Field(AuthSessionRecordV1.Fields.OauthClientId))
            .Project(Row.Fields.Scopes, session.Field(AuthSessionRecordV1.Fields.Scopes))
            .Project(Row.Fields.ClientSessions, session.Field(AuthSessionRecordV1.Fields.ClientSessions))
            .Project(Row.Fields.State, session.Field(AuthSessionRecordV1.Fields.State))
            .Project(Row.Fields.IpAddress, session.Field(AuthSessionRecordV1.Fields.IpAddress))
            .Project(Row.Fields.UserAgent, session.Field(AuthSessionRecordV1.Fields.UserAgent))
            .Project(Row.Fields.DeviceInfo, session.Field(AuthSessionRecordV1.Fields.DeviceInfo))
            .Project(Row.Fields.CreatedAt, session.Field(AuthSessionRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.LastActiveAt, session.Field(AuthSessionRecordV1.Fields.LastActiveAt))
            .Project(Row.Fields.ExpiresAt, session.Field(AuthSessionRecordV1.Fields.ExpiresAt))
            .Project(Row.Fields.SecurityGeneration, session.Field(AuthSessionRecordV1.Fields.SecurityGeneration))
            .Project(Row.Fields.Revision, session.Revision)
            .OrderBy(session.Field(AuthSessionRecordV1.Fields.LastActiveAt), QuerySortDirection.Desc)
            .OrderBy(session.Field(AuthSessionRecordV1.Fields.Id))
            .Limits(200, 524_288, 16, 750);
    }
}

[JsonSerializable(typeof(AuthActiveSessionsReadV1), TypeInfoPropertyName = "AuthActiveSessionsReadV1")]
[JsonSerializable(typeof(AuthActiveSessionsReadV1.Row), TypeInfoPropertyName = "AuthActiveSessionsReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthSessionReadJsonContext : JsonSerializerContext;
