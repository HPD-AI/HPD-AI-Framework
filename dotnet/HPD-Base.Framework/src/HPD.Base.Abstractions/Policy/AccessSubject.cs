namespace HPD.Base.Policy;

public sealed record AccessSubject
{
    public required AccessSubjectKind Kind { get; init; }
    public string? Id { get; init; }
    public string? Qualifier { get; init; }
    public string? TenantId { get; init; }
    public string? Source { get; init; }
}
