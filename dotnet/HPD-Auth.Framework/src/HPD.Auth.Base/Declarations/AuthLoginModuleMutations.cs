using HPD.Base;

namespace HPD.Auth.Base;

// Generated optional/non-null Base authorities intentionally use nullable CLR annotations.
#pragma warning disable CS8620

[BaseRegisteredModuleMutation(
    "hpd.auth.login.link.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthLoginLinkV1),
    typeof(AuthLoginLinkResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.login.mutate")]
internal static partial class AuthLoginLinkOperationV1
{
    private const string IdentityCapture = "hpd.auth.login.link.capture.identity";
    private const string LoginCapture = "hpd.auth.login.link.capture.login";
    private const string UserCapture = "hpd.auth.login.link.capture.user";
    private const string UserGenerationCapture = "hpd.auth.login.link.capture.userGen";
    private const string IdentityCreate = "hpd.auth.login.link.statement.000.createIdentity";
    private const string LoginCreate = "hpd.auth.login.link.statement.001.createLogin";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.login.link.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.login.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-login-link-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-login-link-result-v1.v1",
            SystemCollectionIds = [AuthUserIdentityRecordV1.Collection.Id, AuthUserLoginRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserIdentityRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserLoginRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.user-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [IdentityRecord(), Login(), User(), UserGenerationRecord()],
                Guards = [UserActive(), UserNotDeleted(), UserRevision(), UserTenant()],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.login.link", "userActive", "auth.user.inactive"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.link", "userNotDeleted", "auth.user.deleted"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.link", "userRevision", "auth.user.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.link", "userTenant", "auth.user.scopeMismatch"),
                        CreateIdentity(), CreateLogin(),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.login.link.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.IdentityId,
                            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.link.expression.resultIdentityId.000", RequestProperties.IdentityId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.IdentityRevision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.login.link.expression.identityRevision.000", IdentityCreate)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.LoginId,
                            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.link.expression.resultLoginId.000", RequestProperties.LoginId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.LoginRevision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.login.link.expression.loginRevision.000", LoginCreate)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.CapturedGeneration("hpd.auth.login.link.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserIdentityRecordV1>> IdentityId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserIdentityRecordV1>(
            $"hpd.auth.login.link.expression.identityRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.link.expression.identityIdRequest.{suffix}", RequestProperties.IdentityId));
    private static BaseModuleValue<BaseRecordId<AuthUserLoginRecordV1>> LoginId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthUserLoginRecordV1>(
            $"hpd.auth.login.link.expression.loginRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.link.expression.loginIdRequest.{suffix}", RequestProperties.LoginId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.login.link.expression.userRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.link.expression.userIdRequest.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture IdentityRecord() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        IdentityCapture, IdentityId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleRecordCapture Login() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        LoginCapture, LoginId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGenerationRecord() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            "hpd.auth.login.link.expression.generationKey.000",
            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.link.expression.generationUserId.000", RequestProperties.UserId)),
        BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard UserActive() => BooleanGuard(
        "userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => BooleanGuard(
        "userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.login.link.guard.userRevision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.link.expression.expectedUserRevision.000", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.login.link.guard.userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.link.expression.userTenant.000", RequestProperties.TenantId));

    private static BaseModuleCreateStatement CreateIdentity() => BaseModuleMutationTemplateBuilder.Create(
        IdentityCreate, IdentityId("create"), BaseModuleMutationTemplateBuilder.Object<AuthUserIdentityRecordV1>(
            "hpd.auth.login.link.expression.identity.000",
            Field(AuthUserIdentityRecordV1.Fields.CreatedAt, RequestProperties.OperationTime, "identityCreatedAt"),
            Field(AuthUserIdentityRecordV1.Fields.FederationSourceId, RequestProperties.FederationSourceId, "federationSourceId"),
            Field(AuthUserIdentityRecordV1.Fields.Id, RequestProperties.IdentityId, "identityId"),
            Field(AuthUserIdentityRecordV1.Fields.IdentityData, RequestProperties.IdentityData, "identityData"),
            Field(AuthUserIdentityRecordV1.Fields.LastSignInAt, RequestProperties.OperationTime, "lastSignInAt"),
            Field(AuthUserIdentityRecordV1.Fields.Provider, RequestProperties.LoginProvider, "provider"),
            Field(AuthUserIdentityRecordV1.Fields.ProviderId, RequestProperties.ProviderId, "providerId"),
            Field(AuthUserIdentityRecordV1.Fields.TenantId, RequestProperties.TenantId, "identityTenantId"),
            BaseModuleMutationTemplateBuilder.Field(AuthUserIdentityRecordV1.Fields.UserId, UserId("identityPayload"))));
    private static BaseModuleCreateStatement CreateLogin() => BaseModuleMutationTemplateBuilder.Create(
        LoginCreate, LoginId("create"), BaseModuleMutationTemplateBuilder.Object<AuthUserLoginRecordV1>(
            "hpd.auth.login.link.expression.login.000",
            Field(AuthUserLoginRecordV1.Fields.CreatedAt, RequestProperties.OperationTime, "loginCreatedAt"),
            Field(AuthUserLoginRecordV1.Fields.Id, RequestProperties.LoginId, "loginId"),
            Field(AuthUserLoginRecordV1.Fields.LoginProvider, RequestProperties.LoginProvider, "loginProvider"),
            Field(AuthUserLoginRecordV1.Fields.ProviderDisplayName, RequestProperties.ProviderDisplayName, "providerDisplayName"),
            Field(AuthUserLoginRecordV1.Fields.ProviderKey, RequestProperties.ProviderKey, "providerKey"),
            Field(AuthUserLoginRecordV1.Fields.TenantId, RequestProperties.TenantId, "loginTenantId"),
            BaseModuleMutationTemplateBuilder.Field(AuthUserLoginRecordV1.Fields.UserId, UserId("loginPayload"))));

    private static BaseModuleFieldValue<TRecord> Field<TRecord, T>(BaseField<TRecord, T> field,
        BaseModuleRequestProperty<AuthLoginLinkV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.link.expression.{id}.000", property));
    private static BaseModuleFieldEqualsGuard BooleanGuard(string name, BaseModuleCapturedField<AuthUserRecordV1, bool> field,
        BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals(
        $"hpd.auth.login.link.guard.{name}", UserCapture, field,
        BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.login.link.expression.{name}.000", authority, value));
}

[BaseRegisteredModuleMutation(
    "hpd.auth.login.unlink.v1",
    typeof(AuthBaseJsonSerializerContext),
    typeof(AuthLoginUnlinkV1),
    typeof(AuthLoginUnlinkResultV1),
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    GrantId = "auth.operation.login.mutate")]
internal static partial class AuthLoginUnlinkOperationV1
{
    private const string IdentityCapture = "hpd.auth.login.unlink.capture.identity";
    private const string LoginCapture = "hpd.auth.login.unlink.capture.login";
    private const string UserCapture = "hpd.auth.login.unlink.capture.user";
    private const string UserGenerationCapture = "hpd.auth.login.unlink.capture.userGen";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.login.unlink.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.login.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-login-unlink-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-login-unlink-result-v1.v1",
            SystemCollectionIds = [AuthUserIdentityRecordV1.Collection.Id, AuthUserLoginRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserIdentityRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserLoginRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.user-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [IdentityRecord(), Login(), User(), UserGenerationRecord()],
                Guards =
                [
                    IdentityProvider(), IdentityRevision(), IdentityTenant(), IdentityUser(), LoginRevision(), LoginTenant(),
                    LoginUser(), UserRevision(), UserTenant(),
                ],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "identityProvider", "auth.login.bindingMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "identityRevision", "auth.login.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "identityTenant", "auth.login.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "identityUser", "auth.login.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "loginRevision", "auth.login.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "loginTenant", "auth.login.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "loginUser", "auth.login.scopeMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "userRevision", "auth.user.revisionMismatch"),
                        AuthModuleMutationDefaults.Require("hpd.auth.login.unlink", "userTenant", "auth.user.scopeMismatch"),
                        BaseModuleMutationTemplateBuilder.Delete(
                            "hpd.auth.login.unlink.statement.000.deleteIdentity", IdentityId("delete"),
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.login.unlink.expression.identityDeleteRevision.000", RequestProperties.ExpectedIdentityRevision)),
                        BaseModuleMutationTemplateBuilder.Delete(
                            "hpd.auth.login.unlink.statement.001.deleteLogin", LoginId("delete"),
                            BaseModuleMutationTemplateBuilder.Request(
                                "hpd.auth.login.unlink.expression.loginDeleteRevision.000", RequestProperties.ExpectedLoginRevision)),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject(
                        "hpd.auth.login.unlink.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.IdentityId,
                            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.unlink.expression.resultIdentityId.000", RequestProperties.IdentityId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.LoginId,
                            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.unlink.expression.resultLoginId.000", RequestProperties.LoginId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                            BaseModuleMutationTemplateBuilder.CapturedGeneration("hpd.auth.login.unlink.expression.userGeneration.000", UserGenerationCapture)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthUserIdentityRecordV1>> IdentityId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserIdentityRecordV1>(
            $"hpd.auth.login.unlink.expression.identityRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.unlink.expression.identityIdRequest.{suffix}", RequestProperties.IdentityId));
    private static BaseModuleValue<BaseRecordId<AuthUserLoginRecordV1>> LoginId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthUserLoginRecordV1>(
            $"hpd.auth.login.unlink.expression.loginRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.unlink.expression.loginIdRequest.{suffix}", RequestProperties.LoginId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
            $"hpd.auth.login.unlink.expression.userRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.unlink.expression.userIdRequest.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture IdentityRecord() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        IdentityCapture, IdentityId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRecordCapture Login() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        LoginCapture, LoginId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGenerationRecord() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        UserGenerationCapture, "hpd.auth.user-state-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
            "hpd.auth.login.unlink.expression.generationKey.000",
            BaseModuleMutationTemplateBuilder.Request("hpd.auth.login.unlink.expression.generationUserId.000", RequestProperties.UserId)),
        BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleFieldEqualsGuard IdentityProvider() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.login.unlink.guard.identityProvider", IdentityCapture, AuthUserIdentityRecordV1.Fields.Provider.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Captured("hpd.auth.login.unlink.expression.loginProvider.000", LoginCapture,
            AuthUserLoginRecordV1.Fields.LoginProvider.ModuleMutation));
    private static BaseModuleRevisionEqualsGuard IdentityRevision() => RevisionGuard(
        "identityRevision", IdentityCapture, RequestProperties.ExpectedIdentityRevision);
    private static BaseModuleFieldEqualsGuard IdentityTenant() => TenantGuard(
        "identityTenant", IdentityCapture, AuthUserIdentityRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard IdentityUser() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.login.unlink.guard.identityUser", IdentityCapture, AuthUserIdentityRecordV1.Fields.UserId.ModuleMutation, UserId("identityGuard"));
    private static BaseModuleRevisionEqualsGuard LoginRevision() => RevisionGuard(
        "loginRevision", LoginCapture, RequestProperties.ExpectedLoginRevision);
    private static BaseModuleFieldEqualsGuard LoginTenant() => TenantGuard(
        "loginTenant", LoginCapture, AuthUserLoginRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard LoginUser() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.login.unlink.guard.loginUser", LoginCapture, AuthUserLoginRecordV1.Fields.UserId.ModuleMutation, UserId("loginGuard"));
    private static BaseModuleRevisionEqualsGuard UserRevision() => RevisionGuard(
        "userRevision", UserCapture, RequestProperties.ExpectedUserRevision);
    private static BaseModuleFieldEqualsGuard UserTenant() => TenantGuard(
        "userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleRevisionEqualsGuard RevisionGuard(string name, string capture,
        BaseModuleRequestProperty<AuthLoginUnlinkV1, RevisionToken> property) => BaseModuleMutationTemplateBuilder.RevisionEquals(
        $"hpd.auth.login.unlink.guard.{name}", capture,
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.unlink.expression.{name}.000", property));
    private static BaseModuleFieldEqualsGuard TenantGuard<TRecord>(string name, string capture,
        BaseModuleCapturedField<TRecord, Guid> field) => BaseModuleMutationTemplateBuilder.FieldEquals(
        $"hpd.auth.login.unlink.guard.{name}", capture, field,
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.login.unlink.expression.{name}.000", RequestProperties.TenantId));
}
