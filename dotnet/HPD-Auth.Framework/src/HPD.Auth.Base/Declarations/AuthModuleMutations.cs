using System.Text.Json.Serialization;
using HPD.Base;

#pragma warning disable CS8620 // Generated optional non-null fields carry presence authority separately from CLR annotations.

namespace HPD.Auth.Base;

internal static class AuthModuleMutationDefaults
{
    internal static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 16, MaximumRecordCaptures = 12,
        MaximumRelationTargetCaptures = 16, MaximumGenerationCaptures = 4,
        MaximumRecordMutations = 12, MaximumGenerationReads = 4,
        MaximumGenerationComparisons = 4, MaximumGenerationIncrements = 4,
        MaximumGuardNodes = 64, MaximumGuardDepth = 8, MaximumStatements = 24,
        MaximumBranches = 8, MaximumExpressionNodes = 128,
        MaximumPreconditions = 64, MaximumRequestGuardEvaluations = 128,
        MaximumStaticSetMembers = 128, MaximumStaticSetComparisons = 8_192,
        MaximumDisabledCaptures = 128, MaximumRemovedFields = 64,
        MaximumReadIntervals = 64, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 32, MaximumRelationChecks = 32,
        MaximumUniqueConstraintChecks = 32, MaximumRequestBytes = 262_144,
        MaximumSelectedBytes = 1_048_576, MaximumGenerationBytes = 65_536,
        MaximumEvidenceBytes = 1_048_576, MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 2_097_152, MaximumJournalBytes = 2_621_440,
        MaximumReceiptBytes = 2_621_440, MaximumResultBytes = 65_536,
        MaximumTransientBytes = 8_388_608,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(2),
            TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(2),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    internal static BaseModuleMutationReceiptPolicy Receipt() => new()
    {
        FormatVersion = 1,
        Lifetime = TimeSpan.FromDays(1),
    };

