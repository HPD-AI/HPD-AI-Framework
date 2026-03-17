namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to update branch metadata. All fields are optional — only non-null fields are applied.
/// </summary>
/// <param name="Name">New display name, or null to leave unchanged</param>
/// <param name="Description">New description, or null to leave unchanged</param>
/// <param name="Tags">New tags list, or null to leave unchanged</param>
public record UpdateBranchRequest(
    string? Name,
    string? Description,
    List<string>? Tags);
