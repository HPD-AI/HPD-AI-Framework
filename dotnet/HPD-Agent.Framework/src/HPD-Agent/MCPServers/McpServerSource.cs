using HPD.Agent.MCP;
using System.ComponentModel;

namespace HPD.Agent;

/// <summary>Generated integration descriptor for one ToolHarness-owned MCP source.</summary>
/// <param name="Name">The generated source name.</param>
/// <param name="Description">The optional model-facing description.</param>
/// <param name="ParentToolHarness">The owning ToolHarness name.</param>
/// <param name="CollapseWithinToolHarness">Whether the source has a nested activation container.</param>
/// <param name="FromManifest">An optional final manifest path.</param>
/// <param name="ManifestServerName">An optional server registration selected from that manifest.</param>
/// <param name="PermissionOverride">An optional generated server permission binding.</param>
/// <param name="FactoryProvider">A generated direct capability-source factory delegate.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record McpServerSource(
    string Name,
    string? Description,
    string ParentToolHarness,
    bool CollapseWithinToolHarness,
    string? FromManifest,
    string? ManifestServerName,
    McpPermissionConfig? PermissionOverride,
    Func<object?, IAgentCapabilitySourceFactory?> FactoryProvider);
