using HPD.Base;

namespace HPD.Auth.Base;

#pragma warning disable CS8620

[BaseRegisteredModuleMutation("hpd.auth.refresh.issue.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRefreshIssueV1), typeof(AuthRefreshIssueResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.refresh.issue")]
internal static partial class AuthRefreshIssueOperationV1
{
    private const string DeliveryCapture = "hpd.auth.refresh.issue.capture.delivery";
    private const string RefreshCapture = "hpd.auth.refresh.issue.capture.refresh";
    private const string SecurityCapture = "hpd.auth.refresh.issue.capture.securityGen";
    private const string UserCapture = "hpd.auth.refresh.issue.capture.user";
    private const string CreateDeliveryStatement = "hpd.auth.refresh.issue.statement.001.createDelivery";
    private const string CreateRefreshStatement = "hpd.auth.refresh.issue.statement.000.createRefresh";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.refresh.issue.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.refresh.issue", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-refresh-issue-v1.v1", ResultTypeId = "hpd.auth.type.auth-refresh-issue-result-v1.v1",
            SystemCollectionIds = [AuthRefreshTokenDeliveryRecordV1.Collection.Id, AuthRefreshTokenRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthRefreshTokenDeliveryRecordV1.Collection.Id, GrantId = "auth.token.delivery" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthRefreshTokenRecordV1.Collection.Id, GrantId = "auth.token.mutate" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.read" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Delivery(), Refresh(), SecurityGeneration(), User()],
                Guards = [DigestAlgorithmIsHmac(), SecurityGenerationMatches(), UserActive(), UserNotDeleted(), UserRevision(), UserTenant()], Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("digestAlgorithm", "auth.refresh.digestAlgorithmInvalid"), Require("securityGeneration", "auth.credential.generationMismatch"), Require("userActive", "auth.user.inactive"),
                        Require("userNotDeleted", "auth.user.deleted"), Require("userRevision", "auth.user.revisionMismatch"),
                        Require("userTenant", "auth.user.scopeMismatch"), CreateRefresh(), CreateDelivery(),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    "hpd.auth.refresh.issue.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.DeliveryId, Req("resultDeliveryId", RequestProperties.DeliveryId)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.DeliveryRevision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.refresh.issue.expression.deliveryRevision.000", CreateDeliveryStatement)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.RefreshTokenId, Req("resultRefreshTokenId", RequestProperties.RefreshTokenId)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.RefreshTokenRevision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.refresh.issue.expression.refreshRevision.000", CreateRefreshStatement)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration, Req("resultSecurityGeneration", RequestProperties.ExpectedSecurityGeneration)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthRefreshTokenDeliveryRecordV1>> DeliveryId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRefreshTokenDeliveryRecordV1>(
        $"hpd.auth.refresh.issue.expression.deliveryId.{suffix}", Req($"deliveryIdSource.{suffix}", RequestProperties.DeliveryId));
    private static BaseModuleValue<BaseRecordId<AuthRefreshTokenRecordV1>> RefreshId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRefreshTokenRecordV1>(
        $"hpd.auth.refresh.issue.expression.refreshId.{suffix}", Req($"refreshIdSource.{suffix}", RequestProperties.RefreshTokenId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
        $"hpd.auth.refresh.issue.expression.userId.{suffix}", Req($"userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Delivery() => BaseModuleMutationTemplateBuilder.CaptureRecord(DeliveryCapture, DeliveryId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleRecordCapture Refresh() => BaseModuleMutationTemplateBuilder.CaptureRecord(RefreshCapture, RefreshId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture, "hpd.auth.user-security-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("hpd.auth.refresh.issue.expression.generationKey.000", Req("generationUserId", RequestProperties.UserId)), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationGuard SecurityGenerationMatches() => BaseModuleMutationTemplateBuilder.Generation("hpd.auth.refresh.issue.guard.securityGeneration", SecurityCapture,
        BaseModuleGenerationComparisonKind.MustEqual, Req("expectedSecurityGeneration", RequestProperties.ExpectedSecurityGeneration));
    private static BaseModuleValueEqualsGuard DigestAlgorithmIsHmac() => BaseModuleMutationTemplateBuilder.ValueEquals(
        "hpd.auth.refresh.issue.guard.digestAlgorithm", Req("digestAlgorithmGuardLeft", RequestProperties.DigestAlgorithm),
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.issue.expression.digestAlgorithmGuardRight.000", RequestProperties.DigestAlgorithm.ConstantAuthority, AuthRefreshDigestAlgorithmV1.HmacSha256V1));
    private static BaseModuleFieldEqualsGuard UserActive() => Boolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => Boolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.refresh.issue.guard.userRevision", UserCapture, Req("expectedUserRevision", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.refresh.issue.guard.userTenant", UserCapture,
        AuthUserRecordV1.Fields.TenantId.ModuleMutation, Req("userTenant", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard Boolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field,
        BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.refresh.issue.guard.{suffix}", UserCapture, field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.refresh.issue.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require(
        $"hpd.auth.refresh.issue.require.{suffix}", $"hpd.auth.refresh.issue.guard.{suffix}", requirement);

    private static BaseModuleCreateStatement CreateRefresh() => BaseModuleMutationTemplateBuilder.Create(CreateRefreshStatement, RefreshId("create"),
        BaseModuleMutationTemplateBuilder.Object<AuthRefreshTokenRecordV1>("hpd.auth.refresh.issue.expression.refreshPayload.000",
            RefreshField(AuthRefreshTokenRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "createdAt"),
            RefreshField(AuthRefreshTokenRecordV1.Fields.DigestAlgorithm, RequestProperties.DigestAlgorithm, "digestAlgorithm"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion,
                BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.issue.expression.digestKeyVersion.000", AuthRefreshTokenRecordV1.Fields.DigestKeyVersion.ModuleMutation, Req("digestKeyVersionSource", RequestProperties.DigestKeyVersion))),
            RefreshField(AuthRefreshTokenRecordV1.Fields.ExpiresAt, RequestProperties.ExpiresAt, "expiresAt"),
            RefreshField(AuthRefreshTokenRecordV1.Fields.Id, RequestProperties.RefreshTokenId, "id"),
            RefreshField(AuthRefreshTokenRecordV1.Fields.JwtId, RequestProperties.JwtId, "jwtId"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.Revoked,
                BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.issue.expression.revoked.000", AuthRefreshTokenRecordV1.Fields.Revoked.ConstantAuthority, false)),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.SecurityGeneration,
                BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.issue.expression.securityGeneration.000", AuthRefreshTokenRecordV1.Fields.SecurityGeneration.ModuleMutation, Req("securityGenerationSource", RequestProperties.ExpectedSecurityGeneration))),
            RefreshField(AuthRefreshTokenRecordV1.Fields.SecurityStampDigest, RequestProperties.SecurityStampDigest, "securityStampDigest"),
            RefreshField(AuthRefreshTokenRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
            RefreshField(AuthRefreshTokenRecordV1.Fields.TokenDigest, RequestProperties.TokenDigest, "tokenDigest"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.Used,
                BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.issue.expression.used.000", AuthRefreshTokenRecordV1.Fields.Used.ConstantAuthority, false)),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.UserId, UserId("refreshPayload"))));
    private static BaseModuleCreateStatement CreateDelivery() => BaseModuleMutationTemplateBuilder.Create(CreateDeliveryStatement, DeliveryId("create"),
        BaseModuleMutationTemplateBuilder.Object<AuthRefreshTokenDeliveryRecordV1>("hpd.auth.refresh.issue.expression.deliveryPayload.000",
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "deliveryCreatedAt"),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.ExpiresAt, RequestProperties.DeliveryExpiresAt, "deliveryExpiresAt"),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.Id, RequestProperties.DeliveryId, "deliveryId"),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectedToken, RequestProperties.ProtectedToken, "protectedToken"),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectorVersion, RequestProperties.ProtectorVersion, "protectorVersion"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ReplacementId, RefreshId("deliveryReplacement")),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.RequestScopeDigest, RequestProperties.RequestScopeDigest, "requestScopeDigest"),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.SecurityGeneration, RequestProperties.ExpectedSecurityGeneration, "deliverySecurityGeneration"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.State,
                BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.issue.expression.deliveryState.000", AuthRefreshTokenDeliveryRecordV1.Fields.State.ConstantAuthority, AuthRefreshDeliveryStateV1.available)),
            DeliveryField(AuthRefreshTokenDeliveryRecordV1.Fields.TenantId, RequestProperties.TenantId, "deliveryTenantId"),
            BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.UserId, UserId("deliveryUser"))));
    private static BaseModuleFieldValue<AuthRefreshTokenRecordV1> RefreshField<T>(BaseField<AuthRefreshTokenRecordV1, T> field,
        BaseModuleRequestProperty<AuthRefreshIssueV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field, Req(suffix, property));
    private static BaseModuleFieldValue<AuthRefreshTokenDeliveryRecordV1> DeliveryField<T>(BaseField<AuthRefreshTokenDeliveryRecordV1, T> field,
        BaseModuleRequestProperty<AuthRefreshIssueV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field, Req(suffix, property));
    private static BaseModuleValue<T> Req<T>(string suffix, BaseModuleRequestProperty<AuthRefreshIssueV1, T> property) =>
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.refresh.issue.expression.{suffix}.000", property);
}

[BaseRegisteredModuleMutation("hpd.auth.refresh.rotate.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRefreshRotateV1), typeof(AuthRefreshRotateResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.refresh.rotate")]
internal static partial class AuthRefreshRotateOperationV1
{
    private const string DeliveryCapture = "hpd.auth.refresh.rotate.capture.delivery";
    private const string PredecessorCapture = "hpd.auth.refresh.rotate.capture.predecessor";
    private const string RefreshCapture = "hpd.auth.refresh.rotate.capture.refresh";
    private const string SecurityCapture = "hpd.auth.refresh.rotate.capture.securityGen";
    private const string UserCapture = "hpd.auth.refresh.rotate.capture.user";
    private const string PatchPredecessorStatement = "hpd.auth.refresh.rotate.statement.000.patchPredecessor";
    private const string CreateRefreshStatement = "hpd.auth.refresh.rotate.statement.001.createRefresh";
    private const string CreateDeliveryStatement = "hpd.auth.refresh.rotate.statement.002.createDelivery";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
    {
        Id = "hpd.auth.refresh.rotate.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
        GrantId = "auth.operation.refresh.rotate", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.auth.type.auth-refresh-rotate-v1.v1", ResultTypeId = "hpd.auth.type.auth-refresh-rotate-result-v1.v1",
        SystemCollectionIds = [AuthRefreshTokenDeliveryRecordV1.Collection.Id, AuthRefreshTokenRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant { CollectionId = AuthRefreshTokenDeliveryRecordV1.Collection.Id, GrantId = "auth.token.delivery" },
            new BaseModuleSystemSourceGrant { CollectionId = AuthRefreshTokenRecordV1.Collection.Id, GrantId = "auth.token.mutate" },
            new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.read" },
        ],
        GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [Delivery(), Predecessor(), Refresh(), SecurityGeneration(), User()],
            Guards =
            [
                DigestAlgorithmIsHmac(), PredecessorGeneration(), PredecessorGenerationMatches(), PredecessorGenerationMissing(),
                PredecessorRevision(), PredecessorSecurityStamp(), PredecessorTenant(), PredecessorUnexpired(), PredecessorUnrevoked(),
                PredecessorUnused(), PredecessorUser(), SecurityGenerationMatches(), UserActive(), UserNotDeleted(), UserRevision(), UserTenant(),
            ],
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    Require("digestAlgorithm", "auth.refresh.digestAlgorithmInvalid"), Require("predecessorGeneration", "auth.credential.generationMismatch"),
                    Require("predecessorRevision", "auth.refresh.revisionMismatch"), Require("predecessorSecurityStamp", "auth.refresh.invalid"),
                    Require("predecessorTenant", "auth.refresh.invalid"), Require("predecessorUnexpired", "auth.refresh.expired"),
                    Require("predecessorUnrevoked", "auth.refresh.revoked"), Require("predecessorUnused", "auth.refresh.used"),
                    Require("predecessorUser", "auth.refresh.invalid"), Require("securityGeneration", "auth.credential.generationMismatch"),
                    Require("userActive", "auth.user.inactive"), Require("userNotDeleted", "auth.user.deleted"),
                    Require("userRevision", "auth.user.revisionMismatch"), Require("userTenant", "auth.user.scopeMismatch"),
                    PatchPredecessor(), CreateRefresh(), CreateDelivery(),
                ],
            },
            Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                "hpd.auth.refresh.rotate.expression.result.000",
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.DeliveryId, Req("resultDeliveryId", RequestProperties.DeliveryId)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.DeliveryRevision, BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.refresh.rotate.expression.deliveryRevision.000", CreateDeliveryStatement)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.PredecessorRevision, BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.refresh.rotate.expression.predecessorRevision.000", PatchPredecessorStatement)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.RefreshTokenId, Req("resultRefreshTokenId", RequestProperties.RefreshTokenId)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.RefreshTokenRevision, BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.refresh.rotate.expression.refreshRevision.000", CreateRefreshStatement)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration, Req("resultSecurityGeneration", RequestProperties.ExpectedSecurityGeneration)))),
        },
        Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleValue<BaseRecordId<AuthRefreshTokenDeliveryRecordV1>> DeliveryId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRefreshTokenDeliveryRecordV1>($"hpd.auth.refresh.rotate.expression.deliveryId.{suffix}", Req($"deliveryIdSource.{suffix}", RequestProperties.DeliveryId));
    private static BaseModuleValue<BaseRecordId<AuthRefreshTokenRecordV1>> PredecessorId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRefreshTokenRecordV1>($"hpd.auth.refresh.rotate.expression.predecessorId.{suffix}", Req($"predecessorIdSource.{suffix}", RequestProperties.PredecessorId));
    private static BaseModuleValue<BaseRecordId<AuthRefreshTokenRecordV1>> RefreshId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRefreshTokenRecordV1>($"hpd.auth.refresh.rotate.expression.refreshId.{suffix}", Req($"refreshIdSource.{suffix}", RequestProperties.RefreshTokenId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>($"hpd.auth.refresh.rotate.expression.userId.{suffix}", Req($"userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Delivery() => BaseModuleMutationTemplateBuilder.CaptureRecord(DeliveryCapture, DeliveryId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleRecordCapture Predecessor() => BaseModuleMutationTemplateBuilder.CaptureRecord(PredecessorCapture, PredecessorId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleRecordCapture Refresh() => BaseModuleMutationTemplateBuilder.CaptureRecord(RefreshCapture, RefreshId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture, "hpd.auth.user-security-generation.v1", BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("hpd.auth.refresh.rotate.expression.generationKey.000", Req("generationUserId", RequestProperties.UserId)), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleValueEqualsGuard DigestAlgorithmIsHmac() => BaseModuleMutationTemplateBuilder.ValueEquals("hpd.auth.refresh.rotate.guard.digestAlgorithm", Req("digestAlgorithmLeft", RequestProperties.DigestAlgorithm), BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.rotate.expression.digestAlgorithmRight.000", RequestProperties.DigestAlgorithm.ConstantAuthority, AuthRefreshDigestAlgorithmV1.HmacSha256V1));
    private static BaseModuleLogicalGuard PredecessorGeneration() => BaseModuleMutationTemplateBuilder.Or("hpd.auth.refresh.rotate.guard.predecessorGeneration", "hpd.auth.refresh.rotate.guard.predecessorGenerationMatches", "hpd.auth.refresh.rotate.guard.predecessorGenerationMissing");
    private static BaseModuleFieldEqualsGuard PredecessorGenerationMatches() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.refresh.rotate.guard.predecessorGenerationMatches", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.SecurityGeneration.ModuleMutation, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.predecessorGeneration.000", AuthRefreshTokenRecordV1.Fields.SecurityGeneration.ModuleMutation, Req("predecessorGenerationSource", RequestProperties.ExpectedSecurityGeneration)));
    private static BaseModuleFieldPresenceGuard PredecessorGenerationMissing() => BaseModuleMutationTemplateBuilder.FieldPresence("hpd.auth.refresh.rotate.guard.predecessorGenerationMissing", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.SecurityGeneration.ModuleMutation, BaseModuleFieldPresenceTest.Missing);
    private static BaseModuleRevisionEqualsGuard PredecessorRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.refresh.rotate.guard.predecessorRevision", PredecessorCapture, Req("expectedPredecessorRevision", RequestProperties.ExpectedPredecessorRevision));
    private static BaseModuleFieldEqualsGuard PredecessorSecurityStamp() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.refresh.rotate.guard.predecessorSecurityStamp", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.SecurityStampDigest.ModuleMutation, Req("expectedSecurityStamp", RequestProperties.ExpectedSecurityStampDigest));
    private static BaseModuleFieldEqualsGuard PredecessorTenant() => Tenant("predecessorTenant", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldComparisonGuard PredecessorUnexpired() => BaseModuleMutationTemplateBuilder.FieldCompare("hpd.auth.refresh.rotate.guard.predecessorUnexpired", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.ExpiresAt.ModuleMutation, BaseModuleOrderedComparisonKind.GreaterThan, Req("operationTime", RequestProperties.OperationTime));
    private static BaseModuleFieldEqualsGuard PredecessorUnrevoked() => RefreshBoolean("predecessorUnrevoked", AuthRefreshTokenRecordV1.Fields.Revoked.ModuleMutation, AuthRefreshTokenRecordV1.Fields.Revoked.ConstantAuthority, false);
    private static BaseModuleFieldEqualsGuard PredecessorUnused() => RefreshBoolean("predecessorUnused", AuthRefreshTokenRecordV1.Fields.Used.ModuleMutation, AuthRefreshTokenRecordV1.Fields.Used.ConstantAuthority, false);
    private static BaseModuleFieldEqualsGuard PredecessorUser() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.refresh.rotate.guard.predecessorUser", PredecessorCapture, AuthRefreshTokenRecordV1.Fields.UserId.ModuleMutation, UserId("predecessorGuard"));
    private static BaseModuleGenerationGuard SecurityGenerationMatches() => BaseModuleMutationTemplateBuilder.Generation("hpd.auth.refresh.rotate.guard.securityGeneration", SecurityCapture, BaseModuleGenerationComparisonKind.MustEqual, Req("expectedSecurityGeneration", RequestProperties.ExpectedSecurityGeneration));
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.refresh.rotate.guard.userRevision", UserCapture, Req("expectedUserRevision", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => Tenant("userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard Tenant<T>(string suffix, string capture, BaseModuleCapturedField<T, Guid> field) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.refresh.rotate.guard.{suffix}", capture, field, Req(suffix, RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard RefreshBoolean(string suffix, BaseModuleCapturedField<AuthRefreshTokenRecordV1, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.refresh.rotate.guard.{suffix}", PredecessorCapture, field, BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.refresh.rotate.expression.{suffix}.000", authority, value));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.refresh.rotate.guard.{suffix}", UserCapture, field, BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.refresh.rotate.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require($"hpd.auth.refresh.rotate.require.{suffix}", $"hpd.auth.refresh.rotate.guard.{suffix}", requirement);

    private static BaseModulePatchStatement PatchPredecessor() => BaseModuleMutationTemplateBuilder.Patch(PatchPredecessorStatement, PredecessorId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthRefreshTokenRecordV1>("hpd.auth.refresh.rotate.expression.predecessorPatch.000",
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.ReplacementId, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.replacementId.000", AuthRefreshTokenRecordV1.Fields.ReplacementId.ModuleMutation, RefreshId("replacement"))),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.RetentionEligibleAt, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.retentionEligibleAt.000", AuthRefreshTokenRecordV1.Fields.RetentionEligibleAt.ModuleMutation, Req("retentionEligibleAtSource", RequestProperties.RetentionEligibleAt))),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.Used, BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.rotate.expression.used.000", AuthRefreshTokenRecordV1.Fields.Used.ConstantAuthority, true)),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.UsedAt, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.usedAt.000", AuthRefreshTokenRecordV1.Fields.UsedAt.ModuleMutation, Req("usedAtSource", RequestProperties.OperationTime)))), Req("patchRevision", RequestProperties.ExpectedPredecessorRevision));
    private static BaseModuleCreateStatement CreateRefresh() => BaseModuleMutationTemplateBuilder.Create(CreateRefreshStatement, RefreshId("create"), BaseModuleMutationTemplateBuilder.Object<AuthRefreshTokenRecordV1>("hpd.auth.refresh.rotate.expression.refreshPayload.000",
        RF(AuthRefreshTokenRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "createdAt"), RF(AuthRefreshTokenRecordV1.Fields.DigestAlgorithm, RequestProperties.DigestAlgorithm, "digestAlgorithm"),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.DigestKeyVersion, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.digestKeyVersion.000", AuthRefreshTokenRecordV1.Fields.DigestKeyVersion.ModuleMutation, Req("digestKeyVersionSource", RequestProperties.DigestKeyVersion))),
        RF(AuthRefreshTokenRecordV1.Fields.ExpiresAt, RequestProperties.ExpiresAt, "expiresAt"), RF(AuthRefreshTokenRecordV1.Fields.Id, RequestProperties.RefreshTokenId, "id"), RF(AuthRefreshTokenRecordV1.Fields.JwtId, RequestProperties.JwtId, "jwtId"),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.Revoked, BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.rotate.expression.revoked.000", AuthRefreshTokenRecordV1.Fields.Revoked.ConstantAuthority, false)),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.SecurityGeneration, BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.refresh.rotate.expression.securityGeneration.000", AuthRefreshTokenRecordV1.Fields.SecurityGeneration.ModuleMutation, Req("securityGenerationSource", RequestProperties.ExpectedSecurityGeneration))),
        RF(AuthRefreshTokenRecordV1.Fields.SecurityStampDigest, RequestProperties.SecurityStampDigest, "securityStampDigest"), RF(AuthRefreshTokenRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"), RF(AuthRefreshTokenRecordV1.Fields.TokenDigest, RequestProperties.TokenDigest, "tokenDigest"),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.Used, BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.rotate.expression.newUsed.000", AuthRefreshTokenRecordV1.Fields.Used.ConstantAuthority, false)), BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenRecordV1.Fields.UserId, UserId("refreshPayload"))));
    private static BaseModuleCreateStatement CreateDelivery() => BaseModuleMutationTemplateBuilder.Create(CreateDeliveryStatement, DeliveryId("create"), BaseModuleMutationTemplateBuilder.Object<AuthRefreshTokenDeliveryRecordV1>("hpd.auth.refresh.rotate.expression.deliveryPayload.000",
        DF(AuthRefreshTokenDeliveryRecordV1.Fields.CreatedAt, RequestProperties.CreatedAt, "deliveryCreatedAt"), DF(AuthRefreshTokenDeliveryRecordV1.Fields.ExpiresAt, RequestProperties.DeliveryExpiresAt, "deliveryExpiresAt"), DF(AuthRefreshTokenDeliveryRecordV1.Fields.Id, RequestProperties.DeliveryId, "deliveryId"), DF(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectedToken, RequestProperties.ProtectedToken, "protectedToken"), DF(AuthRefreshTokenDeliveryRecordV1.Fields.ProtectorVersion, RequestProperties.ProtectorVersion, "protectorVersion"),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.ReplacementId, RefreshId("deliveryReplacement")), DF(AuthRefreshTokenDeliveryRecordV1.Fields.RequestScopeDigest, RequestProperties.RequestScopeDigest, "requestScopeDigest"), DF(AuthRefreshTokenDeliveryRecordV1.Fields.SecurityGeneration, RequestProperties.ExpectedSecurityGeneration, "deliverySecurityGeneration"),
        BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.State, BaseModuleMutationTemplateBuilder.Constant("hpd.auth.refresh.rotate.expression.deliveryState.000", AuthRefreshTokenDeliveryRecordV1.Fields.State.ConstantAuthority, AuthRefreshDeliveryStateV1.available)), DF(AuthRefreshTokenDeliveryRecordV1.Fields.TenantId, RequestProperties.TenantId, "deliveryTenantId"), BaseModuleMutationTemplateBuilder.Field(AuthRefreshTokenDeliveryRecordV1.Fields.UserId, UserId("deliveryUser"))));
    private static BaseModuleFieldValue<AuthRefreshTokenRecordV1> RF<T>(BaseField<AuthRefreshTokenRecordV1, T> field, BaseModuleRequestProperty<AuthRefreshRotateV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field, Req(suffix, property));
    private static BaseModuleFieldValue<AuthRefreshTokenDeliveryRecordV1> DF<T>(BaseField<AuthRefreshTokenDeliveryRecordV1, T> field, BaseModuleRequestProperty<AuthRefreshRotateV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field, Req(suffix, property));
    private static BaseModuleValue<T> Req<T>(string suffix, BaseModuleRequestProperty<AuthRefreshRotateV1, T> property) => BaseModuleMutationTemplateBuilder.Request($"hpd.auth.refresh.rotate.expression.{suffix}.000", property);
}
