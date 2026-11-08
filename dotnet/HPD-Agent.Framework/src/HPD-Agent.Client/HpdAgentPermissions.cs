using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>Stable fields used by ordinary permission prompt UI.</summary>
public sealed record HpdAgentPermissionPrompt(
    string PermissionId,
    string CallId,
    string FunctionName,
    string? Action,
    JsonElement Arguments,
    string? Reason);

/// <summary>A simple allow/deny answer to one permission request.</summary>
public sealed record HpdAgentPermissionAnswer(
    string PermissionId,
    bool Approved,
    string? Reason = null);
