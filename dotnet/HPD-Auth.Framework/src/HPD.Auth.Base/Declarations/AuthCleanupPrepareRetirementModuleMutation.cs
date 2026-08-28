using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.cleanup.prepare-retirement.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthCleanupPrepareRetirementV1), typeof(AuthCleanupMutationResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.prepareRetirement")]
internal static partial class AuthCleanupPrepareRetirementOperationV1
{
    private const string Prefix = "hpd.auth.cleanup.prepare-retirement";
    private const string Capture = "cleanupWork";
    private const string Patch = Prefix + ".statement.patch";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "hpd.auth.cleanup.prepare-retirement.v1", Version = 1,
        OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.prepareRetirement",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "hpd.auth.type.auth-cleanup-prepare-retirement-v1.v1",
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
            Guards = [.. Guards()], Preconditions = [], Body = Body(), Result = Result(),
        },
        Limits = AuthModuleMutationDefaults.Limits() with { MaximumStatements = 32, MaximumBranches = 4 },
        ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
        Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
    });

    private static BaseModuleGuard[] Guards() =>
    [
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("completed.role"), Capture, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation,
            Constant("completed.role", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, 29_696L)),
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("completed.user"), Capture, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation,
            Constant("completed.user", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, 16_383L)),
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("incarnation"), Capture, AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation,
            BaseModuleMutationTemplateBuilder.IncarnationBytes(Prefix + ".expression.incarnation", AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation,
                Request("incarnation", RequestProperties.ExpectedIncarnation))),
        BaseModuleMutationTemplateBuilder.RevisionEquals(Guard("revision"), Capture, Request("revision", RequestProperties.ExpectedRevision)),
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("state"), Capture, AuthCleanupWorkRecordV1.Fields.State.ModuleMutation,
            Constant("state", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.readyToPurge)),
        BaseModuleMutationTemplateBuilder.ValueEquals(Guard("subject.role"), Request("subject.role", RequestProperties.SubjectKind),
            Constant("subject.role", RequestProperties.SubjectKind.ConstantAuthority, AuthCleanupSubjectKindV1.role)),
        BaseModuleMutationTemplateBuilder.ValueEquals(Guard("subject.user"), Request("subject.user", RequestProperties.SubjectKind),
            Constant("subject.user", RequestProperties.SubjectKind.ConstantAuthority, AuthCleanupSubjectKindV1.user)),
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("subjectKind"), Capture, AuthCleanupWorkRecordV1.Fields.SubjectKind.ModuleMutation,
            Request("subjectKind", RequestProperties.SubjectKind)),
        BaseModuleMutationTemplateBuilder.FieldEquals(Guard("tombstoneSequence"), Capture, AuthCleanupWorkRecordV1.Fields.TombstoneSequence.ModuleMutation,
            Request("tombstoneSequence", RequestProperties.ExpectedTombstoneSequence)),
    ];

    private static BaseModuleMutationBlock Body() => BaseModuleMutationTemplateBuilder.Block(
        Require("00.revision", "revision"), Require("01.state", "state"), Require("02.subjectKind", "subjectKind"),
        Require("03.incarnation", "incarnation"), Require("04.tombstoneSequence", "tombstoneSequence"),
        BaseModuleMutationTemplateBuilder.If(Prefix + ".statement.subject", Guard("subject.user"),
            BaseModuleMutationTemplateBuilder.Block(Require("05.userComplete", "completed.user")),
            BaseModuleMutationTemplateBuilder.Block(Require("05.roleKind", "subject.role"), Require("06.roleComplete", "completed.role"))),
        BaseModuleMutationTemplateBuilder.Patch(Patch, RecordId("patch"),
            BaseModuleMutationTemplateBuilder.Object<AuthCleanupWorkRecordV1>(Prefix + ".expression.payload",
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope,
                    BaseModuleMutationTemplateBuilder.LiftOptional(Prefix + ".expression.receipt", AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope.ModuleMutation,
                        Request("receipt", RequestProperties.RetirementReceiptScope))),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State,
                    Constant("awaiting", AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.awaitingSemanticRetirement)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.UpdatedAt, Request("operationTime", RequestProperties.OperationTime))),
            Request("patchRevision", RequestProperties.ExpectedRevision)));

    private static BaseModuleResultProjection Result() => BaseModuleMutationTemplateBuilder.Result(
        BaseModuleMutationTemplateBuilder.ResultObject(Prefix + ".expression.result",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.ChunkOrdinal, Captured("result.chunk", AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ModuleMutation)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.CompletedSteps, Captured("result.completed", AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.RetentionEligibleAt,
                BaseModuleMutationTemplateBuilder.Missing(Prefix + ".expression.result.retention", ResultProperties.RetentionEligibleAt)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix + ".expression.result.revision", Patch)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.State,
                Constant("result.state", ResultProperties.State.ConstantAuthority, AuthCleanupStateV1.awaitingSemanticRetirement)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Step, Captured("result.step", AuthCleanupWorkRecordV1.Fields.Step.ModuleMutation))));

    private static BaseModuleValue<T> Captured<T>(string id, BaseModuleCapturedField<AuthCleanupWorkRecordV1, T> field) =>
        BaseModuleMutationTemplateBuilder.Captured(Prefix + ".expression.captured." + id, Capture, field);
    private static BaseModuleValue<T> Request<T>(string id, BaseModuleRequestProperty<AuthCleanupPrepareRetirementV1, T> property) =>
        BaseModuleMutationTemplateBuilder.Request(Prefix + ".expression.request." + id, property);
    private static BaseModuleValue<T> Constant<T>(string id, BaseModuleConstantAuthority<T> authority, T value) =>
        BaseModuleMutationTemplateBuilder.Constant(Prefix + ".expression.constant." + id, authority, value);
    private static BaseModuleValue<BaseRecordId<AuthCleanupWorkRecordV1>> RecordId(string id) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthCleanupWorkRecordV1>(Prefix + ".expression.recordId." + id,
            Request("recordId." + id, RequestProperties.CleanupWorkId));
    private static BaseModuleRequireStatement Require(string id, string guard) =>
        BaseModuleMutationTemplateBuilder.Require(Prefix + ".statement.require." + id, Guard(guard), "auth.cleanup.reconcileConflict");
    private static string Guard(string id) => Prefix + ".guard." + id;
}
