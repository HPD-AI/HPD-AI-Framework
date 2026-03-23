namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to create a new branch.
/// </summary>
/// <param name="BranchId">Unique identifier for the new branch</param>
/// <param name="Name">Optional display name</param>
/// <param name="Description">Optional description</param>
/// <param name="Tags">Optional tags</param>
public record CreateBranchRequest(
    string BranchId,
    string? Name,
    string? Description,
    List<string>? Tags,
    string? AgentId = null);
