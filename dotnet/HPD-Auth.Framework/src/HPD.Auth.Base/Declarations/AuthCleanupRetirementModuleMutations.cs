using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.cleanup.retire-user.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthUserCleanupInitializeV1), typeof(AuthCleanupRetirementResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.retire.user")]
internal static partial class AuthUserCleanupRetireOperationV1
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = AuthCleanupRetirementBuilder.Build(
        "user", "hpd.auth.cleanup.retire-user.v1", "auth.operation.cleanup.retire.user", "hpd.auth.user-subject",
        AuthCleanupSubjectKindV1.user, RequestProperties.CleanupWorkId, RequestProperties.TenantId,
        RequestProperties.SubjectId, RequestProperties.Subject, RequestProperties.Incarnation,
        RequestProperties.TombstoneSequence, RequestProperties.TombstoneRevision, RequestProperties.WorkflowVersion,
        RequestProperties.RetirementReceiptScope, RequestProperties.OperationTime,
        AuthCleanupWorkRecordV1.Fields.UserSubject,
        ResultProperties.CleanupWorkId, ResultProperties.Revision, ResultProperties.Disposition);
}

[BaseRegisteredModuleMutation("hpd.auth.cleanup.retire-role.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRoleCleanupInitializeV1), typeof(AuthCleanupRetirementResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.retire.role")]
internal static partial class AuthRoleCleanupRetireOperationV1
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = AuthCleanupRetirementBuilder.Build(
        "role", "hpd.auth.cleanup.retire-role.v1", "auth.operation.cleanup.retire.role", "hpd.auth.role-subject",
        AuthCleanupSubjectKindV1.role, RequestProperties.CleanupWorkId, RequestProperties.TenantId,
        RequestProperties.SubjectId, RequestProperties.Subject, RequestProperties.Incarnation,
        RequestProperties.TombstoneSequence, RequestProperties.TombstoneRevision, RequestProperties.WorkflowVersion,
        RequestProperties.RetirementReceiptScope, RequestProperties.OperationTime,
        AuthCleanupWorkRecordV1.Fields.RoleSubject,
        ResultProperties.CleanupWorkId, ResultProperties.Revision, ResultProperties.Disposition);
}

