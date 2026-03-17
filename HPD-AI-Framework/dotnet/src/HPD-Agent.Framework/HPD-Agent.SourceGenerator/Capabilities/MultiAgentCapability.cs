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
    /// Whether the multi-agent requires permission to invoke.
    /// Defaults to true since orchestrating multiple agents is a significant action.
    /// </summary>
    public bool RequiresPermission { get; set; } = true;

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this multi-agent workflow.
    /// Creates an AIFunction wrapper that builds and invokes the workflow.
    /// </summary>
    /// <param name="parent">The parent Toolkit that contains this multi-agent (ToolkitInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var toolkit = (ToolkitInfo)parent;
        var sb = new StringBuilder();

        sb.AppendLine("HPDAIFunctionFactory.Create(");
        sb.AppendLine("    async (arguments, cancellationToken) =>");
        sb.AppendLine("    {");
        sb.AppendLine("        // Get workflow instance from method");

        if (IsStatic)
        {
            if (IsAsync)
            {
                sb.AppendLine($"        var workflow = await {toolkit.Name}.{MethodName}();");
            }
            else
            {
                sb.AppendLine($"        var workflow = {toolkit.Name}.{MethodName}();");
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

        // Get parent context for event bubbling, execution hierarchy, and chat client inheritance
        // V2: Use CurrentFunctionContext which IS set during function execution (unlike RootAgent which uses AsyncLocal)
        sb.AppendLine("        // Get parent context from CurrentFunctionContext (set during function execution)");
        sb.AppendLine("        var functionContext = HPD.Agent.Agent.CurrentFunctionContext;");
        sb.AppendLine("        var parentCoordinator = functionContext?.GetParentEventCoordinator();");
        sb.AppendLine("        var parentExecutionContext = functionContext?.GetParentExecutionContext();");
        sb.AppendLine("        var parentChatClient = functionContext?.GetParentChatClient();");
        sb.AppendLine();

        // Get input from arguments OR fall back to last user message from conversation
        sb.AppendLine("        // Extract input from arguments, fall back to last user message if not provided");
        sb.AppendLine("        var jsonArgs = arguments.GetJson();");
        sb.AppendLine("        var input = jsonArgs.TryGetProperty(\"input\", out var inputProp)");
        sb.AppendLine("            ? inputProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine();
        sb.AppendLine("        // If LLM didn't pass input, get last user message from conversation context");
        sb.AppendLine("        if (string.IsNullOrEmpty(input) && functionContext != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            var messages = functionContext.Analyze(s => s.CurrentMessages);");
        sb.AppendLine("            var lastUserMsg = messages?.LastOrDefault(m => m.Role == ChatRole.User);");
        sb.AppendLine("            input = lastUserMsg?.Text ?? string.Empty;");
        sb.AppendLine("        }");
        sb.AppendLine();

        if (StreamEvents)
        {
            // Streaming execution with full hierarchical context and chat client inheritance
            sb.AppendLine("        // Execute with streaming, capturing text output");
            sb.AppendLine("        var textResult = new System.Text.StringBuilder();");
            sb.AppendLine("        await foreach (var evt in workflow.ExecuteStreamingAsync(input, parentCoordinator, parentExecutionContext, parentChatClient, cancellationToken))");
            sb.AppendLine("        {");
            sb.AppendLine("            // TextDeltaEvent is in HPD.Agent namespace");
            sb.AppendLine("            if (evt is HPD.Agent.TextDeltaEvent textDelta)");
            sb.AppendLine("            {");
            sb.AppendLine("                textResult.Append(textDelta.Text);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        return textResult.ToString();");
        }
        else
        {
            // Non-streaming execution
            sb.AppendLine("        // Execute and return result");
            sb.AppendLine("        var result = await workflow.RunAsync(input, cancellationToken);");
            sb.AppendLine("        return result.FinalAnswer ?? result.Outputs.ToString();");
        }

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
        sb.AppendLine($"                typeof({toolkit.Name}MultiAgentInputArgs),");
        sb.AppendLine("                serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("        },");
        sb.AppendLine("        AdditionalProperties = new System.Collections.Generic.Dictionary<string, object>");
        sb.AppendLine("        {");
        sb.AppendLine("            [\"CapabilityType\"] = \"MultiAgent\",");
        sb.AppendLine("            [\"IsMultiAgent\"] = true,");
        sb.AppendLine("            [\"IsContainer\"] = false,");  // NOT a container - same as SubAgent
        sb.AppendLine($"            [\"ParentToolkit\"] = \"{toolkit.Name}\",");  // Required for collapsing visibility
        sb.AppendLine($"            [\"StreamEvents\"] = {StreamEvents.ToString().ToLower()},");
        sb.AppendLine($"            [\"TimeoutSeconds\"] = {TimeoutSeconds}");
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
        props["ParentToolkit"] = ParentToolkitName;  // Required for collapsing visibility
        props["StreamEvents"] = StreamEvents;
        props["TimeoutSeconds"] = TimeoutSeconds;
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
