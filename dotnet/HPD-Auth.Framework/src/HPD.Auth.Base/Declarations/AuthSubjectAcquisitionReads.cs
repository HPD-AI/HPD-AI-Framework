using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.userSubject.acquire.v1", typeof(AuthSubjectAcquisitionReadJsonContext),
    RequiredGrantId = "auth.subject.user.acquire",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = ["auth.users"])]
internal sealed partial record AuthUserSubjectAcquisitionReadV1
{
    [BaseReadParameter("auth.read.userSubject.acquire.v1.parameter.userId")]
    public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userSubject.acquire.v1.row.reference")]
        public required BaseSubjectReference<AuthUserSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserSubjectAcquisitionReadV1, Row> read)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user)
            .Where(user.RecordId.Equal(read.Parameter(Parameters.UserId)))
            .ProjectSubjectReference(Row.Fields.Reference, user, AuthUserSubject.HPDBaseSubjectRegistration);
    }
}

[BaseRead("auth.read.roleSubject.acquire.v1", typeof(AuthSubjectAcquisitionReadJsonContext),
    RequiredGrantId = "auth.subject.role.acquire",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = ["auth.roles"])]
internal sealed partial record AuthRoleSubjectAcquisitionReadV1
{
    [BaseReadParameter("auth.read.roleSubject.acquire.v1.parameter.roleId")]
    public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.roleSubject.acquire.v1.row.reference")]
        public required BaseSubjectReference<AuthRoleSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRoleSubjectAcquisitionReadV1, Row> read)
    {
        read.From(AuthRoleRecordV1.Collection, "role", out BaseReadSource<AuthRoleRecordV1> role)
            .Where(role.RecordId.Equal(read.Parameter(Parameters.RoleId)))
            .ProjectSubjectReference(Row.Fields.Reference, role, AuthRoleSubject.HPDBaseSubjectRegistration);
    }
}

[JsonSerializable(typeof(AuthUserSubjectAcquisitionReadV1))]
[JsonSerializable(typeof(AuthUserSubjectAcquisitionReadV1.Row), TypeInfoPropertyName = "AuthUserSubjectAcquisitionRowV1")]
[JsonSerializable(typeof(AuthRoleSubjectAcquisitionReadV1))]
[JsonSerializable(typeof(AuthRoleSubjectAcquisitionReadV1.Row), TypeInfoPropertyName = "AuthRoleSubjectAcquisitionRowV1")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AuthSubjectAcquisitionReadJsonContext : JsonSerializerContext;
