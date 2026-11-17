namespace HPD.Agent.Studio;

/// <summary>Reads and writes the exact stored Agent configuration used by Studio review and execution.</summary>
public interface IAgentStudioConfigurationAuthority
{
    ValueTask<AgentStudioConfigurationSnapshot?> GetAsync(
        string agentId, CancellationToken cancellationToken);
    ValueTask<AgentStudioConfigurationWriteResult> WriteAsync(
        string agentId, AgentConfig draft, CancellationToken cancellationToken);
}

/// <summary>One current stored configuration and its persistence timestamp.</summary>
public sealed record AgentStudioConfigurationSnapshot(
    string AgentId, AgentConfig Config, DateTime UpdatedAt);

/// <summary>A write that completed, or a safe rejection reported before influence.</summary>
public sealed record AgentStudioConfigurationWriteResult(
    AgentStudioConfigurationWriteStatus Status, DateTime? UpdatedAt = null,
    string? ErrorCode = null, string? ErrorMessage = null)
{
    public static AgentStudioConfigurationWriteResult Success(DateTime updatedAt) =>
        new(AgentStudioConfigurationWriteStatus.Success, updatedAt);
    public static AgentStudioConfigurationWriteResult NotFound { get; } =
        new(AgentStudioConfigurationWriteStatus.NotFound);
    public static AgentStudioConfigurationWriteResult Conflict { get; } =
        new(AgentStudioConfigurationWriteStatus.Conflict);
    public static AgentStudioConfigurationWriteResult Validation { get; } =
        new(AgentStudioConfigurationWriteStatus.Validation);
}

public enum AgentStudioConfigurationWriteStatus : byte
{
    Success = 1,
    NotFound,
    Conflict,
    Validation,
}
