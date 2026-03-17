// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Middleware for Client tool registration, invocation, and visibility management.
/// </summary>
/// <remarks>
/// <para><b>Lifecycle Hooks:</b></para>
/// <list type="bullet">
/// <item><c>BeforeMessageTurnAsync</c> - Process initial tool registration from AgentClientInput</item>
/// <item><c>BeforeIterationAsync</c> - Apply tool visibility based on current state</item>
/// <item><c>BeforeFunctionAsync</c> - Intercept Client tool calls, emit request, wait for response</item>
/// </list>
///
/// <para><b>State Management:</b></para>
/// <para>
/// Uses <see cref="ClientToolStateData"/> stored in <c>context.State.MiddlewareState.ClientTool</c>.
/// State tracks registered tools, visibility, expanded containers, and pending augmentations.
/// </para>
/// </remarks>
public class ClientToolMiddleware : IAgentMiddleware
{
    private readonly ClientToolConfig _config;

    /// <summary>
    /// Creates a new ClientToolMiddleware with optional configuration.
    /// </summary>
    /// <param name="config">Configuration for timeout, validation, etc.</param>
    public ClientToolMiddleware(ClientToolConfig? config = null)
    {
        _config = config ?? new ClientToolConfig();
    }

    // ============================================
    // MESSAGE TURN LEVEL
    // ============================================