internal static class AuthCleanupRetirementBuilder
{
    internal static BaseRegisteredModuleMutationDefinition Build<TRequest, TSubject>(
        string suffix, string operationId, string grantId, string subjectContractId, AuthCleanupSubjectKindV1 subjectKind,
        BaseModuleRequestProperty<TRequest, string> cleanupWorkId, BaseModuleRequestProperty<TRequest, Guid> tenantId,
        BaseModuleRequestProperty<TRequest, Guid> subjectId, BaseModuleRequestProperty<TRequest, BaseSubjectReference<TSubject>> subject,
        BaseModuleRequestProperty<TRequest, BaseSubjectIncarnation> incarnation, BaseModuleRequestProperty<TRequest, long> tombstoneSequence,
        BaseModuleRequestProperty<TRequest, string> tombstoneRevision, BaseModuleRequestProperty<TRequest, int> workflowVersion,
        BaseModuleRequestProperty<TRequest, string> receiptScope,
        BaseModuleRequestProperty<TRequest, DateTimeOffset> operationTime,
        BaseField<AuthCleanupWorkRecordV1, BaseSubjectReference<TSubject>?> selectedSubject,
        BaseModuleResultProperty<AuthCleanupRetirementResultV1, string> resultId,
        BaseModuleResultProperty<AuthCleanupRetirementResultV1, RevisionToken?> resultRevision,
        BaseModuleResultProperty<AuthCleanupRetirementResultV1, BaseSemanticActivationRetirementDisposition> resultDisposition)
    {
        string P(string value) => $"hpd.auth.cleanup.retire-{suffix}.{value}";
        const string capture = "cleanupWork";
        string live = P("semantic.state.live"), missing = P("semantic.state.missing");
        string retired = P("semantic.state.retired"), absent = P("semantic.state.compactedAbsent");
        string patch = P("statement.999.patch");
        BaseModuleValue<T> Req<T>(string id, BaseModuleRequestProperty<TRequest, T> property) =>
            BaseModuleMutationTemplateBuilder.Request(P($"expression.{id}"), property);
        BaseModuleValue<BaseRecordId<AuthCleanupWorkRecordV1>> Id(string id) =>
            BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthCleanupWorkRecordV1>(P($"expression.id.{id}"), Req($"cleanupWorkId.{id}", cleanupWorkId));
        BaseModuleValue<BaseSubjectReference<TSubject>?> OptionalSubject(string id) =>
            BaseModuleMutationTemplateBuilder.LiftOptional(P($"expression.subject.{id}"), selectedSubject.ModuleMutation, Req($"subjectSource.{id}", subject));
        BaseModuleValue<BaseBinary> Incarnation(string id) => BaseModuleMutationTemplateBuilder.IncarnationBytes(
            P($"expression.incarnation.{id}"), AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation, Req($"incarnationSource.{id}", incarnation));

        var captureRecord = BaseModuleMutationTemplateBuilder.CaptureRecord(capture, Id("capture"), BaseModuleCapturePresence.AllowEither);
        BaseModuleGuard[] guards =
        [
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.incarnation"), capture, AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation, Incarnation("guard")),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.receipt"), capture, AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope.ModuleMutation,
                BaseModuleMutationTemplateBuilder.LiftOptional(P("expression.receipt.guard"), AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope.ModuleMutation, Req("receiptSource.guard", receiptScope))),
            BaseModuleMutationTemplateBuilder.RecordPresent(P("guard.recordPresent"), capture, true),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.state"), capture, AuthCleanupWorkRecordV1.Fields.State.ModuleMutation,
                BaseModuleMutationTemplateBuilder.Constant(P("expression.state.guard"), AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.awaitingSemanticRetirement)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.subject"), capture, selectedSubject.ModuleMutation, OptionalSubject("guard")),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.subjectId"), capture, AuthCleanupWorkRecordV1.Fields.SubjectId.ModuleMutation, Req("subjectId.guard", subjectId)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.subjectKind"), capture, AuthCleanupWorkRecordV1.Fields.SubjectKind.ModuleMutation,
                BaseModuleMutationTemplateBuilder.Constant(P("expression.subjectKind.guard"), AuthCleanupWorkRecordV1.Fields.SubjectKind.ConstantAuthority, subjectKind)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.tenant"), capture, AuthCleanupWorkRecordV1.Fields.TenantId.ModuleMutation, Req("tenant.guard", tenantId)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.tombstoneRevision"), capture, AuthCleanupWorkRecordV1.Fields.TombstoneRevision.ModuleMutation, Req("tombstoneRevision.guard", tombstoneRevision)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.tombstoneSequence"), capture, AuthCleanupWorkRecordV1.Fields.TombstoneSequence.ModuleMutation, Req("tombstoneSequence.guard", tombstoneSequence)),
            BaseModuleMutationTemplateBuilder.FieldEquals(P("guard.workflowVersion"), capture, AuthCleanupWorkRecordV1.Fields.WorkflowVersion.ModuleMutation, Req("workflowVersion.guard", workflowVersion)),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(absent, BaseModuleSemanticActivationStateTest.CompactedAbsent),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(live, BaseModuleSemanticActivationStateTest.Live),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(missing, BaseModuleSemanticActivationStateTest.Missing),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(retired, BaseModuleSemanticActivationStateTest.Retired),
        ];
        BaseModuleRequireStatement Require(string n, string guard) => BaseModuleMutationTemplateBuilder.Require(P($"statement.require.{n}"), guard, "auth.cleanup.reconcileConflict");
        BaseModuleMutationBlock Live() => BaseModuleMutationTemplateBuilder.Block(
            Require("00.present", P("guard.recordPresent")), Require("01.incarnation", P("guard.incarnation")),
            Require("02.receipt", P("guard.receipt")), Require("03.state", P("guard.state")), Require("04.subject", P("guard.subject")),
            Require("05.subjectId", P("guard.subjectId")), Require("06.subjectKind", P("guard.subjectKind")), Require("07.tenant", P("guard.tenant")),
            Require("08.tombstoneRevision", P("guard.tombstoneRevision")), Require("09.tombstoneSequence", P("guard.tombstoneSequence")),
            Require("10.workflowVersion", P("guard.workflowVersion")),
            BaseModuleMutationTemplateBuilder.Patch(patch, Id("patch"),
                BaseModuleMutationTemplateBuilder.Object<AuthCleanupWorkRecordV1>(P("expression.patch"),
                    BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State,
                        BaseModuleMutationTemplateBuilder.Constant(P("expression.state.complete"), AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.complete)),
                    BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.UpdatedAt, Req("operationTime.patch", operationTime))),
                BaseModuleMutationTemplateBuilder.CapturedRevision(P("expression.expectedRevision.patch"), capture)));
        BaseModuleMutationBlock RejectMissing() => BaseModuleMutationTemplateBuilder.Block(
            BaseModuleMutationTemplateBuilder.Require(P("statement.require.missing"), live, "auth.cleanup.reconcileConflict"));
        BaseModuleMutationBlock Terminal(string id, string guard) => BaseModuleMutationTemplateBuilder.Block(Require(id, guard));
        BaseModuleIfStatement body = BaseModuleMutationTemplateBuilder.If(P("statement.state.missing"), missing, RejectMissing(),
            BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(P("statement.state.live"), live, Live(),
                BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(P("statement.state.retired"), retired,
                    Terminal("retired", retired), Terminal("compactedAbsent", absent))))));

        BaseModuleValue<RevisionToken?> committed = BaseModuleMutationTemplateBuilder.LiftOptional(P("expression.revision.committed"), resultRevision,
            BaseModuleMutationTemplateBuilder.CommittedRevision(P("expression.revision.value"), patch));
        BaseModuleValue<RevisionToken?> noRevision = BaseModuleMutationTemplateBuilder.Missing(P("expression.revision.missing"), resultRevision);
        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = operationId, Version = 1, OwningModuleId = AuthBaseContract.ModuleId, GrantId = grantId,
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = $"hpd.auth.type.auth-{suffix}-cleanup-retire-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-cleanup-retirement-result-v1.v1",
            SystemCollectionIds = [AuthCleanupWorkRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthCleanupWorkRecordV1.Collection.Id, GrantId = "auth.cleanup.execute" }],
            GenerationCellIds = [], ImportedSubjectContractIds = [subjectContractId],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [captureRecord], Guards = [.. guards], Preconditions = [], Body = BaseModuleMutationTemplateBuilder.Block(body),
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(P("expression.result"),
                    BaseModuleMutationTemplateBuilder.Property(resultId, Req("result.cleanupWorkId", cleanupWorkId)),
                    BaseModuleMutationTemplateBuilder.Property(resultDisposition,
                        BaseModuleMutationTemplateBuilder.SemanticRetirementDisposition(P("expression.result.disposition"), resultDisposition)),
                    BaseModuleMutationTemplateBuilder.Property(resultRevision,
                        BaseModuleMutationTemplateBuilder.Conditional(P("expression.result.revision"), live, committed, noRevision)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });
    }
}
