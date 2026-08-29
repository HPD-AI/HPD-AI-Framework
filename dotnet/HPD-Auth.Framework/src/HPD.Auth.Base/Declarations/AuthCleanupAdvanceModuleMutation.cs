using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.cleanup.advance.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthCleanupAdvanceV1), typeof(AuthCleanupMutationResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance")]
internal static partial class AuthCleanupAdvanceOperationV1
{
    private const string Prefix = "hpd.auth.cleanup.advance";
    private const string Capture = "cleanupWork";
    private const string PatchAll = Prefix + ".statement.patch.allComplete";
    private const string PatchPositive = Prefix + ".statement.patch.positive";
    private const string PatchRetention = Prefix + ".statement.patch.retention";
    private const string PatchZero = Prefix + ".statement.patch.zero";

    private static readonly AuthCleanupStepV1[] ZeroSteps =
    [
        AuthCleanupStepV1.revokeSessions, AuthCleanupStepV1.revokeRefreshTokens,
        AuthCleanupStepV1.deleteDeliveries, AuthCleanupStepV1.waitSecurityRetention,
        AuthCleanupStepV1.deleteSessions, AuthCleanupStepV1.deleteRefreshTokens,
        AuthCleanupStepV1.deletePasskeys, AuthCleanupStepV1.deleteUserClaims,
        AuthCleanupStepV1.deleteUserLogins, AuthCleanupStepV1.deleteUserTokens,
        AuthCleanupStepV1.deleteUserRoles, AuthCleanupStepV1.deleteUserIdentities,
        AuthCleanupStepV1.proveEmpty, AuthCleanupStepV1.deleteRoleClaims,
    ];

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.auth.cleanup.advance.v1", Version = 1,
        OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.advance",
        Audience = BaseModuleMutationAudience.System,
        RequestTypeId = "hpd.auth.type.auth-cleanup-advance-v1.v1",
        ResultTypeId = "hpd.auth.type.auth-cleanup-mutation-result-v1.v1",
        SystemCollectionIds = [AuthCleanupWorkRecordV1.Collection.Id],
        SystemSourceGrants = [new BaseModuleSystemSourceGrant
        {
            CollectionId = AuthCleanupWorkRecordV1.Collection.Id,
            GrantId = "auth.cleanup.execute",
        }],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [BaseModuleMutationTemplateBuilder.CaptureRecord(Capture, RecordId("capture"), BaseModuleCapturePresence.RequirePresent)],
            Guards = [.. Guards()],
            Preconditions = [], Body = Body(), Result = Result(),
        },
        Limits = AuthModuleMutationDefaults.Limits() with
        {
            MaximumStatements = 128,
            MaximumBranches = 32,
            MaximumExpressionNodes = 384,
            MaximumGuardNodes = 192,
            MaximumGuardDepth = 32,
        },
        ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleGuard[] Guards()
    {
        List<(string Id, BaseModuleGuard Guard)> values = [];
        void Add(string id, BaseModuleGuard guard) => values.Add((Guard(id), guard));
        Add("chunk", FieldEquals("chunk", AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ModuleMutation, Request("expectedChunk", RequestProperties.ExpectedChunkOrdinal)));
        Add("incarnation", FieldEquals("incarnation", AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation,
            BaseModuleMutationTemplateBuilder.IncarnationBytes(Prefix + ".expression.incarnation.expected",
                AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation,
                Request("expectedIncarnation", RequestProperties.ExpectedIncarnation))));
        Add("disposition.all", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("disposition.all"), Request("disposition.all", RequestProperties.ChildDisposition), Constant("disposition.all", RequestProperties.ChildDisposition.ConstantAuthority, AuthCleanupChildDispositionV1.allStepsComplete)));
        Add("disposition.positive", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("disposition.positive"), Request("disposition.positive", RequestProperties.ChildDisposition), Constant("disposition.positive", RequestProperties.ChildDisposition.ConstantAuthority, AuthCleanupChildDispositionV1.positiveCohort)));
        Add("disposition.retention", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("disposition.retention"), Request("disposition.retention", RequestProperties.ChildDisposition), Constant("disposition.retention", RequestProperties.ChildDisposition.ConstantAuthority, AuthCleanupChildDispositionV1.retentionBlocked)));
        Add("disposition.zero", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("disposition.zero"), Request("disposition.zero", RequestProperties.ChildDisposition), Constant("disposition.zero", RequestProperties.ChildDisposition.ConstantAuthority, AuthCleanupChildDispositionV1.zeroDrainProof)));
        Add("retention.missing", BaseModuleMutationTemplateBuilder.ValuePresence(Guard("retention.missing"), Request("retention.missing", RequestProperties.RetentionEligibleAt), BaseModuleFieldPresenceTest.Missing));
        Add("retention.present", BaseModuleMutationTemplateBuilder.ValuePresence(Guard("retention.present"), Request("retention.present", RequestProperties.RetentionEligibleAt), BaseModuleFieldPresenceTest.PresentValue));
        Add("revision", BaseModuleMutationTemplateBuilder.RevisionEquals(Guard("revision"), Capture, Request("expectedRevision", RequestProperties.ExpectedRevision)));
        Add("selected.positive", BaseModuleMutationTemplateBuilder.ValueCompare(Guard("selected.positive"), Request("selected.positive", RequestProperties.SelectedCount), BaseModuleOrderedComparisonKind.GreaterThan, Constant("selected.positive", RequestProperties.SelectedCount.ConstantAuthority, 0)));
        Add("selected.zero", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("selected.zero"), Request("selected.zero", RequestProperties.SelectedCount), Constant("selected.zero", RequestProperties.SelectedCount.ConstantAuthority, 0)));
        Add("state", FieldEquals("state", AuthCleanupWorkRecordV1.Fields.State.ModuleMutation, Request("expectedState", RequestProperties.ExpectedState)));
        Add("step", FieldEquals("step", AuthCleanupWorkRecordV1.Fields.Step.ModuleMutation, Request("expectedStep", RequestProperties.ExpectedStep)));
        foreach (AuthCleanupStepV1 step in Enum.GetValues<AuthCleanupStepV1>().OrderBy(static step => step.ToString(), StringComparer.Ordinal))
            Add("step." + step, BaseModuleMutationTemplateBuilder.ValueEquals(StepGuard(step), Request("step." + step, RequestProperties.ExpectedStep), Constant("step." + step, RequestProperties.ExpectedStep.ConstantAuthority, step)));
        Add("subject.user", BaseModuleMutationTemplateBuilder.FieldEquals(Guard("subject.user"), Capture, AuthCleanupWorkRecordV1.Fields.SubjectKind.ModuleMutation, Constant("subject.user", AuthCleanupWorkRecordV1.Fields.SubjectKind.ConstantAuthority, AuthCleanupSubjectKindV1.user)));
        Add("subject.role", BaseModuleMutationTemplateBuilder.FieldEquals(Guard("subject.role"), Capture, AuthCleanupWorkRecordV1.Fields.SubjectKind.ModuleMutation, Constant("subject.role", AuthCleanupWorkRecordV1.Fields.SubjectKind.ConstantAuthority, AuthCleanupSubjectKindV1.role)));
        Add("state.draining", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("state.draining"), Request("state.draining", RequestProperties.ExpectedState), Constant("state.draining", RequestProperties.ExpectedState.ConstantAuthority, AuthCleanupStateV1.draining)));
        Add("state.waiting", BaseModuleMutationTemplateBuilder.ValueEquals(Guard("state.waiting"), Request("state.waiting", RequestProperties.ExpectedState), Constant("state.waiting", RequestProperties.ExpectedState.ConstantAuthority, AuthCleanupStateV1.waitingRetention)));
        values.AddRange(ProgressGuards());
        Add("zero.allowed", BaseModuleMutationTemplateBuilder.Not(Guard("zero.allowed"), StepGuard(AuthCleanupStepV1.finalizeSubject)));
        return [.. values.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(static value => value.Guard)];
    }

    private static BaseModuleMutationBlock Body() => BaseModuleMutationTemplateBuilder.Block(
        Require("00.revision", "revision"), Require("01.state", "state"), Require("02.step", "step"), Require("03.chunk", "chunk"),
        Require("04.incarnation", "incarnation"), ProgressProof(),
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.branch.positive", Guard("disposition.positive"), Positive(),
            BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.branch.zero", Guard("disposition.zero"), Zero(),
                BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.branch.retention", Guard("disposition.retention"), Retention(), AllComplete()))))));

    private static BaseModuleMutationBlock Positive() => BaseModuleMutationTemplateBuilder.Block(
        Require("positive.00.selected", "selected.positive"), Require("positive.01.retention", "retention.missing"), Require("positive.02.state", "state.draining"),
        Patch(PatchPositive,
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.ChunkOrdinal, IncrementChunk("positive.chunk")),
            Receipt("positive"), UpdatedAt("positive")));

    private static BaseModuleMutationBlock Zero() => BaseModuleMutationTemplateBuilder.Block(
        Require("zero.00.selected", "selected.zero"), Require("zero.01.retention", "retention.missing"), Require("zero.02.allowed", "zero.allowed"),
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.zero.state", Guard("state.draining"), BaseModuleMutationTemplateBuilder.Block(Require("zero.03.state.draining", "state.draining")),
            BaseModuleMutationTemplateBuilder.Block(Require("zero.03.state", "state.waiting"))),
        Patch(PatchZero,
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.ChunkOrdinal, Constant("zero.chunk", AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ConstantAuthority, 0L)),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.CompletedSteps, CompletedAfterCurrent("zero")),
            Receipt("zero"),
            BaseModuleMutationTemplateBuilder.Remove(AuthCleanupWorkRecordV1.Fields.RetentionEligibleAt.ModuleMutation),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State, Constant("zero.state", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.draining)),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.Step, NextStep("zero")),
            UpdatedAt("zero")));

    private static BaseModuleMutationBlock Retention() => BaseModuleMutationTemplateBuilder.Block(
        Require("retention.00.selected", "selected.zero"), Require("retention.01.present", "retention.present"), Require("retention.02.step", "step.waitSecurityRetention"), Require("retention.03.state", "state.draining"),
        Patch(PatchRetention, Receipt("retention"),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.RetentionEligibleAt, Request("retention.value", RequestProperties.RetentionEligibleAt)),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State, Constant("retention.state", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.waitingRetention)),
            UpdatedAt("retention")));

    private static BaseModuleMutationBlock AllComplete() => BaseModuleMutationTemplateBuilder.Block(
        Require("all.00.disposition", "disposition.all"), Require("all.01.selected", "selected.zero"),
        Require("all.02.retention", "retention.missing"), Require("all.03.step", "step.finalizeSubject"), Require("all.04.state", "state.draining"),
        Patch(PatchAll,
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.CompletedSteps, Increment("all.completed", Captured("all.completed", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation), 1L << 13)),
            Receipt("all"),
            BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State, Constant("all.state", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.readyToPurge)),
            UpdatedAt("all")));

    private static BaseModulePatchStatement Patch(string statement, params BaseModuleFieldValue<AuthCleanupWorkRecordV1>[] fields) =>
        BaseModuleMutationTemplateBuilder.Patch(statement, RecordId("patch." + statement[(statement.LastIndexOf('.') + 1)..]),
            BaseModuleMutationTemplateBuilder.Object<AuthCleanupWorkRecordV1>(Prefix + ".expression.payload." + statement[(statement.LastIndexOf('.') + 1)..], fields),
            Request("patch.revision." + statement[(statement.LastIndexOf('.') + 1)..], RequestProperties.ExpectedRevision));

    private static BaseModuleFieldValue<AuthCleanupWorkRecordV1> Receipt(string id) => BaseModuleMutationTemplateBuilder.Field(
        AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope,
        BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.receipt.optional." + id, AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope.ModuleMutation, Request("receipt." + id, RequestProperties.ChildReceiptScope)));
    private static BaseModuleFieldValue<AuthCleanupWorkRecordV1> UpdatedAt(string id) => BaseModuleMutationTemplateBuilder.Field(
        AuthCleanupWorkRecordV1.Fields.UpdatedAt, Request("operationTime." + id, RequestProperties.OperationTime));

    private static BaseModuleResultProjection Result()
    {
        BaseModuleValue<RevisionToken> revision = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision.positive", Guard("disposition.positive"),
            BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.positiveValue", PatchPositive),
            BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision.zero", Guard("disposition.zero"),
                BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.zeroValue", PatchZero),
                BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.revision.retention", Guard("disposition.retention"),
                    BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.retentionValue", PatchRetention),
                    BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision.allValue", PatchAll))));
        BaseModuleValue<long> completed = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.completed.zero", Guard("disposition.zero"),
            CompletedAfterCurrent("result.zero"), BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.completed.all", Guard("disposition.all"),
                Increment("result.all", Captured("result.all", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation), 1L << 13),
                Captured("result.existing", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation)));
        BaseModuleValue<long> chunk = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.chunk.positive", Guard("disposition.positive"),
            IncrementChunk("result.chunk.positive"),
            BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.chunk.zero", Guard("disposition.zero"),
                Constant("result.chunk.zero", AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ConstantAuthority, 0L),
                Captured("result.chunk.existing", AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ModuleMutation)));
        BaseModuleValue<AuthCleanupStateV1> state = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.state.retention", Guard("disposition.retention"),
            Constant("result.state.retention", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.waitingRetention),
            BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.state.all", Guard("disposition.all"),
                Constant("result.state.all", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.readyToPurge),
                Constant("result.state.draining", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.draining)));
        BaseModuleValue<AuthCleanupStepV1> step = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.step.zero", Guard("disposition.zero"), NextStep("result"), Captured("result.step.existing", AuthCleanupWorkRecordV1.Fields.Step.ModuleMutation));
        BaseModuleValue<DateTimeOffset?> retention = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.result.retention", Guard("disposition.retention"),
            ResultProperties.RetentionEligibleAt, Request("result.retention.value", RequestProperties.RetentionEligibleAt),
            BaseModuleMutationTemplateBuilder.Missing(Prefix + ".expression.result.retention.missing", ResultProperties.RetentionEligibleAt));
        return BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(Prefix + ".expression.result",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.ChunkOrdinal, chunk),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.CompletedSteps, completed),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.RetentionEligibleAt, retention),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision, revision),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.State, state),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Step, step)));
    }

    private static BaseModuleValue<AuthCleanupStepV1> NextStep(string id)
    {
        BaseModuleValue<AuthCleanupStepV1> userRolesNext = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.next." + id + ".userRoles", Guard("subject.user"),
            Constant("next." + id + ".userIdentities", AuthCleanupWorkRecordV1.Fields.Step.ConstantAuthority, AuthCleanupStepV1.deleteUserIdentities),
            Constant("next." + id + ".roleProve", AuthCleanupWorkRecordV1.Fields.Step.ConstantAuthority, AuthCleanupStepV1.proveEmpty));
        (AuthCleanupStepV1 Current, AuthCleanupStepV1 Next)[] transitions =
        [
            (AuthCleanupStepV1.revokeSessions, AuthCleanupStepV1.revokeRefreshTokens),
            (AuthCleanupStepV1.revokeRefreshTokens, AuthCleanupStepV1.deleteDeliveries),
            (AuthCleanupStepV1.deleteDeliveries, AuthCleanupStepV1.waitSecurityRetention),
            (AuthCleanupStepV1.waitSecurityRetention, AuthCleanupStepV1.deleteSessions),
            (AuthCleanupStepV1.deleteSessions, AuthCleanupStepV1.deleteRefreshTokens),
            (AuthCleanupStepV1.deleteRefreshTokens, AuthCleanupStepV1.deletePasskeys),
            (AuthCleanupStepV1.deletePasskeys, AuthCleanupStepV1.deleteUserClaims),
            (AuthCleanupStepV1.deleteUserClaims, AuthCleanupStepV1.deleteUserLogins),
            (AuthCleanupStepV1.deleteUserLogins, AuthCleanupStepV1.deleteUserTokens),
            (AuthCleanupStepV1.deleteUserTokens, AuthCleanupStepV1.deleteUserRoles),
            (AuthCleanupStepV1.deleteUserIdentities, AuthCleanupStepV1.proveEmpty),
            (AuthCleanupStepV1.proveEmpty, AuthCleanupStepV1.finalizeSubject),
            (AuthCleanupStepV1.deleteRoleClaims, AuthCleanupStepV1.deleteUserRoles),
        ];
        BaseModuleValue<AuthCleanupStepV1> value = userRolesNext;
        foreach ((AuthCleanupStepV1 current, AuthCleanupStepV1 next) in transitions.Reverse())
            value = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.next." + id + "." + current, StepGuard(current),
                Constant("next." + id + "." + current + "." + next, AuthCleanupWorkRecordV1.Fields.Step.ConstantAuthority, next), value);
        return value;
    }

    private static BaseModuleValue<long> CompletedAfterCurrent(string id)
    {
        BaseModuleValue<long> value = Constant("mask." + id + ".deleteRoleClaims", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, 1L << 14);
        foreach (AuthCleanupStepV1 step in ZeroSteps.Reverse().Where(static step => step != AuthCleanupStepV1.deleteRoleClaims))
            value = BaseModuleMutationTemplateBuilder.Conditional(Prefix + ".expression.mask." + id + "." + step, StepGuard(step),
                Constant("mask." + id + "." + step, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, 1L << Bit(step)), value);
        return Increment("completed." + id, Captured("completed." + id, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation), value);
    }

    private static int Bit(AuthCleanupStepV1 step) => step switch
    {
        AuthCleanupStepV1.revokeSessions => 0, AuthCleanupStepV1.revokeRefreshTokens => 1,
        AuthCleanupStepV1.deleteDeliveries => 2, AuthCleanupStepV1.waitSecurityRetention => 3,
        AuthCleanupStepV1.deleteSessions => 4, AuthCleanupStepV1.deleteRefreshTokens => 5,
        AuthCleanupStepV1.deletePasskeys => 6, AuthCleanupStepV1.deleteUserClaims => 7,
        AuthCleanupStepV1.deleteUserLogins => 8, AuthCleanupStepV1.deleteUserTokens => 9,
        AuthCleanupStepV1.deleteUserRoles => 10, AuthCleanupStepV1.deleteUserIdentities => 11,
        AuthCleanupStepV1.proveEmpty => 12, AuthCleanupStepV1.finalizeSubject => 13,
        AuthCleanupStepV1.deleteRoleClaims => 14, _ => throw new InvalidOperationException("auth.cleanup.step.invalid"),
    };

    private static IEnumerable<(string Id, BaseModuleGuard Guard)> ProgressGuards()
    {
        static long Before(int bit) => (1L << bit) - 1L;
        AuthCleanupStepV1[] common = [.. ZeroSteps.Where(static value => value is not AuthCleanupStepV1.deleteRoleClaims
            and not AuthCleanupStepV1.deleteUserRoles and not AuthCleanupStepV1.proveEmpty and not AuthCleanupStepV1.finalizeSubject)];
        foreach (AuthCleanupStepV1 step in common) yield return CompletedGuard(step.ToString(), Before(Bit(step)));
        yield return CompletedGuard("user.deleteUserRoles", Before(10));
        yield return CompletedGuard("user.proveEmpty", Before(12));
        yield return CompletedGuard("user.finalizeSubject", Before(13));
        yield return CompletedGuard("role.deleteRoleClaims", 0);
        yield return CompletedGuard("role.deleteUserRoles", 1L << 14);
        yield return CompletedGuard("role.proveEmpty", (1L << 14) + (1L << 10));
        yield return CompletedGuard("role.finalizeSubject", (1L << 14) + (1L << 10) + (1L << 12));
    }

    private static (string Id, BaseModuleGuard Guard) CompletedGuard(string id, long expected) =>
        (Guard("progress." + id + ".completed"), BaseModuleMutationTemplateBuilder.FieldEquals(Guard("progress." + id + ".completed"), Capture,
            AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation,
            Constant("progress." + id + ".completed", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, expected)));

    private static BaseModuleStatement ProgressProof()
    {
        AuthCleanupStepV1[] common = [.. ZeroSteps.Where(static value => value is not AuthCleanupStepV1.deleteRoleClaims
            and not AuthCleanupStepV1.deleteUserRoles and not AuthCleanupStepV1.proveEmpty and not AuthCleanupStepV1.finalizeSubject)];
        BaseModuleMutationBlock value = KindProof(AuthCleanupStepV1.finalizeSubject);
        value = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.progress.proveEmpty", StepGuard(AuthCleanupStepV1.proveEmpty), KindProof(AuthCleanupStepV1.proveEmpty), value));
        value = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.progress.deleteUserRoles", StepGuard(AuthCleanupStepV1.deleteUserRoles), KindProof(AuthCleanupStepV1.deleteUserRoles), value));
        value = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.progress.deleteRoleClaims", StepGuard(AuthCleanupStepV1.deleteRoleClaims),
            BaseModuleMutationTemplateBuilder.Block(Require("progress.deleteRoleClaims.kind", "subject.role"), Require("progress.deleteRoleClaims.completed", "progress.role.deleteRoleClaims.completed")), value));
        foreach (AuthCleanupStepV1 step in common.Reverse())
            value = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.progress." + step, StepGuard(step),
                BaseModuleMutationTemplateBuilder.Block(Require("progress." + step + ".completed", "progress." + step + ".completed")), value));
        return value.Statements[0];
    }

    private static BaseModuleMutationBlock KindProof(AuthCleanupStepV1 step) => BaseModuleMutationTemplateBuilder.Block(
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.progress." + step + ".kind", Guard("subject.user"),
            BaseModuleMutationTemplateBuilder.Block(Require("progress.user." + step + ".completed", "progress.user." + step + ".completed")),
            BaseModuleMutationTemplateBuilder.Block(Require("progress.role." + step + ".kind", "subject.role"), Require("progress.role." + step + ".completed", "progress.role." + step + ".completed"))));

    private static BaseModuleValue<long> Increment(string id, BaseModuleValue<long> left, long amount) => Increment(id, left, Constant("increment." + id, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, amount));
    private static BaseModuleValue<long> Increment(string id, BaseModuleValue<long> left, BaseModuleValue<long> right) => BaseModuleMutationTemplateBuilder.Integer(Prefix + ".expression.increment." + id, BaseModuleNumericOperator.IntegerAddChecked, left, right);
    private static BaseModuleValue<long> IncrementChunk(string id) => BaseModuleMutationTemplateBuilder.Integer(
        Prefix + ".expression.increment." + id, BaseModuleNumericOperator.IntegerAddChecked,
        Captured(id, AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ModuleMutation),
        Constant("increment." + id, AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ConstantAuthority, 1L));
    private static BaseModuleValue<T> Captured<T>(string id, BaseModuleCapturedField<AuthCleanupWorkRecordV1, T> field) => BaseModuleMutationTemplateBuilder.Captured(Prefix + ".expression.captured." + id, Capture, field);
    private static BaseModuleFieldEqualsGuard FieldEquals<T>(string id, BaseModuleCapturedField<AuthCleanupWorkRecordV1, T> field, BaseModuleValue<T> expected) => BaseModuleMutationTemplateBuilder.FieldEquals(Guard(id), Capture, field, expected);
    private static BaseModuleValue<T> Request<T>(string id, BaseModuleRequestProperty<AuthCleanupAdvanceV1, T> property) => BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.request." + id, property);
    private static BaseModuleValue<T> Constant<T>(string id, BaseModuleConstantAuthority<T> authority, T value) => BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.constant." + id, authority, value);
    private static BaseModuleValue<BaseRecordId<AuthCleanupWorkRecordV1>> RecordId(string id) => BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthCleanupWorkRecordV1>(Prefix + ".expression.recordId." + id, Request("recordId." + id, RequestProperties.CleanupWorkId));
    private static BaseModuleRequireStatement Require(string id, string guard) => BaseModuleMutationTemplateBuilder.Require(Prefix + ".statement.require." + id, Guard(guard), "auth.cleanup.reconcileConflict");
    private static string Guard(string id) => Prefix + ".guard." + id;
    private static string StepGuard(AuthCleanupStepV1 step) => Guard("step." + step);
}
