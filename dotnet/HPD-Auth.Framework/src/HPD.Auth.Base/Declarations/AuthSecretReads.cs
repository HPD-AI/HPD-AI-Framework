using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead(
    "auth.read.userPassword.v1",
    typeof(AuthBaseReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.secret.password",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SecretOutputFieldIds = ["auth.read.userPassword.v1.row.passwordHash"],
    SystemSourceIds = ["auth.users"])]
internal sealed partial record AuthUserPasswordReadV1
{
    [BaseReadParameter("auth.read.userPassword.v1.parameter.tenantId")]
    public required Guid TenantId { get; init; }

    [BaseReadParameter("auth.read.userPassword.v1.parameter.userId")]
    public required Guid UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userPassword.v1.row.passwordHash")]
        public required string PasswordHash { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserPasswordReadV1, Row> read)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user)
            .Where(user.Field(AuthUserRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(user.Field(AuthUserRecordV1.Fields.Id).Equal(read.Parameter(Parameters.UserId)))
                .And(user.Field(AuthUserRecordV1.Fields.PasswordHash).IsDefined())
                .And(user.Field(AuthUserRecordV1.Fields.PasswordHash).IsNull().Not()))
            .Project(Row.Fields.PasswordHash, user.Field(AuthUserRecordV1.Fields.PasswordHash))
            .OrderBy(user.Field(AuthUserRecordV1.Fields.Id))
            .Limits(1, 8_192, 6, 250);
    }
}

