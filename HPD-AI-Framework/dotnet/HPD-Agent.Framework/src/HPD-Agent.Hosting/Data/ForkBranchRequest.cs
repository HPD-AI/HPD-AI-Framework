namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to fork a branch at a specific message id.
/// </summary>
/// <param name="NewBranchId">Unique identifier for the new branch</param>
/// <param name="FromMessageId">Message id where fork occurs (copies messages through that message)</param>
/// <param name="Name">Optional display name for the forked branch</param>
/// <param name="Description">Optional description</param>
/// <param name="Tags">Optional tags</param>
/// <param name="Metadata">Optional branch-level metadata</param>
public record ForkBranchRequest(
    string? NewBranchId,
    string FromMessageId,
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? Metadata = null);
