namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to fork a branch at a specific message index.
/// </summary>
/// <param name="NewBranchId">Unique identifier for the new branch</param>
/// <param name="FromMessageIndex">Message index where fork occurs (copies messages 0..index)</param>
/// <param name="Name">Optional display name for the forked branch</param>
/// <param name="Description">Optional description</param>
/// <param name="Tags">Optional tags</param>
public record ForkBranchRequest(
    string? NewBranchId,
    int FromMessageIndex,
    string? Name,
    string? Description,
    List<string>? Tags,
    string? AgentId = null);
