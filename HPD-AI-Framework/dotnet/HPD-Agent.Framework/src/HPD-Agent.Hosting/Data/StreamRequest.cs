using System.Text.Json;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to start streaming agent responses.
/// Contains messages, context, client tools, and optional run configuration.
/// </summary>
/// <param name="Messages">User messages to send to the agent</param>
/// <param name="clientToolKits">Client-side tool groups available for this run</param>
/// <param name="Context">Additional context items</param>
/// <param name="State">Client state to pass through</param>
/// <param name="ExpandedContainers">Expanded container IDs</param>
/// <param name="HiddenTools">Tools to hide from the agent</param>
/// <param name="ResetClientState">Whether to reset client state</param>
/// <param name="RunConfig">Optional agent run configuration</param>
/// <param name="AgentId">Agent definition ID to run. Defaults to "default" if not specified.</param>
public record StreamRequest(
    List<StreamMessage> Messages,
    List<JsonElement>? clientToolKits,
    List<JsonElement>? Context,
    JsonElement? State,
    List<string>? ExpandedContainers,
    List<string>? HiddenTools,
    bool ResetClientState,
    StreamRunConfigDto? RunConfig,
    string? AgentId = null);
