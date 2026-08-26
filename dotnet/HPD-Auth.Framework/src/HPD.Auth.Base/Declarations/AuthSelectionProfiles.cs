using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal static class AuthSelectionProfiles
{
    internal static ImmutableArray<BaseSelectionOperationProfile> All { get; } =
    [
        Profile("auth.sessions.revoke-user.v1", "auth.sessions", BaseSelectionMutationKind.MergePatch, "auth.session.mutate"),
        Profile("auth.sessions.expire-due.v1", "auth.sessions", BaseSelectionMutationKind.MergePatch, "auth.cleanup.execute"),
        Profile("auth.refreshTokens.revoke-user.v1", "auth.refreshTokens", BaseSelectionMutationKind.MergePatch, "auth.token.mutate"),
        Profile("auth.sessions.delete-user.v1", "auth.sessions", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.refreshTokens.delete-user.v1", "auth.refreshTokens", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.refreshTokens.delete-expired.v1", "auth.refreshTokens", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.refreshTokenDeliveries.delete-expired.v1", "auth.refreshTokenDeliveries", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.passkeys.delete-user.v1", "auth.passkeys", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userClaims.delete-user.v1", "auth.userClaims", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userLogins.delete-user.v1", "auth.userLogins", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userTokens.delete-user.v1", "auth.userTokens", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userRoles.delete-user.v1", "auth.userRoles", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userIdentities.delete-user.v1", "auth.userIdentities", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.roleClaims.delete-role.v1", "auth.roleClaims", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.userRoles.delete-role.v1", "auth.userRoles", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
        Profile("auth.maintenanceRuns.delete-expired.v1", "auth.maintenanceRuns", BaseSelectionMutationKind.Delete, "auth.cleanup.execute"),
    ];

    internal static BaseGeneratedSelectionProfileIdentity SessionsRevokeUser { get; } = Identity(All[0]);
    internal static BaseGeneratedSelectionProfileIdentity SessionsExpireDue { get; } = Identity(All[1]);
    internal static BaseGeneratedSelectionProfileIdentity RefreshTokensRevokeUser { get; } = Identity(All[2]);
    internal static BaseGeneratedSelectionProfileIdentity SessionsDeleteUser { get; } = Identity(All[3]);
    internal static BaseGeneratedSelectionProfileIdentity RefreshTokensDeleteUser { get; } = Identity(All[4]);
    internal static BaseGeneratedSelectionProfileIdentity RefreshTokensDeleteExpired { get; } = Identity(All[5]);
    internal static BaseGeneratedSelectionProfileIdentity RefreshTokenDeliveriesDeleteExpired { get; } = Identity(All[6]);
    internal static BaseGeneratedSelectionProfileIdentity PasskeysDeleteUser { get; } = Identity(All[7]);
    internal static BaseGeneratedSelectionProfileIdentity UserClaimsDeleteUser { get; } = Identity(All[8]);
    internal static BaseGeneratedSelectionProfileIdentity UserLoginsDeleteUser { get; } = Identity(All[9]);
    internal static BaseGeneratedSelectionProfileIdentity UserTokensDeleteUser { get; } = Identity(All[10]);
    internal static BaseGeneratedSelectionProfileIdentity UserRolesDeleteUser { get; } = Identity(All[11]);
    internal static BaseGeneratedSelectionProfileIdentity UserIdentitiesDeleteUser { get; } = Identity(All[12]);
    internal static BaseGeneratedSelectionProfileIdentity RoleClaimsDeleteRole { get; } = Identity(All[13]);
    internal static BaseGeneratedSelectionProfileIdentity UserRolesDeleteRole { get; } = Identity(All[14]);
    internal static BaseGeneratedSelectionProfileIdentity MaintenanceRunsDeleteExpired { get; } = Identity(All[15]);

    private static BaseGeneratedSelectionProfileIdentity Identity(BaseSelectionOperationProfile profile)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(profile, AuthSelectionProfileJsonContext.Default.BaseSelectionOperationProfile);
        return BaseGeneratedSelectionProfiles.RegisterSelectionProfile(
            BaseGeneratedModules.RegisterCollectionModule(profile.ApplicationId, profile.CollectionId),
            new BaseGeneratedSelectionProfileDescriptor
            {
                ApplicationId = profile.ApplicationId,
                CollectionId = profile.CollectionId,
                ProfileId = profile.Id,
                Version = profile.Version,
                Kind = profile.MutationKind,
                Checksum = Convert.ToHexStringLower(SHA256.HashData(canonical)),
            });
    }

    private static BaseSelectionOperationProfile Profile(
        string id,
        string collectionId,
        BaseSelectionMutationKind mutationKind,
        string grantId) => new()
    {
        Id = id,
        Version = 1,
        ApplicationId = AuthBaseContract.ApplicationId,
        CollectionId = collectionId,
        RequiredGrantId = grantId,
        MutationKind = mutationKind,
        HttpProjection = null,
        Limits = new BaseSelectionOperationLimits
        {
            MaximumQueryNodes = 24,
            MaximumQueryDepth = 8,
            MaximumLiteralValues = 32,
            MaximumSelectedRecords = 200,
            MaximumSelectedBytes = 1_048_576,
            MaximumProducedMutations = 200,
            MaximumQueryExecutions = 1,
            MaximumReadIntervals = 64,
            MaximumWrittenBytes = 1_048_576,
            MaximumFactBytes = 2_097_152,
            MaximumJournalBytes = 2_621_440,
            MaximumReceiptBytes = 2_621_440,
            MaximumRelationChecks = 400,
            MaximumUniqueConstraintChecks = 400,
            MaximumPreviousStateRequirements = 8,
            MaximumTransientBytes = 8_388_608,
            MaximumResultBytes = 32_768,
            AcquisitionTimeout = TimeSpan.FromSeconds(2),
            ExecutionTimeout = TimeSpan.FromSeconds(5),
            CallerCommitObservationTimeout = TimeSpan.FromSeconds(2),
        },
    };
}

[JsonSerializable(typeof(BaseSelectionOperationProfile))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
internal sealed partial class AuthSelectionProfileJsonContext : JsonSerializerContext;
