using System.Collections.Generic;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents a multi-agent workflow capability - an orchestrated graph of multiple agents.
/// Decorated with [MultiAgent] attribute. MultiAgents are NOT containers - they're function wrappers
/// that delegate to a workflow (same pattern as SubAgent).
/// </summary>
internal class MultiAgentCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.MultiAgent;
    public override bool IsContainer => false;  // NOT a container - just a function that runs a workflow (like SubAgent)
    public override bool EmitsIntoCreateTools => true;
    public override bool RequiresInstance => !IsStatic;  // Instance required unless static method

    // ========== MultiAgent-Specific Properties ==========

    /// <summary>
    /// Method name (e.g., "CreateAnalysisWorkflow")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the method is async (returns Task&lt;AgentWorkflowInstance&gt;).
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// Whether this multi-agent method is static.
    /// Static methods don't require an instance parameter.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Whether to stream events during execution. Default: true.
    /// </summary>
    public bool StreamEvents { get; set; } = true;

    /// <summary>
    /// Timeout for workflow execution in seconds. Default: 300 (5 min).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Invocation mode policy declared by the [MultiAgent] attribute.
    /// </summary>
    public string InvocationModePolicy { get; set; } = "SynchronousOnly";

    /// <summary>
    /// Whether the multi-agent requires permission to invoke.
    /// Defaults to true since orchestrating multiple agents is a significant action.
    /// </summary>
    public bool RequiresPermission { get; set; } = true;

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this multi-agent workflow.
    /// Creates an AIFunction wrapper that builds and invokes the workflow.
    /// </summary>
    /// <param name="parent">The parent ToolHarness that contains this multi-agent (ToolHarnessInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var toolharness = (ToolHarnessInfo)parent;
        var sb = new StringBuilder();

        sb.AppendLine("HPDAIFunctionFactory.Create(");
        sb.AppendLine("    async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("    {");
        sb.AppendLine("        // Get workflow instance from method");

        if (IsStatic)
        {
            if (IsAsync)
            {
                sb.AppendLine($"        var workflow = await {toolharness.ClassName}.{MethodName}();");
            }
            else
            {
                sb.AppendLine($"        var workflow = {toolharness.ClassName}.{MethodName}();");
            }
        }
        else
        {
            if (IsAsync)
            {
                sb.AppendLine($"        var workflow = await instance.{MethodName}();");
            }
            else
            {
                sb.AppendLine($"        var workflow = instance.{MethodName}();");
            }
        }
        sb.AppendLine();

        sb.AppendLine("        // Extract input from arguments");
        sb.AppendLine("        var jsonArgs = arguments.GetJson();");
        sb.AppendLine("        var input = jsonArgs.TryGetProperty(\"input\", out var inputProp)");
        sb.AppendLine("            ? inputProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine("        var requestedMode = global::HPD.Agent.AgentInvocationModes.ReadRequestedMode(jsonArgs);");
        sb.AppendLine();
        sb.AppendLine("        var result = await global::HPD.Agent.MultiAgentRuntime.InvokeAsync(");
        sb.AppendLine("            new global::HPD.Agent.MultiAgentRuntime.MultiAgentInvocationRequest");
        sb.AppendLine("            {");
        sb.AppendLine("                Workflow = workflow,");
        sb.AppendLine($"                Name = \"{Name}\",");
        sb.AppendLine("                Input = input,");
        sb.AppendLine("                ParentContext = functionContext,");
        sb.AppendLine($"                StreamEvents = {StreamEvents.ToString().ToLower()},");
        sb.AppendLine($"                InvocationModePolicy = global::HPD.Agent.AgentInvocationModePolicy.{InvocationModePolicy},");
        sb.AppendLine("                RequestedMode = requestedMode");
        sb.AppendLine("            },");
        sb.AppendLine("            cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        return result.ToToolResult();");

        sb.AppendLine("    },");
        sb.AppendLine("    new HPDAIFunctionFactoryOptions");
        sb.AppendLine("    {");
        sb.AppendLine($"        Name = \"{Name}\",");
        sb.AppendLine($"        Description = \"{EscapeString(Description)}\",");
        sb.AppendLine($"        RequiresPermission = {RequiresPermission.ToString().ToLower()},");
        sb.AppendLine("        SchemaProvider = () =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false };");
        sb.AppendLine($"            return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(");
        sb.AppendLine($"                typeof({toolharness.ClassName}{(InvocationModePolicy == "ModelChoice" ? "MultiAgentInputWithModeArgs" : "MultiAgentInputArgs")}),");
        sb.AppendLine("                serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("        },");
        sb.AppendLine("        AdditionalProperties = new System.Collections.Generic.Dictionary<string, object>");
        sb.AppendLine("        {");
        var owner = string.IsNullOrEmpty(toolharness.Namespace) ? toolharness.ClassName : $"{toolharness.Namespace}.{toolharness.ClassName}";
        sb.AppendLine("            [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata");
        sb.AppendLine("            {");
        sb.AppendLine($"                Id = CapabilityId.Create(@\"generated:{toolharness.ClassName}.{Name}\"),");
        sb.AppendLine("                Kind = HPDCapabilityKind.MultiAgent,");
        if (toolharness.IsCollapsed)
            sb.AppendLine($"                ParentContainerIds = System.Collections.Immutable.ImmutableArray.Create(CapabilityId.Create(@\"generated:{owner}:harness\"))");
        sb.AppendLine("            },");
        sb.AppendLine("            [\"CapabilityType\"] = \"MultiAgent\",");
        sb.AppendLine("            [\"IsMultiAgent\"] = true,");
        sb.AppendLine("            [\"IsContainer\"] = false,");  // NOT a container - same as SubAgent
        sb.AppendLine($"            [\"ParentToolHarness\"] = \"{toolharness.EffectiveName}\",");  // Required for collapsing visibility
        sb.AppendLine($"            [\"StreamEvents\"] = {StreamEvents.ToString().ToLower()},");
        sb.AppendLine($"            [\"TimeoutSeconds\"] = {TimeoutSeconds},");
        sb.AppendLine($"            [\"InvocationModePolicy\"] = \"{InvocationModePolicy}\"");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine(")");

        return sb.ToString();
    }

    /// <summary>
    /// MultiAgents are NOT containers - they don't expand.
    /// Same pattern as SubAgent.
    /// </summary>
    public override string? GenerateContainerCode()
    {
        // MultiAgents are not containers - they execute as regular functions
        return null;
    }

    /// <summary>
    /// Gets additional metadata properties for this multi-agent.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();

        // NOTE: IsContainer is intentionally FALSE for MultiAgents (same pattern as SubAgent)
        // MultiAgents are function wrappers that delegate to workflows, not containers
        props["IsContainer"] = false;
        props["IsMultiAgent"] = true;
        props["ParentToolHarness"] = ParentToolHarnessName;  // Required for collapsing visibility
        props["StreamEvents"] = StreamEvents;
        props["TimeoutSeconds"] = TimeoutSeconds;
        props["InvocationModePolicy"] = InvocationModePolicy;
        props["RequiresPermission"] = RequiresPermission;

        return props;
    }

    // ========== Helper Methods ==========

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
