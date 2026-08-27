// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Collections.Immutable;
using HPD.Agent.Middleware;
using Microsoft.Extensions.DependencyInjection;
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
/// <item><c>BeforeFunctionAsync</c> - Intercept Client tool calls, emit request, wait for outcome</item>
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
    /// Process initial ToolHarness registration from AgentClientInput.
    /// Tools are always inside ToolHarnesses - this is the only way to register Client tools.
    ///
    /// Registration is ATOMIC: if any ToolHarness fails validation (including cross-ToolHarness
    /// skill references), NO ToolHarnesses are registered. This prevents partial state.
    /// </summary>
    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
    {
        // Get AgentClientInput from RunConfig (if provided)
        var clientinput = context.RunConfig.Tools?.ClientInput;

        var providerReferences = context.RunConfig.Tools?.ClientAppProviders;
        if (clientinput == null &&
            (providerReferences is null || providerReferences.Count == 0))
        {
            return;
        }

        // Handle state persistence vs reset
        var existingState = context.Analyze(s => s.MiddlewareState.ClientTool());
        var state = clientinput?.ResetClientState == true || existingState == null
            ? new ClientToolStateData()
            : existingState;

        // =============================================
        // PHASE 1: Register all ToolHarnesses (tools only)
        // Build pending list without committing to state
        // =============================================
        var pendingToolHarnesses = new List<clientToolHarnessDefinition>();

        if (clientinput?.clientToolHarnesses != null)
        {
            foreach (var ToolHarness in clientinput.clientToolHarnesses)
            {
                // Validate ToolHarness structure (name, description, tools)
                ToolHarness.Validate();

                // Validate JSON Schema if configured
                if (_config.ValidateSchemaOnRegistration)
                {
                    foreach (var tool in ToolHarness.Tools)
                    {
                        ValidateToolSchema(tool);
                    }
                }

                pendingToolHarnesses.Add(ToolHarness);
                state = state.WithRegisteredToolHarness(ToolHarness);
            }
        }

        state = await RegisterProviderToolHarnessesAsync(context, state, pendingToolHarnesses, ct)
            .ConfigureAwait(false);

        // =============================================
        // PHASE 2: Validate ALL cross-ToolHarness references
        // If any skill references a non-existent tool, fail here
        // =============================================
        foreach (var ToolHarness in pendingToolHarnesses)
        {
            if (ToolHarness.Skills == null) continue;

            foreach (var skill in ToolHarness.Skills)
            {
                skill.ValidateReferences(ToolHarness.Name, state.RegisteredToolHarnesses);
            }
        }

        // =============================================
        // PHASE 3: All validations passed - apply settings
        // =============================================

        // Set initial expanded ToolHarnesses
        if (clientinput?.ExpandedContainers != null)
        {
            foreach (var toolName in clientinput.ExpandedContainers)
            {
                state = state.WithExpandedToolHarness(toolName);
            }
        }

        // Set initial hidden tools
        if (clientinput?.HiddenTools != null)
        {
            foreach (var tool in clientinput.HiddenTools)
            {
                state = state.WithHiddenTool(tool);
            }
        }

        // Set context
        if (clientinput?.Context != null)
        {
            state = state.WithContext(clientinput.Context);
        }

        // Set state
        if (clientinput?.State.HasValue == true)
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

        return;
    }

    private static async ValueTask<ClientToolStateData> RegisterProviderToolHarnessesAsync(
        BeforeMessageTurnContext context,
        ClientToolStateData state,
        List<clientToolHarnessDefinition> pendingToolHarnesses,
        CancellationToken cancellationToken)
    {
        var references = context.RunConfig.Tools?.ClientAppProviders;
        if (references is null || references.Count == 0)
            return state;

        var registry = context.Services?.GetService<IClientToolProviderRegistry>();
        if (registry is null)
        {
            if (references.Any(static r => r.Required))
                throw new InvalidOperationException("Client app providers were requested, but no provider registry is available.");

            return state;
        }

        foreach (var reference in references)
        {
            var bindingResult = await registry.TryAcquireBindingAsync(
                reference,
                new ClientToolProviderBindingScope
                {
                    AgentId = context.AgentName,
                    SessionId = context.SessionId,
                    ThreadId = context.ThreadId
                },
                cancellationToken).ConfigureAwait(false);

            if (bindingResult is null)
            {
                if (reference.Required)
                    throw new InvalidOperationException($"Required client app provider '{reference.Name}' is not available.");

                continue;
            }

            var provider = bindingResult.Provider;
            var lease = bindingResult.Lease;
            var manifest = provider.Manifest;
            if (manifest is null)
                continue;

            var selectedHarnesses = SelectHarnesses(manifest, reference);
            foreach (var (harness, selector) in selectedHarnesses)
            {
                var visibleHarnessName = CreateVisibleName(manifest.AppProvider.Name, harness.Name);
                var selectedTools = SelectTools(harness, selector, reference)
                    .Select(tool =>
                    {
                        var visibleToolName = CreateVisibleName(manifest.AppProvider.Name, harness.Name, tool.Name);
                        var binding = new ClientToolProviderToolBinding
                        {
                            BindingId = lease.BindingId,
                            ClientRuntimeId = provider.ClientRuntimeId,
                            ConnectionId = provider.ConnectionId,
                            AppProviderName = manifest.AppProvider.Name,
                            HarnessName = harness.Name,
                            ProviderToolName = tool.Name,
                            VisibleToolName = visibleToolName
                        };
                        state = state.WithProviderToolBinding(binding);
                        return tool with { Name = visibleToolName };
                    })
                    .ToArray();

                if (selectedTools.Length == 0)
                    continue;

                var providerHarness = new clientToolHarnessDefinition(
                    visibleHarnessName,
                    harness.Description,
                    selectedTools,
                    harness.Skills,
                    harness.FunctionResult,
                    harness.SystemPrompt,
                    harness.StartCollapsed);

                providerHarness.Validate();
                pendingToolHarnesses.Add(providerHarness);
                state = state.WithRegisteredToolHarness(providerHarness);

                if (selector?.Expanded == true)
                    state = state.WithExpandedToolHarness(visibleHarnessName);
            }
        }

        return state;
    }

    private static IEnumerable<(clientToolHarnessDefinition Harness, ClientToolHarnessSelector? Selector)> SelectHarnesses(
        ClientToolProviderManifest manifest,
        ClientAppProviderReference reference)
    {
        if (reference.Harnesses is null || reference.Harnesses.Count == 0)
            return manifest.ClientToolHarnesses.Select(h => (h, (ClientToolHarnessSelector?)null));

        return reference.Harnesses
            .Select(selector => (Harness: manifest.ClientToolHarnesses.FirstOrDefault(h =>
                string.Equals(h.Name, selector.Name, StringComparison.OrdinalIgnoreCase)), Selector: selector))
            .Where(static item => item.Harness is not null)!
            .Select(static item => (item.Harness!, item.Selector));
    }

    private static IEnumerable<ClientToolDefinition> SelectTools(
        clientToolHarnessDefinition harness,
        ClientToolHarnessSelector? selector,
        ClientAppProviderReference reference)
    {
        var tools = harness.Tools.AsEnumerable();
        if (selector?.Tools is { Count: > 0 })
        {
            tools = tools.Where(tool => selector.Tools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase));
        }

        if (reference.Tools is { Count: > 0 })
        {
            tools = tools.Where(tool => reference.Tools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase));
        }

        return tools;
    }

    private static string CreateVisibleName(params string[] parts)
        => string.Join("_", parts.Select(SanitizeIdentifierPart).Where(static p => p.Length > 0));

    private static string SanitizeIdentifierPart(string value)
    {
        var chars = value.Select(static c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "provider" : sanitized;
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
        if (state == null || state.RegisteredToolHarnesses.Count == 0)
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

        // Convert ToolHarnesses to AIFunctions
        var visibleAIFunctions = ConvertToolHarnessesToAIFunctions(state);

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

        // Remove ToolHarnesses
        if (aug.RemoveToolHarnesses != null)
        {
            foreach (var toolName in aug.RemoveToolHarnesses)
            {
                state = state.WithoutRegisteredToolHarness(toolName);
            }
        }

        // Inject ToolHarnesses
        if (aug.InjectToolHarnesses != null)
        {
            foreach (var ToolHarness in aug.InjectToolHarnesses)
            {
                ToolHarness.Validate();
                state = state.WithRegisteredToolHarness(ToolHarness);
            }
        }

        // Expand ToolHarnesses
        if (aug.ExpandToolHarnesses != null)
        {
            foreach (var toolName in aug.ExpandToolHarnesses)
            {
                state = state.WithExpandedToolHarness(toolName);
            }
        }

        // Collapse ToolHarnesses
        if (aug.CollapseToolHarnesses != null)
        {
            foreach (var toolName in aug.CollapseToolHarnesses)
            {
                state = state.WithCollapsedToolHarness(toolName);
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
    /// Converts Client ToolHarnesses to AIFunctions using ExternalToolCollapsingWrapper.
    /// </summary>
    private List<AIFunction> ConvertToolHarnessesToAIFunctions(ClientToolStateData state)
    {
        var allFunctions = new List<AIFunction>();

        foreach (var (toolName, ToolHarness) in state.RegisteredToolHarnesses)
        {
            // Convert ClientToolDefinitions to AIFunctions
            var toolAIFunctions = ToolHarness.Tools
                .Where(t => !state.HiddenTools.Contains(t.Name))
                .Select(t =>
                {
                    state.ProviderToolBindings.TryGetValue(t.Name, out var binding);
                    return ConvertToolToAIFunction(t, toolName, binding);
                })
                .ToList();

            // Convert skills to AIFunctions (if any)
            var skillAIFunctions = new List<AIFunction>();
            if (ToolHarness.Skills != null)
            {
                foreach (var skill in ToolHarness.Skills)
                {
                    var skillFunction = ConvertSkillToAIFunction(skill, toolName);
                    skillAIFunctions.Add(skillFunction);
                }
            }

            // Determine if this ToolHarness should be collapsed
            var shouldCollapse = ToolHarness.StartCollapsed && !state.ExpandedToolHarnesses.Contains(toolName);

            if (shouldCollapse)
            {
                // Use ExternalToolCollapsingWrapper pattern - creates container + Collapsed tools
                var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapclientToolHarness(
                    toolName,
                    ToolHarness.Description!,  // Validated to exist for collapsed ToolHarnesses
                    toolAIFunctions,
                    maxFunctionNamesInDescription: 10,
                    FunctionResult: ToolHarness.FunctionResult,
                   SystemPrompt: ToolHarness.SystemPrompt);

                allFunctions.Add(container);
                allFunctions.AddRange(CollapsedTools);

                // Skill activations remain graph children of the collapsed client harness.
                allFunctions.AddRange(skillAIFunctions);
            }
            else
            {
                // Not collapsed - add tools and skills directly (no container)
                allFunctions.AddRange(toolAIFunctions);
                allFunctions.AddRange(skillAIFunctions);
            }
        }

        NormalizeClientCapabilityGraph(allFunctions, state);
        return allFunctions;
    }

    /// <summary>
    /// Converts a ClientToolDefinition to an AIFunction.
    /// The resulting function is intercepted by BeforeFunctionAsync.
    /// </summary>
    private static AIFunction ConvertToolToAIFunction(
        ClientToolDefinition tool,
        string toolName,
        ClientToolProviderToolBinding? providerBinding)
    {
        var defaultPolicy = ClientToolPolicy.Resolve(tool.DefaultPolicy);
        var modelInvocationPolicy = tool.OperationContract?.Actions.Values
            .Select(policy => ClientToolPolicy.Resolve(tool.DefaultPolicy, policy).InvocationModePolicy)
            .Any(policy => policy != AgentInvocationModePolicy.SynchronousOnly) == true
                ? AgentInvocationModePolicy.ModelChoice
                : defaultPolicy.InvocationModePolicy!.Value;
        var modelChoiceActions = tool.OperationContract?.Actions
            .Where(pair => ClientToolPolicy.Resolve(
                tool.DefaultPolicy,
                pair.Value).InvocationModePolicy == AgentInvocationModePolicy.ModelChoice)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var additionalProperties = new Dictionary<string, object?>
        {
            ["IsClientTool"] = true,
            ["clientToolHarnessName"] = toolName,
            ["SourceType"] = providerBinding is null ? "clientToolHarness" : "clientToolProvider",
            ["ClientToolDefinition"] = tool,
            ["InvocationModePolicy"] = modelInvocationPolicy
        };
        if (providerBinding is not null)
            additionalProperties["ClientToolProviderBinding"] = providerBinding;

        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
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
                RequiresPermission = defaultPolicy.RequiresPermission!.Value,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => AgentInvocationModes.CreateSchema(
                    tool.ParametersSchema,
                    modelInvocationPolicy,
                    tool.OperationContract?.Discriminator,
                    modelChoiceActions),
                AdditionalProperties = additionalProperties
            });
    }

    /// <summary>
    /// Converts a ClientSkillDefinition to an AIFunction.
    /// </summary>
    private static AIFunction ConvertSkillToAIFunction(ClientSkillDefinition skill, string toolName)
    {
        var returnMessage = skill.Instructions;
        var skillId = CapabilityId.Create($"client:{toolName}:skill:{skill.Name}");
        var childIds = (skill.References ?? Array.Empty<ClientSkillReference>())
            .Select(reference => CapabilityId.Create(
                $"client:{reference.ToolsetName ?? toolName}:tool:{reference.ToolName}"))
            .ToImmutableArray();
        var definition = Skill.Create(
            id: skillId.Value,
            name: skill.Name,
            description: skill.Description,
            instructions: SkillInstructions.FromText(skill.Instructions),
            reinforcement: string.IsNullOrWhiteSpace(skill.Reinforcement)
                ? null
                : SkillInstructions.FromText(skill.Reinforcement));

        return HPDAIFunctionFactory.Create(
            async (args, functionContext, ct) =>
            {
                await functionContext.PublishAsync(
                    new SkillActivationStartedEvent(skillId, skill.Name), ct).ConfigureAwait(false);
                try
                {
                    if (!string.IsNullOrWhiteSpace(skill.Reinforcement))
                        functionContext.ResultMetadata.Set("HPD.SkillReinforcement", skill.Reinforcement);
                    await functionContext.PublishAsync(
                        new SkillActivatedEvent(
                            skillId,
                            skill.Name,
                            childIds.Length,
                            SkillActivationLifetime.MessageTurn),
                        ct).ConfigureAwait(false);
                    return returnMessage;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await functionContext.PublishAsync(
                        new SkillActivationFailedEvent(skillId, skill.Name, exception.GetType().Name),
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = skill.Name,
                Description = skill.Description,
                RequiresPermission = false, // Skills are entry points
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptySchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata
                    {
                        Id = skillId,
                        Kind = HPDCapabilityKind.SkillActivation,
                        Reveals = childIds
                    },
                    [SkillRuntimeMetadata.SkillDefinitionKey] = definition,
                    ["clientToolHarnessName"] = toolName,
                    ["SourceType"] = "clientToolHarness"
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
    /// Intercept Client tool calls - emit request and wait for outcome.
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
        var tool = ReadClientToolDefinition(context);
        var sanitizedArguments = CreateSanitizedArgumentDictionary(context.Arguments, out var requestedMode);
        ClientToolResolvedOperation? operation;
        try
        {
            operation = tool?.ResolveOperation(sanitizedArguments);
        }
        catch (ArgumentException exception)
        {
            context.BlockExecution = true;
            context.OverrideResult = $"Client tool request rejected: {exception.Message}";
            return;
        }
        var effectivePolicy = operation?.Policy ?? ClientToolPolicy.Resolve(tool?.DefaultPolicy);
        var invocationModePolicy = effectivePolicy.InvocationModePolicy!.Value;
        var resolvedMode = AgentInvocationModes.Resolve(invocationModePolicy, requestedMode);

        ClientToolInvokeOutcomeEvent outcome;
        var providerBinding = ReadProviderBinding(context);
        if (providerBinding is not null)
        {
            outcome = await InvokeProviderToolAsync(
                context,
                providerBinding,
                requestId,
                toolName,
                sanitizedArguments,
                operation,
                effectivePolicy.RequiresFreshContext is true,
                requestedMode,
                resolvedMode,
                ct).ConfigureAwait(false);
        }
        else
        {
            // Wait for the client's immediate outcome.
            try
            {
                outcome = await context.RequestAsync<ClientToolInvokeRequestEvent, ClientToolInvokeOutcomeEvent>(
                    new ClientToolInvokeRequestEvent(
                        RequestId: requestId,
                        ToolName: toolName,
                        CallId: context.FunctionCallId ?? string.Empty,
                        Arguments: sanitizedArguments,
                        Description: context.Function.Description),
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
        }

        // Block execution (we have the result from Client)
        context.BlockExecution = true;

        if (outcome.Outcome == ClientToolInvokeOutcomeKind.AcceptedBackground)
        {
            context.OverrideResult = await HandleAcceptedBackgroundOutcomeAsync(
                context,
                providerBinding,
                tool,
                toolName,
                requestId,
                resolvedMode,
                invocationModePolicy,
                effectivePolicy,
                outcome,
                ct).ConfigureAwait(false);
            return;
        }

        context.OverrideResult = outcome.Outcome switch
        {
            ClientToolInvokeOutcomeKind.Completed => HandleCompletedOutcome(context, outcome),
            ClientToolInvokeOutcomeKind.Rejected =>
                $"Client tool request rejected: {FormatError(outcome.Error, outcome.ErrorMessage, "No reason provided.")}",
            ClientToolInvokeOutcomeKind.Failed =>
                $"Client tool failed: {FormatError(outcome.Error, outcome.ErrorMessage, "Unknown error")}",
            _ => $"Client tool failed: unsupported outcome '{outcome.Outcome}'."
        };
    }

    private static ClientToolDefinition? ReadClientToolDefinition(BeforeFunctionContext context)
    {
        if (context.Function?.AdditionalProperties?.TryGetValue("ClientToolDefinition", out var value) == true &&
            value is ClientToolDefinition definition)
        {
            return definition;
        }

        return null;
    }

    private static ClientToolProviderToolBinding? ReadProviderBinding(BeforeFunctionContext context)
    {
        if (context.Function?.AdditionalProperties?.TryGetValue("ClientToolProviderBinding", out var value) == true &&
            value is ClientToolProviderToolBinding binding)
        {
            return binding;
        }

        return null;
    }

    private async ValueTask<ClientToolInvokeOutcomeEvent> InvokeProviderToolAsync(
        BeforeFunctionContext context,
        ClientToolProviderToolBinding binding,
        string requestId,
        string toolName,
        IReadOnlyDictionary<string, object?> sanitizedArguments,
        ClientToolResolvedOperation? operation,
        bool requiresFreshContext,
        AgentInvocationMode? requestedMode,
        AgentInvocationMode resolvedMode,
        CancellationToken ct)
    {
        var registry = context.Services?.GetService<IClientToolProviderRegistry>();
        if (registry is null)
        {
            return new ClientToolInvokeOutcomeEvent
            {
                RequestId = requestId,
                Outcome = ClientToolInvokeOutcomeKind.Failed,
                ErrorMessage = "Client app provider registry is not available."
            };
        }

        return await registry.InvokeToolAsync(
            new ClientToolProviderInvocationRequest
            {
                Binding = binding,
                RequestId = requestId,
                CallId = context.FunctionCallId ?? string.Empty,
                Arguments = sanitizedArguments,
                Operation = operation,
                RequiresFreshContext = requiresFreshContext,
                RequestedInvocationMode = requestedMode,
                ResolvedInvocationMode = resolvedMode,
                Description = context.Function.Description
            },
            _config.InvokeTimeout,
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object?> CreateSanitizedArgumentDictionary(
        IReadOnlyDictionary<string, object?> arguments,
        out AgentInvocationMode? requestedMode)
    {
        requestedMode = null;
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key, "invocationMode", StringComparison.Ordinal))
            {
                requestedMode = ParseRequestedMode(value);
                continue;
            }

            sanitized[key] = value;
        }

        return sanitized;
    }

    private static AgentInvocationMode ParseRequestedMode(object? value)
    {
        if (value is AgentInvocationMode mode)
            return mode;

        if (value is string text)
        {
            if (string.Equals(text, "synchronous", StringComparison.OrdinalIgnoreCase))
                return AgentInvocationMode.Synchronous;
            if (string.Equals(text, "background", StringComparison.OrdinalIgnoreCase))
                return AgentInvocationMode.Background;
        }

        if (value is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");

            var jsonText = json.GetString();
            if (string.Equals(jsonText, "synchronous", StringComparison.OrdinalIgnoreCase))
                return AgentInvocationMode.Synchronous;
            if (string.Equals(jsonText, "background", StringComparison.OrdinalIgnoreCase))
                return AgentInvocationMode.Background;
        }

        throw new InvalidOperationException("invocationMode must be either 'synchronous' or 'background'.");
    }

    private static object? HandleCompletedOutcome(
        BeforeFunctionContext context,
        ClientToolInvokeOutcomeEvent outcome)
    {
        ApplyAugmentation(context, outcome.Augmentation);
        return ConvertContentToResult(outcome.Content);
    }

    private static string FormatError(
        ClientToolError? error,
        string? fallbackMessage,
        string defaultMessage)
    {
        if (error is not null)
            return JsonSerializer.Serialize(error, HPDJsonContext.Default.ClientToolError);

        return fallbackMessage ?? defaultMessage;
    }

    private async ValueTask<object?> HandleAcceptedBackgroundOutcomeAsync(
        BeforeFunctionContext context,
        ClientToolProviderToolBinding? providerBinding,
        ClientToolDefinition? tool,
        string toolName,
        string requestId,
        AgentInvocationMode resolvedMode,
        AgentInvocationModePolicy invocationModePolicy,
        ClientToolPolicy effectivePolicy,
        ClientToolInvokeOutcomeEvent outcome,
        CancellationToken cancellationToken)
    {
        if (invocationModePolicy == AgentInvocationModePolicy.SynchronousOnly ||
            resolvedMode == AgentInvocationMode.Synchronous)
        {
            return "Client tool accepted background work, but this tool call was resolved as synchronous.";
        }

        if (string.IsNullOrWhiteSpace(outcome.ClientOperationId))
            return "Client tool accepted background work without a clientOperationId.";

        if (!context.RuntimeCapabilities.TryGet<AgentOperationRegistry>(out var operations) ||
            string.IsNullOrWhiteSpace(context.SessionId) ||
            string.IsNullOrWhiteSpace(context.ThreadId) ||
            (providerBinding is null && context.ClientToolOperations is null))
        {
            return AgentInvocationModes.CreateFailureResult(
                toolName,
                AgentOperationSourceKind.ProviderOperation,
                "Client tool background work could not be started because no runtime background registry is available.")
                .ToToolResult();
        }

        ApplyAugmentation(context, outcome.Augmentation);

        var clientOperationId = outcome.ClientOperationId!;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invocation.kind"] = "client-tool",
            ["invocation.mode"] = "background",
            ["clientTool.toolName"] = toolName,
            ["clientTool.requestId"] = requestId,
            ["clientTool.clientOperationId"] = clientOperationId
        };

        ClientToolProviderOperationRegistration? providerOperation = null;
        if (providerBinding is not null)
        {
            var registry = context.Services?.GetService<IClientToolProviderRegistry>()
                ?? throw new InvalidOperationException("Client app provider registry is not available.");
            providerOperation = registry.RegisterOperation(
                new ClientToolProviderOperationDescriptor
                {
                    Binding = providerBinding,
                    ClientOperationId = clientOperationId,
                    ToolName = toolName,
                    RequestId = requestId,
                    CallId = context.FunctionCallId,
                    SessionId = context.SessionId,
                    ThreadId = context.ThreadId
                });
        }

        var receipt = await AgentLocalOperationScheduler.StartAsync(
            operations!,
            AgentOperationSourceKind.ProviderOperation,
            toolName,
            new AgentExecutionAddress(context.AgentName, context.SessionId!, context.ThreadId!),
            context.ThreadExecutionId,
            invocation: null,
            metadata: metadata,
            notification: effectivePolicy.OperationNotification ?? new AgentOperationNotificationPolicy(),
            work: async (operationId, runtimeToken) =>
            {
                var result = await WaitForBackgroundOperationAsync(
                    context,
                    providerBinding,
                    clientOperationId,
                    toolName,
                    requestId,
                    operationId,
                    null,
                    providerOperation,
                    runtimeToken).ConfigureAwait(false);
                switch (result.State)
                {
                    case ClientToolOperationOutcomeState.Completed:
                        return new AgentOperationCompletion(ConvertContentToSummary(result.Content));

                    case ClientToolOperationOutcomeState.Cancelled:
                        throw new OperationCanceledException(
                            result.CancellationReason ?? "Client tool background operation was cancelled.",
                            runtimeToken);

                    case ClientToolOperationOutcomeState.Faulted:
                        throw new InvalidOperationException(
                            FormatError(
                                result.Error,
                                result.ErrorMessage,
                                "Client tool background operation failed."));

                    case ClientToolOperationOutcomeState.Unknown:
                        throw new InvalidOperationException(
                            FormatError(
                                result.Error,
                                result.ErrorMessage,
                                "Client tool background operation has an unknown outcome and must not be replayed."));

                    default:
                        throw new InvalidOperationException(
                            FormatError(
                                result.Error,
                                result.ErrorMessage,
                                "Client tool background operation failed."));
                }
            }).ConfigureAwait(false);

        return new AgentInvocationResult
        {
            Mode = AgentInvocationMode.Background,
            Operation = receipt
        }.ToToolResult();
    }

    private static async Task<ClientToolOperationResult> WaitForBackgroundOperationAsync(
        BeforeFunctionContext context,
        ClientToolProviderToolBinding? providerBinding,
        string clientOperationId,
        string toolName,
        string requestId,
        string taskId,
        string? handleId,
        ClientToolProviderOperationRegistration? providerOperation,
        CancellationToken runtimeToken)
    {
        if (providerBinding is not null)
        {
            if (providerOperation is null)
                throw new InvalidOperationException("Provider background operation was not registered.");

            return await providerOperation.Completion.WaitAsync(runtimeToken).ConfigureAwait(false);
        }

        var inlineOperation = context.ClientToolOperations!.RegisterClientToolOperation(
            new ClientToolOperationDescriptor
            {
                ClientOperationId = clientOperationId,
                ToolName = toolName,
                RequestId = requestId,
                CallId = context.FunctionCallId,
                TaskId = taskId,
                HandleId = handleId,
                SessionId = context.SessionId,
                ThreadId = context.ThreadId
            });

        return await inlineOperation.Completion.WaitAsync(runtimeToken).ConfigureAwait(false);
    }

    private static void ApplyAugmentation(
        BeforeFunctionContext context,
        ClientToolAugmentation? augmentation)
    {
        if (augmentation == null)
            return;

        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        if (state == null)
            return;

        var updatedState = state.WithPendingAugmentation(augmentation);
        context.UpdateState(s => s with
        {
            MiddlewareState = s.MiddlewareState.WithClientTool(updatedState)
        });
    }

    private static string? ConvertContentToSummary(IReadOnlyList<IToolResultContent>? content)
    {
        var result = ConvertContentToResult(content);
        return result switch
        {
            null => null,
            string text => text,
            JsonElement json => json.GetRawText(),
            _ => JsonSerializer.Serialize(result, HPDJsonContext.Default.Options)
        };
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

    private static void NormalizeClientCapabilityGraph(
        List<AIFunction> functions,
        ClientToolStateData state)
    {
        var referenceParents = state.RegisteredToolHarnesses
            .SelectMany(owner => (owner.Value.Skills ?? Array.Empty<ClientSkillDefinition>())
                .SelectMany(skill => (skill.References ?? Array.Empty<ClientSkillReference>())
                    .Select(reference => new
                    {
                        Toolset = reference.ToolsetName ?? owner.Key,
                        reference.ToolName,
                        SkillId = CapabilityId.Create($"client:{owner.Key}:skill:{skill.Name}")
                    })))
            .GroupBy(entry => (entry.Toolset, entry.ToolName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.SkillId).Distinct().ToArray());

        foreach (var (toolsetName, toolset) in state.RegisteredToolHarnesses)
        {
            var harnessId = CapabilityId.Create($"client:{toolsetName}");
            var skills = toolset.Skills ?? Array.Empty<ClientSkillDefinition>();
            foreach (var tool in toolset.Tools)
            {
                var function = functions.FirstOrDefault(candidate =>
                    candidate.Name == tool.Name &&
                    candidate.AdditionalProperties?.TryGetValue("clientToolHarnessName", out var owner) == true &&
                    string.Equals(owner?.ToString(), toolsetName, StringComparison.Ordinal));
                if (function is null)
                    continue;
                var parents = referenceParents.TryGetValue((toolsetName, tool.Name), out var skillParents)
                    ? skillParents.ToImmutableArray()
                    : toolset.StartCollapsed
                        ? ImmutableArray.Create(harnessId)
                        : ImmutableArray<CapabilityId>.Empty;
                SetCapabilityMetadata(function, new HPDCapabilityMetadata
                {
                    Id = CapabilityId.Create($"client:{toolsetName}:tool:{tool.Name}"),
                    Kind = HPDCapabilityKind.Function,
                    ParentContainerIds = parents
                });
            }

            foreach (var skill in skills)
            {
                var function = functions.First(candidate => candidate.Name == skill.Name);
                var skillId = CapabilityId.Create($"client:{toolsetName}:skill:{skill.Name}");
                var childIds = (skill.References ?? Array.Empty<ClientSkillReference>())
                    .Select(reference => CapabilityId.Create(
                        $"client:{reference.ToolsetName ?? toolsetName}:tool:{reference.ToolName}"))
                    .ToImmutableArray();
                SetCapabilityMetadata(function, new HPDCapabilityMetadata
                {
                    Id = skillId,
                    Kind = HPDCapabilityKind.SkillActivation,
                    ParentContainerIds = toolset.StartCollapsed ? [harnessId] : [],
                    Reveals = childIds
                });
            }

            if (toolset.StartCollapsed)
            {
                var container = functions.FirstOrDefault(candidate =>
                    candidate.Name == toolsetName || candidate.Name == $"Client_{toolsetName}");
                if (container is not null)
                {
                    var reveals = skills
                        .Select(skill => CapabilityId.Create($"client:{toolsetName}:skill:{skill.Name}"))
                        .Concat(toolset.Tools
                            .Where(tool => !referenceParents.ContainsKey((toolsetName, tool.Name)))
                            .Select(tool => CapabilityId.Create($"client:{toolsetName}:tool:{tool.Name}")))
                        .ToImmutableArray();
                    SetCapabilityMetadata(container, new HPDCapabilityMetadata
                    {
                        Id = harnessId,
                        Kind = HPDCapabilityKind.ToolHarnessActivation,
                        Reveals = reveals
                    });
                }
            }
        }
    }

    private static void SetCapabilityMetadata(AIFunction function, HPDCapabilityMetadata metadata)
    {
        if (function.AdditionalProperties is not IDictionary<string, object?> properties)
            throw new InvalidOperationException($"Client function '{function.Name}' metadata is immutable.");
        properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata;
    }
}
