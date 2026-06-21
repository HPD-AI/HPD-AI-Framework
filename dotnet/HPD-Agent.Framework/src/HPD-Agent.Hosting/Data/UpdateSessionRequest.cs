namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to update session metadata (PATCH semantics).
/// Provided keys are added or overwritten; unmentioned keys are preserved.
/// Set a key to null to remove it.
/// </summary>
/// <param name="Metadata">Metadata fields to update</param>
public record UpdateSessionRequest(
    Dictionary<string, object?> Metadata);
