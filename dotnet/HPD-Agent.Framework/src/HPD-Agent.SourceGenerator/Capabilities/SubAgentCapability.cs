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
    public override bool EmitsIntoCreateTools => true;
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
        var ToolHarness = (ToolHarnessInfo)parent;
        var sb = new StringBuilder();

        // PHASE 2A FIX: Return just the factory call (NO local function wrapper, NO functions.Add)
        // The caller (HPDToolSourceGenerator) will add the functions.Add() wrapper
        if (IsStatic)
            sb.AppendLine($"global::HPD.Agent.SubAgentRuntime.CreateFrozenFunction({ToolHarness.ClassName}.{MethodName}(), subAgentDef => HPDAIFunctionFactory.Create(");
        else
            sb.AppendLine($"global::HPD.Agent.SubAgentRuntime.CreateFrozenFunction(instance.{MethodName}(), subAgentDef => HPDAIFunctionFactory.Create(");
        sb.AppendLine("    async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("    {");
        sb.AppendLine("        // Extract input from arguments");
        sb.AppendLine("        var jsonArgs = arguments.GetJson();");
        sb.AppendLine("        var input = jsonArgs.TryGetProperty(\"input\", out var inputProp)");
        sb.AppendLine("            ? inputProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine("        var taskName = jsonArgs.TryGetProperty(\"taskName\", out var taskNameProp)");
        sb.AppendLine("            ? taskNameProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine("        var requestedMode = global::HPD.Agent.AgentInvocationModes.ReadRequestedMode(jsonArgs);");
        sb.AppendLine("        var requestedContext = global::HPD.Agent.SubAgentContexts.ReadRequestedContext(jsonArgs);");
        sb.AppendLine();
        sb.AppendLine("        var result = await global::HPD.Agent.SubAgentRuntime.InvokeAsync(");
        sb.AppendLine("            new global::HPD.Agent.SubAgentRuntime.SubAgentInvocationRequest");
        sb.AppendLine("            {");
        sb.AppendLine("                Definition = subAgentDef,");
        sb.AppendLine("                Input = input,");
        sb.AppendLine("                TaskName = taskName,");
        sb.AppendLine("                ParentContext = functionContext,");
        sb.AppendLine("                RequestedMode = requestedMode,");
        sb.AppendLine("                RequestedContext = requestedContext");
        sb.AppendLine("            },");
        sb.AppendLine("            cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        return result.ToToolResult();");
        sb.AppendLine("    },");
        sb.AppendLine("    new HPDAIFunctionFactoryOptions");
        sb.AppendLine("    {");
        sb.AppendLine($"        Name = \"{SubAgentName}\",");
        sb.AppendLine($"        Description = \"{EscapeString(Description)}\",");
        sb.AppendLine($"        RequiresPermission = {RequiresPermission.ToString().ToLower()},");
        sb.AppendLine("        SchemaProvider = () =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false };");
        sb.AppendLine("            var schema = global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(");
        sb.AppendLine("                subAgentDef.InvocationModePolicy == global::HPD.Agent.AgentInvocationModePolicy.ModelChoice");
        sb.AppendLine($"                    ? typeof({ToolHarness.ClassName}SubAgentInputWithModeArgs)");
        sb.AppendLine($"                    : typeof({ToolHarness.ClassName}SubAgentInputArgs),");
        sb.AppendLine("                serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("            return global::HPD.Agent.SubAgentContexts.CreateSchema(schema, subAgentDef.ContextPolicy);");
        sb.AppendLine("        },");
        sb.AppendLine("        AdditionalProperties = new System.Collections.Generic.Dictionary<string, object>");
        sb.AppendLine("        {");
        var owner = string.IsNullOrEmpty(ToolHarness.Namespace) ? ToolHarness.ClassName : $"{ToolHarness.Namespace}.{ToolHarness.ClassName}";
        sb.AppendLine("            [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata");
        sb.AppendLine("            {");
        sb.AppendLine($"                Id = CapabilityId.Create(@\"generated:{ToolHarness.ClassName}.{SubAgentName}\"),");
        sb.AppendLine("                Kind = HPDCapabilityKind.SubAgent,");
        if (ToolHarness.IsCollapsed)
            sb.AppendLine($"                ParentContainerIds = System.Collections.Immutable.ImmutableArray.Create(CapabilityId.Create(@\"generated:{owner}:harness\"))");
        sb.AppendLine("            },");
        sb.AppendLine("            [\"IsSubAgent\"] = true,");
        sb.AppendLine("            [\"ExecutionModel\"] = \"ThreadNative\",");
        sb.AppendLine($"            [\"ParentToolHarness\"] = \"{ToolHarness.ClassName}\",");
        sb.AppendLine($"            [\"SubAgentMember\"] = \"{MethodName}\",");
        sb.AppendLine($"            [\"SubAgentAssembly\"] = typeof({ToolHarness.ClassName}).Assembly.GetName().Name ?? string.Empty,");
        sb.AppendLine("            [\"SubAgentDefinition\"] = subAgentDef");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("))");

        return sb.ToString();
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
