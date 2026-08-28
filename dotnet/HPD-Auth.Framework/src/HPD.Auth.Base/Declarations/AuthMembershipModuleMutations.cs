using HPD.Base;

namespace HPD.Auth.Base;

// Generated optional/non-null Base authorities intentionally use nullable CLR annotations.
#pragma warning disable CS8620

[BaseRegisteredModuleMutation(
    "hpd.auth.membership.add.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthMembershipAddV1),
    typeof(AuthMembershipAddResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.membership.mutate")]
internal static partial class AuthMembershipAddOperationV1
{
    private const string MembershipCapture = "hpd.auth.membership.add.capture.membership";
    private const string MembershipGenerationCapture = "hpd.auth.membership.add.capture.membershipGen";
    private const string RoleCapture = "hpd.auth.membership.add.capture.role";
    private const string UserCapture = "hpd.auth.membership.add.capture.user";
    private const string CreateStatement = "hpd.auth.membership.add.statement.000.createMembership";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.membership.add.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.membership.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-membership-add-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-membership-add-result-v1.v1",
            SystemCollectionIds = [AuthRoleRecordV1.Collection.Id, AuthUserRoleRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthRoleRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRoleRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.membership-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Membership(), MembershipGeneration(), Role(), User()],
                Guards = [RoleActive(), RoleNotDeleted(), RoleRevision(), RoleTenant(), UserActive(), UserNotDeleted(), UserRevision(), UserTenant()],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "roleActive", "auth.role.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "roleNotDeleted", "auth.role.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "roleRevision", "auth.role.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "roleTenant", "auth.role.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "userActive", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "userNotDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "userRevision", "auth.user.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.add", "userTenant", "auth.user.scopeMismatch"),
                        CreateMembership(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.membership.add.statement.001.incrementMembershipGeneration", MembershipGenerationCapture, true),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.membership.add.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.MembershipGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.membership.add.expression.membershipGeneration.000", MembershipGenerationCapture)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.MembershipId,
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.membership.add.expression.membershipId.000", RequestProperties.MembershipId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision(
                                "hpd.auth.membership.add.expression.revision.000", CreateStatement)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRoleRecordV1>> MembershipId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthUserRoleRecordV1>(
            $"hpd.auth.membership.add.expression.membershipRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.membership.add.expression.membershipIdRequest.{suffix}", RequestProperties.MembershipId));
    private static BaseModuleValue<BaseRecordId<AuthRoleRecordV1>> RoleId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthRoleRecordV1>(
            $"hpd.auth.membership.add.expression.roleRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.membership.add.expression.roleIdRequest.{suffix}", RequestProperties.RoleId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.membership.add.expression.userRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.membership.add.expression.userIdRequest.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(BaseModuleRequestProperty<AuthMembershipAddV1, Guid> property, string suffix) =>
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            $"hpd.auth.membership.add.expression.generationKey.{suffix}",
            BaseModuleMutationTemplateBuilder.Request(
                $"hpd.auth.membership.add.expression.generationId.{suffix}", property));

    private static BaseModuleRecordCapture Membership() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        MembershipCapture, MembershipId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleGenerationCapture MembershipGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        MembershipGenerationCapture, "hpd.auth.membership-generation.v1", GenerationKey(RequestProperties.UserId, "membership"),
        BaseModuleGenerationAbsenceBehavior.AllowEither);
    private static BaseModuleRecordCapture Role() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        RoleCapture, RoleId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleFieldEqualsGuard RoleActive() => BooleanGuard(
        "hpd.auth.membership.add.guard.roleActive", RoleCapture, AuthRoleRecordV1.Fields.IsActive.ModuleMutation,
        AuthRoleRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard RoleNotDeleted() => BooleanGuard(
        "hpd.auth.membership.add.guard.roleNotDeleted", RoleCapture, AuthRoleRecordV1.Fields.IsDeleted.ModuleMutation,
        AuthRoleRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard RoleRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.membership.add.guard.roleRevision", RoleCapture,
        BaseModuleMutationTemplateBuilder.Request(
            "hpd.auth.membership.add.expression.expectedRoleRevision.000", RequestProperties.ExpectedRoleRevision));
    private static BaseModuleFieldEqualsGuard RoleTenant() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.membership.add.guard.roleTenant", RoleCapture, AuthRoleRecordV1.Fields.TenantId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.membership.add.expression.roleTenant.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard UserActive() => BooleanGuard(
        "hpd.auth.membership.add.guard.userActive", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation,
        AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => BooleanGuard(
        "hpd.auth.membership.add.guard.userNotDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation,
        AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.membership.add.guard.userRevision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request(
            "hpd.auth.membership.add.expression.expectedUserRevision.000", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.membership.add.guard.userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.membership.add.expression.userTenant.000", RequestProperties.TenantId));

    private static BaseModuleCreateStatement CreateMembership() => BaseModuleMutationTemplateBuilder.Create(
        CreateStatement, MembershipId("create"), BaseModuleMutationTemplateBuilder.Object<AuthUserRoleRecordV1>(
            "hpd.auth.membership.add.expression.create.000",
            Field(AuthUserRoleRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "createdAt"),
            Field(AuthUserRoleRecordV1.Fields.Id, RequestProperties.MembershipId, "id"),
            BaseModuleMutationTemplateBuilder.Field(AuthUserRoleRecordV1.Fields.RoleId, RoleId("payload")),
            Field(AuthUserRoleRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
            BaseModuleMutationTemplateBuilder.Field(AuthUserRoleRecordV1.Fields.UserId, UserId("payload"))));

    private static BaseModuleFieldValue<AuthUserRoleRecordV1> Field<T>(BaseField<AuthUserRoleRecordV1, T> field,
        BaseModuleRequestProperty<AuthMembershipAddV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.add.expression.{id}.000", property));
    private static BaseModuleFieldEqualsGuard BooleanGuard<TRecord>(string id, string capture,
        BaseModuleCapturedField<TRecord, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) =>
        BaseModuleMutationTemplateBuilder.FieldEquals(id, capture, field,
            BaseModuleMutationTemplateBuilder.Constant($"{id}.constant", authority, value));

}

[BaseRegisteredModuleMutation(
    "hpd.auth.membership.remove.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthMembershipRemoveV1),
    typeof(AuthMembershipRemoveResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.membership.mutate")]
internal static partial class AuthMembershipRemoveOperationV1
{
    private const string MembershipCapture = "hpd.auth.membership.remove.capture.membership";
    private const string MembershipGenerationCapture = "hpd.auth.membership.remove.capture.membershipGen";
    private const string RoleCapture = "hpd.auth.membership.remove.capture.role";
    private const string UserCapture = "hpd.auth.membership.remove.capture.user";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.membership.remove.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.membership.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-membership-remove-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-membership-remove-result-v1.v1",
            SystemCollectionIds = [AuthRoleRecordV1.Collection.Id, AuthUserRoleRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthRoleRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRoleRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.membership-generation.v1"],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Membership(), MembershipGenerationRecord(), Role(), User()],
                Guards =
                [
                    MembershipRevision(), MembershipRole(), MembershipTenant(), MembershipUser(),
                    RoleActive(), RoleNotDeleted(), RoleRevision(), RoleTenant(),
                    UserActive(), UserNotDeleted(), UserRevision(), UserTenant(),
                ],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "membershipRevision", "auth.membership.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "membershipRole", "auth.membership.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "membershipTenant", "auth.membership.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "membershipUser", "auth.membership.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "roleActive", "auth.role.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "roleNotDeleted", "auth.role.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "roleRevision", "auth.role.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "roleTenant", "auth.role.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "userActive", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "userNotDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "userRevision", "auth.user.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.membership.remove", "userTenant", "auth.user.scopeMismatch"),
                        BaseModuleMutationTemplateBuilder.Delete(
                            "hpd.auth.membership.remove.statement.000.deleteMembership", MembershipId("delete"),
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.membership.remove.expression.deleteRevision.000", RequestProperties.ExpectedMembershipRevision)),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration(
                            "hpd.auth.membership.remove.statement.001.incrementMembershipGeneration", MembershipGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.membership.remove.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.MembershipGeneration,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "hpd.auth.membership.remove.expression.membershipGeneration.000", MembershipGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserRoleRecordV1>> MembershipId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthUserRoleRecordV1>(
            $"hpd.auth.membership.remove.expression.membershipRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.membershipIdRequest.{suffix}", RequestProperties.MembershipId));
    private static BaseModuleValue<BaseRecordId<AuthRoleRecordV1>> RoleId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthRoleRecordV1>(
            $"hpd.auth.membership.remove.expression.roleRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.roleIdRequest.{suffix}", RequestProperties.RoleId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.membership.remove.expression.userRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.userIdRequest.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(BaseModuleRequestProperty<AuthMembershipRemoveV1, Guid> property, string suffix) =>
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            $"hpd.auth.membership.remove.expression.generationKey.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.generationId.{suffix}", property));
    private static BaseModuleRecordCapture Membership() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        MembershipCapture, MembershipId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture MembershipGenerationRecord() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        MembershipGenerationCapture, "hpd.auth.membership-generation.v1", GenerationKey(RequestProperties.UserId, "membership"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture Role() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        RoleCapture, RoleId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRevisionEqualsGuard MembershipRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.membership.remove.guard.membershipRevision", MembershipCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.membership.remove.expression.membershipRevision.000", RequestProperties.ExpectedMembershipRevision));
    private static BaseModuleFieldEqualsGuard MembershipRole() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.membership.remove.guard.membershipRole", MembershipCapture, AuthUserRoleRecordV1.Fields.RoleId.ModuleMutation, RoleId("guard"));
    private static BaseModuleFieldEqualsGuard MembershipTenant() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.membership.remove.guard.membershipTenant", MembershipCapture, AuthUserRoleRecordV1.Fields.TenantId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.membership.remove.expression.membershipTenant.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard MembershipUser() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.membership.remove.guard.membershipUser", MembershipCapture, AuthUserRoleRecordV1.Fields.UserId.ModuleMutation, UserId("guard"));
    private static BaseModuleFieldEqualsGuard RoleActive() => BooleanGuard(
        "roleActive", RoleCapture, AuthRoleRecordV1.Fields.IsActive.ModuleMutation, AuthRoleRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard RoleNotDeleted() => BooleanGuard(
        "roleNotDeleted", RoleCapture, AuthRoleRecordV1.Fields.IsDeleted.ModuleMutation, AuthRoleRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard RoleRevision() => RevisionGuard(
        "roleRevision", RoleCapture, RequestProperties.ExpectedRoleRevision);
    private static BaseModuleFieldEqualsGuard RoleTenant() => TenantGuard(
        "roleTenant", RoleCapture, AuthRoleRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard UserActive() => BooleanGuard(
        "userActive", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => BooleanGuard(
        "userNotDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => RevisionGuard(
        "userRevision", UserCapture, RequestProperties.ExpectedUserRevision);
    private static BaseModuleFieldEqualsGuard UserTenant() => TenantGuard(
        "userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);

    private static BaseModuleRevisionEqualsGuard RevisionGuard(string name, string capture,
        BaseModuleRequestProperty<AuthMembershipRemoveV1, RevisionToken> expected) => BaseModuleMutationTemplateBuilder.RevisionEquals(
        $"hpd.auth.membership.remove.guard.{name}", capture,
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.{name}.000", expected));
    private static BaseModuleFieldEqualsGuard TenantGuard<TRecord>(string name, string capture,
        BaseModuleCapturedField<TRecord, Guid> field) => BaseModuleMutationTemplateBuilder.FieldEquals(
        $"hpd.auth.membership.remove.guard.{name}", capture, field,
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.membership.remove.expression.{name}.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard BooleanGuard<TRecord>(string name, string capture,
        BaseModuleCapturedField<TRecord, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) =>
        BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.membership.remove.guard.{name}", capture, field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.membership.remove.expression.{name}.000", authority, value));
}
