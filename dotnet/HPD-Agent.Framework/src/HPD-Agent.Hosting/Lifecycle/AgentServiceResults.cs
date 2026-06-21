namespace HPD.Agent.Hosting.Lifecycle;

public enum AgentServiceStatus
{
    Success,
    NotFound,
    Conflict,
    ValidationError
}

public sealed record AgentServiceResult(
    AgentServiceStatus Status,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? ErrorMessages = null)
{
    public static AgentServiceResult Success { get; } = new(AgentServiceStatus.Success);
    public static AgentServiceResult NotFound { get; } = new(AgentServiceStatus.NotFound);
    public static AgentServiceResult Conflict { get; } = new(AgentServiceStatus.Conflict);

    public static AgentServiceResult ConflictWith(string errorCode, string errorMessage) =>
        new(AgentServiceStatus.Conflict, errorCode, errorMessage);

    public static AgentServiceResult Validation(string errorCode, string errorMessage) =>
        new(AgentServiceStatus.ValidationError, errorCode, errorMessage);

    public static AgentServiceResult Validation(string errorCode, IReadOnlyList<string> errorMessages) =>
        new(AgentServiceStatus.ValidationError, errorCode, null, errorMessages);
}

public sealed record AgentServiceResult<T>(
    AgentServiceStatus Status,
    T? Value,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? ErrorMessages = null)
{
    public static AgentServiceResult<T> Success(T value) =>
        new(AgentServiceStatus.Success, value);

    public static AgentServiceResult<T> NotFound { get; } =
        new(AgentServiceStatus.NotFound, default);

    public static AgentServiceResult<T> Conflict { get; } =
        new(AgentServiceStatus.Conflict, default);

    public static AgentServiceResult<T> ConflictWith(string errorCode, string errorMessage) =>
        new(AgentServiceStatus.Conflict, default, errorCode, errorMessage);

    public static AgentServiceResult<T> Validation(string errorCode, string errorMessage) =>
        new(AgentServiceStatus.ValidationError, default, errorCode, errorMessage);

    public static AgentServiceResult<T> Validation(string errorCode, IReadOnlyList<string> errorMessages) =>
        new(AgentServiceStatus.ValidationError, default, errorCode, null, errorMessages);
}
