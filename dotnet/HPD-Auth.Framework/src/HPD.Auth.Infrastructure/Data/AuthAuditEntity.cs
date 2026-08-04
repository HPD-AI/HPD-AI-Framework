namespace HPD.Auth.Infrastructure.Data;

internal sealed class AuthAuditEntity
{
    public Guid AuditId { get; init; }
    public Guid InstanceId { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool Success { get; init; }
    public Guid? SubjectUserId { get; init; }
    public Guid? SubjectSessionId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? FailureCode { get; init; }
    public string? CorrelationId { get; init; }
    public string FactsJson { get; init; } = "[]";
}
