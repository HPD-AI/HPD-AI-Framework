using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal enum AuthImportStatusV1 { prepared, importing, verified, attested, activated, failed }
internal enum AuthCleanupSubjectKindV1 { user, role }
internal enum AuthCleanupStepV1
{
    revokeSessions, revokeRefreshTokens, deleteDeliveries, waitSecurityRetention,
    deleteSessions, deleteRefreshTokens, deletePasskeys, deleteUserClaims,
    deleteUserLogins, deleteUserTokens, deleteUserRoles, deleteUserIdentities,
    proveEmpty, finalizeSubject, deleteRoleClaims,
}
internal enum AuthCleanupStateV1 { draining, waitingRetention, readyToPurge, awaitingSemanticRetirement, complete }
internal enum AuthMaintenanceKindV1 { sessionExpiration, refreshExpiration, deliveryExpiration }

[BaseCollection("auth.securityAudit", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.audit.action")]
[BaseIndexPart("auth.idx.audit.action", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.audit.action", 1, nameof(Action))]
[BaseIndexPart("auth.idx.audit.action", 2, nameof(OccurredAt))]
[BaseIndexPart("auth.idx.audit.action", 3, nameof(Id))]
[BaseIndex("auth.idx.audit.category")]
[BaseIndexPart("auth.idx.audit.category", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.audit.category", 1, nameof(Category))]
[BaseIndexPart("auth.idx.audit.category", 2, nameof(OccurredAt))]
[BaseIndexPart("auth.idx.audit.category", 3, nameof(Id))]
[BaseIndex("auth.idx.audit.correlation")]
[BaseIndexPart("auth.idx.audit.correlation", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.audit.correlation", 1, nameof(CorrelationId))]
[BaseIndexPart("auth.idx.audit.correlation", 2, nameof(OccurredAt))]
[BaseIndexPart("auth.idx.audit.correlation", 3, nameof(Id))]
[BaseIndexPredicate("auth.idx.audit.correlation", "root", BaseIndexPredicateNodeKind.And, Children = ["defined", "not-null"])]
[BaseIndexPredicate("auth.idx.audit.correlation", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(CorrelationId))]
[BaseIndexPredicate("auth.idx.audit.correlation", "not-null", BaseIndexPredicateNodeKind.Not, Children = ["null"])]
[BaseIndexPredicate("auth.idx.audit.correlation", "null", BaseIndexPredicateNodeKind.IsNull, Field = nameof(CorrelationId))]
[BaseIndex("auth.idx.audit.subject")]
[BaseIndexPart("auth.idx.audit.subject", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.audit.subject", 1, nameof(SubjectUserId))]
[BaseIndexPart("auth.idx.audit.subject", 2, nameof(OccurredAt))]
[BaseIndexPart("auth.idx.audit.subject", 3, nameof(Id))]
[BaseIndexPredicate("auth.idx.audit.subject", "root", BaseIndexPredicateNodeKind.And, Children = ["defined", "not-null"])]
[BaseIndexPredicate("auth.idx.audit.subject", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(SubjectUserId))]
[BaseIndexPredicate("auth.idx.audit.subject", "not-null", BaseIndexPredicateNodeKind.Not, Children = ["null"])]
[BaseIndexPredicate("auth.idx.audit.subject", "null", BaseIndexPredicateNodeKind.IsNull, Field = nameof(SubjectUserId))]
internal sealed partial record AuthSecurityAuditRecordV1
{
    [BaseField("auth.securityAudit.id", Operators = BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.securityAudit.tenantId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.securityAudit.occurredAt", Operators = BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OccurredAt { get; init; }
    [BaseField("auth.securityAudit.action", MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Action { get; init; }
    [BaseField("auth.securityAudit.category", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Category { get; init; }
    [BaseField("auth.securityAudit.success"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Success { get; init; }
    [BaseField("auth.securityAudit.subjectUserId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public Guid? SubjectUserId { get; init; }
    [BaseField("auth.securityAudit.subjectSessionId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public Guid? SubjectSessionId { get; init; }
    [BaseField("auth.securityAudit.ipAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? IpAddress { get; init; }
    [BaseField("auth.securityAudit.userAgent", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 512, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? UserAgent { get; init; }
    [BaseField("auth.securityAudit.failureCode", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? FailureCode { get; init; }
    [BaseField("auth.securityAudit.correlationId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? CorrelationId { get; init; }
    [BaseField("auth.securityAudit.facts", MaximumCanonicalJsonBytes = 1024, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 1024, MaximumJsonTotalNameUtf8Bytes = 1024), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson Facts { get; init; }
}

[BaseCollection("auth.dataProtectionKeys", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.dp.name", Unique = true)]
[BaseIndexPart("auth.idx.dp.name", 0, nameof(ApplicationDiscriminator))]
[BaseIndexPart("auth.idx.dp.name", 1, nameof(FriendlyName))]
internal sealed partial record AuthDataProtectionKeyRecordV1
{
    [BaseField("auth.dataProtectionKeys.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.dataProtectionKeys.applicationDiscriminator", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string ApplicationDiscriminator { get; init; }
    [BaseField("auth.dataProtectionKeys.friendlyName", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string FriendlyName { get; init; }
    [BaseField("auth.dataProtectionKeys.canonicalXml", MaximumBytes = 262144), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary CanonicalXml { get; init; }
    [BaseField("auth.dataProtectionKeys.contentDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary ContentDigest { get; init; }
    [BaseField("auth.dataProtectionKeys.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.dataProtectionKeys.formatVersion", MinimumInt32 = 1, HasMinimumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int FormatVersion { get; init; }
}

[BaseCollection("auth.importState", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.import.source", Unique = true)]
[BaseIndexPart("auth.idx.import.source", 0, nameof(SourceSchemaId))]
[BaseIndexPart("auth.idx.import.source", 1, nameof(SourceStoreDigest))]
internal sealed partial record AuthImportStateRecordV1
{
    [BaseField("auth.importState.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.importState.sourceSchemaId", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string SourceSchemaId { get; init; }
    [BaseField("auth.importState.sourceCatalogDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary SourceCatalogDigest { get; init; }
    [BaseField("auth.importState.sourceStoreDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary SourceStoreDigest { get; init; }
    [BaseField("auth.importState.targetGraphChecksum", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary TargetGraphChecksum { get; init; }
    [BaseField("auth.importState.status", AllowedEnumLiterals = ["activated", "attested", "failed", "importing", "prepared", "verified"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthImportStatusV1>))] public required AuthImportStatusV1 Status { get; init; }
    [BaseField("auth.importState.tableOrdinal", MinimumInt32 = 0, HasMinimumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int TableOrdinal { get; init; }
    [BaseField("auth.importState.chunkOrdinal", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long ChunkOrdinal { get; init; }
    [BaseField("auth.importState.rowsImported", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long RowsImported { get; init; }
    [BaseField("auth.importState.manifest", MaximumBytes = 1048576), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseBinary Manifest { get; init; }
    [BaseField("auth.importState.manifestDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary ManifestDigest { get; init; }
    [BaseField("auth.importState.attestationKeyId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? AttestationKeyId { get; init; }
    [BaseField("auth.importState.attestation", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumBytes = 128), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public BaseBinary? Attestation { get; init; }
    [BaseField("auth.importState.failureCode", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? FailureCode { get; init; }
    [BaseField("auth.importState.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.importState.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
    [BaseField("auth.importState.activatedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? ActivatedAt { get; init; }
}

[BaseCollection("auth.cleanupWork", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.cleanupWork.subject", Unique = true)]
[BaseIndexPart("auth.idx.cleanupWork.subject", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.cleanupWork.subject", 1, nameof(SubjectKind))]
[BaseIndexPart("auth.idx.cleanupWork.subject", 2, nameof(SubjectId))]
[BaseIndexPart("auth.idx.cleanupWork.subject", 3, nameof(Incarnation))]
[BaseIndexPart("auth.idx.cleanupWork.subject", 4, nameof(Id))]
[BaseIndex("auth.idx.cleanupWork.progress")]
[BaseIndexPart("auth.idx.cleanupWork.progress", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.cleanupWork.progress", 1, nameof(State))]
[BaseIndexPart("auth.idx.cleanupWork.progress", 2, nameof(UpdatedAt))]
[BaseIndexPart("auth.idx.cleanupWork.progress", 3, nameof(Id))]
internal sealed partial record AuthCleanupWorkRecordV1
{
    [BaseField("auth.cleanupWork.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.cleanupWork.tenantId"), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.cleanupWork.subjectKind", AllowedEnumLiterals = ["role", "user"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public required AuthCleanupSubjectKindV1 SubjectKind { get; init; }
    [BaseField("auth.cleanupWork.subjectId"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid SubjectId { get; init; }
    [BaseField("auth.cleanupWork.userSubject", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseSubjectReference(typeof(AuthUserSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)] public BaseSubjectReference<AuthUserSubject>? UserSubject { get; init; }
    [BaseField("auth.cleanupWork.roleSubject", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseSubjectReference(typeof(AuthRoleSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)] public BaseSubjectReference<AuthRoleSubject>? RoleSubject { get; init; }
    [BaseField("auth.cleanupWork.incarnation", MaximumBytes = 24), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary Incarnation { get; init; }
    [BaseField("auth.cleanupWork.tombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long TombstoneSequence { get; init; }
    [BaseField("auth.cleanupWork.tombstoneRevision", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string TombstoneRevision { get; init; }
    [BaseField("auth.cleanupWork.workflowVersion", MinimumInt32 = 1, HasMinimumInt32 = true, MaximumInt32 = 1, HasMaximumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int WorkflowVersion { get; init; }
    [BaseField("auth.cleanupWork.step", AllowedEnumLiterals = ["deleteDeliveries", "deletePasskeys", "deleteRefreshTokens", "deleteRoleClaims", "deleteSessions", "deleteUserClaims", "deleteUserIdentities", "deleteUserLogins", "deleteUserRoles", "deleteUserTokens", "finalizeSubject", "proveEmpty", "revokeRefreshTokens", "revokeSessions", "waitSecurityRetention"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStepV1>))] public required AuthCleanupStepV1 Step { get; init; }
    [BaseField("auth.cleanupWork.chunkOrdinal", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long ChunkOrdinal { get; init; }
    [BaseField("auth.cleanupWork.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
    [BaseField("auth.cleanupWork.completedSteps", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long CompletedSteps { get; init; }
    [BaseField("auth.cleanupWork.lastChildReceiptScope", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? LastChildReceiptScope { get; init; }
    [BaseField("auth.cleanupWork.state", AllowedEnumLiterals = ["awaitingSemanticRetirement", "complete", "draining", "readyToPurge", "waitingRetention"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStateV1>))] public required AuthCleanupStateV1 State { get; init; }
    [BaseField("auth.cleanupWork.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.cleanupWork.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
}

[BaseCollection("auth.maintenanceCursors", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.maintenanceCursor.id", Unique = true)]
[BaseIndexPart("auth.idx.maintenanceCursor.id", 0, nameof(Id))]
internal sealed partial record AuthMaintenanceCursorRecordV1
{
    [BaseField("auth.maintenanceCursors.id", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.maintenanceCursors.passGeneration", MinimumInt64 = 1, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long PassGeneration { get; init; }
    [BaseField("auth.maintenanceCursors.afterTenantId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public Guid? AfterTenantId { get; init; }
    [BaseField("auth.maintenanceCursors.afterSubjectKind", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["role", "user"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))] public AuthCleanupSubjectKindV1? AfterSubjectKind { get; init; }
    [BaseField("auth.maintenanceCursors.afterSubjectId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public Guid? AfterSubjectId { get; init; }
    [BaseField("auth.maintenanceCursors.lastPageDigest", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public BaseBinary? LastPageDigest { get; init; }
    [BaseField("auth.maintenanceCursors.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
}

[BaseCollection("auth.maintenanceRuns", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.maintenanceRun.activation", Unique = true)]
[BaseIndexPart("auth.idx.maintenanceRun.activation", 0, nameof(ActivationId))]
[BaseIndexPart("auth.idx.maintenanceRun.activation", 1, nameof(Id))]
internal sealed partial record AuthMaintenanceRunRecordV1
{
    [BaseField("auth.maintenanceRuns.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.maintenanceRuns.activationId", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string ActivationId { get; init; }
    [BaseField("auth.maintenanceRuns.kind", AllowedEnumLiterals = ["deliveryExpiration", "refreshExpiration", "sessionExpiration"]), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthMaintenanceKindV1>))] public required AuthMaintenanceKindV1 Kind { get; init; }
    [BaseField("auth.maintenanceRuns.cutoff"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset Cutoff { get; init; }
    [BaseField("auth.maintenanceRuns.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}
