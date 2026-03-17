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
    /// Sub-agent name (from SubAgentFactory.Create(...) call)
    /// </summary>
    public string SubAgentName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this sub-agent method is static.
    /// Static methods don't require an instance parameter.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Session mode for this sub-agent.
    /// - Stateless: New thread per invocation (default)
    /// - SharedSession: Shared thread across calls
    /// - PerSession: User-managed session
    /// </summary>
    public string SessionMode { get; set; } = "Stateless";

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
    /// <param name="parent">The parent Toolkit that contains this sub-agent (ToolkitInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    public override string GenerateRegistrationCode(object parent)
    {
        var Toolkit = (ToolkitInfo)parent;
        var sb = new StringBuilder();

        // PHASE 2A FIX: Return just the factory call (NO local function wrapper, NO functions.Add)
        // The caller (HPDToolSourceGenerator) will add the functions.Add() wrapper
        sb.AppendLine("HPDAIFunctionFactory.Create(");
        sb.AppendLine("    async (arguments, cancellationToken) =>");
        sb.AppendLine("    {");
        sb.AppendLine("        // Get sub-agent definition from method");

        if (IsStatic)
        {
            sb.AppendLine($"        var subAgentDef = {Toolkit.Name}.{MethodName}();");
        }
        else
        {
            sb.AppendLine($"        var subAgentDef = instance.{MethodName}();");
        }
        sb.AppendLine();
        sb.AppendLine("        // Get parent context from CurrentFunctionContext (set during function execution)");
        sb.AppendLine("        var functionContext = HPD.Agent.Agent.CurrentFunctionContext;");
        sb.AppendLine("        var parentCoordinator = functionContext?.GetParentEventCoordinator();");
        sb.AppendLine();
        sb.AppendLine("        // Build agent from config");
        sb.AppendLine("        var agentBuilder = new AgentBuilder(subAgentDef.AgentConfig);");
        sb.AppendLine();
        sb.AppendLine("        // If no provider specified in SubAgent config, inherit parent's chat client");
        sb.AppendLine("        var parentChatClient = functionContext?.GetParentChatClient();");
        sb.AppendLine("        if (subAgentDef.AgentConfig.Provider == null && parentChatClient != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            agentBuilder.WithChatClient(parentChatClient);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        // Register Toolkits if any are specified (uses AOT-compatible catalog)");
        sb.AppendLine("        if (subAgentDef.ToolkitTypes != null && subAgentDef.ToolkitTypes.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var toolType in subAgentDef.ToolkitTypes)");
        sb.AppendLine("            {");
        sb.AppendLine("                agentBuilder.WithTools(toolType);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        // Extract query (needed by all modes, before BuildAsync so PerSession can attach store first)
        sb.AppendLine("        // Extract query from arguments");
        sb.AppendLine("        var jsonArgs = arguments.GetJson();");
        sb.AppendLine("        var query = jsonArgs.TryGetProperty(\"query\", out var queryProp)");
        sb.AppendLine("            ? queryProp.GetString() ?? string.Empty");
        sb.AppendLine("            : string.Empty;");
        sb.AppendLine();

        // PerSession needs to attach the parent's store to the builder BEFORE BuildAsync
        sb.AppendLine("        // PerSession: attach parent's session store before building so RunAsync(sessionId) works");
        sb.AppendLine("        if (subAgentDef.SessionMode == SubAgentSessionMode.PerSession)");
        sb.AppendLine("        {");
        sb.AppendLine("            var parentStore = functionContext?.Session?.Store;");
        sb.AppendLine("            if (parentStore != null)");
        sb.AppendLine("                agentBuilder.WithSessionStore(parentStore);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        var agent = await agentBuilder.BuildAsync();");
        sb.AppendLine();

        // Set up event bubbling via parent-child linking
        sb.AppendLine("        // Set up event bubbling (use parentCoordinator from CurrentFunctionContext)");
        sb.AppendLine("        if (parentCoordinator != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            agent.EventCoordinator.SetParent(parentCoordinator);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Build execution context for event attribution
        sb.AppendLine("        // Build hierarchical execution context for event attribution");
        sb.AppendLine("        var currentAgent = HPD.Agent.Agent.RootAgent;");
        sb.AppendLine("        var parenTMetadata = currentAgent?.ExecutionContext;");
        sb.AppendLine("        var randomId = System.Guid.NewGuid().ToString(\"N\")[..8];");
        sb.AppendLine("        var sanitizedAgentName = System.Text.RegularExpressions.Regex.Replace(");
        sb.AppendLine($"            \"{SubAgentName}\",");
        sb.AppendLine("            @\"[^a-zA-Z0-9]\",");
        sb.AppendLine("            \"_\");");
        sb.AppendLine();
        sb.AppendLine("        var agentId = parenTMetadata != null");
        sb.AppendLine("            ? $\"{parenTMetadata.AgentId}-{sanitizedAgentName}-{randomId}\"");
        sb.AppendLine("            : $\"{sanitizedAgentName}-{randomId}\";");
        sb.AppendLine();
        sb.AppendLine("        var agentChain = parenTMetadata != null");
        sb.AppendLine($"            ? new System.Collections.Generic.List<string>(parenTMetadata.AgentChain) {{ \"{SubAgentName}\" }}");
        sb.AppendLine($"            : new System.Collections.Generic.List<string> {{ \"{SubAgentName}\" }};");
        sb.AppendLine();
        sb.AppendLine("        agent.ExecutionContext = new HPD.Agent.AgentExecutionContext");
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

        sb.AppendLine("        switch (subAgentDef.SessionMode)");
        sb.AppendLine("        {");

        // SharedSession — fixed session, guard against duplicate CreateSessionAsync (pre-existing bug fix)
        sb.AppendLine("            case SubAgentSessionMode.SharedSession:");
        sb.AppendLine("            {");
        sb.AppendLine("                var sessionId = subAgentDef.SharedSessionId ?? System.Guid.NewGuid().ToString(\"N\");");
        sb.AppendLine("                var branchId = subAgentDef.SharedBranchId ?? \"main\";");
        sb.AppendLine("                // Only create session on first invocation — SharedSession reuses it across calls");
        sb.AppendLine("                var existingSession = await agent.Config.SessionStore!.LoadSessionAsync(sessionId, cancellationToken);");
        sb.AppendLine("                if (existingSession == null)");
        sb.AppendLine("                    await agent.CreateSessionAsync(sessionId, cancellationToken: cancellationToken);");
        sb.AppendLine("                await foreach (var evt in agent.RunAsync(query, sessionId: sessionId, branchId: branchId, cancellationToken: cancellationToken))");
        sb.AppendLine("                {");
        sb.AppendLine("                    parentCoordinator?.Emit(evt);");
        sb.AppendLine("                    if (evt is HPD.Agent.TextDeltaEvent td) textResult.Append(td.Text);");
        sb.AppendLine("                }");
        sb.AppendLine("                if (textResult.Length > 0) return textResult.ToString();");
        sb.AppendLine("                var fallbackBranch = await agent.Config.SessionStore!.LoadBranchAsync(sessionId, branchId, cancellationToken);");
        sb.AppendLine("                return fallbackBranch?.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;");
        sb.AppendLine("            }");

        // PerSession — inherit parent's session + branch via shared store; session already exists, skip CreateSessionAsync
        sb.AppendLine("            case SubAgentSessionMode.PerSession:");
        sb.AppendLine("            {");
        sb.AppendLine("                var parentSessionId = functionContext?.SessionId;");
        sb.AppendLine("                var parentBranchId = functionContext?.BranchId;");
        sb.AppendLine("                if (parentSessionId != null && parentBranchId != null)");
        sb.AppendLine("                {");
        sb.AppendLine("                    // Session already exists in the inherited store — do NOT call CreateSessionAsync");
        sb.AppendLine("                    await foreach (var evt in agent.RunAsync(query, sessionId: parentSessionId, branchId: parentBranchId, cancellationToken: cancellationToken))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        parentCoordinator?.Emit(evt);");
        sb.AppendLine("                        if (evt is HPD.Agent.TextDeltaEvent td) textResult.Append(td.Text);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    return textResult.ToString();");
        sb.AppendLine("                }");
        sb.AppendLine("                // Fallback: no parent session available — behave stateless");
        sb.AppendLine("                goto case SubAgentSessionMode.Stateless;");
        sb.AppendLine("            }");

        // Stateless — fresh isolated session per call (default)
        sb.AppendLine("            case SubAgentSessionMode.Stateless:");
        sb.AppendLine("            default:");
        sb.AppendLine("            {");
        sb.AppendLine("                var sessionId = System.Guid.NewGuid().ToString(\"N\");");
        sb.AppendLine("                await agent.CreateSessionAsync(sessionId, cancellationToken: cancellationToken);");
        sb.AppendLine("                await foreach (var evt in agent.RunAsync(query, sessionId: sessionId, branchId: \"main\", cancellationToken: cancellationToken))");
        sb.AppendLine("                {");
        sb.AppendLine("                    parentCoordinator?.Emit(evt);");
        sb.AppendLine("                    if (evt is HPD.Agent.TextDeltaEvent textDelta) textResult.Append(textDelta.Text);");
        sb.AppendLine("                }");
        sb.AppendLine("                if (textResult.Length > 0) return textResult.ToString();");
        sb.AppendLine("                var fallbackBranch = await agent.Config.SessionStore!.LoadBranchAsync(sessionId, \"main\", cancellationToken);");
        sb.AppendLine("                return fallbackBranch?.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;");
        sb.AppendLine("            }");
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
        sb.AppendLine($"            var method = typeof({ParentToolkitName}).GetMethod(\"{MethodName}\")");
        sb.AppendLine("                ?.GetCustomAttributes(typeof(SubAgentAttribute), false)");
        sb.AppendLine("                ?.FirstOrDefault();");
        sb.AppendLine("            return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(");
        sb.AppendLine($"                typeof({Toolkit.Name}SubAgentQueryArgs),");
        sb.AppendLine("                serializerOptions: global::Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions,");
        sb.AppendLine("                inferenceOptions: options");
        sb.AppendLine("            );");
        sb.AppendLine("        },");
        sb.AppendLine("        AdditionalProperties = new System.Collections.Generic.Dictionary<string, object>");
        sb.AppendLine("        {");
        sb.AppendLine("            [\"IsSubAgent\"] = true,");
        sb.AppendLine($"            [\"SessionMode\"] = \"{SessionMode}\",");
        sb.AppendLine($"            [\"ParentToolkit\"] = \"{Toolkit.Name}\"");
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
    /// CRITICAL: This metadata schema must be byte-for-byte identical to the old system
    /// for runtime ContainerMiddleware compatibility.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();

        // NOTE: IsContainer is intentionally FALSE for SubAgents
        // SubAgents are wrappers that delegate to another agent, not containers
        props["IsContainer"] = false;
        props["IsSubAgent"] = true;
        props["SessionMode"] = SessionMode;
        props["ParentToolkit"] = ParentToolkitName;
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
