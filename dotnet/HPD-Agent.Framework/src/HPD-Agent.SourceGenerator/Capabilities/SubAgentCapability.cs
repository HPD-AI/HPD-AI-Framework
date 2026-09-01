using System.Collections.Generic;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents a sub-agent capability - a wrapper that delegates to another agent.
/// Decorated with [SubAgent] attribute. SubAgents are NOT containers - they're wrappers.
/// </summary>
internal class SubAgentCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.SubAgent;
    public override bool IsContainer => false;  // SubAgents are NOT containers (they're wrappers that delegate)
    public override bool EmitsIntoCreateTools => false;
    public override bool RequiresInstance => !IsStatic;  // Instance required unless static method

    // ========== SubAgent-Specific Properties ==========

    /// <summary>
    /// Method name (e.g., "CreateResearchAgent")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Sub-agent name (from SubAgent.FromConfig(...) or FromAgentId(...) call)
    /// </summary>
    public string SubAgentName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this sub-agent method is static.
    /// Static methods don't require an instance parameter.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Whether the sub-agent requires permission to invoke.
    /// Defaults to true since delegating to another agent is a significant action.
    /// Can be overridden with [RequiresPermission] attribute (absence = default true).
    /// </summary>
    public bool RequiresPermission { get; set; } = true;

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this sub-agent.
    /// Creates an AIFunction wrapper that builds and invokes the agent.
    ///
    /// Phase 3: Full implementation migrated from SubAgentCodeGenerator.GenerateSubAgentFunction().
    /// </summary>
    /// <param name="parent">The parent ToolHarness that contains this sub-agent (ToolHarnessInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        throw new InvalidOperationException(
            "Subagent capabilities emit immutable action descriptors through the harness collection seam.");
    }

    /// <summary>
    /// SubAgents are NOT containers, so this returns null.
    /// </summary>
    public override string? GenerateContainerCode()
    {
        // SubAgents are wrappers, not containers
        return null;
    }

    /// <summary>
    /// Gets additional metadata properties for this sub-agent.
    /// Gets metadata that lets runtime scoping treat sub-agents as thread-native wrappers.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();

        // NOTE: IsContainer is intentionally FALSE for SubAgents
        // SubAgents are wrappers that delegate to another agent, not containers
        props["IsContainer"] = false;
        props["IsSubAgent"] = true;
        props["ExecutionModel"] = "ThreadNative";
        props["ParentToolHarness"] = ParentToolHarnessName;
        props["RequiresPermission"] = RequiresPermission;

        return props;
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Formats a property value for code generation.
    /// </summary>
    private string FormatPropertyValue(object value)
    {
        return value switch
        {
            string s => $"\"{EscapeString(s)}\"",
            bool b => b.ToString().ToLower(),
            int i => i.ToString(),
            string[] arr => $"new string[] {{ {string.Join(", ", System.Linq.Enumerable.Select(arr, s => $"\"{s}\""))} }}",
            _ => value.ToString() ?? "null"
        };
    }

    /// <summary>
    /// Escapes quotes and newlines in strings for code generation.
    /// </summary>
    private static string EscapeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }
}
