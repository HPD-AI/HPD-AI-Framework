using HPD.Base;

namespace HPD.Auth.Base;

[BaseRegisteredModuleMutation("hpd.auth.cleanup.initialize-user.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthUserCleanupInitializeV1), typeof(AuthCleanupInitializeResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.initialize.user")]
internal static partial class AuthUserCleanupInitializeOperationV1
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        AuthCleanupInitializationBuilder.Build(
            "user", "hpd.auth.cleanup.initialize-user.v1", "auth.operation.cleanup.initialize.user",
            "hpd.auth.user-subject", AuthCleanupSubjectKindV1.user, AuthCleanupStepV1.revokeSessions,
            RequestProperties.CleanupWorkId, RequestProperties.TenantId, RequestProperties.SubjectId,
            RequestProperties.Subject, RequestProperties.Incarnation, RequestProperties.TombstoneSequence,
            RequestProperties.TombstoneRevision, RequestProperties.WorkflowVersion, RequestProperties.TombstonedAt,
            RequestProperties.RetirementReceiptScope, RequestProperties.OperationTime,
            AuthCleanupWorkRecordV1.Fields.UserSubject,
            BaseModuleMutationTemplateBuilder.FieldPresence("hpd.auth.cleanup.initialize-user.guard.otherSubjectMissing", "cleanupWork", AuthCleanupWorkRecordV1.Fields.RoleSubject.ModuleMutation, BaseModuleFieldPresenceTest.Missing),
            ResultProperties.ChunkOrdinal, ResultProperties.CleanupWorkId, ResultProperties.CompletedSteps,
            ResultProperties.RetentionEligibleAt, ResultProperties.Revision, ResultProperties.SemanticActivationId,
            ResultProperties.SemanticActivationWasMaterialized, ResultProperties.SemanticDisposition,
            ResultProperties.State, ResultProperties.Step);
}

[BaseRegisteredModuleMutation("hpd.auth.cleanup.initialize-role.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthRoleCleanupInitializeV1), typeof(AuthCleanupInitializeResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.cleanup.initialize.role")]
internal static partial class AuthRoleCleanupInitializeOperationV1
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } =
        AuthCleanupInitializationBuilder.Build(
            "role", "hpd.auth.cleanup.initialize-role.v1", "auth.operation.cleanup.initialize.role",
            "hpd.auth.role-subject", AuthCleanupSubjectKindV1.role, AuthCleanupStepV1.deleteRoleClaims,
            RequestProperties.CleanupWorkId, RequestProperties.TenantId, RequestProperties.SubjectId,
            RequestProperties.Subject, RequestProperties.Incarnation, RequestProperties.TombstoneSequence,
            RequestProperties.TombstoneRevision, RequestProperties.WorkflowVersion, RequestProperties.TombstonedAt,
            RequestProperties.RetirementReceiptScope, RequestProperties.OperationTime,
            AuthCleanupWorkRecordV1.Fields.RoleSubject,
            BaseModuleMutationTemplateBuilder.FieldPresence("hpd.auth.cleanup.initialize-role.guard.otherSubjectMissing", "cleanupWork", AuthCleanupWorkRecordV1.Fields.UserSubject.ModuleMutation, BaseModuleFieldPresenceTest.Missing),
            ResultProperties.ChunkOrdinal, ResultProperties.CleanupWorkId, ResultProperties.CompletedSteps,
            ResultProperties.RetentionEligibleAt, ResultProperties.Revision, ResultProperties.SemanticActivationId,
            ResultProperties.SemanticActivationWasMaterialized, ResultProperties.SemanticDisposition,
            ResultProperties.State, ResultProperties.Step);
}

