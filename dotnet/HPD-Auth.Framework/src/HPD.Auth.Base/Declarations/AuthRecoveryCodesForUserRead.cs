using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.recoveryCodesForUser.v1", typeof(AuthRecoveryCodesForUserReadJsonContext),
    RequiredGrantId = "auth.identity.secret.twoFactor",
    Disclosure = BaseRegisteredReadDisclosure.SecretProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = ["auth.recoveryCodes"])]
internal sealed partial record AuthRecoveryCodesForUserReadV1
{
    [BaseReadParameter("auth.read.recoveryCodesForUser.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.recoveryCodesForUser.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.recoveryCodesForUser.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.recoveryCodesForUser.v1.row.digestKeyVersion")] public required int DigestKeyVersion { get; init; }
        [BaseReadField("auth.read.recoveryCodesForUser.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRecoveryCodesForUserReadV1, Row> read)
    {
        read.From(AuthRecoveryCodeRecordV1.Collection, "code", out BaseReadSource<AuthRecoveryCodeRecordV1> code)
            .Where(code.Field(AuthRecoveryCodeRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(code.Field(AuthRecoveryCodeRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.Id, code.Field(AuthRecoveryCodeRecordV1.Fields.Id))
            .Project(Row.Fields.DigestKeyVersion, code.Field(AuthRecoveryCodeRecordV1.Fields.DigestKeyVersion))
            .Project(Row.Fields.Revision, code.Revision)
            .OrderBy(code.Field(AuthRecoveryCodeRecordV1.Fields.Id))
            .Limits(64, 32_768, 10, 500);
    }
}

[JsonSerializable(typeof(AuthRecoveryCodesForUserReadV1))]
[JsonSerializable(typeof(AuthRecoveryCodesForUserReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthRecoveryCodesForUserReadJsonContext : JsonSerializerContext;