    internal static BaseModuleRequireStatement Require(string operation, string suffix, string requirementId) =>
        BaseModuleMutationTemplateBuilder.Require($"{operation}.require.{suffix}", $"{operation}.guard.{suffix}", requirementId);
}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.create.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthCreateUserV1),
    typeof(AuthCreateUserResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.create")]
internal static partial class AuthCreateUserOperationV1
{
    private const string UserCapture = "hpd.auth.user.create.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.create.capture.userGen";
    private const string SecurityGenerationCapture = "hpd.auth.user.create.capture.securityGen";
    private const string CreateUserStatement = "hpd.auth.user.create.statement.000.createUser";
    private const string IncrementUserGenerationStatement = "hpd.auth.user.create.statement.001.incrementUserGeneration";
    private const string IncrementSecurityGenerationStatement = "hpd.auth.user.create.statement.002.incrementSecurityGeneration";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.create.v1",
            Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.user.create",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-create-user-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-create-user-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant
                {
                    CollectionId = AuthUserRecordV1.Collection.Id,
                    GrantId = "auth.identity.mutate",
                },
            ],
            GenerationCellIds =
            [
                "hpd.auth.user-security-generation.v1",
                "hpd.auth.user-state-generation.v1",
            ],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), User(), UserGeneration()],
                Guards = [],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements = [CreateUser(), IncrementUserGeneration(), IncrementSecurityGeneration()],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.user.create.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(
                            ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.user.create.expression.revision.000",
                                CreateUserStatement)),
                        BaseModuleMutationTemplateBuilder.Property(
                            ResultProperties.SecurityGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.create.expression.securityGeneration.000",
                                SecurityGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(
                            ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.create.expression.userGeneration.000",
                                UserGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(
                            ResultProperties.UserId,
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.user.create.expression.userId.000",
                                RequestProperties.UserId)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(),
            ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.create.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.create.expression.requestUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleGenerationKey GenerationKey(string suffix) =>
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            $"hpd.auth.user.create.expression.generationKey.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.create.expression.generationUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleRecordCapture User() =>
        BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequireMissing);

    private static BaseModuleGenerationCapture UserGeneration() =>
        BaseModuleMutationTemplateBuilder.CaptureGeneration(UserGenerationCapture,
            "hpd.auth.user-state-generation.v1", GenerationKey("user"),
            BaseModuleGenerationAbsenceBehavior.RequireMissing);

    private static BaseModuleGenerationCapture SecurityGeneration() =>
        BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityGenerationCapture,
            "hpd.auth.user-security-generation.v1", GenerationKey("security"),
            BaseModuleGenerationAbsenceBehavior.RequireMissing);

    private static BaseModuleCreateStatement CreateUser() =>
        BaseModuleMutationTemplateBuilder.Create(CreateUserStatement, UserId("create"),
            BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
                "hpd.auth.user.create.expression.payload.000",
                Field(AuthUserRecordV1.Fields.AccessFailedCount, RequestProperties.AccessFailedCount, "accessFailedCount"),
                Field(AuthUserRecordV1.Fields.AppMetadata, RequestProperties.AppMetadata, "appMetadata"),
                Field(AuthUserRecordV1.Fields.Audience, RequestProperties.Audience, "audience"),
                Field(AuthUserRecordV1.Fields.AvatarUrl, RequestProperties.AvatarUrl, "avatarUrl"),
                Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
                Field(AuthUserRecordV1.Fields.CreatedAt, RequestProperties.OperationTime, "createdAt"),
                Field(AuthUserRecordV1.Fields.DisplayName, RequestProperties.DisplayName, "displayName"),
                Field(AuthUserRecordV1.Fields.Email, RequestProperties.Email, "email"),
                Field(AuthUserRecordV1.Fields.EmailConfirmed, RequestProperties.EmailConfirmed, "emailConfirmed"),
                Field(AuthUserRecordV1.Fields.EmailConfirmedAt, RequestProperties.EmailConfirmedAt, "emailConfirmedAt"),
                Field(AuthUserRecordV1.Fields.FirstName, RequestProperties.FirstName, "firstName"),
                Field(AuthUserRecordV1.Fields.Id, RequestProperties.UserId, "id"),
                Field(AuthUserRecordV1.Fields.IsActive, RequestProperties.IsActive, "isActive"),
                Constant(AuthUserRecordV1.Fields.IsDeleted, false, "isDeleted"),
                Field(AuthUserRecordV1.Fields.LastLoginAt, RequestProperties.LastLoginAt, "lastLoginAt"),
                Field(AuthUserRecordV1.Fields.LastLoginIp, RequestProperties.LastLoginIp, "lastLoginIp"),
                Field(AuthUserRecordV1.Fields.LastName, RequestProperties.LastName, "lastName"),
                Field(AuthUserRecordV1.Fields.LockoutEnabled, RequestProperties.LockoutEnabled, "lockoutEnabled"),
                Field(AuthUserRecordV1.Fields.LockoutEnd, RequestProperties.LockoutEnd, "lockoutEnd"),
                Field(AuthUserRecordV1.Fields.NormalizedEmail, RequestProperties.NormalizedEmail, "normalizedEmail"),
                Field(AuthUserRecordV1.Fields.NormalizedUserName, RequestProperties.NormalizedUserName, "normalizedUserName"),
                Field(AuthUserRecordV1.Fields.PasswordHash, RequestProperties.PasswordHash, "passwordHash"),
                Field(AuthUserRecordV1.Fields.PhoneNumber, RequestProperties.PhoneNumber, "phoneNumber"),
                Field(AuthUserRecordV1.Fields.PhoneNumberConfirmed, RequestProperties.PhoneNumberConfirmed, "phoneNumberConfirmed"),
                Field(AuthUserRecordV1.Fields.RequiredActions, RequestProperties.RequiredActions, "requiredActions"),
                Field(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"),
                Field(AuthUserRecordV1.Fields.SubscriptionTier, RequestProperties.SubscriptionTier, "subscriptionTier"),
                Field(AuthUserRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
                Constant(AuthUserRecordV1.Fields.TombstoneGeneration, 0L, "tombstoneGeneration"),
                Field(AuthUserRecordV1.Fields.TwoFactorEnabled, RequestProperties.TwoFactorEnabled, "twoFactorEnabled"),
                Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt"),
                Field(AuthUserRecordV1.Fields.UserMetadata, RequestProperties.UserMetadata, "userMetadata"),
                Field(AuthUserRecordV1.Fields.UserName, RequestProperties.UserName, "userName")));

    private static BaseModuleIncrementGenerationStatement IncrementUserGeneration() =>
        BaseModuleMutationTemplateBuilder.IncrementGeneration(IncrementUserGenerationStatement, UserGenerationCapture, true);

    private static BaseModuleIncrementGenerationStatement IncrementSecurityGeneration() =>
        BaseModuleMutationTemplateBuilder.IncrementGeneration(IncrementSecurityGenerationStatement, SecurityGenerationCapture, true);

    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(
        BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthCreateUserV1, T> property,
        string id) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.create.expression.{id}.000", property));

    private static BaseModuleFieldValue<AuthUserRecordV1> Constant<T>(
        BaseField<AuthUserRecordV1, T> field,
        T value,
        string id) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.user.create.expression.{id}.000", field.ConstantAuthority, value));

}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.update-profile.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthUpdateUserProfileV1),
    typeof(AuthUpdateUserProfileResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.update")]
internal static partial class AuthUpdateUserProfileOperationV1
{
    private const string UserCapture = "hpd.auth.user.update-profile.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.update-profile.capture.userGen";
    private const string PatchStatement = "hpd.auth.user.update-profile.statement.000.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.update-profile.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.update",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-update-user-profile-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-update-user-profile-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.user-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [User(), UserGeneration()],
                Guards = [NotDeleted(), Revision()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.user.update-profile", "notDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.update-profile", "revision", "auth.user.revisionMismatch"),
                        Patch(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.update-profile.statement.001.incrementUserGeneration", UserGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.user.update-profile.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.user.update-profile.expression.revision.000", PatchStatement)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.update-profile.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.update-profile.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.update-profile.expression.requestUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            "hpd.auth.user.update-profile.expression.generationKey.000",
            BaseModuleMutationTemplateBuilder.Request(
                "hpd.auth.user.update-profile.expression.generationUserId.000", RequestProperties.UserId)),
        BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.update-profile.guard.notDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.update-profile.expression.notDeleted.000", AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false));
    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.user.update-profile.guard.revision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.update-profile.expression.expectedRevision.000", RequestProperties.ExpectedRevision));
    private static BaseModulePatchStatement Patch() => BaseModuleMutationTemplateBuilder.Patch(
        PatchStatement, UserId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
            "hpd.auth.user.update-profile.expression.patch.000",
            Field(AuthUserRecordV1.Fields.AppMetadata, RequestProperties.AppMetadata, "appMetadata"),
            Field(AuthUserRecordV1.Fields.Audience, RequestProperties.Audience, "audience"),
            Field(AuthUserRecordV1.Fields.AvatarUrl, RequestProperties.AvatarUrl, "avatarUrl"),
            Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            Field(AuthUserRecordV1.Fields.DisplayName, RequestProperties.DisplayName, "displayName"),
            Field(AuthUserRecordV1.Fields.Email, RequestProperties.Email, "email"),
            Field(AuthUserRecordV1.Fields.EmailConfirmed, RequestProperties.EmailConfirmed, "emailConfirmed"),
            Field(AuthUserRecordV1.Fields.EmailConfirmedAt, RequestProperties.EmailConfirmedAt, "emailConfirmedAt"),
            Field(AuthUserRecordV1.Fields.FirstName, RequestProperties.FirstName, "firstName"),
            Field(AuthUserRecordV1.Fields.IsActive, RequestProperties.IsActive, "isActive"),
            Field(AuthUserRecordV1.Fields.LastLoginAt, RequestProperties.LastLoginAt, "lastLoginAt"),
            Field(AuthUserRecordV1.Fields.LastLoginIp, RequestProperties.LastLoginIp, "lastLoginIp"),
            Field(AuthUserRecordV1.Fields.LastName, RequestProperties.LastName, "lastName"),
            Field(AuthUserRecordV1.Fields.NormalizedEmail, RequestProperties.NormalizedEmail, "normalizedEmail"),
            Field(AuthUserRecordV1.Fields.NormalizedUserName, RequestProperties.NormalizedUserName, "normalizedUserName"),
            Field(AuthUserRecordV1.Fields.PhoneNumber, RequestProperties.PhoneNumber, "phoneNumber"),
            Field(AuthUserRecordV1.Fields.PhoneNumberConfirmed, RequestProperties.PhoneNumberConfirmed, "phoneNumberConfirmed"),
            Field(AuthUserRecordV1.Fields.RequiredActions, RequestProperties.RequiredActions, "requiredActions"),
            Field(AuthUserRecordV1.Fields.SubscriptionTier, RequestProperties.SubscriptionTier, "subscriptionTier"),
            Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt"),
            Field(AuthUserRecordV1.Fields.UserMetadata, RequestProperties.UserMetadata, "userMetadata"),
            Field(AuthUserRecordV1.Fields.UserName, RequestProperties.UserName, "userName")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.update-profile.expression.patchRevision.000", RequestProperties.ExpectedRevision));
    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthUpdateUserProfileV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.update-profile.expression.{id}.000", property));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.role.create.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRoleCreateV1),
    typeof(AuthRoleCreateResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.role.mutate")]
internal static partial class AuthCreateRoleOperationV1
{
    private const string RoleCapture = "hpd.auth.role.create.capture.role";
    private const string RoleGenerationCapture = "hpd.auth.role.create.capture.roleGen";
    private const string CreateRoleStatement = "hpd.auth.role.create.statement.000.createRole";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.role.create.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.role.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-role-create-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-role-create-result-v1.v1",
            SystemCollectionIds = [AuthRoleRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant
            {
                CollectionId = AuthRoleRecordV1.Collection.Id,
                GrantId = "auth.identity.mutate",
            }],
            GenerationCellIds = ["hpd.auth.role-state-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Role(), RoleGeneration()], Guards = [], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        CreateRole(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.role.create.statement.001.incrementRoleGeneration",
                            RoleGenerationCapture, true),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.role.create.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.role.create.expression.revision.000", CreateRoleStatement)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.RoleGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.role.create.expression.roleGeneration.000", RoleGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.RoleId,
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.role.create.expression.roleId.000", RequestProperties.RoleId)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(),
            ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthRoleRecordV1>> RoleId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthRoleRecordV1>(
            $"hpd.auth.role.create.expression.roleId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.role.create.expression.requestRoleId.{suffix}", RequestProperties.RoleId));

    private static BaseModuleRecordCapture Role() =>
        BaseModuleMutationTemplateBuilder.CaptureRecord(RoleCapture, RoleId("capture"), BaseModuleCapturePresence.RequireMissing);

    private static BaseModuleGenerationCapture RoleGeneration() =>
        BaseModuleMutationTemplateBuilder.CaptureGeneration(RoleGenerationCapture,
            "hpd.auth.role-state-generation.v1",
            BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
                "hpd.auth.role.create.expression.generationKey.000",
                BaseModuleMutationTemplateBuilder.Request(
                    "hpd.auth.role.create.expression.generationRoleId.000", RequestProperties.RoleId)),
            BaseModuleGenerationAbsenceBehavior.RequireMissing);

    private static BaseModuleCreateStatement CreateRole() =>
        BaseModuleMutationTemplateBuilder.Create(CreateRoleStatement, RoleId("create"),
            BaseModuleMutationTemplateBuilder.Object<AuthRoleRecordV1>(
                "hpd.auth.role.create.expression.payload.000",
                Field(AuthRoleRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
                Field(AuthRoleRecordV1.Fields.CreatedAt, RequestProperties.OperationTime, "createdAt"),
                Field(AuthRoleRecordV1.Fields.Description, RequestProperties.Description, "description"),
                Field(AuthRoleRecordV1.Fields.Id, RequestProperties.RoleId, "id"),
                Constant(AuthRoleRecordV1.Fields.IsActive, true, "isActive"),
                Constant(AuthRoleRecordV1.Fields.IsDeleted, false, "isDeleted"),
                Field(AuthRoleRecordV1.Fields.Name, RequestProperties.Name, "name"),
                Field(AuthRoleRecordV1.Fields.NormalizedName, RequestProperties.NormalizedName, "normalizedName"),
                Field(AuthRoleRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
                Constant(AuthRoleRecordV1.Fields.TombstoneGeneration, 0L, "tombstoneGeneration"),
                Field(AuthRoleRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")));

    private static BaseModuleFieldValue<AuthRoleRecordV1> Field<T>(
        BaseField<AuthRoleRecordV1, T> field,
        BaseModuleRequestProperty<AuthRoleCreateV1, T> property,
        string id) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.role.create.expression.{id}.000", property));

    private static BaseModuleFieldValue<AuthRoleRecordV1> Constant<T>(
        BaseField<AuthRoleRecordV1, T> field,
        T value,
        string id) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.role.create.expression.{id}.000", field.ConstantAuthority, value));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.role.rename.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRoleRenameV1),
    typeof(AuthRoleMutationResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.role.mutate")]
internal static partial class AuthRenameRoleOperationV1
{
    private const string RoleCapture = "hpd.auth.role.rename.capture.role";
    private const string RoleGenerationCapture = "hpd.auth.role.rename.capture.roleGen";
    private const string PatchRoleStatement = "hpd.auth.role.rename.statement.000.patchRole";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.role.rename.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.role.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-role-rename-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-role-mutation-result-v1.v1",
            SystemCollectionIds = [AuthRoleRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthRoleRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.role-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Role(), RoleGeneration()],
                Guards = [Active(), NotDeleted(), Revision()],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.role.rename", "active", "auth.role.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.role.rename", "notDeleted", "auth.role.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.role.rename", "revision", "auth.role.revisionMismatch"),
                        PatchRole(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.role.rename.statement.001.incrementRoleGeneration", RoleGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.role.rename.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.role.rename.expression.revision.000", PatchRoleStatement)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.RoleGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.role.rename.expression.roleGeneration.000", RoleGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthRoleRecordV1>> RoleId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthRoleRecordV1>(
            $"hpd.auth.role.rename.expression.roleId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.role.rename.expression.requestRoleId.{suffix}", RequestProperties.RoleId));

    private static BaseModuleRecordCapture Role() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        RoleCapture, RoleId("capture"), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleGenerationCapture RoleGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        RoleGenerationCapture, "hpd.auth.role-state-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            "hpd.auth.role.rename.expression.generationKey.000",
            BaseModuleMutationTemplateBuilder.Request(
                "hpd.auth.role.rename.expression.generationRoleId.000", RequestProperties.RoleId)),
        BaseModuleGenerationAbsenceBehavior.RequireExisting);

    private static BaseModuleFieldEqualsGuard Active() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.role.rename.guard.active", RoleCapture, AuthRoleRecordV1.Fields.IsActive.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.role.rename.expression.active.000", AuthRoleRecordV1.Fields.IsActive.ConstantAuthority, true));

    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.role.rename.guard.notDeleted", RoleCapture, AuthRoleRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.role.rename.expression.notDeleted.000", AuthRoleRecordV1.Fields.IsDeleted.ConstantAuthority, false));

    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.role.rename.guard.revision", RoleCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.role.rename.expression.expectedRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModulePatchStatement PatchRole() => BaseModuleMutationTemplateBuilder.Patch(
        PatchRoleStatement, RoleId("patch"),
        BaseModuleMutationTemplateBuilder.Object<AuthRoleRecordV1>(
            "hpd.auth.role.rename.expression.patch.000",
            Field(AuthRoleRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            Field(AuthRoleRecordV1.Fields.Name, RequestProperties.Name, "name"),
            Field(AuthRoleRecordV1.Fields.NormalizedName, RequestProperties.NormalizedName, "normalizedName"),
            Field(AuthRoleRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.role.rename.expression.patchRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModuleFieldValue<AuthRoleRecordV1> Field<T>(
        BaseField<AuthRoleRecordV1, T> field, BaseModuleRequestProperty<AuthRoleRenameV1, T> property, string id) =>
        BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.role.rename.expression.{id}.000", property));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.change-password.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthChangePasswordV1),
    typeof(AuthSecurityMutationResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.security")]
internal static partial class AuthChangePasswordOperationV1
{
    private const string SecurityGenerationCapture = "hpd.auth.user.change-password.capture.securityGen";
    private const string UserCapture = "hpd.auth.user.change-password.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.change-password.capture.userGen";
    private const string PatchSetStatement = "hpd.auth.user.change-password.statement.000.patchSetPassword";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.change-password.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-change-password-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-security-mutation-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), User(), UserGeneration()],
                Guards = [Active(), NotDeleted(), Revision()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.user.change-password", "active", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.change-password", "notDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.change-password", "revision", "auth.user.revisionMismatch"),
                        PatchSet(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.change-password.statement.001.incrementUserGeneration", UserGenerationCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.change-password.statement.002.incrementSecurityGeneration", SecurityGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.user.change-password.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.user.change-password.expression.revision.000", PatchSetStatement)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.change-password.expression.securityGeneration.000", SecurityGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.change-password.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.change-password.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.change-password.expression.requestUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleGenerationKey GenerationKey(string suffix) =>
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            $"hpd.auth.user.change-password.expression.generationKey.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.change-password.expression.generationUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        SecurityGenerationCapture, "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);

    private static BaseModuleFieldEqualsGuard Active() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.change-password.guard.active", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.change-password.expression.active.000", AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true));
    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.change-password.guard.notDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.change-password.expression.notDeleted.000", AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false));
    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.user.change-password.guard.revision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.change-password.expression.expectedRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModulePatchStatement PatchSet() => BaseModuleMutationTemplateBuilder.Patch(
        PatchSetStatement, UserId("set"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
            "hpd.auth.user.change-password.expression.setPatch.000",
            Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.PasswordHash,
                BaseModuleMutationTemplateBuilder.LiftOptional(
                    "hpd.auth.user.change-password.expression.passwordHash.000",
                    AuthUserRecordV1.Fields.PasswordHash.ModuleMutation,
                    BaseModuleMutationTemplateBuilder.Request(
                        "hpd.auth.user.change-password.expression.passwordHashSource.000",
                        RequestProperties.PasswordHash))),
            Field(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"),
            Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.change-password.expression.patchRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthChangePasswordV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.change-password.expression.{id}.000", property));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.remove-password.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRemovePasswordV1),
    typeof(AuthSecurityMutationResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.security")]
internal static partial class AuthRemovePasswordOperationV1
{
    private const string SecurityGenerationCapture = "hpd.auth.user.remove-password.capture.securityGen";
    private const string UserCapture = "hpd.auth.user.remove-password.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.remove-password.capture.userGen";
    private const string PatchStatement = "hpd.auth.user.remove-password.statement.000.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.remove-password.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-remove-password-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-security-mutation-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), User(), UserGeneration()],
                Guards = [Active(), NotDeleted(), Revision()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.user.remove-password", "active", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.remove-password", "notDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.remove-password", "revision", "auth.user.revisionMismatch"),
                        Patch(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.remove-password.statement.001.incrementUserGeneration", UserGenerationCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.remove-password.statement.002.incrementSecurityGeneration", SecurityGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.user.remove-password.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.user.remove-password.expression.revision.000", PatchStatement)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.remove-password.expression.securityGeneration.000", SecurityGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.remove-password.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.remove-password.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.remove-password.expression.requestUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleGenerationKey GenerationKey(string suffix) =>
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            $"hpd.auth.user.remove-password.expression.generationKey.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.remove-password.expression.generationUserId.{suffix}", RequestProperties.UserId));

    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        SecurityGenerationCapture, "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard Active() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.remove-password.guard.active", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.remove-password.expression.active.000", AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true));
    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.remove-password.guard.notDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.remove-password.expression.notDeleted.000", AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false));
    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.user.remove-password.guard.revision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.remove-password.expression.expectedRevision.000", RequestProperties.ExpectedRevision));
    private static BaseModulePatchStatement Patch() => BaseModuleMutationTemplateBuilder.Patch(
        PatchStatement, UserId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
            "hpd.auth.user.remove-password.expression.patch.000",
            Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            BaseModuleMutationTemplateBuilder.Remove(AuthUserRecordV1.Fields.PasswordHash.ModuleMutation),
            Field(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"),
            Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.remove-password.expression.patchRevision.000", RequestProperties.ExpectedRevision));
    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthRemovePasswordV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.remove-password.expression.{id}.000", property));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.reset-password.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthResetPasswordV1),
    typeof(AuthSecurityMutationResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.security")]
internal static partial class AuthResetPasswordOperationV1
{
    private const string SecurityGenerationCapture = "hpd.auth.user.reset-password.capture.securityGen";
    private const string UserCapture = "hpd.auth.user.reset-password.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.reset-password.capture.userGen";
    private const string PatchStatement = "hpd.auth.user.reset-password.statement.000.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = CreateDefinition();

    private static BaseRegisteredModuleMutationDefinition CreateDefinition() => BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.reset-password.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-reset-password-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-security-mutation-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), User(), UserGeneration()],
                Guards = [Active(), NotDeleted(), Revision()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.user.reset-password", "active", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.reset-password", "notDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.reset-password", "revision", "auth.user.revisionMismatch"),
                        Patch(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.reset-password.statement.001.incrementUserGeneration", UserGenerationCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.reset-password.statement.002.incrementSecurityGeneration", SecurityGenerationCapture, false),
                    ],
                },
                Result = SecurityResult("hpd.auth.user.reset-password"),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.reset-password.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.reset-password.expression.requestUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(string suffix) => BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
        $"hpd.auth.user.reset-password.expression.generationKey.{suffix}",
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.reset-password.expression.generationUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        SecurityGenerationCapture, "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard Active() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.reset-password.guard.active", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.reset-password.expression.active.000", AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true));
    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.reset-password.guard.notDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.reset-password.expression.notDeleted.000", AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false));
    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.user.reset-password.guard.revision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.reset-password.expression.expectedRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModulePatchStatement Patch() => BaseModuleMutationTemplateBuilder.Patch(
        PatchStatement, UserId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
            "hpd.auth.user.reset-password.expression.patch.000",
            BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.AccessFailedCount,
                BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.reset-password.expression.accessFailedCount.000", AuthUserRecordV1.Fields.AccessFailedCount.ConstantAuthority, 0)),
            Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            Field(AuthUserRecordV1.Fields.LockoutEnabled, RequestProperties.LockoutEnabled, "lockoutEnabled"),
            BaseModuleMutationTemplateBuilder.Remove(AuthUserRecordV1.Fields.LockoutEnd.ModuleMutation),
            BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.PasswordHash,
                BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.user.reset-password.expression.passwordHash.000",
                    AuthUserRecordV1.Fields.PasswordHash.ModuleMutation,
                    BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.reset-password.expression.passwordHashSource.000", RequestProperties.PasswordHash))),
            Field(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"),
            Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.reset-password.expression.patchRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModuleResultProjection SecurityResult(string prefix) => BaseModuleMutationTemplateBuilder.Result(
        BaseModuleMutationTemplateBuilder.ResultObject($"{prefix}.expression.result.000",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                BaseModuleMutationTemplateBuilder.CommittedRevision($"{prefix}.expression.revision.000", PatchStatement)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                BaseModuleMutationTemplateBuilder.ResultingGeneration($"{prefix}.expression.securityGeneration.000", SecurityGenerationCapture)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                BaseModuleMutationTemplateBuilder.ResultingGeneration($"{prefix}.expression.userGeneration.000", UserGenerationCapture))));
    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthResetPasswordV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.reset-password.expression.{id}.000", property));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.user.set-security-state.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthSetSecurityStateV1),
    typeof(AuthSecurityMutationResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.user.security")]
internal static partial class AuthSetSecurityStateOperationV1
{
    private const string SecurityGenerationCapture = "hpd.auth.user.set-security-state.capture.securityGen";
    private const string UserCapture = "hpd.auth.user.set-security-state.capture.user";
    private const string UserGenerationCapture = "hpd.auth.user.set-security-state.capture.userGen";
    private const string PatchClearStatement = "hpd.auth.user.set-security-state.statement.000.patchUserClearLockout";
    private const string PatchValueStatement = "hpd.auth.user.set-security-state.statement.001.patchUserWithLockout";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.user.set-security-state.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-set-security-state-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-security-mutation-result-v1.v1",
            SystemCollectionIds = [AuthUserRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" }],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), User(), UserGeneration()],
                Guards = [Active(), ClearLockoutEnd(), NotDeleted(), Revision()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.user.set-security-state", "active", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.set-security-state", "notDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.user.set-security-state", "revision", "auth.user.revisionMismatch"),
                        BaseModuleMutationTemplateBuilder.If(
                            "hpd.auth.user.set-security-state.statement.lockoutBranch",
                            "hpd.auth.user.set-security-state.guard.clearLockoutEnd",
                            BaseModuleMutationTemplateBuilder.Block(Patch(clearLockoutEnd: true)),
                            BaseModuleMutationTemplateBuilder.Block(Patch(clearLockoutEnd: false))),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.set-security-state.statement.001.incrementUserGeneration", UserGenerationCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.user.set-security-state.statement.002.incrementSecurityGeneration", SecurityGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.user.set-security-state.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.Conditional(
                                "hpd.auth.user.set-security-state.expression.revision.000",
                                "hpd.auth.user.set-security-state.guard.clearLockoutEnd",
                                BaseModuleMutationTemplateBuilder.CommittedRevision(
                                    "hpd.auth.user.set-security-state.expression.revision.clear", PatchClearStatement),
                                BaseModuleMutationTemplateBuilder.CommittedRevision(
                                    "hpd.auth.user.set-security-state.expression.revision.value", PatchValueStatement))),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.set-security-state.expression.securityGeneration.000", SecurityGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.user.set-security-state.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.user.set-security-state.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.user.set-security-state.expression.requestUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(string suffix) => BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
        $"hpd.auth.user.set-security-state.expression.generationKey.{suffix}",
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.set-security-state.expression.generationUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        SecurityGenerationCapture, "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard Active() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.set-security-state.guard.active", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.set-security-state.expression.active.000", AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true));
    private static BaseModuleValueEqualsGuard ClearLockoutEnd() => BaseModuleMutationTemplateBuilder.ValueEquals(
        "hpd.auth.user.set-security-state.guard.clearLockoutEnd",
        BaseModuleMutationTemplateBuilder.Request(
            "hpd.auth.user.set-security-state.expression.clearLockoutEnd.left", RequestProperties.ClearLockoutEnd),
        BaseModuleMutationTemplateBuilder.Constant(
            "hpd.auth.user.set-security-state.expression.clearLockoutEnd.right",
            RequestProperties.ClearLockoutEnd.ConstantAuthority, true));
    private static BaseModuleFieldEqualsGuard NotDeleted() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.user.set-security-state.guard.notDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.user.set-security-state.expression.notDeleted.000", AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false));
    private static BaseModuleRevisionEqualsGuard Revision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.user.set-security-state.guard.revision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.user.set-security-state.expression.expectedRevision.000", RequestProperties.ExpectedRevision));

    private static BaseModulePatchStatement Patch(bool clearLockoutEnd)
    {
        string branch = clearLockoutEnd ? "clear" : "value";
        return BaseModuleMutationTemplateBuilder.Patch(
        clearLockoutEnd ? PatchClearStatement : PatchValueStatement,
        UserId(clearLockoutEnd ? "patchClear" : "patchValue"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>(
            $"hpd.auth.user.set-security-state.expression.patch.{branch}",
            Field(AuthUserRecordV1.Fields.AccessFailedCount, RequestProperties.AccessFailedCount, $"accessFailedCount.{branch}"),
            Field(AuthUserRecordV1.Fields.AuthenticatorKey, RequestProperties.AuthenticatorKey, $"authenticatorKey.{branch}"),
            Field(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, $"concurrencyStamp.{branch}"),
            Field(AuthUserRecordV1.Fields.LockoutEnabled, RequestProperties.LockoutEnabled, $"lockoutEnabled.{branch}"),
            clearLockoutEnd
                ? BaseModuleMutationTemplateBuilder.Remove(AuthUserRecordV1.Fields.LockoutEnd.ModuleMutation)
                : Field(AuthUserRecordV1.Fields.LockoutEnd, RequestProperties.LockoutEnd, $"lockoutEnd.{branch}"),
            Field(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, $"securityStamp.{branch}"),
            Field(AuthUserRecordV1.Fields.TwoFactorEnabled, RequestProperties.TwoFactorEnabled, $"twoFactorEnabled.{branch}"),
            Field(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, $"updatedAt.{branch}")),
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.set-security-state.expression.patchRevision.{branch}", RequestProperties.ExpectedRevision));
    }

    private static BaseModuleFieldValue<AuthUserRecordV1> Field<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthSetSecurityStateV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.user.set-security-state.expression.{id}.000", property));
}
