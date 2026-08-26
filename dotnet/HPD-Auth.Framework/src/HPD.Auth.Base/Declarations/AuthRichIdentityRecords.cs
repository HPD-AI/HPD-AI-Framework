using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseCollection("auth.ssoProviders", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.sso.provider", Unique = true)]
[BaseIndexPart("auth.idx.sso.provider", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.sso.provider", 1, nameof(ProviderId))]
internal sealed partial record AuthSsoProviderRecordV1
{
    [BaseField("auth.ssoProviders.id"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.ssoProviders.tenantId"), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.ssoProviders.providerId", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string ProviderId { get; init; }
    [BaseField("auth.ssoProviders.clientId", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string ClientId { get; init; }
    [BaseField("auth.ssoProviders.clientSecret", MaximumBytes = 16384), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary ClientSecret { get; init; }
    [BaseField("auth.ssoProviders.scopes", MaximumUtf8Bytes = 1000, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string Scopes { get; init; }
    [BaseField("auth.ssoProviders.entityId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? EntityId { get; init; }
    [BaseField("auth.ssoProviders.metadataXml", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 262144, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? MetadataXml { get; init; }
    [BaseField("auth.ssoProviders.attributeMapping", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public BaseCanonicalJson? AttributeMapping { get; init; }
    [BaseField("auth.ssoProviders.nameIdFormat", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? NameIdFormat { get; init; }
    [BaseField("auth.ssoProviders.signingCertificate", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public BaseBinary? SigningCertificate { get; init; }
    [BaseField("auth.ssoProviders.enabled"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Enabled { get; init; }
    [BaseField("auth.ssoProviders.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.ssoProviders.updatedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? UpdatedAt { get; init; }
}

[BaseCollection("auth.userIdentities", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.identities.provider", Unique = true)]
[BaseIndexPart("auth.idx.identities.provider", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.identities.provider", 1, nameof(Provider))]
[BaseIndexPart("auth.idx.identities.provider", 2, nameof(ProviderId))]
[BaseIndex("auth.idx.identities.user")]
[BaseIndexPart("auth.idx.identities.user", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.identities.user", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.identities.user", 2, nameof(Id))]
internal sealed partial record AuthUserIdentityRecordV1
{
    [BaseField("auth.userIdentities.id"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.userIdentities.tenantId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.userIdentities.userId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.identity.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.userIdentities.provider", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Provider { get; init; }
    [BaseField("auth.userIdentities.providerId", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string ProviderId { get; init; }
    [BaseField("auth.userIdentities.identityData", MaximumCanonicalJsonBytes = 65536, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 65536, MaximumJsonTotalNameUtf8Bytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson IdentityData { get; init; }
    [BaseField("auth.userIdentities.lastSignInAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset LastSignInAt { get; init; }
    [BaseField("auth.userIdentities.federationSourceId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.identity.ssoProvider", typeof(AuthSsoProviderRecordV1))] public BaseRecordId<AuthSsoProviderRecordV1>? FederationSourceId { get; init; }
    [BaseField("auth.userIdentities.lastSyncAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastSyncAt { get; init; }
    [BaseField("auth.userIdentities.providerTokens", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumBytes = 65536), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public BaseBinary? ProviderTokens { get; init; }
    [BaseField("auth.userIdentities.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.userIdentities.updatedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? UpdatedAt { get; init; }
}

[BaseCollection("auth.tenantSettings", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
internal sealed partial record AuthTenantSettingsRecordV1
{
    [BaseField("auth.tenantSettings.tenantId"), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.tenantSettings.displayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 200, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? DisplayName { get; init; }
    [BaseField("auth.tenantSettings.logoUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? LogoUrl { get; init; }
    [BaseField("auth.tenantSettings.faviconUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? FaviconUrl { get; init; }
    [BaseField("auth.tenantSettings.primaryColor", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 7, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? PrimaryColor { get; init; }
    [BaseField("auth.tenantSettings.accentColor", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 7, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? AccentColor { get; init; }
    [BaseField("auth.tenantSettings.emailFromName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 200, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? EmailFromName { get; init; }
    [BaseField("auth.tenantSettings.emailFromAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? EmailFromAddress { get; init; }
    [BaseField("auth.tenantSettings.siteUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? SiteUrl { get; init; }
    [BaseField("auth.tenantSettings.supportEmail", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? SupportEmail { get; init; }
    [BaseField("auth.tenantSettings.settings", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson Settings { get; init; }
    [BaseField("auth.tenantSettings.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.tenantSettings.updatedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? UpdatedAt { get; init; }
}
