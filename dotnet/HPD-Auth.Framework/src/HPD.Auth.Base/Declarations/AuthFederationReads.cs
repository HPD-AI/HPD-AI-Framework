using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.externalIdentity.v1", typeof(AuthFederationReadJsonContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.externalIdentity.v1.row.tenantId", "auth.read.externalIdentity.v1.row.providerId", "auth.read.externalIdentity.v1.row.identityData", "auth.read.externalIdentity.v1.row.lastSignInAt", "auth.read.externalIdentity.v1.row.lastSyncAt"],
    SystemSourceIds = ["auth.userIdentities"])]
internal sealed partial record AuthExternalIdentityReadV1
{
    [BaseReadParameter("auth.read.externalIdentity.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.externalIdentity.v1.parameter.provider")] public required string Provider { get; init; }
    [BaseReadParameter("auth.read.externalIdentity.v1.parameter.providerId")] public required string ProviderId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.externalIdentity.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.provider")] public required string Provider { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.providerId")] public required string ProviderId { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.identityData")] public required BaseCanonicalJson IdentityData { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.lastSignInAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastSignInAt { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.federationSourceId")] public BaseRecordId<AuthSsoProviderRecordV1>? FederationSourceId { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.lastSyncAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastSyncAt { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.updatedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? UpdatedAt { get; init; }
        [BaseReadField("auth.read.externalIdentity.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthExternalIdentityReadV1, Row> read)
    {
        read.From(AuthUserIdentityRecordV1.Collection, "identity", out BaseReadSource<AuthUserIdentityRecordV1> identity)
            .Where(identity.Field(AuthUserIdentityRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(identity.Field(AuthUserIdentityRecordV1.Fields.Provider).Equal(read.Parameter(Parameters.Provider)))
                .And(identity.Field(AuthUserIdentityRecordV1.Fields.ProviderId).Equal(read.Parameter(Parameters.ProviderId))))
            .Project(Row.Fields.Id, identity.Field(AuthUserIdentityRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, identity.Field(AuthUserIdentityRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, identity.Field(AuthUserIdentityRecordV1.Fields.UserId))
            .Project(Row.Fields.Provider, identity.Field(AuthUserIdentityRecordV1.Fields.Provider))
            .Project(Row.Fields.ProviderId, identity.Field(AuthUserIdentityRecordV1.Fields.ProviderId))
            .Project(Row.Fields.IdentityData, identity.Field(AuthUserIdentityRecordV1.Fields.IdentityData))
            .Project(Row.Fields.LastSignInAt, identity.Field(AuthUserIdentityRecordV1.Fields.LastSignInAt))
            .Project(Row.Fields.FederationSourceId, identity.Field(AuthUserIdentityRecordV1.Fields.FederationSourceId))
            .Project(Row.Fields.LastSyncAt, identity.Field(AuthUserIdentityRecordV1.Fields.LastSyncAt))
            .Project(Row.Fields.CreatedAt, identity.Field(AuthUserIdentityRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.UpdatedAt, identity.Field(AuthUserIdentityRecordV1.Fields.UpdatedAt))
            .Project(Row.Fields.Revision, identity.Revision)
            .OrderBy(identity.Field(AuthUserIdentityRecordV1.Fields.Id))
            .Limits(1, 131_072, 10, 500);
    }
}

[BaseRead("auth.read.ssoProviderSecret.v1", typeof(AuthFederationReadJsonContext),
    RequiredGrantId = "auth.identity.secret.provider", Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.ssoProviderSecret.v1.row.tenantId", "auth.read.ssoProviderSecret.v1.row.clientId", "auth.read.ssoProviderSecret.v1.row.scopes", "auth.read.ssoProviderSecret.v1.row.entityId", "auth.read.ssoProviderSecret.v1.row.metadataXml", "auth.read.ssoProviderSecret.v1.row.attributeMapping"],
    SecretOutputFieldIds = ["auth.read.ssoProviderSecret.v1.row.clientSecret", "auth.read.ssoProviderSecret.v1.row.signingCertificate"],
    SystemSourceIds = ["auth.ssoProviders"])]
internal sealed partial record AuthSsoProviderSecretReadV1
{
    [BaseReadParameter("auth.read.ssoProviderSecret.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.ssoProviderSecret.v1.parameter.providerId")] public required string ProviderId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.providerId")] public required string ProviderId { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.clientId")] public required string ClientId { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.clientSecret", MaximumBytes = 16384)] public required BaseBinary ClientSecret { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.scopes")] public required string Scopes { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.entityId")] public string? EntityId { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.metadataXml")] public string? MetadataXml { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.attributeMapping")] public BaseCanonicalJson? AttributeMapping { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.nameIdFormat")] public string? NameIdFormat { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.signingCertificate", MaximumBytes = 65536)] public BaseBinary? SigningCertificate { get; init; }
        [BaseReadField("auth.read.ssoProviderSecret.v1.row.enabled")] public required bool Enabled { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthSsoProviderSecretReadV1, Row> read)
    {
        read.From(AuthSsoProviderRecordV1.Collection, "provider", out BaseReadSource<AuthSsoProviderRecordV1> provider)
            .Where(provider.Field(AuthSsoProviderRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(provider.Field(AuthSsoProviderRecordV1.Fields.ProviderId).Equal(read.Parameter(Parameters.ProviderId))))
            .Project(Row.Fields.Id, provider.Field(AuthSsoProviderRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, provider.Field(AuthSsoProviderRecordV1.Fields.TenantId))
            .Project(Row.Fields.ProviderId, provider.Field(AuthSsoProviderRecordV1.Fields.ProviderId))
            .Project(Row.Fields.ClientId, provider.Field(AuthSsoProviderRecordV1.Fields.ClientId))
            .Project(Row.Fields.ClientSecret, provider.Field(AuthSsoProviderRecordV1.Fields.ClientSecret))
            .Project(Row.Fields.Scopes, provider.Field(AuthSsoProviderRecordV1.Fields.Scopes))
            .Project(Row.Fields.EntityId, provider.Field(AuthSsoProviderRecordV1.Fields.EntityId))
            .Project(Row.Fields.MetadataXml, provider.Field(AuthSsoProviderRecordV1.Fields.MetadataXml))
            .Project(Row.Fields.AttributeMapping, provider.Field(AuthSsoProviderRecordV1.Fields.AttributeMapping))
            .Project(Row.Fields.NameIdFormat, provider.Field(AuthSsoProviderRecordV1.Fields.NameIdFormat))
            .Project(Row.Fields.SigningCertificate, provider.Field(AuthSsoProviderRecordV1.Fields.SigningCertificate))
            .Project(Row.Fields.Enabled, provider.Field(AuthSsoProviderRecordV1.Fields.Enabled))
            .OrderBy(provider.Field(AuthSsoProviderRecordV1.Fields.Id))
            .Limits(1, 393_216, 8, 500);
    }
}

[JsonSerializable(typeof(AuthExternalIdentityReadV1), TypeInfoPropertyName = "AuthExternalIdentityReadV1")]
[JsonSerializable(typeof(AuthExternalIdentityReadV1.Row), TypeInfoPropertyName = "AuthExternalIdentityReadV1Row")]
[JsonSerializable(typeof(AuthSsoProviderSecretReadV1), TypeInfoPropertyName = "AuthSsoProviderSecretReadV1")]
[JsonSerializable(typeof(AuthSsoProviderSecretReadV1.Row), TypeInfoPropertyName = "AuthSsoProviderSecretReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthFederationReadJsonContext : JsonSerializerContext;
