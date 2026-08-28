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

[JsonSerializable(typeof(AuthUserSubjectAcquisitionReadV1))]
[JsonSerializable(typeof(AuthUserSubjectAcquisitionReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AuthSubjectAcquisitionReadJsonContext : JsonSerializerContext;