internal static class AuthCleanupInitializationBuilder
{
    internal static BaseRegisteredModuleMutationDefinition Build<TRequest, TSubject>(
        string suffix, string operationId, string grantId, string subjectContractId,
        AuthCleanupSubjectKindV1 subjectKind, AuthCleanupStepV1 initialStep,
        BaseModuleRequestProperty<TRequest, string> cleanupWorkId,
        BaseModuleRequestProperty<TRequest, Guid> tenantId,
        BaseModuleRequestProperty<TRequest, Guid> subjectId,
        BaseModuleRequestProperty<TRequest, BaseSubjectReference<TSubject>> subject,
        BaseModuleRequestProperty<TRequest, BaseSubjectIncarnation> incarnation,
        BaseModuleRequestProperty<TRequest, long> tombstoneSequence,
        BaseModuleRequestProperty<TRequest, string> tombstoneRevision,
        BaseModuleRequestProperty<TRequest, int> workflowVersion,
        BaseModuleRequestProperty<TRequest, DateTimeOffset> tombstonedAt,
        BaseModuleRequestProperty<TRequest, string> retirementReceiptScope,
        BaseModuleRequestProperty<TRequest, DateTimeOffset> operationTime,
        BaseField<AuthCleanupWorkRecordV1, BaseSubjectReference<TSubject>?> selectedSubjectField,
        BaseModuleFieldPresenceGuard otherSubjectMissing,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, long?> resultChunkOrdinal,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, string> resultCleanupWorkId,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, long?> resultCompletedSteps,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, DateTimeOffset?> resultRetentionEligibleAt,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, RevisionToken?> resultRevision,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, string?> resultSemanticActivationId,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, bool> resultSemanticMaterialized,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, BaseSemanticActivationEnsureDisposition> resultSemanticDisposition,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, AuthCleanupStateV1?> resultState,
        BaseModuleResultProperty<AuthCleanupInitializeResultV1, AuthCleanupStepV1?> resultStep)
    {
        string Prefix(string value) => $"hpd.auth.cleanup.initialize-{suffix}.{value}";
        const string capture = "cleanupWork";
        string createStatement = Prefix("statement.000.create");
        string missingState = Prefix("semantic.state.missing");
        string liveState = Prefix("semantic.state.live");
        string retiredState = Prefix("semantic.state.retired");
        string absentState = Prefix("semantic.state.compactedAbsent");
        string recordMissing = Prefix("guard.recordMissing");
        string recordPresent = Prefix("guard.recordPresent");

        BaseModuleValue<T> Req<T>(string id, BaseModuleRequestProperty<TRequest, T> property) =>
            BaseModuleMutationTemplateBuilder.Request(Prefix($"expression.{id}"), property);
        BaseModuleValue<BaseRecordId<AuthCleanupWorkRecordV1>> RecordId(string id) =>
            BaseModuleMutationTemplateBuilder.RecordIdFromString<AuthCleanupWorkRecordV1>(
                Prefix($"expression.recordId.{id}"), Req($"cleanupWorkId.{id}", cleanupWorkId));
        BaseModuleValue<BaseBinary> Incarnation(string id) => BaseModuleMutationTemplateBuilder.IncarnationBytes(
            Prefix($"expression.incarnation.{id}"), AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation,
            Req($"incarnationSource.{id}", incarnation));
        BaseModuleValue<BaseSubjectReference<TSubject>?> OptionalSubject(string id) =>
            BaseModuleMutationTemplateBuilder.LiftOptional(Prefix($"expression.subject.{id}"), selectedSubjectField.ModuleMutation,
                Req($"subjectSource.{id}", subject));
        BaseModuleFieldValue<AuthCleanupWorkRecordV1> SubjectPayload() =>
            BaseModuleMutationTemplateBuilder.Field<AuthCleanupWorkRecordV1, BaseSubjectReference<TSubject>?>(
                selectedSubjectField, OptionalSubject("payload"));

        BaseModuleRecordCapture recordCapture = BaseModuleMutationTemplateBuilder.CaptureRecord(
            capture, RecordId("capture"), BaseModuleCapturePresence.AllowEither);
        BaseModuleGuard[] guards =
        [
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.incarnation"), capture,
                AuthCleanupWorkRecordV1.Fields.Incarnation.ModuleMutation, Incarnation("guard")),
            BaseModuleMutationTemplateBuilder.ValueEquals(Prefix("guard.initializationReceipt"), Req("initializationReceipt", retirementReceiptScope),
                BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.initializationReceipt"), retirementReceiptScope.ConstantAuthority, "auth.cleanup.initialize")),
            BaseModuleMutationTemplateBuilder.ValueEquals(Prefix("guard.initializationTime"), Req("initializationTime", operationTime), Req("tombstonedAt.initializationTime", tombstonedAt)),
            otherSubjectMissing,
            BaseModuleMutationTemplateBuilder.RecordPresent(recordMissing, capture, false),
            BaseModuleMutationTemplateBuilder.RecordPresent(recordPresent, capture, true),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.subject"), capture, selectedSubjectField.ModuleMutation, OptionalSubject("guard")),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.subjectId"), capture,
                AuthCleanupWorkRecordV1.Fields.SubjectId.ModuleMutation, Req("subjectId.guard", subjectId)),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.subjectKind"), capture,
                AuthCleanupWorkRecordV1.Fields.SubjectKind.ModuleMutation,
                BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.subjectKind.guard"),
                    AuthCleanupWorkRecordV1.Fields.SubjectKind.ConstantAuthority, subjectKind)),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.tenant"), capture,
                AuthCleanupWorkRecordV1.Fields.TenantId.ModuleMutation, Req("tenant.guard", tenantId)),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.tombstoneRevision"), capture,
                AuthCleanupWorkRecordV1.Fields.TombstoneRevision.ModuleMutation, Req("tombstoneRevision.guard", tombstoneRevision)),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.tombstoneSequence"), capture,
                AuthCleanupWorkRecordV1.Fields.TombstoneSequence.ModuleMutation, Req("tombstoneSequence.guard", tombstoneSequence)),
            BaseModuleMutationTemplateBuilder.FieldEquals(Prefix("guard.workflowVersion"), capture,
                AuthCleanupWorkRecordV1.Fields.WorkflowVersion.ModuleMutation, Req("workflowVersion.guard", workflowVersion)),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(absentState, BaseModuleSemanticActivationStateTest.CompactedAbsent),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(liveState, BaseModuleSemanticActivationStateTest.Live),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(missingState, BaseModuleSemanticActivationStateTest.Missing),
            BaseModuleMutationTemplateBuilder.SemanticActivationState(retiredState, BaseModuleSemanticActivationStateTest.Retired),
        ];

        BaseModuleMutationBlock Require(string id, string guard) => BaseModuleMutationTemplateBuilder.Block(
            BaseModuleMutationTemplateBuilder.Require(Prefix($"statement.require.{id}"), guard, "auth.cleanup.reconcileConflict"));
        BaseModuleMutationBlock Live() => BaseModuleMutationTemplateBuilder.Block(
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.00.present"), recordPresent, "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.01.incarnation"), Prefix("guard.incarnation"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.02.otherSubject"), Prefix("guard.otherSubjectMissing"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.03.subject"), Prefix("guard.subject"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.04.subjectId"), Prefix("guard.subjectId"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.05.subjectKind"), Prefix("guard.subjectKind"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.06.tenant"), Prefix("guard.tenant"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.07.tombstoneRevision"), Prefix("guard.tombstoneRevision"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.08.tombstoneSequence"), Prefix("guard.tombstoneSequence"), "auth.cleanup.reconcileConflict"),
            BaseModuleMutationTemplateBuilder.Require(Prefix("statement.live.09.workflowVersion"), Prefix("guard.workflowVersion"), "auth.cleanup.reconcileConflict"));

        BaseModuleCreateStatement Create() => BaseModuleMutationTemplateBuilder.Create(createStatement, RecordId("create"),
            BaseModuleMutationTemplateBuilder.Object<AuthCleanupWorkRecordV1>(Prefix("expression.payload"),
            [
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.ChunkOrdinal,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.chunkOrdinal"), AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ConstantAuthority, 0L)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.CompletedSteps,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.completedSteps"), AuthCleanupWorkRecordV1.Fields.CompletedSteps.ConstantAuthority, 0L)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.CreatedAt, Req("createdAt", tombstonedAt)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.Id, Req("id.payload", cleanupWorkId)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.Incarnation, Incarnation("payload")),
                ..(subjectKind == AuthCleanupSubjectKindV1.role ? new[] { SubjectPayload() } : []),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.State,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.state"), AuthCleanupWorkRecordV1.Fields.State.ConstantAuthority, AuthCleanupStateV1.draining)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.Step,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.step"), AuthCleanupWorkRecordV1.Fields.Step.ConstantAuthority, initialStep)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.SubjectId, Req("subjectId.payload", subjectId)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.SubjectKind,
                    BaseModuleMutationTemplateBuilder.Constant(Prefix("expression.subjectKind.payload"), AuthCleanupWorkRecordV1.Fields.SubjectKind.ConstantAuthority, subjectKind)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.TenantId, Req("tenant.payload", tenantId)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.TombstoneRevision, Req("tombstoneRevision.payload", tombstoneRevision)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.TombstoneSequence, Req("tombstoneSequence.payload", tombstoneSequence)),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.UpdatedAt, Req("updatedAt", tombstonedAt)),
                ..(subjectKind == AuthCleanupSubjectKindV1.user ? new[] { SubjectPayload() } : []),
                BaseModuleMutationTemplateBuilder.Field(AuthCleanupWorkRecordV1.Fields.WorkflowVersion, Req("workflowVersion.payload", workflowVersion)),
            ]));

        BaseModuleIfStatement StateBody() => BaseModuleMutationTemplateBuilder.If(Prefix("statement.state.missing"), missingState,
            BaseModuleMutationTemplateBuilder.Block(
                BaseModuleMutationTemplateBuilder.Require(Prefix("statement.missing.require"), recordMissing, "auth.cleanup.reconcileConflict"), Create()),
            BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix("statement.state.live"), liveState, Live(),
                BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.If(Prefix("statement.state.retired"), retiredState,
                    Require("retired", retiredState), Require("compactedAbsent", absentState))))));

        BaseModuleValue<T?> TerminalMissing<T>(string id, BaseModuleResultProperty<AuthCleanupInitializeResultV1, T?> property) where T : struct =>
            BaseModuleMutationTemplateBuilder.Missing(Prefix($"expression.{id}"), property);
        BaseModuleValue<T?> RecordValue<T>(string id, BaseModuleResultProperty<AuthCleanupInitializeResultV1, T?> result,
            BaseModuleCapturedField<AuthCleanupWorkRecordV1, T> field, T initial) where T : struct
        {
            BaseModuleValue<T?> created = BaseModuleMutationTemplateBuilder.LiftOptional(Prefix($"expression.{id}.created"), result,
                BaseModuleMutationTemplateBuilder.Constant(Prefix($"expression.{id}.initial"), field.ConstantAuthority, initial));
            BaseModuleValue<T?> existing = BaseModuleMutationTemplateBuilder.LiftOptional(Prefix($"expression.{id}.existing"), result,
                BaseModuleMutationTemplateBuilder.Captured(Prefix($"expression.{id}.captured"), capture, field));
            BaseModuleValue<T?> terminal = TerminalMissing(Prefix($"{id}.terminal"), result);
            BaseModuleValue<T?> liveOrTerminal = BaseModuleMutationTemplateBuilder.Conditional(Prefix($"expression.{id}.live"), liveState, existing, terminal);
            return BaseModuleMutationTemplateBuilder.Conditional(Prefix($"expression.{id}.missing"), missingState, created, liveOrTerminal);
        }

        BaseModuleValue<RevisionToken?> revisionCreated = BaseModuleMutationTemplateBuilder.LiftOptional(Prefix("expression.revision.created"),
            resultRevision,
            BaseModuleMutationTemplateBuilder.CommittedRevision(Prefix("expression.revision.committed"), createStatement));
        BaseModuleValue<RevisionToken?> revisionExisting = BaseModuleMutationTemplateBuilder.LiftOptional(Prefix("expression.revision.existing"),
            resultRevision,
            BaseModuleMutationTemplateBuilder.CapturedRevision(Prefix("expression.revision.captured"), capture));
        BaseModuleValue<RevisionToken?> revisionTerminal = BaseModuleMutationTemplateBuilder.Missing(Prefix("expression.revision.terminal"), resultRevision);
        BaseModuleValue<RevisionToken?> revision = BaseModuleMutationTemplateBuilder.Conditional(Prefix("expression.revision.missing"), missingState,
            revisionCreated, BaseModuleMutationTemplateBuilder.Conditional(Prefix("expression.revision.live"), liveState, revisionExisting, revisionTerminal));

        BaseModuleValue<DateTimeOffset?> retentionExisting = BaseModuleMutationTemplateBuilder.Captured(
            Prefix("expression.retention.existing"), capture, AuthCleanupWorkRecordV1.Fields.RetentionEligibleAt.ModuleMutation);
        BaseModuleValue<DateTimeOffset?> retentionMissing = BaseModuleMutationTemplateBuilder.Missing(
            Prefix("expression.retention.missing"), resultRetentionEligibleAt);
        BaseModuleValue<DateTimeOffset?> retention = BaseModuleMutationTemplateBuilder.Conditional(
            Prefix("expression.retention.live"), liveState, resultRetentionEligibleAt, retentionExisting, retentionMissing);

        BaseModuleValue<string?> semanticActivationId = BaseModuleMutationTemplateBuilder.SemanticActivationId(
            Prefix("expression.semanticActivationId.value"), resultSemanticActivationId);
        BaseModuleValue<string?> semanticActivationIdAbsent = BaseModuleMutationTemplateBuilder.Missing(
            Prefix("expression.semanticActivationId.absent"), resultSemanticActivationId);
        BaseModuleValue<string?> semanticActivationIdRetired = BaseModuleMutationTemplateBuilder.Missing(
            Prefix("expression.semanticActivationId.retired"), resultSemanticActivationId);
        BaseModuleValue<string?> semanticActivationIdUnlessAbsent = BaseModuleMutationTemplateBuilder.Conditional(
            Prefix("expression.semanticActivationId.unlessAbsent"), absentState,
            semanticActivationIdAbsent, semanticActivationId);
        BaseModuleValue<string?> projectedSemanticActivationId = BaseModuleMutationTemplateBuilder.Conditional(
            Prefix("expression.semanticActivationId.unlessRetired"), retiredState,
            semanticActivationIdRetired, semanticActivationIdUnlessAbsent);

        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = operationId, Version = 1, OwningModuleId = AuthBaseContract.ModuleId, GrantId = grantId,
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = $"hpd.auth.type.auth-{suffix}-cleanup-initialize-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-cleanup-initialize-result-v1.v1",
            SystemCollectionIds = [AuthCleanupWorkRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthCleanupWorkRecordV1.Collection.Id, GrantId = "auth.cleanup.execute" }],
            GenerationCellIds = [], ImportedSubjectContractIds = [subjectContractId],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [recordCapture], Guards = [.. guards], Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.Require(Prefix("statement.initializationReceipt"), Prefix("guard.initializationReceipt"), "auth.cleanup.reconcileConflict"),
                    BaseModuleMutationTemplateBuilder.Require(Prefix("statement.initializationTime"), Prefix("guard.initializationTime"), "auth.cleanup.reconcileConflict"),
                    StateBody()),
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.ResultObject(
                    Prefix("expression.result"),
                    BaseModuleMutationTemplateBuilder.Property(resultChunkOrdinal,
                        RecordValue("chunkOrdinal", resultChunkOrdinal, AuthCleanupWorkRecordV1.Fields.ChunkOrdinal.ModuleMutation, 0L)),
                    BaseModuleMutationTemplateBuilder.Property(resultCleanupWorkId, Req("result.cleanupWorkId", cleanupWorkId)),
                    BaseModuleMutationTemplateBuilder.Property(resultCompletedSteps,
                        RecordValue("completedSteps", resultCompletedSteps, AuthCleanupWorkRecordV1.Fields.CompletedSteps.ModuleMutation, 0L)),
                    BaseModuleMutationTemplateBuilder.Property(resultRetentionEligibleAt, retention),
                    BaseModuleMutationTemplateBuilder.Property(resultRevision, revision),
                    BaseModuleMutationTemplateBuilder.Property(resultSemanticActivationId, projectedSemanticActivationId),
                    BaseModuleMutationTemplateBuilder.Property(resultSemanticMaterialized,
                        BaseModuleMutationTemplateBuilder.SemanticActivationWasMaterialized(Prefix("expression.semanticMaterialized"), resultSemanticMaterialized)),
                    BaseModuleMutationTemplateBuilder.Property(resultSemanticDisposition,
                        BaseModuleMutationTemplateBuilder.SemanticEnsureDisposition(Prefix("expression.semanticDisposition"), resultSemanticDisposition)),
                    BaseModuleMutationTemplateBuilder.Property(resultState,
                        RecordValue("state", resultState, AuthCleanupWorkRecordV1.Fields.State.ModuleMutation, AuthCleanupStateV1.draining)),
                    BaseModuleMutationTemplateBuilder.Property(resultStep,
                        RecordValue("step", resultStep, AuthCleanupWorkRecordV1.Fields.Step.ModuleMutation, initialStep)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });
    }
}