[BaseRead(
    "auth.read.userTwoFactorSecrets.v1",
    typeof(AuthBaseReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.secret.twoFactor",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SecretOutputFieldIds = ["auth.read.userTwoFactorSecrets.v1.row.authenticatorKey"],
    SystemSourceIds = ["auth.users"])]
internal sealed partial record AuthUserTwoFactorSecretsReadV1
{
    [BaseReadParameter("auth.read.userTwoFactorSecrets.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userTwoFactorSecrets.v1.parameter.userId")] public required Guid UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userTwoFactorSecrets.v1.row.authenticatorKey")] public string? AuthenticatorKey { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserTwoFactorSecretsReadV1, Row> read)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user)
            .Where(user.Field(AuthUserRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(user.Field(AuthUserRecordV1.Fields.Id).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.AuthenticatorKey, user.Field(AuthUserRecordV1.Fields.AuthenticatorKey))
            .OrderBy(user.Field(AuthUserRecordV1.Fields.Id))
            .Limits(1, 49_152, 6, 250);
    }
}

[BaseRead(
    "auth.read.dataProtectionKeys.v1",
    typeof(AuthBaseReadJsonSerializerContext),
    RequiredGrantId = "auth.dataProtection.read",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.dataProtectionKeys.v1.row.applicationDiscriminator", "auth.read.dataProtectionKeys.v1.row.contentDigest", "auth.read.dataProtectionKeys.v1.row.friendlyName"],
    SecretOutputFieldIds = ["auth.read.dataProtectionKeys.v1.row.canonicalXml"],
    SystemSourceIds = ["auth.dataProtectionKeys"])]
internal sealed partial record AuthDataProtectionKeysReadV1
{
    [BaseReadParameter("auth.read.dataProtectionKeys.v1.parameter.applicationDiscriminator")]
    public required string ApplicationDiscriminator { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.applicationDiscriminator")] public required string ApplicationDiscriminator { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.friendlyName")] public required string FriendlyName { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.canonicalXml", MaximumBytes = 262144)] public required BaseBinary CanonicalXml { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.contentDigest", MaximumBytes = 32)] public required BaseBinary ContentDigest { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.dataProtectionKeys.v1.row.formatVersion")] public required int FormatVersion { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthDataProtectionKeysReadV1, Row> read)
    {
        read.From(AuthDataProtectionKeyRecordV1.Collection, "key", out BaseReadSource<AuthDataProtectionKeyRecordV1> key)
            .Where(key.Field(AuthDataProtectionKeyRecordV1.Fields.ApplicationDiscriminator).Equal(read.Parameter(Parameters.ApplicationDiscriminator)))
            .Project(Row.Fields.Id, key.Field(AuthDataProtectionKeyRecordV1.Fields.Id))
            .Project(Row.Fields.ApplicationDiscriminator, key.Field(AuthDataProtectionKeyRecordV1.Fields.ApplicationDiscriminator))
            .Project(Row.Fields.FriendlyName, key.Field(AuthDataProtectionKeyRecordV1.Fields.FriendlyName))
            .Project(Row.Fields.CanonicalXml, key.Field(AuthDataProtectionKeyRecordV1.Fields.CanonicalXml))
            .Project(Row.Fields.ContentDigest, key.Field(AuthDataProtectionKeyRecordV1.Fields.ContentDigest))
            .Project(Row.Fields.CreatedAt, key.Field(AuthDataProtectionKeyRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.FormatVersion, key.Field(AuthDataProtectionKeyRecordV1.Fields.FormatVersion))
            .OrderBy(key.Field(AuthDataProtectionKeyRecordV1.Fields.CreatedAt))
            .OrderBy(key.Field(AuthDataProtectionKeyRecordV1.Fields.Id))
            .Limits(256, 16_777_216, 8, 2_000);
    }
}

[BaseRead(
    "auth.read.passkeyByDigest.v1",
    typeof(AuthBaseReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.secret.passkey",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.passkeyByDigest.v1.row.credentialDigest", "auth.read.passkeyByDigest.v1.row.tenantId"],
    SecretOutputFieldIds = ["auth.read.passkeyByDigest.v1.row.credentialId", "auth.read.passkeyByDigest.v1.row.publicKey"],
    SystemSourceIds = ["auth.passkeys"])]
internal sealed partial record AuthPasskeyByDigestReadV1
{
    [BaseReadParameter("auth.read.passkeyByDigest.v1.parameter.credentialDigest", MaximumBytes = 32)]
    public required BaseBinary CredentialDigest { get; init; }

    [BaseReadParameter("auth.read.passkeyByDigest.v1.parameter.tenantHint")]
    public Guid? TenantHint { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.passkeyByDigest.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.credentialDigest", MaximumBytes = 32)] public required BaseBinary CredentialDigest { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.credentialId", MaximumBytes = 1024)] public required BaseBinary CredentialId { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.publicKey", MaximumBytes = 16384)] public required BaseBinary PublicKey { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.signatureCounter")] public required long SignatureCounter { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.userVerified")] public required bool UserVerified { get; init; }
        [BaseReadField("auth.read.passkeyByDigest.v1.row.isDiscoverable")] public required bool IsDiscoverable { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthPasskeyByDigestReadV1, Row> read)
    {
        read.From(AuthPasskeyRecordV1.Collection, "passkey", out BaseReadSource<AuthPasskeyRecordV1> passkey);
        BaseReadOperand<Guid> tenant = read.OptionalParameter(Parameters.TenantHint);
        read.Where(passkey.Field(AuthPasskeyRecordV1.Fields.CredentialDigest).Equal(read.Parameter(Parameters.CredentialDigest))
                .And(tenant.IsNull().Or(passkey.Field(AuthPasskeyRecordV1.Fields.TenantId).Equal(tenant))))
            .Project(Row.Fields.Id, passkey.Field(AuthPasskeyRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, passkey.Field(AuthPasskeyRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, passkey.Field(AuthPasskeyRecordV1.Fields.UserId))
            .Project(Row.Fields.CredentialDigest, passkey.Field(AuthPasskeyRecordV1.Fields.CredentialDigest))
            .Project(Row.Fields.CredentialId, passkey.Field(AuthPasskeyRecordV1.Fields.CredentialId))
            .Project(Row.Fields.PublicKey, passkey.Field(AuthPasskeyRecordV1.Fields.PublicKey))
            .Project(Row.Fields.SignatureCounter, passkey.Field(AuthPasskeyRecordV1.Fields.SignatureCounter))
            .Project(Row.Fields.UserVerified, passkey.Field(AuthPasskeyRecordV1.Fields.UserVerified))
            .Project(Row.Fields.IsDiscoverable, passkey.Field(AuthPasskeyRecordV1.Fields.IsDiscoverable))
            .OrderBy(passkey.Field(AuthPasskeyRecordV1.Fields.Id))
            .Limits(1, 32_768, 10, 250);
    }
}

[BaseRead(
    "auth.read.userTokenSecret.v1",
    typeof(AuthBaseReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.secret.twoFactor",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SecretOutputFieldIds = ["auth.read.userTokenSecret.v1.row.value"],
    SystemSourceIds = ["auth.userTokens"])]
internal sealed partial record AuthUserTokenSecretReadV1
{
    [BaseReadParameter("auth.read.userTokenSecret.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userTokenSecret.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseReadParameter("auth.read.userTokenSecret.v1.parameter.provider")] public required string Provider { get; init; }
    [BaseReadParameter("auth.read.userTokenSecret.v1.parameter.name")] public required string Name { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userTokenSecret.v1.row.value")] public required string Value { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserTokenSecretReadV1, Row> read)
    {
        read.From(AuthUserTokenRecordV1.Collection, "token", out BaseReadSource<AuthUserTokenRecordV1> token)
            .Where(token.Field(AuthUserTokenRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(token.Field(AuthUserTokenRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId)))
                .And(token.Field(AuthUserTokenRecordV1.Fields.LoginProvider).Equal(read.Parameter(Parameters.Provider)))
                .And(token.Field(AuthUserTokenRecordV1.Fields.Name).Equal(read.Parameter(Parameters.Name)))
                .And(token.Field(AuthUserTokenRecordV1.Fields.Value).IsDefined())
                .And(token.Field(AuthUserTokenRecordV1.Fields.Value).IsNull().Not()))
            .Project(Row.Fields.Value, token.Field(AuthUserTokenRecordV1.Fields.Value))
            .OrderBy(token.Field(AuthUserTokenRecordV1.Fields.Id))
            .Limits(1, 32_768, 10, 250);
    }
}

[JsonSerializable(typeof(AuthUserPasswordReadV1), TypeInfoPropertyName = "AuthUserPasswordReadV1")]
[JsonSerializable(typeof(AuthUserPasswordReadV1.Row), TypeInfoPropertyName = "AuthUserPasswordReadV1Row")]
[JsonSerializable(typeof(AuthUserTwoFactorSecretsReadV1), TypeInfoPropertyName = "AuthUserTwoFactorSecretsReadV1")]
[JsonSerializable(typeof(AuthUserTwoFactorSecretsReadV1.Row), TypeInfoPropertyName = "AuthUserTwoFactorSecretsReadV1Row")]
[JsonSerializable(typeof(AuthDataProtectionKeysReadV1), TypeInfoPropertyName = "AuthDataProtectionKeysReadV1")]
[JsonSerializable(typeof(AuthDataProtectionKeysReadV1.Row), TypeInfoPropertyName = "AuthDataProtectionKeysReadV1Row")]
[JsonSerializable(typeof(AuthPasskeyByDigestReadV1), TypeInfoPropertyName = "AuthPasskeyByDigestReadV1")]
[JsonSerializable(typeof(AuthPasskeyByDigestReadV1.Row), TypeInfoPropertyName = "AuthPasskeyByDigestReadV1Row")]
[JsonSerializable(typeof(AuthUserTokenSecretReadV1), TypeInfoPropertyName = "AuthUserTokenSecretReadV1")]
[JsonSerializable(typeof(AuthUserTokenSecretReadV1.Row), TypeInfoPropertyName = "AuthUserTokenSecretReadV1Row")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseReadJsonSerializerContext : JsonSerializerContext;