    /// <summary>
    /// Process initial Toolkit registration from AgentClientInput.
    /// Tools are always inside Toolkits - this is the only way to register Client tools.
    ///
    /// Registration is ATOMIC: if any Toolkit fails validation (including cross-Toolkit
    /// skill references), NO Toolkits are registered. This prevents partial state.
    /// </summary>
    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
    {
        // Get AgentClientInput from RunConfig (if provided)
        var clientinput = context.RunConfig.ClientToolInput;

        if (clientinput == null)
            return Task.CompletedTask;

        // Handle state persistence vs reset
        var existingState = context.Analyze(s => s.MiddlewareState.ClientTool());
        var state = clientinput.ResetClientState || existingState == null
            ? new ClientToolStateData()
            : existingState;

        // =============================================
        // PHASE 1: Register all Toolkits (tools only)
        // Build pending list without committing to state
        // =============================================
        var pendingToolkits = new List<clientToolKitDefinition>();

        if (clientinput.clientToolKits != null)
        {
            foreach (var Toolkit in clientinput.clientToolKits)
            {
                // Validate Toolkit structure (name, description, tools)
                Toolkit.Validate();

                // Validate JSON Schema if configured
                if (_config.ValidateSchemaOnRegistration)
                {
                    foreach (var tool in Toolkit.Tools)
                    {
                        ValidateToolSchema(tool);
                    }
                }

                pendingToolkits.Add(Toolkit);
                state = state.WithRegisteredToolkit(Toolkit);
            }
        }

        // =============================================
        // PHASE 2: Validate ALL cross-Toolkit references
        // If any skill references a non-existent tool, fail here
        // =============================================
        foreach (var Toolkit in pendingToolkits)
        {
            if (Toolkit.Skills == null) continue;

            foreach (var skill in Toolkit.Skills)
            {
                skill.ValidateReferences(Toolkit.Name, state.RegisteredToolKits);
            }
        }

        // =============================================
        // PHASE 3: All validations passed - apply settings
        // =============================================

        // Set initial expanded Toolkits
        if (clientinput.ExpandedContainers != null)
        {
            foreach (var toolName in clientinput.ExpandedContainers)
            {
                state = state.WithExpandedToolkit(toolName);
            }
        }

        // Set initial hidden tools
        if (clientinput.HiddenTools != null)
        {
            foreach (var tool in clientinput.HiddenTools)
            {
                state = state.WithHiddenTool(tool);
            }
        }

        // Set context
        if (clientinput.Context != null)
        {
            state = state.WithContext(clientinput.Context);
        }

        // Set state
        if (clientinput.State.HasValue)
        {
            state = state.WithState(clientinput.State);
        }

        // =============================================
        // PHASE 4: Commit state (atomic - all or nothing)
        // =============================================
        context.UpdateState(s => s with
        {
            MiddlewareState = s.MiddlewareState.WithClientTool(state)
        });

        // Emit registration confirmation (optional - works without EventCoordinator)
        context.TryEmit(new clientToolKitsRegisteredEvent(
           RegisteredToolKits: state.RegisteredToolKits.Keys.ToList(),
            TotalTools: state.RegisteredToolKits.Values.Sum(p => p.Tools.Count),
            Timestamp: DateTimeOffset.UtcNow));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates that a tool's JSON Schema is well-formed.
    /// Called during registration when ValidateSchemaOnRegistration is true.
    /// </summary>
    private static void ValidateToolSchema(ClientToolDefinition tool)
    {
        try
        {
            // Verify the schema is valid JSON
            var schemaText = tool.ParametersSchema.GetRawText();

            // Basic structure validation - ensure it's an object schema
            if (tool.ParametersSchema.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Schema must be a JSON object");
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException(
                $"Tool '{tool.Name}' has invalid JSON Schema: {ex.Message}",
                nameof(tool.ParametersSchema), ex);
        }
    }

    // ============================================
    // ITERATION LEVEL
    // ============================================

    /// <summary>
    /// Apply tool visibility based on current state.
    /// Converts Client tool definitions to AIFunction and adds to context.Options.Tools.
    /// </summary>
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken ct)
    {
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        if (state == null || state.RegisteredToolKits.Count == 0)
            return Task.CompletedTask;

        // Apply any pending augmentation from previous iteration
        if (state.PendingAugmentation != null)
        {
            state = ApplyPendingAugmentation(state);
            state = state.ClearPendingAugmentation();

            // Update state after augmentation
            context.UpdateState(s => s with
            {
                MiddlewareState = s.MiddlewareState.WithClientTool(state)
            });
        }

        // Convert Toolkits to AIFunctions
        var visibleAIFunctions = ConvertToolkitsToAIFunctions(state);

        // Clone options and add Client tools
        if (context.Options != null)
        {
            // V2: Options is mutable - modify Tools collection directly
            if (context.Options.Tools == null)
            {
                context.Options.Tools = new List<AITool>(visibleAIFunctions);
            }
            else
            {
                var toolsList = context.Options.Tools as IList<AITool>;
                if (toolsList != null)
                {
                    foreach (var tool in visibleAIFunctions)
                    {
                        toolsList.Add(tool);
                    }
                }
                else
                {
                    // Tools is not a mutable list, need to recreate
                    var existingTools = context.Options.Tools.ToList();
                    existingTools.AddRange(visibleAIFunctions);
                    context.Options.Tools = existingTools;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies pending augmentation to state.
    /// </summary>
    private ClientToolStateData ApplyPendingAugmentation(ClientToolStateData state)
    {
        var aug = state.PendingAugmentation;
        if (aug == null) return state;

        // Remove Toolkits
        if (aug.RemoveToolkits != null)
        {
            foreach (var toolName in aug.RemoveToolkits)
            {
                state = state.WithoutRegisteredToolkit(toolName);
            }
        }

        // Inject Toolkits
        if (aug.InjectToolkits != null)
        {
            foreach (var Toolkit in aug.InjectToolkits)
            {
                Toolkit.Validate();
                state = state.WithRegisteredToolkit(Toolkit);
            }
        }

        // Expand Toolkits
        if (aug.ExpandToolkits != null)
        {
            foreach (var toolName in aug.ExpandToolkits)
            {
                state = state.WithExpandedToolkit(toolName);
            }
        }

        // Collapse Toolkits
        if (aug.CollapseToolkits != null)
        {
            foreach (var toolName in aug.CollapseToolkits)
            {
                state = state.WithCollapsedToolkit(toolName);
            }
        }

        // Hide tools
        if (aug.HideTools != null)
        {
            foreach (var toolName in aug.HideTools)
            {
                state = state.WithHiddenTool(toolName);
            }
        }

        // Show tools
        if (aug.ShowTools != null)
        {
            foreach (var toolName in aug.ShowTools)
            {
                state = state.WithVisibleTool(toolName);
            }
        }

        // Add context
        if (aug.AddContext != null)
        {
            state = state.WithContext(aug.AddContext);
        }

        // Remove context
        if (aug.RemoveContext != null)
        {
            foreach (var key in aug.RemoveContext)
            {
                state = state.WithouTMetadata(key);
            }
        }

        // Update state (full replacement)
        if (aug.UpdateState.HasValue)
        {
            state = state.WithState(aug.UpdateState);
        }
        // Patch state (merge) - simplified implementation
        else if (aug.PatchState.HasValue)
        {
            // For now, patch just replaces - could implement deep merge later
            state = state.WithState(aug.PatchState);
        }

        return state;
    }

    /// <summary>
    /// Converts Client Toolkits to AIFunctions using ExternalToolCollapsingWrapper.
    /// </summary>
    private List<AIFunction> ConvertToolkitsToAIFunctions(ClientToolStateData state)
    {
        var allFunctions = new List<AIFunction>();

        foreach (var (toolName, Toolkit) in state.RegisteredToolKits)
        {
            // Convert ClientToolDefinitions to AIFunctions
            var toolAIFunctions = Toolkit.Tools
                .Where(t => !state.HiddenTools.Contains(t.Name))
                .Select(t => ConvertToolToAIFunction(t, toolName))
                .ToList();

            // Convert skills to AIFunctions (if any)
            var skillAIFunctions = new List<AIFunction>();
            if (Toolkit.Skills != null)
            {
                foreach (var skill in Toolkit.Skills)
                {
                    var skillFunction = ConvertSkillToAIFunction(skill, toolName);
                    skillAIFunctions.Add(skillFunction);
                }
            }

            // Determine if this Toolkit should be collapsed
            var shouldCollapse = Toolkit.StartCollapsed && !state.ExpandedToolkits.Contains(toolName);

            if (shouldCollapse)
            {
                // Use ExternalToolCollapsingWrapper pattern - creates container + Collapsed tools
                var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapclientToolKit(
                    toolName,
                    Toolkit.Description!,  // Validated to exist for collapsed Toolkits
                    toolAIFunctions,
                    maxFunctionNamesInDescription: 10,
                    FunctionResult: Toolkit.FunctionResult,
                   SystemPrompt: Toolkit.SystemPrompt);

                allFunctions.Add(container);
                allFunctions.AddRange(CollapsedTools);

                // Skills are always visible (collapsed state) - they're entry points
                allFunctions.AddRange(skillAIFunctions);
            }
            else
            {
                // Not collapsed - add tools and skills directly (no container)
                allFunctions.AddRange(toolAIFunctions);
                allFunctions.AddRange(skillAIFunctions);
            }
        }

        return allFunctions;
    }

    /// <summary>
    /// Converts a ClientToolDefinition to an AIFunction.
    /// The resulting function is intercepted by BeforeFunctionAsync.
    /// </summary>
    private static AIFunction ConvertToolToAIFunction(ClientToolDefinition tool, string toolName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, ct) =>
            {
                // This should never be called - ClientToolMiddleware intercepts
                throw new InvalidOperationException(
                    $"ClientTool '{tool.Name}' should not be invoked directly. " +
                    "Ensure ClientToolMiddleware is registered.");
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = tool.Description,
                RequiresPermission = tool.RequiresPermission,
                Validator = _ => new List<ValidationError>(),
                SchemaProvider = () => tool.ParametersSchema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsClientTool"] = true,
                    ["clientToolKitName"] = toolName,
                    ["SourceType"] = "clientToolKit"
                }
            });
    }

    /// <summary>
    /// Converts a ClientSkillDefinition to an AIFunction.
    /// </summary>
    private static AIFunction ConvertSkillToAIFunction(ClientSkillDefinition skill, string toolName)
    {
        // Build a return message with the FunctionResult (ephemeral, one-time)
        var returnMessage = $"Skill '{skill.Name}' activated.";
        if (!string.IsNullOrWhiteSpace(skill.FunctionResult))
        {
            returnMessage += $"\n\n{skill.FunctionResult}";
        }

        // Build document reference list for activation message (V3: content_read paths)
        if (skill.Documents != null && skill.Documents.Count > 0)
        {
            returnMessage += "\n\nReference documents available in the content store:";
            foreach (var doc in skill.Documents)
            {
                returnMessage += $"\n- content_read(\"/skills/{doc.DocumentId}\") — {doc.Description}";
            }
        }

        // Build referenced function names for ToolVisibilityManager
        var referencedFunctions = Array.Empty<string>();
        var referencedToolkits = Array.Empty<string>();
        if (skill.References != null && skill.References.Count > 0)
        {
            var funcList = new List<string>();
            var ToolkitSet = new HashSet<string>();
            foreach (var reference in skill.References)
            {
                // Build qualified function name
                var qualifiedName = string.IsNullOrEmpty(reference.ToolsetName)
                    ? reference.ToolName  // Local reference
                    : $"{reference.ToolsetName}.{reference.ToolName}";  // Cross-Toolkit reference
                funcList.Add(qualifiedName);

                // Track referenced Toolkits for visibility
                if (!string.IsNullOrEmpty(reference.ToolsetName))
                {
                    ToolkitSet.Add(reference.ToolsetName);
                }
            }
            referencedFunctions = funcList.ToArray();
            referencedToolkits = ToolkitSet.ToArray();
        }

        return HPDAIFunctionFactory.Create(
            async (args, ct) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = skill.Name,
                Description = skill.Description,
                RequiresPermission = false, // Skills are entry points
                Validator = _ => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptySchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsSkill"] = true,
                    ["IsContainer"] = true,  // Skills are containers (for ToolVisibilityManager)
                    ["IsClientSkill"] = true,
                    ["clientToolKitName"] = toolName,
                    ["SourceType"] = "clientToolKit",
                    // Dual-context architecture: FunctionResult for ephemeral, SystemPrompt for persistent
                    ["FunctionResult"] = skill.FunctionResult,
                    ["SystemPrompt"] = skill.SystemPrompt,
                    // Legacy key for backward compatibility with ContainerMiddleware
                    ["Instructions"] = skill.SystemPrompt,
                    // These are used by ToolVisibilityManager for visibility rules
                    ["ReferencedFunctions"] = referencedFunctions,
                    ["ReferencedToolkits"] = referencedToolkits
                }
            });
    }

