using System.Collections.Immutable;

namespace HPD.Auth.Core.Audit;

public sealed class AuthAuditFact
{
    public string Key { get; }
    public string Value { get; }

    internal AuthAuditFact(string key, string value)
    {
        Key = key;
        Value = value;
    }
}

public sealed class AuthAuditWrite
{
    public string Action { get; }
    public string Category { get; }
    public bool Success { get; }
    public Guid? SubjectUserId { get; }
    public Guid? SubjectSessionId { get; }
    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public string? FailureCode { get; }
    public string? CorrelationId { get; }
    public ImmutableArray<AuthAuditFact> Facts { get; }

    internal AuthAuditWrite(
        string action,
        string category,
        bool success,
        Guid? subjectUserId,
        Guid? subjectSessionId,
        string? ipAddress,
        string? userAgent,
        string? failureCode,
        string? correlationId,
        ImmutableArray<AuthAuditFact> facts)
    {
        Action = action;
        Category = category;
        Success = success;
        SubjectUserId = subjectUserId;
        SubjectSessionId = subjectSessionId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        FailureCode = failureCode;
        CorrelationId = correlationId;
        Facts = facts;
    }
}

public sealed record AuthAuditRecord
{
    public required Guid AuditId { get; init; }
    public required Guid InstanceId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Action { get; init; }
    public required string Category { get; init; }
    public required bool Success { get; init; }
    public Guid? SubjectUserId { get; init; }
    public Guid? SubjectSessionId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? FailureCode { get; init; }
    public string? CorrelationId { get; init; }
    public ImmutableArray<AuthAuditFact> Facts { get; init; } = [];
}

public sealed record AuthAuditQuery
{
    public Guid? SubjectUserId { get; init; }
    public string? Action { get; init; }
    public string? Category { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 50;
}

public interface IAuthAuditWriter
{
    ValueTask WriteAsync(AuthAuditWrite write, CancellationToken cancellationToken = default);
}

public interface IAuthAuditReader
{
    ValueTask<ImmutableArray<AuthAuditRecord>> ReadAsync(
        AuthAuditQuery query,
        CancellationToken cancellationToken = default);
}
