using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.usersForClaim.v1", typeof(AuthUsersForClaimReadJsonContext),
    RequiredGrantId = "auth.identity.read",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = ["auth.userClaims"])]
internal sealed partial record AuthUsersForClaimReadV1
{
    [BaseReadParameter("auth.read.usersForClaim.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.usersForClaim.v1.parameter.claimType")] public required string ClaimType { get; init; }
    [BaseReadParameter("auth.read.usersForClaim.v1.parameter.claimValue")] public required string ClaimValue { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.usersForClaim.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUsersForClaimReadV1, Row> read)
    {
        read.From(AuthUserClaimRecordV1.Collection, "claim", out BaseReadSource<AuthUserClaimRecordV1> claim)
            .Where(claim.Field(AuthUserClaimRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(claim.Field(AuthUserClaimRecordV1.Fields.ClaimType).Equal(read.Parameter(Parameters.ClaimType)))
                .And(claim.Field(AuthUserClaimRecordV1.Fields.ClaimValue).Equal(read.Parameter(Parameters.ClaimValue))))
            .Project(Row.Fields.UserId, claim.Field(AuthUserClaimRecordV1.Fields.UserId))
            .Distinct()
            .OrderBy(claim.Field(AuthUserClaimRecordV1.Fields.UserId))
            .OrderBy(claim.Field(AuthUserClaimRecordV1.Fields.Id))
            .Limits(256, 32_768, 10, 500);
    }
}

[JsonSerializable(typeof(AuthUsersForClaimReadV1))]
[JsonSerializable(typeof(AuthUsersForClaimReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthUsersForClaimReadJsonContext : JsonSerializerContext;