    /// <summary>
    /// Creates an empty JSON schema for skills (no parameters).
    /// </summary>
    private static JsonElement CreateEmptySchema()
    {
        return Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(
            null,
            serializerOptions: HPDJsonContext.Default.Options,
            inferenceOptions: new Microsoft.Extensions.AI.AIJsonSchemaCreateOptions
            {
                IncludeSchemaKeyword = false
            }
        );
    }

    // ============================================
    // FUNCTION LEVEL
    // ============================================

    /// <summary>
    /// Intercept Client tool calls - emit request and wait for response.
    /// Detects Client tools by checking IsClientTool in AdditionalProperties.
    /// </summary>
    public async Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken ct)
    {
        // Check if this is a Client tool
        if (context.Function?.AdditionalProperties?.TryGetValue("IsClientTool", out var isClientTool) != true
            || isClientTool is not true)
        {
            return; // Not a Client tool, let normal execution proceed
        }

        var requestId = Guid.NewGuid().ToString();
        var toolName = context.Function.Name;

        // Emit invocation request
        context.Emit(new ClientToolInvokeRequestEvent(
            RequestId: requestId,
            ToolName: toolName,
            CallId: context.FunctionCallId ?? string.Empty,
            Arguments: context.Arguments?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value) ?? new Dictionary<string, object?>(),
            Description: context.Function.Description));

        // Wait for response
        ClientToolInvokeResponseEvent response;
        try
        {
            response = await context.WaitForResponseAsync<ClientToolInvokeResponseEvent>(
                requestId,
                _config.InvokeTimeout);
        }
        catch (TimeoutException)
        {
            context.BlockExecution = true;
            context.OverrideResult = HandleTimeout(toolName);
            return;
        }
        catch (OperationCanceledException)
        {
            context.BlockExecution = true;
            context.OverrideResult = $"Client tool '{toolName}' was cancelled.";
            return;
        }

        // Block execution (we have the result from Client)
        context.BlockExecution = true;

        if (response.Success)
        {
            // Convert Content to appropriate result format
            context.OverrideResult = ConvertContentToResult(response.Content);

            // Store augmentation for next iteration
            if (response.Augmentation != null)
            {
                var state = context.Analyze(s => s.MiddlewareState.ClientTool());
                if (state != null)
                {
                    var updatedState = state.WithPendingAugmentation(response.Augmentation);
                    context.UpdateState(s => s with
                    {
                        MiddlewareState = s.MiddlewareState.WithClientTool(updatedState)
                    });
                }
            }
        }
        else
        {
            context.OverrideResult = $"Error: {response.ErrorMessage ?? "Unknown error"}";
        }
    }

    /// <summary>
    /// Handles timeout based on configuration.
    /// </summary>
    private string HandleTimeout(string toolName)
    {
        return _config.DisconnectionStrategy switch
        {
            ClientDisconnectionStrategy.FallbackMessage =>
                string.Format(_config.FallbackMessageTemplate, toolName),
            ClientDisconnectionStrategy.FailFast =>
                throw new TimeoutException($"Client tool '{toolName}' timed out waiting for response."),
            _ => $"Client tool '{toolName}' timed out."
        };
    }

    /// <summary>
    /// Converts tool result content to a format suitable for FunctionResult.
    /// </summary>
    private static object? ConvertContentToResult(IReadOnlyList<IToolResultContent>? content)
    {
        if (content == null || content.Count == 0)
            return null;

        // Single text content - return as string
        if (content.Count == 1 && content[0] is TextContent text)
            return text.Text;

        // Single JSON content - return the value
        if (content.Count == 1 && content[0] is JsonContent json)
            return json.Value;

        // Multiple items or binary - return as structured list
        return content;
    }
}
