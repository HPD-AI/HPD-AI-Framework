using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

// Generated optional/non-null Base authorities intentionally use nullable CLR annotations.
#pragma warning disable CS8620

internal sealed record AuthAuditAppendV1
{
    [BaseField("auth.operation.audit.append.auditId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid AuditId { get; init; }
    [BaseField("auth.operation.audit.append.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.audit.append.occurredAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OccurredAt { get; init; }
    [BaseField("auth.operation.audit.append.action", MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string Action { get; init; }
    [BaseField("auth.operation.audit.append.category", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string Category { get; init; }
    [BaseField("auth.operation.audit.append.success")] public required bool Success { get; init; }
    [BaseField("auth.operation.audit.append.subjectUserId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? SubjectUserId { get; init; }
    [BaseField("auth.operation.audit.append.subjectSessionId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))] public Guid? SubjectSessionId { get; init; }
    [BaseField("auth.operation.audit.append.ipAddress", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? IpAddress { get; init; }
    [BaseField("auth.operation.audit.append.userAgent", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 512, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserAgent { get; init; }
    [BaseField("auth.operation.audit.append.failureCode", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? FailureCode { get; init; }
    [BaseField("auth.operation.audit.append.correlationId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? CorrelationId { get; init; }
    [BaseField("auth.operation.audit.append.facts", MaximumCanonicalJsonBytes = 1024, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 1024, MaximumJsonTotalNameUtf8Bytes = 1024)] public required BaseCanonicalJson Facts { get; init; }
}

internal sealed record AuthAuditAppendResultV1
{
    [BaseField("auth.operation.audit.append.result.auditId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid AuditId { get; init; }
    [BaseField("auth.operation.audit.append.result.revision")] public required RevisionToken Revision { get; init; }
}

[BaseRegisteredModuleMutation("hpd.auth.audit.append.v1", typeof(AuthBaseJsonSerializerContext),
    typeof(AuthAuditAppendV1), typeof(AuthAuditAppendResultV1), Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId, GrantId = "auth.operation.audit.append")]
internal static partial class AuthAuditAppendOperationV1
{
    private const string AuditCapture = "hpd.auth.audit.append.capture.audit";
    private const string CreateStatement = "hpd.auth.audit.append.statement.000.createAudit";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(
        new BaseRegisteredModuleMutationDefinition
        {
            Id = "hpd.auth.audit.append.v1", Version = 1, OwningModuleId = AuthBaseContract.ModuleId,
            GrantId = "auth.operation.audit.append", Audience = BaseModuleMutationAudience.Service,
            RequestTypeId = "hpd.auth.type.auth-audit-append-v1.v1",
            ResultTypeId = "hpd.auth.type.auth-audit-append-result-v1.v1",
            SystemCollectionIds = [AuthSecurityAuditRecordV1.Collection.Id],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant { CollectionId = AuthSecurityAuditRecordV1.Collection.Id, GrantId = "auth.audit.append" }],
            GenerationCellIds = [], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [BaseModuleMutationTemplateBuilder.CaptureRecord(AuditCapture, AuditId("capture"), BaseModuleCapturePresence.RequireMissing)],
                Guards = [], Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(CreateAudit()),
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject("hpd.auth.audit.append.expression.result.000",
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.AuditId,
                            BaseModuleMutationTemplateBuilder.Request("hpd.auth.audit.append.expression.resultAuditId.000", RequestProperties.AuditId)),
                        BaseModuleMutationTemplateBuilder.Property(ResultProperties.Revision,
                            BaseModuleMutationTemplateBuilder.CommittedRevision("hpd.auth.audit.append.expression.revision.000", CreateStatement)))),
            },
            Limits = AuthModuleMutationDefaults.Limits(), ReceiptPolicy = AuthModuleMutationDefaults.Receipt(),
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

    private static BaseModuleValue<BaseRecordId<AuthSecurityAuditRecordV1>> AuditId(string suffix) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromGuid<AuthSecurityAuditRecordV1>(
            $"hpd.auth.audit.append.expression.auditRecordId.{suffix}",
            BaseModuleMutationTemplateBuilder.Request($"hpd.auth.audit.append.expression.auditIdRequest.{suffix}", RequestProperties.AuditId));
    private static BaseModuleCreateStatement CreateAudit() => BaseModuleMutationTemplateBuilder.Create(
        CreateStatement, AuditId("create"), BaseModuleMutationTemplateBuilder.Object<AuthSecurityAuditRecordV1>(
            "hpd.auth.audit.append.expression.payload.000",
            Field(AuthSecurityAuditRecordV1.Fields.Action, RequestProperties.Action, "action"),
            Field(AuthSecurityAuditRecordV1.Fields.Category, RequestProperties.Category, "category"),
            Field(AuthSecurityAuditRecordV1.Fields.CorrelationId, RequestProperties.CorrelationId, "correlationId"),
            Field(AuthSecurityAuditRecordV1.Fields.Facts, RequestProperties.Facts, "facts"),
            Field(AuthSecurityAuditRecordV1.Fields.FailureCode, RequestProperties.FailureCode, "failureCode"),
            Field(AuthSecurityAuditRecordV1.Fields.Id, RequestProperties.AuditId, "id"),
            Field(AuthSecurityAuditRecordV1.Fields.IpAddress, RequestProperties.IpAddress, "ipAddress"),
            Field(AuthSecurityAuditRecordV1.Fields.OccurredAt, RequestProperties.OccurredAt, "occurredAt"),
            Field(AuthSecurityAuditRecordV1.Fields.SubjectSessionId, RequestProperties.SubjectSessionId, "subjectSessionId"),
            Field(AuthSecurityAuditRecordV1.Fields.SubjectUserId, RequestProperties.SubjectUserId, "subjectUserId"),
            Field(AuthSecurityAuditRecordV1.Fields.Success, RequestProperties.Success, "success"),
            Field(AuthSecurityAuditRecordV1.Fields.TenantId, RequestProperties.TenantId, "tenantId"),
            Field(AuthSecurityAuditRecordV1.Fields.UserAgent, RequestProperties.UserAgent, "userAgent")));
    private static BaseModuleFieldValue<AuthSecurityAuditRecordV1> Field<T>(BaseField<AuthSecurityAuditRecordV1, T> field,
        BaseModuleRequestProperty<AuthAuditAppendV1, T> property, string id) => BaseModuleMutationTemplateBuilder.Field(
        field, BaseModuleMutationTemplateBuilder.Request($"hpd.auth.audit.append.expression.{id}.000", property));
}
