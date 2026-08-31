using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal enum AuthCleanupChildDispositionV1
{
    positiveCohort,
    zeroDrainProof,
    retentionBlocked,
    allStepsComplete,
}

internal sealed record AuthUserCleanupInitializeV1
{
    [BaseField("auth.operation.cleanup.initialize.user.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.subjectId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SubjectId { get; init; }
    [BaseField("auth.cleanup.user.subject.v1")]
    [BaseSubjectReference(typeof(AuthUserSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)]
    public required BaseSubjectReference<AuthUserSubject> Subject { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.incarnation")] public required BaseSubjectIncarnation Incarnation { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.tombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long TombstoneSequence { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.tombstoneRevision", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string TombstoneRevision { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.workflowVersion", MinimumInt32 = 1, HasMinimumInt32 = true, MaximumInt32 = 1, HasMaximumInt32 = true)] public required int WorkflowVersion { get; init; }
    [BaseField("auth.operation.cleanup.initialize.user.tombstonedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset TombstonedAt { get; init; }
    [BaseField("auth.operation.cleanup.semantic.user.retirementReceiptScope", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string RetirementReceiptScope { get; init; }
    [BaseField("auth.operation.cleanup.semantic.user.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthRoleCleanupInitializeV1
{
    [BaseField("auth.operation.cleanup.initialize.role.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.subjectId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SubjectId { get; init; }
    [BaseField("auth.cleanup.role.subject.v1")]
    [BaseSubjectReference(typeof(AuthRoleSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)]
    public required BaseSubjectReference<AuthRoleSubject> Subject { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.incarnation")] public required BaseSubjectIncarnation Incarnation { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.tombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long TombstoneSequence { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.tombstoneRevision", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string TombstoneRevision { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.workflowVersion", MinimumInt32 = 1, HasMinimumInt32 = true, MaximumInt32 = 1, HasMaximumInt32 = true)] public required int WorkflowVersion { get; init; }
    [BaseField("auth.operation.cleanup.initialize.role.tombstonedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset TombstonedAt { get; init; }
    [BaseField("auth.operation.cleanup.semantic.role.retirementReceiptScope", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string RetirementReceiptScope { get; init; }
    [BaseField("auth.operation.cleanup.semantic.role.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthCleanupInitializeResultV1
{
    [BaseField("auth.operation.cleanup.initialize.result.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.revision", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public RevisionToken? Revision { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.state", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["awaitingSemanticRetirement", "complete", "draining", "readyToPurge", "waitingRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStateV1>))] public AuthCleanupStateV1? State { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.step", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["deleteDeliveries", "deletePasskeys", "deleteRefreshTokens", "deleteRoleClaims", "deleteSessions", "deleteUserClaims", "deleteUserIdentities", "deleteUserLogins", "deleteUserRoles", "deleteUserTokens", "proveEmpty", "proveSubjectReady", "revokeRefreshTokens", "revokeSessions", "waitSecurityRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStepV1>))] public AuthCleanupStepV1? Step { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.chunkOrdinal", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MinimumInt64 = 0, HasMinimumInt64 = true)] public long? ChunkOrdinal { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.completedSteps", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MinimumInt64 = 0, HasMinimumInt64 = true)] public long? CompletedSteps { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.semanticDisposition", AllowedEnumLiterals = ["created", "existing", "retired"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationEnsureDisposition>))] public required BaseSemanticActivationEnsureDisposition SemanticDisposition { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.semanticActivationId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256)] public string? SemanticActivationId { get; init; }
    [BaseField("auth.operation.cleanup.initialize.result.semanticActivationWasMaterialized")] public required bool SemanticActivationWasMaterialized { get; init; }
}

internal sealed record AuthCleanupRetirementResultV1
{
    [BaseField("auth.operation.cleanup.retire.result.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.retire.result.revision", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public RevisionToken? Revision { get; init; }
    [BaseField("auth.operation.cleanup.retire.result.disposition", AllowedEnumLiterals = ["alreadyCompacted", "alreadyRetired", "retiredNow"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationRetirementDisposition>))] public required BaseSemanticActivationRetirementDisposition Disposition { get; init; }
}

internal sealed record AuthCleanupAdvanceV1
{
    [BaseField("auth.operation.cleanup.advance.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.advance.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.cleanup.advance.expectedState", AllowedEnumLiterals = ["awaitingSemanticRetirement", "complete", "draining", "readyToPurge", "waitingRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStateV1>))] public required AuthCleanupStateV1 ExpectedState { get; init; }
    [BaseField("auth.operation.cleanup.advance.expectedStep", AllowedEnumLiterals = ["deleteDeliveries", "deletePasskeys", "deleteRefreshTokens", "deleteRoleClaims", "deleteSessions", "deleteUserClaims", "deleteUserIdentities", "deleteUserLogins", "deleteUserRoles", "deleteUserTokens", "proveEmpty", "proveSubjectReady", "revokeRefreshTokens", "revokeSessions", "waitSecurityRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStepV1>))] public required AuthCleanupStepV1 ExpectedStep { get; init; }
    [BaseField("auth.operation.cleanup.advance.expectedChunkOrdinal", MinimumInt64 = 0, HasMinimumInt64 = true)] public required long ExpectedChunkOrdinal { get; init; }
    [BaseField("auth.operation.cleanup.advance.expectedIncarnation")] public required BaseSubjectIncarnation ExpectedIncarnation { get; init; }
    [BaseField("auth.operation.cleanup.advance.childDisposition", AllowedEnumLiterals = ["allStepsComplete", "positiveCohort", "retentionBlocked", "zeroDrainProof"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupChildDispositionV1>))] public required AuthCleanupChildDispositionV1 ChildDisposition { get; init; }
    [BaseField("auth.operation.cleanup.advance.selectedCount", MinimumInt32 = 0, HasMinimumInt32 = true, MaximumInt32 = 200, HasMaximumInt32 = true)] public required int SelectedCount { get; init; }
    [BaseField("auth.operation.cleanup.advance.childReceiptScope", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ChildReceiptScope { get; init; }
    [BaseField("auth.operation.cleanup.advance.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
    [BaseField("auth.operation.cleanup.advance.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthCleanupPrepareRetirementV1
{
    [BaseField("auth.operation.cleanup.prepareRetirement.cleanupWorkId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CleanupWorkId { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.subjectKind", AllowedEnumLiterals = ["role", "user"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public required AuthCleanupSubjectKindV1 SubjectKind { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.expectedIncarnation")] public required BaseSubjectIncarnation ExpectedIncarnation { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.expectedTombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long ExpectedTombstoneSequence { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.retirementReceiptScope", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string RetirementReceiptScope { get; init; }
    [BaseField("auth.operation.cleanup.prepareRetirement.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthCleanupMutationResultV1
{
    [BaseField("auth.operation.cleanup.mutation.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.cleanup.mutation.result.state", AllowedEnumLiterals = ["awaitingSemanticRetirement", "complete", "draining", "readyToPurge", "waitingRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStateV1>))] public required AuthCleanupStateV1 State { get; init; }
    [BaseField("auth.operation.cleanup.mutation.result.step", AllowedEnumLiterals = ["deleteDeliveries", "deletePasskeys", "deleteRefreshTokens", "deleteRoleClaims", "deleteSessions", "deleteUserClaims", "deleteUserIdentities", "deleteUserLogins", "deleteUserRoles", "deleteUserTokens", "proveEmpty", "proveSubjectReady", "revokeRefreshTokens", "revokeSessions", "waitSecurityRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStepV1>))] public required AuthCleanupStepV1 Step { get; init; }
    [BaseField("auth.operation.cleanup.mutation.result.chunkOrdinal", MinimumInt64 = 0, HasMinimumInt64 = true)] public required long ChunkOrdinal { get; init; }
    [BaseField("auth.operation.cleanup.mutation.result.completedSteps", MinimumInt64 = 0, HasMinimumInt64 = true)] public required long CompletedSteps { get; init; }
    [BaseField("auth.operation.cleanup.mutation.result.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
}

internal sealed record AuthCleanupReconcileCursorV1
{
    [BaseField("auth.operation.cleanup.cursor.id", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string CursorId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.expectedRevision", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public RevisionToken? ExpectedRevision { get; init; }
    [BaseField("auth.operation.cleanup.cursor.expectedPassGeneration", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MinimumInt64 = 1, HasMinimumInt64 = true)] public long? ExpectedPassGeneration { get; init; }
    [BaseField("auth.operation.cleanup.cursor.expectedAfterTenantId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? ExpectedAfterTenantId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.expectedAfterSubjectKind", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["role", "user"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public AuthCleanupSubjectKindV1? ExpectedAfterSubjectKind { get; init; }
    [BaseField("auth.operation.cleanup.cursor.expectedAfterSubjectId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? ExpectedAfterSubjectId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.pageDigest", MaximumBytes = 32)] public required BaseBinary PageDigest { get; init; }
    [BaseField("auth.operation.cleanup.cursor.nextTenantId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? NextTenantId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.nextSubjectKind", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["role", "user"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public AuthCleanupSubjectKindV1? NextSubjectKind { get; init; }
    [BaseField("auth.operation.cleanup.cursor.nextSubjectId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? NextSubjectId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.wrap")] public required bool Wrap { get; init; }
    [BaseField("auth.operation.cleanup.cursor.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthCleanupReconcileCursorResultV1
{
    [BaseField("auth.operation.cleanup.cursor.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.cleanup.cursor.result.passGeneration", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long PassGeneration { get; init; }
    [BaseField("auth.operation.cleanup.cursor.result.afterTenantId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? AfterTenantId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.result.afterSubjectKind", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["role", "user"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public AuthCleanupSubjectKindV1? AfterSubjectKind { get; init; }
    [BaseField("auth.operation.cleanup.cursor.result.afterSubjectId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? AfterSubjectId { get; init; }
    [BaseField("auth.operation.cleanup.cursor.result.pageDigest", MaximumBytes = 32)] public required BaseBinary PageDigest { get; init; }
}
