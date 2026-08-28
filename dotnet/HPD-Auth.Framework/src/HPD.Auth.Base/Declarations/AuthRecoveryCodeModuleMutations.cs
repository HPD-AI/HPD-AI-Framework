using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.recovery-codes.replace.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRecoveryCodesReplaceV1), typeof(AuthRecoveryCodeMutationResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security")]
internal static partial class AuthRecoveryCodesReplaceOperationV1
{
    private const int CohortSize = 64;
    private const string SecurityCapture = "hpd.auth.recovery-codes.replace.capture.securityGen";
    private const string UserCapture = "hpd.auth.recovery-codes.replace.capture.user";
    private const string UserGenerationCapture = "hpd.auth.recovery-codes.replace.capture.userGen";
    private const string PatchStatement = "hpd.auth.recovery-codes.replace.statement.128.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = CreateDefinition();

    private static BaseRegisteredModuleMutationDefinition CreateDefinition()
    {
        BaseModuleGuard[] slotGuards =
        [
            .. Enumerable.Range(0, CohortSize).Select(NewActiveGuard),
        ];
        BaseModuleGuard newCountGuard = BaseModuleMutationTemplateBuilder.ValueCompare("hpd.auth.recovery-codes.replace.guard.newCountPositive",
            BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.newCountPositiveLeft.000", RequestProperties.NewCount),
            BaseModuleOrderedComparisonKind.GreaterThan,
            BaseModuleMutationTemplateBuilder.Constant("hpd.auth.recovery-codes.replace.expression.newCountPositiveRight.000", RequestProperties.NewCount.ConstantAuthority, 0));
        BaseModuleGuard[] remainingSlotGuards =
        [
            .. Enumerable.Range(0, CohortSize).Select(NewDigestSentinelGuard),
            .. Enumerable.Range(0, CohortSize).Select(index => CountEnabled("new", index)),
            .. Enumerable.Range(0, CohortSize).Select(NewIdSentinelGuard),
            .. Enumerable.Range(0, CohortSize).Select(NewInactiveGuard),
            .. Enumerable.Range(0, CohortSize).Select(NewKeySentinelGuard),
            .. Enumerable.Range(0, CohortSize).Select(PriorActiveGuard),
            .. Enumerable.Range(0, CohortSize).Select(index => CountEnabled("prior", index)),
            .. Enumerable.Range(0, CohortSize).Select(PriorIdSentinelGuard),
            .. Enumerable.Range(0, CohortSize).Select(PriorInactiveGuard),
        ];
        BaseModuleGuard[] authorityGuards = [UserActive(), UserNotDeleted(), UserRevision(), UserTenant()];
        BaseModuleGuard[] setGuards =
        [
            BaseModuleMutationTemplateBuilder.Disjoint("hpd.auth.recovery-codes.replace.guard.setDisjoint", NewSet("disjointNew"), PriorSet("disjointPrior")),
            BaseModuleMutationTemplateBuilder.StrictlyIncreasing("hpd.auth.recovery-codes.replace.guard.setNewOrdered", NewSet("orderedNew")),
            BaseModuleMutationTemplateBuilder.StrictlyIncreasing("hpd.auth.recovery-codes.replace.guard.setPriorOrdered", PriorSet("orderedPrior")),
        ];
        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.recovery-codes.replace.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.user.security", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-recovery-codes-replace-v1.v1", ResultTypeId = "hpd.auth.type.auth-recovery-code-mutation-result-v1.v1",
            SystemCollectionIds = [AuthRecoveryCodeRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
            SystemSourceGrants =
            [
                new BaseModuleSystemSourceGrant { CollectionId = AuthRecoveryCodeRecordV1.Collection.Id, GrantId = "auth.identity.secret.twoFactor" },
                new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
            ],
            GenerationCellIds = ["hpd.auth.user-security-generation.v1", "hpd.auth.user-state-generation.v1"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures =
                [
                    .. Enumerable.Range(0, CohortSize).Select(NewCapture),
                    .. Enumerable.Range(0, CohortSize).Select(PriorCapture),
                    SecurityGeneration(), User(), UserGeneration(),
                ],
                Guards = [.. slotGuards, newCountGuard, .. remainingSlotGuards, .. setGuards, .. authorityGuards],
                Preconditions =
                [
                    BaseModuleMutationTemplateBuilder.Precondition("hpd.auth.recovery-codes.replace.precondition.newCountPositive", "hpd.auth.recovery-codes.replace.guard.newCountPositive", "auth.recoveryCodes.empty"),
                    BaseModuleMutationTemplateBuilder.Precondition("hpd.auth.recovery-codes.replace.precondition.setDisjoint", "hpd.auth.recovery-codes.replace.guard.setDisjoint", "auth.recoveryCodes.overlap"),
                    BaseModuleMutationTemplateBuilder.Precondition("hpd.auth.recovery-codes.replace.precondition.setNewOrdered", "hpd.auth.recovery-codes.replace.guard.setNewOrdered", "auth.recoveryCodes.unordered"),
                    BaseModuleMutationTemplateBuilder.Precondition("hpd.auth.recovery-codes.replace.precondition.setPriorOrdered", "hpd.auth.recovery-codes.replace.guard.setPriorOrdered", "auth.recoveryCodes.unordered"),
                ],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        Require("userActive", "auth.user.inactive"),
                        Require("userNotDeleted", "auth.user.deleted"), Require("userRevision", "auth.user.revisionMismatch"), Require("userTenant", "auth.user.scopeMismatch"),
                        NewBranch(0), PriorBranch(0), PatchUser(),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration("hpd.auth.recovery-codes.replace.statement.129.incrementSecurityGeneration", SecurityCapture, false),
                        BaseModuleMutationTemplateBuilder.IncrementGeneration("hpd.auth.recovery-codes.replace.statement.130.incrementUserGeneration", UserGenerationCapture, false),
                    ],
                },
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject("hpd.auth.recovery-codes.replace.expression.result.000",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration, BaseModuleMutationTemplateBuilder.ResultingGeneration("hpd.auth.recovery-codes.replace.expression.resultSecurityGeneration.000", SecurityCapture)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserRevision, BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.recovery-codes.replace.expression.resultUserRevision.000", PatchStatement)))),
            },
            Limits = ReplacementLimits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(), Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });
    }

    private static string N(int index) => index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    private static string Enabled(string cohort, int index) => $"hpd.auth.recovery-codes.replace.guard.{cohort}Enabled.{N(index)}";
    private static BaseModuleValue<int> Count(string cohort, int index) => BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.{cohort}Count.{N(index)}", cohort == "new" ? RequestProperties.NewCount : RequestProperties.PriorCount);
    private static BaseModuleGuard CountEnabled(string cohort, int index) => BaseModuleMutationTemplateBuilder.ValueCompare(Enabled(cohort, index), Count(cohort, index), BaseModuleOrderedComparisonKind.GreaterThan, BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.{cohort}Index.{N(index)}", (cohort == "new" ? RequestProperties.NewCount : RequestProperties.PriorCount).ConstantAuthority, index));
    private static BaseModuleValue<string> NewId(int index, string usage) => BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newId.{usage}.{N(index)}", NewIdProperty(index));
    private static BaseModuleValue<string> PriorId(int index, string usage) => BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.priorId.{usage}.{N(index)}", PriorIdProperty(index));
    private static BaseModuleValue<BaseRecordId<AuthRecoveryCodeRecordV1>> NewRecordId(int index, string usage) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRecoveryCodeRecordV1>($"hpd.auth.recovery-codes.replace.expression.newRecordId.{usage}.{N(index)}", NewId(index, usage));
    private static BaseModuleValue<BaseRecordId<AuthRecoveryCodeRecordV1>> PriorRecordId(int index, string usage) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRecoveryCodeRecordV1>($"hpd.auth.recovery-codes.replace.expression.priorRecordId.{usage}.{N(index)}", PriorId(index, usage));
    private static BaseModuleRecordCapture NewCapture(int index) => BaseModuleMutationTemplateBuilder.CaptureRecord($"hpd.auth.recovery-codes.replace.capture.new.{N(index)}", NewRecordId(index, "capture"), BaseModuleCapturePresence.RequireMissing, Enabled("new", index));
    private static BaseModuleRecordCapture PriorCapture(int index) => BaseModuleMutationTemplateBuilder.CaptureRecord($"hpd.auth.recovery-codes.replace.capture.prior.{N(index)}", PriorRecordId(index, "capture"), BaseModuleCapturePresence.RequirePresent, Enabled("prior", index));

    private static BaseModuleValueEqualsGuard NewActiveGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.newActive.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newActiveLeft.{N(index)}", NewActiveProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.newActiveRight.{N(index)}", NewActiveProperty(index).ConstantAuthority, true));
    private static BaseModuleValueEqualsGuard NewInactiveGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.newInactive.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newInactiveLeft.{N(index)}", NewActiveProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.newInactiveRight.{N(index)}", NewActiveProperty(index).ConstantAuthority, false));
    private static BaseModuleValueEqualsGuard NewDigestSentinelGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.newDigestSentinel.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newDigestSentinelLeft.{N(index)}", NewDigestProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.newDigestSentinelRight.{N(index)}", NewDigestProperty(index).ConstantAuthority, BaseBinary.From([])));
    private static BaseModuleValueEqualsGuard NewIdSentinelGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.newIdSentinel.{N(index)}", NewId(index, "sentinelLeft"), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.newIdSentinelRight.{N(index)}", NewIdProperty(index).ConstantAuthority, new string('0', 64)));
    private static BaseModuleValueEqualsGuard NewKeySentinelGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.newKeySentinel.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newKeySentinelLeft.{N(index)}", NewKeyProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.newKeySentinelRight.{N(index)}", NewKeyProperty(index).ConstantAuthority, 1));
    private static BaseModuleValueEqualsGuard PriorActiveGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.priorActive.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.priorActiveLeft.{N(index)}", PriorActiveProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.priorActiveRight.{N(index)}", PriorActiveProperty(index).ConstantAuthority, true));
    private static BaseModuleValueEqualsGuard PriorInactiveGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.priorInactive.{N(index)}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.priorInactiveLeft.{N(index)}", PriorActiveProperty(index)), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.priorInactiveRight.{N(index)}", PriorActiveProperty(index).ConstantAuthority, false));
    private static BaseModuleValueEqualsGuard PriorIdSentinelGuard(int index) => BaseModuleMutationTemplateBuilder.ValueEquals($"hpd.auth.recovery-codes.replace.guard.priorIdSentinel.{N(index)}", PriorId(index, "sentinelLeft"), BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.priorIdSentinelRight.{N(index)}", PriorIdProperty(index).ConstantAuthority, new string('0', 64)));
    private static BaseModuleStaticValueSet<string> NewSet(string usage) => BaseModuleMutationTemplateBuilder.ValueSet($"hpd.auth.recovery-codes.replace.set.{usage}", [.. Enumerable.Range(0, CohortSize).Select(index => BaseModuleMutationTemplateBuilder.ValueMember($"hpd.auth.recovery-codes.replace.member.{usage}.{N(index)}", NewId(index, usage), Enabled("new", index)))]);
    private static BaseModuleStaticValueSet<string> PriorSet(string usage) => BaseModuleMutationTemplateBuilder.ValueSet($"hpd.auth.recovery-codes.replace.set.{usage}", [.. Enumerable.Range(0, CohortSize).Select(index => BaseModuleMutationTemplateBuilder.ValueMember($"hpd.auth.recovery-codes.replace.member.{usage}.{N(index)}", PriorId(index, usage), Enabled("prior", index)))]);

    private static BaseModuleIfStatement NewBranch(int index) => BaseModuleMutationTemplateBuilder.If($"hpd.auth.recovery-codes.replace.statement.newBranch.{N(index)}", Enabled("new", index), new BaseModuleMutationBlock { Statements = index + 1 == CohortSize ? [SlotRequire("newActive", index), NewCreate(index)] : [SlotRequire("newActive", index), NewCreate(index), NewBranch(index + 1)] }, new BaseModuleMutationBlock { Statements = [SlotRequire("newDigestSentinel", index), SlotRequire("newIdSentinel", index), SlotRequire("newInactive", index), SlotRequire("newKeySentinel", index)] });
    private static BaseModuleIfStatement PriorBranch(int index) => BaseModuleMutationTemplateBuilder.If($"hpd.auth.recovery-codes.replace.statement.priorBranch.{N(index)}", Enabled("prior", index), new BaseModuleMutationBlock { Statements = index + 1 == CohortSize ? [SlotRequire("priorActive", index), PriorDelete(index)] : [SlotRequire("priorActive", index), PriorDelete(index), PriorBranch(index + 1)] }, new BaseModuleMutationBlock { Statements = [SlotRequire("priorIdSentinel", index), SlotRequire("priorInactive", index)] });
    private static BaseModuleRequireStatement SlotRequire(string guard, int index) => BaseModuleMutationTemplateBuilder.Require($"hpd.auth.recovery-codes.replace.require.{guard}.{N(index)}", $"hpd.auth.recovery-codes.replace.guard.{guard}.{N(index)}", "auth.recoveryCodes.slotInvalid");
    private static BaseModuleCreateStatement NewCreate(int index) => BaseModuleMutationTemplateBuilder.Create($"hpd.auth.recovery-codes.replace.statement.newCreate.{N(index)}", NewRecordId(index, "create"), BaseModuleMutationTemplateBuilder.Object<AuthRecoveryCodeRecordV1>($"hpd.auth.recovery-codes.replace.expression.newPayload.{N(index)}",
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.CodeDigest, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newDigest.{N(index)}", NewDigestProperty(index))),
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.CreatedAt, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newCreatedAt.{N(index)}", RequestProperties.OperationTime)),
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.DigestKeyVersion, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newKeyVersion.{N(index)}", NewKeyProperty(index))),
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.Id, NewId(index, "payload")),
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.TenantId, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.newTenant.{N(index)}", RequestProperties.TenantId)),
        BaseModuleMutationTemplateBuilder.Field(AuthRecoveryCodeRecordV1.Fields.UserId, UserId($"newPayload.{N(index)}"))));
    private static BaseModuleDeleteStatement PriorDelete(int index) => BaseModuleMutationTemplateBuilder.Delete<AuthRecoveryCodeRecordV1>($"hpd.auth.recovery-codes.replace.statement.priorDelete.{N(index)}", PriorRecordId(index, "delete"));

    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture, "hpd.auth.user-security-generation.v1", GenerationKey("security"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture UserGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(UserGenerationCapture, "hpd.auth.user-state-generation.v1", GenerationKey("user"), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>($"hpd.auth.recovery-codes.replace.expression.userId.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleGenerationKey GenerationKey(string suffix) => BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid($"hpd.auth.recovery-codes.replace.expression.generationKey.{suffix}", BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-codes.replace.expression.generationUserId.{suffix}", RequestProperties.UserId));
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.recovery-codes.replace.guard.userRevision", UserCapture, BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.userRevision.000", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.recovery-codes.replace.guard.userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation, BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.userTenant.000", RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.recovery-codes.replace.guard.{suffix}", UserCapture, field, BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-codes.replace.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require($"hpd.auth.recovery-codes.replace.require.{suffix}", $"hpd.auth.recovery-codes.replace.guard.{suffix}", requirement);
    private static BaseModulePatchStatement PatchUser() => BaseModuleMutationTemplateBuilder.Patch(PatchStatement, UserId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>("hpd.auth.recovery-codes.replace.expression.userPatch.000",
        BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.ConcurrencyStamp, BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.concurrencyStamp.000", RequestProperties.ConcurrencyStamp)),
        BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.SecurityStamp, BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.securityStamp.000", RequestProperties.SecurityStamp)),
        BaseModuleMutationTemplateBuilder.Field(AuthUserRecordV1.Fields.UpdatedAt, BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.updatedAt.000", RequestProperties.OperationTime))), BaseModuleMutationTemplateBuilder.Request("hpd.auth.recovery-codes.replace.expression.patchRevision.000", RequestProperties.ExpectedUserRevision));

    private static BaseModuleMutationLimits ReplacementLimits() => AuthModuleMutationDefaults.Limits() with
    {
        MaximumCaptures = 131, MaximumRecordCaptures = 129, MaximumRelationTargetCaptures = 64, MaximumGenerationCaptures = 2,
        MaximumRecordMutations = 129, MaximumGenerationReads = 2, MaximumGenerationComparisons = 2, MaximumGenerationIncrements = 2,
        MaximumGuardNodes = 700, MaximumGuardDepth = 8, MaximumStatements = 780, MaximumBranches = 128, MaximumExpressionNodes = 2048,
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 8192, MaximumStaticSetMembers = 256, MaximumStaticSetComparisons = 4224,
        MaximumDisabledCaptures = 128, MaximumReadIntervals = 256, MaximumSubjectValidations = 1, MaximumAuthorityReads = 256,
        MaximumRelationChecks = 128, MaximumUniqueConstraintChecks = 128, MaximumRequestBytes = 1_048_576, MaximumSelectedBytes = 16_777_216,
        MaximumGenerationBytes = 1_048_576, MaximumEvidenceBytes = 16_777_216, MaximumWrittenBytes = 16_777_216,
        MaximumFactBytes = 16_777_216, MaximumJournalBytes = 16_777_216, MaximumReceiptBytes = 16_777_216,
        MaximumResultBytes = 65_536, MaximumTransientBytes = 32_000_000,
        Deadlines = new BaseAtomicMutationDeadlines { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30), CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30) },
    };

    // Generated scalar handles are selected through closed switches; no reflection or dynamic path exists.
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, bool> NewActiveProperty(int index);
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, BaseBinary> NewDigestProperty(int index);
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, int> NewKeyProperty(int index);
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, string> NewIdProperty(int index);
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, bool> PriorActiveProperty(int index);
    private static partial BaseModuleRequestProperty<AuthRecoveryCodesReplaceV1, string> PriorIdProperty(int index);
}

[BaseRegisteredModuleMutation("hpd.auth.recovery-code.consume.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRecoveryCodeConsumeV1), typeof(AuthRecoveryCodeMutationResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.user.security")]
internal static partial class AuthRecoveryCodeConsumeOperationV1
{
    private const string CodeCapture = "hpd.auth.recovery-code.consume.capture.code";
    private const string SecurityCapture = "hpd.auth.recovery-code.consume.capture.securityGen";
    private const string UserCapture = "hpd.auth.recovery-code.consume.capture.user";
    private const string PatchStatement = "hpd.auth.recovery-code.consume.statement.001.patchUser";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
    {
        Id = "hpd.auth.recovery-code.consume.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
        GrantId = "auth.operation.user.security", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.auth.type.auth-recovery-code-consume-v1.v1", ResultTypeId = "hpd.auth.type.auth-recovery-code-mutation-result-v1.v1",
        SystemCollectionIds = [AuthRecoveryCodeRecordV1.Collection.Id, AuthUserRecordV1.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant { CollectionId = AuthRecoveryCodeRecordV1.Collection.Id, GrantId = "auth.identity.secret.twoFactor" },
            new BaseModuleSystemSourceGrant { CollectionId = AuthUserRecordV1.Collection.Id, GrantId = "auth.identity.mutate" },
        ],
        GenerationCellIds = ["hpd.auth.user-security-generation.v1"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [Code(), SecurityGeneration(), User()],
            Guards = [CodeDigest(), CodeRevision(), CodeTenant(), CodeUser(), UserActive(), UserNotDeleted(), UserRevision(), UserTenant()], Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    Require("codeDigest", "auth.recoveryCode.invalid"), Require("codeRevision", "auth.recoveryCode.invalid"),
                    Require("codeTenant", "auth.recoveryCode.invalid"), Require("codeUser", "auth.recoveryCode.invalid"),
                    Require("userActive", "auth.user.inactive"),
                    Require("userNotDeleted", "auth.user.deleted"), Require("userRevision", "auth.user.revisionMismatch"),
                    Require("userTenant", "auth.user.scopeMismatch"),
                    BaseModuleMutationTemplateBuilder.Delete<AuthRecoveryCodeRecordV1>("hpd.auth.recovery-code.consume.statement.000.deleteCode", CodeId("delete"), Req("deleteRevision", RequestProperties.ExpectedCodeRevision)),
                    PatchUser(), BaseModuleMutationTemplateBuilder.IncrementGeneration("hpd.auth.recovery-code.consume.statement.002.incrementSecurityGeneration", SecurityCapture, false),
                ],
            },
            Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject("hpd.auth.recovery-code.consume.expression.result.000",
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.SecurityGeneration, BaseModuleMutationTemplateBuilder.ResultingGeneration("hpd.auth.recovery-code.consume.expression.resultSecurityGeneration.000", SecurityCapture)),
                BaseModuleMutationTemplateBuilder.Property(ResultProperties.UserRevision, BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.recovery-code.consume.expression.resultUserRevision.000", PatchStatement)))),
        },
        Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(), Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleValue<BaseRecordId<AuthRecoveryCodeRecordV1>> CodeId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthRecoveryCodeRecordV1>($"hpd.auth.recovery-code.consume.expression.codeId.{suffix}", Req($"codeIdSource.{suffix}", RequestProperties.CodeId));
    private static BaseModuleValue<BaseRecordId<AuthUserRecordV1>> UserId(string suffix) => BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthUserRecordV1>($"hpd.auth.recovery-code.consume.expression.userId.{suffix}", Req($"userIdSource.{suffix}", RequestProperties.UserId));
    private static BaseModuleRecordCapture Code() => BaseModuleMutationTemplateBuilder.CaptureRecord(CodeCapture, CodeId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleGenerationCapture SecurityGeneration() => BaseModuleMutationTemplateBuilder.CaptureGeneration(SecurityCapture, "hpd.auth.user-security-generation.v1", BaseModuleMutationTemplateBuilder.GenerationKeyFromGuid("hpd.auth.recovery-code.consume.expression.generationKey.000", Req("generationUserId", RequestProperties.UserId)), BaseModuleGenerationAbsenceBehavior.RequireExisting);
    private static BaseModuleRecordCapture User() => BaseModuleMutationTemplateBuilder.CaptureRecord(UserCapture, UserId("capture"), BaseModuleCapturePresence.RequirePresent);
    private static BaseModuleFieldEqualsGuard CodeDigest() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.recovery-code.consume.guard.codeDigest", CodeCapture, AuthRecoveryCodeRecordV1.Fields.CodeDigest.ModuleMutation, Req("codeDigest", RequestProperties.CodeDigest));
    private static BaseModuleRevisionEqualsGuard CodeRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.recovery-code.consume.guard.codeRevision", CodeCapture, Req("codeRevision", RequestProperties.ExpectedCodeRevision));
    private static BaseModuleFieldEqualsGuard CodeTenant() => Tenant("codeTenant", CodeCapture, AuthRecoveryCodeRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard CodeUser() => BaseModuleMutationTemplateBuilder.FieldEquals("hpd.auth.recovery-code.consume.guard.codeUser", CodeCapture, AuthRecoveryCodeRecordV1.Fields.UserId.ModuleMutation, UserId("codeGuard"));
    private static BaseModuleFieldEqualsGuard UserActive() => UserBoolean("userActive", AuthUserRecordV1.Fields.IsActive.ModuleMutation, AuthUserRecordV1.Fields.IsActive.ConstantAuthority, true);
    private static BaseModuleFieldEqualsGuard UserNotDeleted() => UserBoolean("userNotDeleted", AuthUserRecordV1.Fields.IsDeleted.ModuleMutation, AuthUserRecordV1.Fields.IsDeleted.ConstantAuthority, false);
    private static BaseModuleRevisionEqualsGuard UserRevision() => BaseModuleMutationTemplateBuilder.RevisionEquals("hpd.auth.recovery-code.consume.guard.userRevision", UserCapture, Req("userRevision", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldEqualsGuard UserTenant() => Tenant("userTenant", UserCapture, AuthUserRecordV1.Fields.TenantId.ModuleMutation);
    private static BaseModuleFieldEqualsGuard Tenant<T>(string suffix, string capture, BaseModuleCapturedField<T, Guid> field) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.recovery-code.consume.guard.{suffix}", capture, field, Req(suffix, RequestProperties.TenantId));
    private static BaseModuleFieldEqualsGuard UserBoolean(string suffix, BaseModuleCapturedField<AuthUserRecordV1, bool> field, BaseModuleConstantAuthority<bool> authority, bool value) => BaseModuleMutationTemplateBuilder.FieldEquals($"hpd.auth.recovery-code.consume.guard.{suffix}", UserCapture, field, BaseModuleMutationTemplateBuilder.Constant($"hpd.auth.recovery-code.consume.expression.{suffix}.000", authority, value));
    private static BaseModuleRequireStatement Require(string suffix, string requirement) => BaseModuleMutationTemplateBuilder.Require($"hpd.auth.recovery-code.consume.require.{suffix}", $"hpd.auth.recovery-code.consume.guard.{suffix}", requirement);
    private static BaseModulePatchStatement PatchUser() => BaseModuleMutationTemplateBuilder.Patch(PatchStatement, UserId("patch"), BaseModuleMutationTemplateBuilder.Object<AuthUserRecordV1>("hpd.auth.recovery-code.consume.expression.userPatch.000",
        UserField(AuthUserRecordV1.Fields.ConcurrencyStamp, RequestProperties.ConcurrencyStamp, "concurrencyStamp"), UserField(AuthUserRecordV1.Fields.SecurityStamp, RequestProperties.SecurityStamp, "securityStamp"), UserField(AuthUserRecordV1.Fields.UpdatedAt, RequestProperties.OperationTime, "updatedAt")), Req("patchRevision", RequestProperties.ExpectedUserRevision));
    private static BaseModuleFieldValue<AuthUserRecordV1> UserField<T>(BaseField<AuthUserRecordV1, T> field, BaseModuleRequestProperty<AuthRecoveryCodeConsumeV1, T> property, string suffix) => BaseModuleMutationTemplateBuilder.Field(field, Req(suffix, property));
    private static BaseModuleValue<T> Req<T>(string suffix, BaseModuleRequestProperty<AuthRecoveryCodeConsumeV1, T> property) => BaseModuleMutationTemplateBuilder.Request($"hpd.auth.recovery-code.consume.expression.{suffix}.000", property);
}
