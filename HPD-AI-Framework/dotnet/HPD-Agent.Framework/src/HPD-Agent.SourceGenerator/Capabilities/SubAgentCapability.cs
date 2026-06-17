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
        sb.AppendLine("HPDAIFunctionFactory.Create(");
        sb.AppendLine("    async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("    {");
        sb.AppendLine("        // Get sub-agent definition from method");

        if (IsStatic)
        {
            sb.AppendLine($"        var subAgentDef = {ToolHarness.ClassName}.{MethodName}();");
        }
        else
        {
            sb.AppendLine($"        var subAgentDef = instance.{MethodName}();");
        }
        sb.AppendLine();
        sb.AppendLine("        // Use the explicit runtime context supplied by the agent runtime");
        sb.AppendLine("        var parentCoordinator = functionContext?.GetParentEventCoordinator();");
        sb.AppendLine();
        sb.AppendLine("        // Build agent from inline config or stored agent id");
        sb.AppendLine("        AgentBuilder agentBuilder;");
        sb.AppendLine("        if (subAgentDef.SourceKind == SubAgentSourceKind.StoredAgent)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (string.IsNullOrWhiteSpace(subAgentDef.AgentId))");
        sb.AppendLine("                throw new System.InvalidOperationException(\"Stored-agent subagents require AgentId.\");");
        sb.AppendLine("            agentBuilder = new AgentBuilder().WithAgentId(subAgentDef.AgentId);");
        sb.AppendLine("            var parentAgentStore = functionContext?.GetParentAgentStore();");
        sb.AppendLine("            if (parentAgentStore != null)");
        sb.AppendLine("                agentBuilder.WithAgentStore(parentAgentStore);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            if (subAgentDef.AgentConfig == null)");
        sb.AppendLine("                throw new System.InvalidOperationException(\"Inline-config subagents require AgentConfig.\");");
        sb.AppendLine("            agentBuilder = new AgentBuilder(subAgentDef.AgentConfig);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        // Inline-config subagents inherit parent's chat client only when no provider is specified");
        sb.AppendLine("        var parentChatClient = functionContext?.GetParentChatClient();");
        sb.AppendLine("        if (subAgentDef.SourceKind == SubAgentSourceKind.InlineConfig &&");
        sb.AppendLine("            subAgentDef.AgentConfig?.ResolveClientConfig(HPD.Agent.Providers.ProviderClientFamily.Chat) == null && parentChatClient != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            agentBuilder.WithChatClient(parentChatClient);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        // Register ToolHarnesses if any are specified (uses AOT-compatible catalog)");
        sb.AppendLine("        if (subAgentDef.ToolHarnessTypes != null && subAgentDef.ToolHarnessTypes.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var toolType in subAgentDef.ToolHarnessTypes)");
        sb.AppendLine("            {");
        sb.AppendLine("                agentBuilder.WithToolHarness(toolType);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        // Extract query before BuildAsync so the generated function can resolve routing cleanly.
        sb.AppendLine("        // Extract query from arguments");
        sb.AppendLine("        var jsonArgs = arguments.GetJson();");
        sb.AppendLine("        var query = jsonArgs.TryGetProperty(\"query\", out var queryProp)");
        sb.AppendLine("            ? queryProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine();

        sb.AppendLine("        // Use the parent session store when available so subagent threads remain inspectable");
        sb.AppendLine("        var parentStore = functionContext?.GetParentSessionStore();");
        sb.AppendLine("        if (parentStore != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            agentBuilder.WithSessionStore(parentStore);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        var agent = await agentBuilder.BuildAsync();");
        sb.AppendLine();

        // Set up event bubbling via parent-child linking
        sb.AppendLine("        // Set up event bubbling through the parent coordinator");
        sb.AppendLine("        if (parentCoordinator != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            agent.EventCoordinator.SetParent(parentCoordinator);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Build execution context for event attribution
        sb.AppendLine("        // Build hierarchical execution context for event attribution");
        sb.AppendLine("        var parenTMetadata = functionContext?.GetParentAgentMetadata();");
        sb.AppendLine("        var agentId = agent.AgentId;");
        sb.AppendLine();
        sb.AppendLine("        var agentChain = parenTMetadata != null");
        sb.AppendLine($"            ? new System.Collections.Generic.List<string>(parenTMetadata.AgentChain) {{ \"{SubAgentName}\" }}");
        sb.AppendLine($"            : new System.Collections.Generic.List<string> {{ \"{SubAgentName}\" }};");
        sb.AppendLine();
        sb.AppendLine("        agent.AgentMetadata = new HPD.Agent.AgentMetadata");
        sb.AppendLine("        {");
        sb.AppendLine($"            AgentName = \"{SubAgentName}\",");
        sb.AppendLine("            AgentId = agentId,");
        sb.AppendLine("            ParentAgentId = parenTMetadata?.AgentId,");
        sb.AppendLine("            AgentChain = agentChain,");
        sb.AppendLine("            Depth = (parenTMetadata?.Depth ?? -1) + 1");
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        var textResult = new System.Text.StringBuilder();");
        sb.AppendLine();

        sb.AppendLine("        var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgentDef, functionContext, cancellationToken);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            using var outputSubscription = agent.SubscribeAny(evt =>");
        sb.AppendLine("            {");
        sb.AppendLine("                if (evt is HPD.Agent.TextDeltaEvent textDelta) textResult.Append(textDelta.Text);");
        sb.AppendLine("                return System.Threading.Tasks.ValueTask.CompletedTask;");
        sb.AppendLine("            });");
        sb.AppendLine("            await agent.RunAsync(new HPD.Agent.UserTextInputEvent(query)");
        sb.AppendLine("            {");
        sb.AppendLine("                SessionId = route.SessionId,");
        sb.AppendLine("                ThreadId = route.ThreadId");
        sb.AppendLine("            }, cancellationToken);");
        sb.AppendLine("            SubAgentRuntime.MarkCompleted(functionContext, route);");
        sb.AppendLine("            if (textResult.Length > 0) return textResult.ToString();");
        sb.AppendLine("            var fallbackThread = await agent.Config.SessionStore!.LoadThreadAsync(route.SessionId, route.ThreadId, cancellationToken);");
        sb.AppendLine("            return fallbackThread?.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (System.Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            SubAgentRuntime.MarkFailed(functionContext, route, ex);");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine("    },");
        sb.AppendLine("    new HPDAIFunctionFactoryOptions");
        sb.AppendLine("    {");
        sb.AppendLine($"        Name = \"{SubAgentName}\",");
        sb.AppendLine($"        Description = \"{EscapeString(Description)}\",");
        sb.AppendLine($"        RequiresPermission = {RequiresPermission.ToString().ToLower()},");
        sb.AppendLine("        SchemaProvider = () =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var options = new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false };");
        sb.AppendLine($"            var method = typeof({ParentToolHarnessName}).GetMethod(\"{MethodName}\")");
        sb.AppendLine("                ?.GetCustomAttributes(typeof(SubAgentAttribute), false)");
        sb.AppendLine("                ?.FirstOrDefault();");
        sb.AppendLine("            return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(");
        sb.AppendLine($"                typeof({ToolHarness.ClassName}SubAgentQueryArgs),");
        sb.AppendLine("                serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("        },");
        sb.AppendLine("        AdditionalProperties = new System.Collections.Generic.Dictionary<string, object>");
        sb.AppendLine("        {");
        sb.AppendLine("            [\"IsSubAgent\"] = true,");
        sb.AppendLine("            [\"ExecutionModel\"] = \"ThreadNative\",");
        sb.AppendLine($"            [\"ParentToolHarness\"] = \"{ToolHarness.ClassName}\"");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine(")");

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
