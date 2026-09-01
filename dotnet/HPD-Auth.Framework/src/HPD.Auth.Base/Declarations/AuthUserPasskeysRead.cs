using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.userPasskeys.v1", typeof(AuthUserPasskeysReadJsonContext),
    RequiredGrantId = "auth.identity.secret.passkey",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.userPasskeys.v1.row.name", "auth.read.userPasskeys.v1.row.tenantId"],
    SecretOutputFieldIds = ["auth.read.userPasskeys.v1.row.attestationObject", "auth.read.userPasskeys.v1.row.clientDataJson", "auth.read.userPasskeys.v1.row.credentialId", "auth.read.userPasskeys.v1.row.publicKey"],
    SystemSourceIds = ["auth.passkeys"])]
internal sealed partial record AuthUserPasskeysReadV1
{
    [BaseReadParameter("auth.read.userPasskeys.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userPasskeys.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userPasskeys.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.credentialId", MaximumBytes = 1024)] public required BaseBinary CredentialId { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.publicKey", MaximumBytes = 16384)] public required BaseBinary PublicKey { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.signatureCounter")] public required long SignatureCounter { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.name")] public string? Name { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.transports")] public required BaseCanonicalJson Transports { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.userVerified")] public required bool UserVerified { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.backupEligible")] public required bool BackupEligible { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.backedUp")] public required bool BackedUp { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.attestationObject", MaximumBytes = 65536)] public required BaseBinary AttestationObject { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.clientDataJson", MaximumBytes = 65536)] public required BaseBinary ClientDataJson { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.userPasskeys.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserPasskeysReadV1, Row> read)
    {
        read.From(AuthPasskeyRecordV1.Collection, "passkey", out BaseReadSource<AuthPasskeyRecordV1> passkey)
            .Where(passkey.Field(AuthPasskeyRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(passkey.Field(AuthPasskeyRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.Id, passkey.Field(AuthPasskeyRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, passkey.Field(AuthPasskeyRecordV1.Fields.TenantId))
            .Project(Row.Fields.CredentialId, passkey.Field(AuthPasskeyRecordV1.Fields.CredentialId))
            .Project(Row.Fields.PublicKey, passkey.Field(AuthPasskeyRecordV1.Fields.PublicKey))
            .Project(Row.Fields.SignatureCounter, passkey.Field(AuthPasskeyRecordV1.Fields.SignatureCounter))
            .Project(Row.Fields.Name, passkey.Field(AuthPasskeyRecordV1.Fields.Name))
            .Project(Row.Fields.Transports, passkey.Field(AuthPasskeyRecordV1.Fields.Transports))
            .Project(Row.Fields.UserVerified, passkey.Field(AuthPasskeyRecordV1.Fields.UserVerified))
            .Project(Row.Fields.BackupEligible, passkey.Field(AuthPasskeyRecordV1.Fields.BackupEligible))
            .Project(Row.Fields.BackedUp, passkey.Field(AuthPasskeyRecordV1.Fields.BackedUp))
            .Project(Row.Fields.AttestationObject, passkey.Field(AuthPasskeyRecordV1.Fields.AttestationObject))
            .Project(Row.Fields.ClientDataJson, passkey.Field(AuthPasskeyRecordV1.Fields.ClientDataJson))
            .Project(Row.Fields.CreatedAt, passkey.Field(AuthPasskeyRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.Revision, passkey.Revision)
            .OrderBy(passkey.Field(AuthPasskeyRecordV1.Fields.CreatedAt))
            .OrderBy(passkey.Field(AuthPasskeyRecordV1.Fields.Id))
            .Limits(64, 8_388_608, 10, 750);
    }
}

[JsonSerializable(typeof(AuthUserPasskeysReadV1))]
[JsonSerializable(typeof(AuthUserPasskeysReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthUserPasskeysReadJsonContext : JsonSerializerContext;
