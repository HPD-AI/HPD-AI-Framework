using HPD.Base;

namespace HPD.Auth.Base;

// Generated optional/non-null Base authorities intentionally use nullable CLR annotations.
#pragma warning disable CS8620

[BaseRegisteredModuleMutation("hpd.auth.passkey.register.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthPasskeyRegisterV1), typeof(AuthPasskeyRegisterResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.passkey.mutate")]
internal static partial class AuthPasskeyRegisterOperationV1
{
    private const string PasskeyCapture = "hpd.auth.passkey.register.capture.passkey";
    private const string SecurityGenerationCapture = "hpd.auth.passkey.register.capture.securityGen";
    private const string UserCapture = "hpd.auth.passkey.register.capture.user";
    private const string UserGenerationCapture = "hpd.auth.passkey.register.capture.userGen";
    private const string CreateStatement = "hpd.auth.passkey.register.statement.000.createPasskey";
    private const string PatchStatement = "hpd.auth.passkey.register.statement.001.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.passkey.register.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.passkey.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-passkey-register-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-passkey-register-result-v1.v1",
            SystemCollectionIds = [AuthPasskeyRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthPasskeyRecordV1.Collection.Id, GrantId = "auth.identity.secret.passkey" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Passkey(), SecurityGeneration(), User(), UserGeneration()],
                Guards = [SecurityGenerationMatches(), UserActive(), UserGenerationMatches(), UserNotDeleted(), UserRevision(), UserTenant()],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("securityGeneration", "auth.credential.generationMismatch"),
                        Require("userActive", "auth.user.inactive"),
                        Require("userGeneration", "auth.user.generationMismatch"),
                        Require("userNotDeleted", "auth.user.deleted"),
                        Require("userRevision", "auth.user.revisionMismatch"),
                        Require("userTenant", "auth.user.scopeMismatch"),
                        CreatePasskey(), PatchUser(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration("hpd.auth.passkey.register.statement.002.incrementSecurityGeneration", SecurityGenerationCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration("hpd.auth.passkey.register.statement.003.incrementUserGeneration", UserGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    "hpd.auth.passkey.register.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.PasskeyId,
                        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.register.expression.resultPasskeyId.000", RequestProperties.PasskeyId)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.PasskeyRevision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.passkey.register.expression.passkeyRevision.000", CreateStatement)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration,
                        BaseModuleMutationTemplateBuilder.ResultingGeneration("hpd.auth.passkey.register.expression.securityGeneration.000", SecurityGenerationCapture)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserGeneration,
                        BaseModuleMutationTemplateBuilder.ResultingGeneration("hpd.auth.passkey.register.expression.userGeneration.000", UserGenerationCapture)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserRevision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.passkey.register.expression.userRevision.000", PatchStatement)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthPasskeyRecordV1>> PasskeyId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthPasskeyRecordV1>(
        $"hpd.auth.passkey.register.expression.passkeyId.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.passkeyIdSource.{suffix}", RequestProperties.PasskeyId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>(
        $"hpd.auth.passkey.register.expression.userId.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(string suffix) => BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid(
        $"hpd.auth.passkey.register.expression.generationKey.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.generationUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Passkey() => BaseModuleMutationTemplateBuilder.CaptureRecord(PasskeyCapture, PasskeyId("capture"), BaseModuleCapturePresence.RequireMissing);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityGenerationCapture,
        "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(UserGenerationCapture,
        "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleGenerationGuard SecurityGenerationMatches() => Generation("securityGeneration", SecurityGenerationCapture, RequestProperties.ExpectedSecurityGeneration);
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleGenerationGuard UserGenerationMatches() => Generation("userGeneration", UserGenerationCapture, RequestProperties.ExpectedUserGeneration);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.passkey.register.guard.userRevision", UserCapture,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.register.expression.expectedUserRevision.000", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.passkey.register.guard.userTenant", UserCapture,
        AuthUserRecordV1.Fields.TenantId.ModuleMutation, BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.register.expression.userTenant.000", RequestProperties.TenantId));

    private static BaseModuleCreateStatement CreatePasskey() => BaseModuleMutationTemplateBuilder.Create(CreateStatement, PasskeyId("create"),
        BaseModuleMutationTemplateBuilder.Object<AuthPasskeyRecordV1>("hpd.auth.passkey.register.expression.passkey.000",
            PasskeyField(AuthPasskeyRecordV1.Fields.AaGuid, RequestProperties.AaGuid, "aaGuid"),
            PasskeyField(AuthPasskeyRecordV1.Fields.CreatedAt, RequestProperties.OperationTime, "createdAt"),
            PasskeyField(AuthPasskeyRecordV1.Fields.CredentialDigest, RequestProperties.CredentialDigest, "credentialDigest"),
            PasskeyField(AuthPasskeyRecordV1.Fields.CredentialId, RequestProperties.CredentialId, "credentialId"),
            PasskeyField(AuthPasskeyRecordV1.Fields.Id, RequestProperties.PasskeyId, "id"),
            PasskeyField(AuthPasskeyRecordV1.Fields.IsDiscoverable, RequestProperties.IsDiscoverable, "isDiscoverable"),
            PasskeyField(AuthPasskeyRecordV1.Fields.Name, RequestProperties.Name, "name"),
            PasskeyField(AuthPasskeyRecordV1.Fields.PublicKey, RequestProperties.PublicKey, "publicKey"),
            PasskeyField(AuthPasskeyRecordV1.Fields.SignatureCounter, RequestProperties.SignatureCounter, "signatureCounter"),
            PasskeyField(AuthPasskeyRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
            BaseModuleMutationTemplateBuilder.Field(AuthPasskeyRecordV1.Fields.UserId, UserId("payload")),
            PasskeyField(AuthPasskeyRecordV1.Fields.UserVerified, RequestProperties.UserVerified, "userVerified")));
    private static BaseModulePatchStatement PatchUser() => BaseModuleMutationTemplateBuilder.Patch(PatchStatement, UserId("patch"),
        BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>("hpd.auth.passkey.register.expression.userPatch.000",
            UserField(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"),
            UserField(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"),
            UserField(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.register.expression.userPatchRevision.000", RequestProperties.ExpectedUserRevision));

    private static BaseModuleGenerationGuard Generation(string suffix, string capture, BaseModuleRequestProperty<AuthPasskeyRegisterV1, BaseModuleGeneration> property) =>
        BaseModuleMutationTemplateBuilder.Generation($"hpd.auth.passkey.register.guard.{suffix}", capture, BaseModuleGenerationComparisonKind.MustEqual,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.{suffix}.000", property));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field,
        BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.passkey.register.guard.{suffix}", UserCapture, field,
            BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.passkey.register.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require(
        $"hpd.auth.passkey.register.require.{suffix}", $"hpd.auth.passkey.register.guard.{suffix}", requirement);
    private static BaseModuleFieldValue<AuthPasskeyRecordV1> PasskeyField<T>(BaseField<AuthPasskeyRecordV1, T> field,
        BaseModuleRequestProperty<AuthPasskeyRegisterV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.{suffix}.000", property));
    private static BaseModuleFieldValue<AuthUserRecordV1> UserField<T>(BaseField<AuthUserRecordV1, T> field,
        BaseModuleRequestProperty<AuthPasskeyRegisterV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.register.expression.{suffix}.000", property));
}

[BaseRegisteredModuleMutation("hpd.auth.passkey.record-assertion.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthPasskeyRecordAssertionV1), typeof(AuthPasskeyAssertionResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.passkey.mutate")]
internal static partial class AuthPasskeyRecordAssertionOperationV1
{
    private const string PasskeyCapture = "hpd.auth.passkey.assert.capture.passkey";
    private const string SecurityGenerationCapture = "hpd.auth.passkey.assert.capture.securityGen";
    private const string UserCapture = "hpd.auth.passkey.assert.capture.user";
    private const string PatchStatement = "hpd.auth.passkey.assert.statement.000.patch";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.passkey.record-assertion.v1", Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.passkey.mutate",
            Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-passkey-record-assertion-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-passkey-assertion-result-v1.v1",
            SystemCollectionIds = [AuthPasskeyRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthPasskeyRecordV1.Collection.Id, GrantId = "auth.identity.secret.passkey" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [Passkey(), SecurityGeneration(), User()],
                Guards =
                [
                    CounterIncreases(), CounterSupported(), CounterUnsupported(), PasskeyRevision(), PasskeyTenant(), PasskeyUser(),
                    PresentedCounterZero(), SecurityGenerationMatches(), StoredCounterZero(), UserActive(), UserNotDeleted(),
                    UserRevision(), UserTenant(),
                ],
                Preconditions = [],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("passkeyRevision", "auth.passkey.revisionMismatch"),
                        Require("passkeyTenant", "auth.passkey.scopeMismatch"),
                        Require("passkeyUser", "auth.passkey.scopeMismatch"),
                        Require("securityGeneration", "auth.credential.generationMismatch"),
                        Require("userActive", "auth.user.inactive"),
                        Require("userNotDeleted", "auth.user.deleted"),
                        Require("userRevision", "auth.user.revisionMismatch"),
                        Require("userTenant", "auth.user.scopeMismatch"),
                        BaseModuleMutationTemplateBuilder.If("hpd.auth.passkey.assert.branch.counterMode", "hpd.auth.passkey.assert.guard.counterSupported",
                            new BaseModuleMutationBlock
                            {
                                Statements =
                                [
                                    BaseModuleMutationTemplateBuilder.Require("hpd.auth.passkey.assert.require.counterIncrease",
                                        "hpd.auth.passkey.assert.guard.counterIncreases", "auth.passkey.counterRegression"),
                                ],
                            },
                            new BaseModuleMutationBlock
                            {
                                Statements =
                                [
                                    BaseModuleMutationTemplateBuilder.Require("hpd.auth.passkey.assert.require.counterUnsupported",
                                        "hpd.auth.passkey.assert.guard.counterUnsupported", "auth.passkey.counterRegression"),
                                    BaseModuleMutationTemplateBuilder.Require("hpd.auth.passkey.assert.require.presentedZero",
                                        "hpd.auth.passkey.assert.guard.presentedCounterZero", "auth.passkey.counterRegression"),
                                    BaseModuleMutationTemplateBuilder.Require("hpd.auth.passkey.assert.require.storedZero",
                                        "hpd.auth.passkey.assert.guard.storedCounterZero", "auth.passkey.counterRegression"),
                                ],
                            }),
                        Patch(),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    "hpd.auth.passkey.assert.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.passkey.assert.expression.revision.000", PatchStatement)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthPasskeyRecordV1>> PasskeyId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthPasskeyRecordV1>($"hpd.auth.passkey.assert.expression.passkeyId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.passkeyIdSource.{suffix}", RequestProperties.PasskeyId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>($"hpd.auth.passkey.assert.expression.userId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Passkey() => BaseModuleMutationTemplateBuilder.CaptureRecord(PasskeyCapture, PasskeyId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(
        SecurityGenerationCapture, "hpd.auth.user-security-generation.v1",
        BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("hpd.auth.passkey.assert.expression.securityKey.000",
            BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.securityUserId.000", RequestProperties.UserId)),
        BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);

    private static BaseModuleValueEqualsGuard CounterSupported() => RequestBoolean("counterSupported", RequestProperties.CounterSupported, true);
    private static BaseModuleValueEqualsGuard CounterUnsupported() => RequestBoolean("counterUnsupported", RequestProperties.CounterSupported, false);
    private static BaseModuleFieldComparisonGuard CounterIncreases() => BaseModuleMutationTemplateBuilder.FieldCompare(
        "hpd.auth.passkey.assert.guard.counterIncreases", PasskeyCapture, AuthPasskeyRecordV1.Fields.SignatureCounter.ModuleMutation,
        BaseModuleOrderedComparisonKind.LessThan,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.presentedCounter.000", RequestProperties.PresentedCounter));
    private static BaseModuleValueEqualsGuard PresentedCounterZero() => BaseModuleMutationTemplateBuilder.ValueEquals(
        "hpd.auth.passkey.assert.guard.presentedCounterZero",
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.presentedCounterZeroLeft.000", RequestProperties.PresentedCounter),
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.passkey.assert.expression.presentedCounterZeroRight.000", RequestProperties.PresentedCounter.ConstantAuthority, 0L));
    private static BaseModuleFieldEqualsGuard StoredCounterZero() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.passkey.assert.guard.storedCounterZero", PasskeyCapture, AuthPasskeyRecordV1.Fields.SignatureCounter.ModuleMutation,
        BaseModuleMutationTemplateBuilder.Constant("hpd.auth.passkey.assert.expression.storedCounterZero.000", AuthPasskeyRecordV1.Fields.SignatureCounter.ConstantAuthority, 0L));
    private static BaseModuleRevisionEqualsGuard PasskeyRevision() => Revision("passkeyRevision", PasskeyCapture, RequestProperties.ExpectedPasskeyRevision);
    private static BaseModuleFieldEqualsGuard PasskeyTenant() => Tenant("passkeyTenant", PasskeyCapture, AuthPasskeyRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard PasskeyUser() => BaseModuleMutationTemplateBuilder.FieldEquals(
        "hpd.auth.passkey.assert.guard.passkeyUser", PasskeyCapture, AuthPasskeyRecordV1.Fields.UserId.ModuleMutation, UserId("passkeyGuard"));
    private static BaseModuleGenerationGuard SecurityGenerationMatches() => BaseModuleMutationTemplateBuilder.Generation(
        "hpd.auth.passkey.assert.guard.securityGeneration", SecurityGenerationCapture, BaseModuleGenerationComparisonKind.MustEqual,
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.expectedSecurityGeneration.000", RequestProperties.ExpectedSecurityGeneration));
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => Revision("userRevision", UserCapture, RequestProperties.ExpectedUserRevision);
    private static BaseModuleFieldEqualsGuard UserTenant() => Tenant("userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);

    private static BaseModulePatchStatement Patch() => BaseModuleMutationTemplateBuilder.Patch(PatchStatement, PasskeyId("patch"),
        BaseModuleMutationTemplateBuilder.Object<AuthPasskeyRecordV1>("hpd.auth.passkey.assert.expression.patch.000",
            BaseModuleMutationTemplateBuilder.Field(AuthPasskeyRecordV1.Fields.LastUsedAt,
                BaseModuleMutationTemplateBuilder.LiftOptional("hpd.auth.passkey.assert.expression.lastUsedAt.000",
                    AuthPasskeyRecordV1.Fields.LastUsedAt.ModuleMutation,
                    BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.lastUsedAtSource.000", RequestProperties.OperationTime))),
            Field(AuthPasskeyRecordV1.Fields.SignatureCounter, RequestProperties.PresentedCounter, "signatureCounter"),
            Field(AuthPasskeyRecordV1.Fields.UserVerified, RequestProperties.UserVerified, "userVerified")),
        BaseModuleMutationTemplateBuilder.Request("hpd.auth.passkey.assert.expression.patchRevision.000", RequestProperties.ExpectedPasskeyRevision));

    private static BaseModuleRevisionEqualsGuard Revision(string suffix, string capture, BaseModuleRequestProperty<AuthPasskeyRecordAssertionV1, RevisionToken> property) =>
        BaseModuleMutationTemplateBuilder.RevisionEquals($"hpd.auth.passkey.assert.guard.{suffix}", capture,
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.{suffix}.000", property));
    private static BaseModuleFieldEqualsGuard Tenant<TRecord>(string suffix, string capture, BaseModuleCapturedField<TRecord, Guid> field) => BaseModuleMutationTemplateBuilder.FieldEquals(
        $"hpd.auth.passkey.assert.guard.{suffix}", capture, field,
        BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.{suffix}.000", RequestProperties.TenantId));
    private static BaseModuleValueEqualsGuard RequestBoolean(string suffix, BaseModuleRequestProperty<AuthPasskeyRecordAssertionV1, bool> property, bool value) => BaseModuleMutationTemplateBuilder.ValueEquals(
        $"hpd.auth.passkey.assert.guard.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.{suffix}Left.000", property),
        BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.passkey.assert.expression.{suffix}Right.000", property.ConstantAuthority, value));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals(
        $"hpd.auth.passkey.assert.guard.{suffix}", UserCapture, field,
        BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.passkey.assert.expression.{suffix}.000", authority, value));
    private static BaseModuleFieldValue<AuthPasskeyRecordV1> Field<T>(BaseField<AuthPasskeyRecordV1, T> field,
        BaseModuleRequestProperty<AuthPasskeyRecordAssertionV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(
            field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.passkey.assert.expression.{suffix}.000", property));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require(
        $"hpd.auth.passkey.assert.require.{suffix}", $"hpd.auth.passkey.assert.guard.{suffix}", requirement);
}
