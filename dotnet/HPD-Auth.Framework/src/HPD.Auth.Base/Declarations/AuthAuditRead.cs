using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.audit.v1", typeof(AuthAuditReadJsonContext),
    RequiredGrantId = "auth.audit.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.audit.v1.row.instanceId", "auth.read.audit.v1.row.occurredAt", "auth.read.audit.v1.row.subjectUserId", "auth.read.audit.v1.row.subjectSessionId", "auth.read.audit.v1.row.ipAddress", "auth.read.audit.v1.row.userAgent", "auth.read.audit.v1.row.correlationId", "auth.read.audit.v1.row.facts"],
    SystemSourceIds = ["auth.securityAudit"])]
internal sealed partial record AuthAuditReadV1
{
    [BaseReadParameter("auth.read.audit.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.subjectUserId")] public Guid? SubjectUserId { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.action")] public string? Action { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.category")] public string? Category { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.correlationId")] public string? CorrelationId { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.from"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? From { get; init; }
    [BaseReadParameter("auth.read.audit.v1.parameter.to"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? To { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.audit.v1.row.auditId")] public required Guid AuditId { get; init; }
        [BaseReadField("auth.read.audit.v1.row.instanceId")] public required Guid InstanceId { get; init; }
        [BaseReadField("auth.read.audit.v1.row.occurredAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OccurredAt { get; init; }
        [BaseReadField("auth.read.audit.v1.row.action")] public required string Action { get; init; }
        [BaseReadField("auth.read.audit.v1.row.category")] public required string Category { get; init; }
        [BaseReadField("auth.read.audit.v1.row.success")] public required bool Success { get; init; }
        [BaseReadField("auth.read.audit.v1.row.subjectUserId")] public Guid? SubjectUserId { get; init; }
        [BaseReadField("auth.read.audit.v1.row.subjectSessionId")] public Guid? SubjectSessionId { get; init; }
        [BaseReadField("auth.read.audit.v1.row.ipAddress")] public string? IpAddress { get; init; }
        [BaseReadField("auth.read.audit.v1.row.userAgent")] public string? UserAgent { get; init; }
        [BaseReadField("auth.read.audit.v1.row.failureCode")] public string? FailureCode { get; init; }
        [BaseReadField("auth.read.audit.v1.row.correlationId")] public string? CorrelationId { get; init; }
        [BaseReadField("auth.read.audit.v1.row.facts")] public required BaseCanonicalJson Facts { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAuditReadV1, Row> read)
    {
        read.From(AuthSecurityAuditRecordV1.Collection, "audit", out BaseReadSource<AuthSecurityAuditRecordV1> audit);
        BaseReadOperand<Guid> subject = read.OptionalParameter(Parameters.SubjectUserId);
        BaseReadOperand<DateTimeOffset> from = read.OptionalParameter(Parameters.From);
        BaseReadOperand<DateTimeOffset> to = read.OptionalParameter(Parameters.To);
        BaseReadOperand<string> action = read.Parameter(Parameters.Action);
        BaseReadOperand<string> category = read.Parameter(Parameters.Category);
        BaseReadOperand<string> correlation = read.Parameter(Parameters.CorrelationId);
        read.Where(audit.Field(AuthSecurityAuditRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(subject.IsNull().Or(audit.OptionalField(AuthSecurityAuditRecordV1.Fields.SubjectUserId).Equal(subject)))
                .And(action.IsNull().Or(audit.Field(AuthSecurityAuditRecordV1.Fields.Action).Equal(action)))
                .And(category.IsNull().Or(audit.Field(AuthSecurityAuditRecordV1.Fields.Category).Equal(category)))
                .And(correlation.IsNull().Or(audit.Field(AuthSecurityAuditRecordV1.Fields.CorrelationId).Equal(correlation)))
                .And(from.IsNull().Or(audit.Field(AuthSecurityAuditRecordV1.Fields.OccurredAt).GreaterThanOrEqual(from)))
                .And(to.IsNull().Or(audit.Field(AuthSecurityAuditRecordV1.Fields.OccurredAt).LessThan(to))))
            .Project(Row.Fields.AuditId, audit.Field(AuthSecurityAuditRecordV1.Fields.Id))
            .Project(Row.Fields.InstanceId, audit.Field(AuthSecurityAuditRecordV1.Fields.TenantId))
            .Project(Row.Fields.OccurredAt, audit.Field(AuthSecurityAuditRecordV1.Fields.OccurredAt))
            .Project(Row.Fields.Action, audit.Field(AuthSecurityAuditRecordV1.Fields.Action))
            .Project(Row.Fields.Category, audit.Field(AuthSecurityAuditRecordV1.Fields.Category))
            .Project(Row.Fields.Success, audit.Field(AuthSecurityAuditRecordV1.Fields.Success))
            .Project(Row.Fields.SubjectUserId, audit.Field(AuthSecurityAuditRecordV1.Fields.SubjectUserId))
            .Project(Row.Fields.SubjectSessionId, audit.Field(AuthSecurityAuditRecordV1.Fields.SubjectSessionId))
            .Project(Row.Fields.IpAddress, audit.Field(AuthSecurityAuditRecordV1.Fields.IpAddress))
            .Project(Row.Fields.UserAgent, audit.Field(AuthSecurityAuditRecordV1.Fields.UserAgent))
            .Project(Row.Fields.FailureCode, audit.Field(AuthSecurityAuditRecordV1.Fields.FailureCode))
            .Project(Row.Fields.CorrelationId, audit.Field(AuthSecurityAuditRecordV1.Fields.CorrelationId))
            .Project(Row.Fields.Facts, audit.Field(AuthSecurityAuditRecordV1.Fields.Facts))
            .OrderBy(audit.Field(AuthSecurityAuditRecordV1.Fields.OccurredAt), QuerySortDirection.Desc)
            .OrderBy(audit.Field(AuthSecurityAuditRecordV1.Fields.Id), QuerySortDirection.Desc)
            .Limits(200, 524_288, 24, 1_000)
            .AllowOffsetPagination(100_000);
    }
}

[JsonSerializable(typeof(AuthAuditReadV1))]
[JsonSerializable(typeof(AuthAuditReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthAuditReadJsonContext : JsonSerializerContext;
