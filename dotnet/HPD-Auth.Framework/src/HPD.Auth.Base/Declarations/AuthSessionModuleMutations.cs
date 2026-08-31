using HPD.Base;

namespace HPD.Auth.Base;

#pragma warning disable CS8620

[BaseRegisteredModuleMutation("hpd.auth.session.create.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthSessionCreateV1), typeof(AuthSessionCreateResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.session.mutate")]
internal static partial class AuthSessionCreateOperationV1
{
    private const string SessionCapture = "hpd.auth.session.create.capture.session";
    private const string SecurityCapture = "hpd.auth.session.create.capture.securityGen";
    private const string UserCapture = "hpd.auth.session.create.capture.user";
    private const string CreateStatement = "hpd.auth.session.create.statement.000.create";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.session.create.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.session.mutate", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-session-create-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-session-create-result-v1.v1",
            SystemCollectionIds = [AuthSessionRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthSessionRecordV1.Collection.Id, GrantId = "auth.session.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.read" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), Session(), User()],
                Guards = [UserActive(), UserNotDeleted(), UserRevision(), UserTenant()],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("userActive", "auth.user.inactive"),
                        Require("userNotDeleted", "auth.user.deleted"),
                        Require("userRevision", "auth.user.revisionMismatch"),
                        Require("userTenant", "auth.user.scopeMismatch"),
                        Create(),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    "hpd.auth.session.create.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.session.create.expression.resultRevision.000", CreateStatement)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                        BaseModuleMutationTemplateBuilder.CapturedGeneration("hpd.auth.session.create.expression.resultSecurityGeneration.000", SecurityCapture)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SessionId,
                        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.create.expression.resultSessionId.000", RequestProperties.SessionId)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthSessionRecordV1>> SessionId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthSessionRecordV1>($"hpd.auth.session.create.expression.sessionId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.create.expression.sessionIdSource.{suffix}", RequestProperties.SessionId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>($"hpd.auth.session.create.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.create.expression.userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey() => BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
        "hpd.auth.session.create.expression.generationKey.000",
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.create.expression.generationUserId.000", RequestProperties.UserId));
    private static BaseModuleRecordCapture Session() => BaseModuleMutationTemplateBuilder.CaptureRecord(SessionCapture, SessionId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture,
        "hpd.auth.user-security-generation.v1", GenerationKey(), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals(
        "hpd.auth.session.create.guard.userRevision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.create.expression.expectedUserRevision.000", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.session.create.guard.userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.create.expression.userTenant.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field,
        BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals(
            $"hpd.auth.session.create.guard.{suffix}", UserCapture, field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.session.create.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require(
        $"hpd.auth.session.create.require.{suffix}", $"hpd.auth.session.create.guard.{suffix}", requirement);

    private static BaseModuleCreateStatement Create() => BaseModuleMutationTemplateBuilder.Create(CreateStatement, SessionId("create"),
        BaseModuleMutationTemplateBuilder.Object<AuthSessionRecordV1>("hpd.auth.session.create.expression.payload.000",
            Field(AuthSessionRecordV1.Fields.Aal, RequestProperties.Aal, "aal"),
            Field(AuthSessionRecordV1.Fields.BrokerSessionId, RequestProperties.BrokerSessionId, "brokerSessionId"),
            Field(AuthSessionRecordV1.Fields.BrokerUserId, RequestProperties.BrokerUserId, "brokerUserId"),
            Field(AuthSessionRecordV1.Fields.ClientSessions, RequestProperties.ClientSessions, "clientSessions"),
            Field(AuthSessionRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "createdAt"),
            Field(AuthSessionRecordV1.Fields.DeviceInfo, RequestProperties.DeviceInfo, "deviceInfo"),
            Field(AuthSessionRecordV1.Fields.ExpiresAt, RequestProperties.ExpiresAt, "expiresAt"),
            Field(AuthSessionRecordV1.Fields.Id, RequestProperties.SessionId, "id"),
            Field(AuthSessionRecordV1.Fields.IpAddress, RequestProperties.IpAddress, "ipAddress"),
            Field(AuthSessionRecordV1.Fields.LastActiveAt, RequestProperties.LastActiveAt, "lastActiveAt"),
            Field(AuthSessionRecordV1.Fields.NotAfter, RequestProperties.NotAfter, "notAfter"),
            Field(AuthSessionRecordV1.Fields.NotBefore, RequestProperties.NotBefore, "notBefore"),
            Field(AuthSessionRecordV1.Fields.OauthClientId, RequestProperties.OauthClientId, "oauthClientId"),
            Field(AuthSessionRecordV1.Fields.RetentionEligibleAt, RequestProperties.RetentionEligibleAt, "retentionEligibleAt"),
            Field(AuthSessionRecordV1.Fields.Revoked, RequestProperties.Revoked, "revoked"),
            Field(AuthSessionRecordV1.Fields.RevokedAt, RequestProperties.RevokedAt, "revokedAt"),
            Field(AuthSessionRecordV1.Fields.Scopes, RequestProperties.Scopes, "scopes"),
            BaseModuleMutationTemplateBuilder.Field(AuthSessionRecordV1.Fields.SecurityGeneration,
                BaseModuleMutationTemplateBuilder.CapturedGeneration("hpd.auth.session.create.expression.securityGeneration.000", SecurityCapture)),
            Field(AuthSessionRecordV1.Fields.SsoProviderId, RequestProperties.SsoProviderId, "ssoProviderId"),
            Field(AuthSessionRecordV1.Fields.State, RequestProperties.State, "state"),
            Field(AuthSessionRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
            Field(AuthSessionRecordV1.Fields.UserAgent, RequestProperties.UserAgent, "userAgent"),
            BaseModuleMutationTemplateBuilder.Field(AuthSessionRecordV1.Fields.UserId, UserId("payload"))));
    private static BaseModuleFieldValue<AuthSessionRecordV1> Field<T>(BaseField<AuthSessionRecordV1, T> field,
        BaseModuleRequestProperty<AuthSessionCreateV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.create.expression.{suffix}.000", property));
}

[BaseRegisteredModuleMutation("hpd.auth.session.touch.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthSessionTouchV1), typeof(AuthSessionTouchResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.session.mutate")]
internal static partial class AuthSessionTouchOperationV1
{
    private const string SessionCapture = "hpd.auth.session.touch.capture.session";
    private const string SecurityCapture = "hpd.auth.session.touch.capture.securityGen";
    private const string UserCapture = "hpd.auth.session.touch.capture.user";
    private const string PatchStatement = "hpd.auth.session.touch.statement.000.patch";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.session.touch.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.session.mutate", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-session-touch-v1.v1", ResultTypeId = "hpd.auth.type.auth-session-touch-result-v1.v1",
            SystemCollectionIds = [AuthSessionRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthSessionRecordV1.Collection.Id, GrantId = "auth.session.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.read" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [SecurityGeneration(), Session(), User()],
                Guards = [ActivityMonotonic(), ActivityNotFuture(), SessionActive(), SessionGeneration(), SessionRevision(), SessionSsoProvider(), SessionTenant(), SessionUnrevoked(), SessionUser(), UserActive(), UserNotDeleted(), UserRevision(), UserTenant()],
                Preconditions = [BaseModuleMutationTemplateBuilder.Precondition(
                    "hpd.auth.session.touch.precondition.activityNotFuture",
                    "hpd.auth.session.touch.guard.activityNotFuture",
                    "auth.session.activityInFuture")],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("activityMonotonic", "auth.session.activityRegression"),
                        Require("sessionActive", "auth.session.inactive"), Require("sessionGeneration", "auth.credential.generationMismatch"),
                        Require("sessionRevision", "auth.session.revisionMismatch"), Require("sessionSsoProvider", "auth.session.scopeMismatch"),
                        Require("sessionTenant", "auth.session.scopeMismatch"),
                        Require("sessionUser", "auth.session.scopeMismatch"), Require("sessionUnrevoked", "auth.session.revoked"),
                        Require("userActive", "auth.user.inactive"), Require("userNotDeleted", "auth.user.deleted"),
                        Require("userRevision", "auth.user.revisionMismatch"), Require("userTenant", "auth.user.scopeMismatch"), Patch(),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    "hpd.auth.session.touch.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.session.touch.expression.resultRevision.000", PatchStatement)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SessionId,
                        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.resultSessionId.000", RequestProperties.SessionId)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthSessionRecordV1>> SessionId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthSessionRecordV1>(
        $"hpd.auth.session.touch.expression.sessionId.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.touch.expression.sessionIdSource.{suffix}", RequestProperties.SessionId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
        $"hpd.auth.session.touch.expression.userId.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.touch.expression.userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Session() => BaseModuleMutationTemplateBuilder.CaptureRecord(SessionCapture, SessionId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture, "hpd.auth.user-security-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("hpd.auth.session.touch.expression.generationKey.000", BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.generationUserId.000", RequestProperties.UserId)), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleFieldComparisonGuard ActivityMonotonic() => BaseModuleMutationTemplateBuilder.FieldCompare("hpd.auth.session.touch.guard.activityMonotonic", SessionCapture,
        AuthSessionRecordV1.Fields.LastActiveAt.ModuleMutation, BaseModuleOrderedComparisonKind.LessThanOrEqual,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.lastActiveAt.000", RequestProperties.LastActiveAt));
    private static BaseModuleValueComparisonGuard ActivityNotFuture() => BaseModuleMutationTemplateBuilder.ValueCompare("hpd.auth.session.touch.guard.activityNotFuture",
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.activityTime.000", RequestProperties.LastActiveAt), BaseModuleOrderedComparisonKind.LessThanOrEqual,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.operationTime.000", RequestProperties.OperationTime));
    private static BaseModuleFieldEqualsGuard SessionActive() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.session.touch.guard.sessionActive", SessionCapture,
        AuthSessionRecordV1.Fields.State.ModuleMutation, BaseModuleMutationTemplateBuilder.Constant("hpd.auth.session.touch.expression.active.000", AuthSessionRecordV1.Fields.State.ConstantAuthority, AuthSessionStateV1.active));
    private static BaseModuleFieldEqualsGuard SessionGeneration() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.session.touch.guard.sessionGeneration", SessionCapture,
        AuthSessionRecordV1.Fields.SecurityGeneration.ModuleMutation, BaseModuleMutationTemplateBuilder.CapturedGeneration("hpd.auth.session.touch.expression.sessionGeneration.000", SecurityCapture));
    private static BaseModuleRevisionEqualsGuard SessionRevision() => Revision("sessionRevision", SessionCapture, RequestProperties.ExpectedSessionRevision);
    private static BaseModuleFieldEqualsGuard SessionSsoProvider() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.session.touch.guard.sessionSsoProvider", SessionCapture, AuthSessionRecordV1.Fields.SsoProviderId.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.sessionSsoProvider.000", RequestProperties.SsoProviderId));
    private static BaseModuleFieldEqualsGuard SessionTenant() => Tenant("sessionTenant", SessionCapture, AuthSessionRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard SessionUser() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.session.touch.guard.sessionUser", SessionCapture, AuthSessionRecordV1.Fields.UserId.ModuleMutation, UserId("sessionGuard"));
    private static BaseModuleFieldEqualsGuard SessionUnrevoked() => Boolean("sessionUnrevoked", SessionCapture, AuthSessionRecordV1.Fields.Revoked.ModuleMutation, AuthSessionRecordV1.Fields.Revoked.ConstantAuthority, false);
    private static BaseModuleFieldEqualsGuard UserActive() => Boolean("userActive", UserCapture, AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => Boolean("userNotDeleted", UserCapture, AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => Revision("userRevision", UserCapture, RequestProperties.ExpectedUserRevision);
    private static BaseModuleFieldEqualsGuard UserTenant() => Tenant("userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleRevisionEqualsGuard Revision<T>(string suffix, string capture, BaseModuleRequestProperty<AuthSessionTouchV1, T> property) where T : notnull =>
        BaseModuleMutationTemplateBuilder.RevisionEquals($"hpd.auth.session.touch.guard.{suffix}", capture,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.touch.expression.{suffix}.000", property) as BaseModuleValue<RevisionToken>
                ?? throw new InvalidOperationException("Revision authority mismatch."));
    private static BaseModuleFieldEqualsGuard Tenant<TRecord>(string suffix, string capture, BaseModuleCapturedField<TRecord, Guid> field) =>
        BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.session.touch.guard.{suffix}", capture, field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.touch.expression.{suffix}.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard Boolean<TRecord>(string suffix, string capture, BaseModuleCapturedField<TRecord, bool> field,
        BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.session.touch.guard.{suffix}", capture, field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.session.touch.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require(
        $"hpd.auth.session.touch.require.{suffix}", $"hpd.auth.session.touch.guard.{suffix}", requirement);
    private static BaseModulePatchStatement Patch() => BaseModuleMutationTemplateBuilder.Patch(PatchStatement, SessionId("patch"),
        BaseModuleMutationTemplateBuilder.Object<AuthSessionRecordV1>("hpd.auth.session.touch.expression.patch.000",
            Field(AuthSessionRecordV1.Fields.DeviceInfo, RequestProperties.DeviceInfo, "deviceInfo"),
            Field(AuthSessionRecordV1.Fields.IpAddress, RequestProperties.IpAddress, "ipAddress"),
            Field(AuthSessionRecordV1.Fields.LastActiveAt, RequestProperties.LastActiveAt, "lastActiveAt"),
            Field(AuthSessionRecordV1.Fields.SsoProviderId, RequestProperties.SsoProviderId, "ssoProviderId"),
            Field(AuthSessionRecordV1.Fields.UserAgent, RequestProperties.UserAgent, "userAgent"),
            BaseModuleMutationTemplateBuilder.Field(
                AuthSessionRecordV1.Fields.UserId,
                UserId("patchRelation"))),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.session.touch.expression.patchRevision.000", RequestProperties.ExpectedSessionRevision));
    private static BaseModuleFieldValue<AuthSessionRecordV1> Field<T>(BaseField<AuthSessionRecordV1, T> field,
        BaseModuleRequestProperty<AuthSessionTouchV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.session.touch.expression.{suffix}.000", property));
}
