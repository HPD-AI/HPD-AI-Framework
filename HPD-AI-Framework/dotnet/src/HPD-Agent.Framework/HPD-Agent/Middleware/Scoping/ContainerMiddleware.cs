// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Immutable;
using System.Text;
using HPD.Agent.Middleware;
using HPD.Agent.Collapsing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Agent;

/// <summary>
/// Unified middleware for all container operations (Toolkits and skills).
/// Handles tool visibility, instruction injection, expansion detection, and cleanup.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b></para>
/// <para>
/// This middleware consolidates all container-related operations into a single, cohesive middleware.
/// It replaces the previous separate ToolCollapsingMiddleware and SkillInstructionMiddleware.
/// </para>
///
/// <para><b>Lifecycle Hooks Used:</b></para>
/// <list type="bullet">
/// <item><c>BeforeIterationAsync</c> - Filter tools + injectSystemPrompt</item>
/// <item><c>AfterIterationAsync</c> - Detect container expansions, update state</item>
/// <item><c>AfterMessageTurnAsync</c> - Filter ephemeral results + clear instructions (configurable)</item>
/// </list>
///
/// <para><b>Container Unification (V2):</b></para>
/// <para>
/// Treats Toolkits and skills uniformly as "containers" with dual-context support:
/// - <b>FunctionResult</b>: Ephemeral instructions returned in function result
/// - <b>SystemPrompt</b>: Persistent instructions injected into system prompt
/// </para>
///
/// <para><b>State Management:</b></para>
/// <para>
/// Uses <see cref="ContainerMiddlewareState"/> stored in <c>context.State.MiddlewareState.Collapsing</c>.
/// State is immutable and flows through the context, preserving thread-safety.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Register via AgentBuilder
/// var agent = new AgentBuilder()
///     .WithToolCollapsing()  // Auto-registers ContainerMiddleware
///     .Build();
///
/// // Or with custom configuration
/// var agent = new AgentBuilder()
///     .WithToolCollapsing(config =>
///     {
///         config.Enabled = true;
///         config.PersistSystemPromptInjections = false;
///     })
///     .Build();
/// </code>
/// </example>
public class ContainerMiddleware : IAgentMiddleware
{
    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // DEPENDENCIES
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    private readonly ToolVisibilityManager _visibilityManager;
    private readonly CollapsingConfig _config;
    private readonly Microsoft.Extensions.Logging.ILogger<ContainerMiddleware>? _logger;
    private readonly IList<AITool> _initialTools; // V2: Store for container detection

    // Smart Recovery: Maps to identify hidden tools and containers
    private readonly Dictionary<string, string> _itemToContainerMap;
    private readonly HashSet<string> _knownContainerNames;

    // Toolkit-scoped middleware (015): factory registry for building scoped pipelines at expansion time
    private readonly IReadOnlyDictionary<string, ToolkitFactory>? _toolkitFactories;

    // Toolkit-scoped middleware (015 §5B): builder-time DI instances, merged at expansion
    private readonly IReadOnlyDictionary<string, List<IAgentMiddleware>>? _toolkitScopedMiddlewares;

    // Toolkit-scoped middleware (015 §5A): per-toolkit middleware config overrides from ToolkitReference.MiddlewareConfigs
    // Key: toolkit name → (middleware type name → JsonElement config)
    private readonly IReadOnlyDictionary<string, Dictionary<string, System.Text.Json.JsonElement>>? _middlewareConfigs;

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new ContainerMiddleware instance.
    /// </summary>
    /// <param name="initialTools">All available tools for the agent</param>
    /// <param name="explicitlyRegisteredToolKits">Toolkits explicitly registered via WithTools (always visible)</param>
    /// <param name="toolkitFactories">Toolkit factory registry for building scoped middleware pipelines at expansion time . Pass null to disable scoped middleware.</param>
    /// <param name="toolkitScopedMiddlewares">Builder-time DI middleware instances per toolkit . Merged with factory-declared instances at expansion time.</param>
    /// <param name="middlewareConfigs">Per-toolkit middleware config overrides from ToolkitReference.MiddlewareConfigs . Used with config-constructor middleware factories.</param>
    /// <param name="config">Container configuration (optional, defaults to enabled)</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public ContainerMiddleware(
        IList<AITool> initialTools,
        ImmutableHashSet<string> explicitlyRegisteredToolKits,
        IReadOnlyDictionary<string, ToolkitFactory>? toolkitFactories = null,
        IReadOnlyDictionary<string, List<IAgentMiddleware>>? toolkitScopedMiddlewares = null,
        IReadOnlyDictionary<string, Dictionary<string, System.Text.Json.JsonElement>>? middlewareConfigs = null,
        CollapsingConfig? config = null,
        Microsoft.Extensions.Logging.ILogger<ContainerMiddleware>? logger = null)
    {
        if (initialTools == null)
            throw new ArgumentNullException(nameof(initialTools));
        if (explicitlyRegisteredToolKits == null)
            throw new ArgumentNullException(nameof(explicitlyRegisteredToolKits));

        // Extract AIFunctions from tools
        var aiFunctions = initialTools.OfType<AIFunction>().ToList();

        _config = config ?? new CollapsingConfig { Enabled = true };

        // Create ToolVisibilityManager for filtering, passing NeverCollapse config
        _visibilityManager = new ToolVisibilityManager(
            aiFunctions,
            explicitlyRegisteredToolKits,
            _config.NeverCollapse);
        _logger = logger;
        _initialTools = initialTools; // V2: Store for container detection
        _toolkitFactories = toolkitFactories;
        _toolkitScopedMiddlewares = toolkitScopedMiddlewares;
        _middlewareConfigs = middlewareConfigs;

        // Initialize Smart Recovery maps for hidden items and qualified names
        _itemToContainerMap = BuildItemToContainerMap(initialTools);
        _knownContainerNames = new HashSet<string>(
            _itemToContainerMap.Values,
            StringComparer.OrdinalIgnoreCase);
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // BEFORE ITERATION: Filter tools + InjectSystemPrompt
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before each LLM call.
    /// 1. Filters tools based on expansion state (collapsing)
    /// 2. InjectsSystemPrompt for active containers into system prompt
    /// </summary>
    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        // Skip if disabled
        if (!_config.Enabled || context.Options == null)
        {
            return;
        }

        var collapsingState = context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 0A: Remove stale container protocols from previous turns (session restoration)
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // ALWAYS remove stale protocols from ChatOptions.Instructions
        // This handles session restoration where ChatOptions is reused across message turns
        // State clearing in AfterMessageTurnAsync removes from state, but can't touch ChatOptions
        if (context.Options.Instructions != null && context.Options.Instructions.Contains(" ACTIVE"))
        {
            RemoveStaleContainerProtocols(context.Options);
        }

        // Check if we have tools to process
        var hasTools = context.Options.Tools != null && context.Options.Tools.Count > 0;

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 0B: Filter [Collapse] container calls from messages (IMMEDIATE TRANSPARENCY)
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // [CONTAINER] Log messages BEFORE filtering
        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            _logger.LogDebug("[CONTAINER] BeforeIterationAsync: Messages BEFORE filtering ({Count} messages):\n{Messages}",
                context.Messages.Count,
                FormatMessagesForLogging(context.Messages));
        }

        // Remove [Collapse] container calls/results from the messages that will be sent to the LLM
        // This implements "immediate transparency" - containers disappear even within the same turn
        if (hasTools && collapsingState.ContainersExpandedThisTurn.Count > 0)
        {
            FilterCollapseContainersFromMessages(context, collapsingState);

            // [CONTAINER] Log messages AFTER filtering
            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                _logger.LogDebug("[CONTAINER] BeforeIterationAsync: Messages AFTER filtering ({Count} messages):\n{Messages}",
                    context.Messages.Count,
                    FormatMessagesForLogging(context.Messages));
            }
        }

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 1: Filter tool visibility
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // Only process tools if they exist
        if (hasTools)
        {
            var expandedContainers = collapsingState.ExpandedContainers;

            //  CRITICAL FIX: Always filter from _initialTools (complete list), not context.Options.Tools (already filtered)
            // This ensures that when containers expand, their nested functions become visible in subsequent iterations
            var aiFunctions = _initialTools.OfType<AIFunction>().ToList();

            // Apply visibility rules (unified container tracking)
            var visibleFunctions = _visibilityManager.GetToolsForAgentTurn(
                aiFunctions,
                expandedContainers);

            // Convert back to AITool list
            var visibleTools = new List<AITool>(visibleFunctions.Count);
            for (int i = 0; i < visibleFunctions.Count; i++)
            {
                visibleTools.Add(visibleFunctions[i]);
            }

            // V2: Mutate Options.Tools directly (ChatOptions object is mutable)
            // The Options property itself is init-only, but the object it references is mutable
            context.Options.Tools = visibleTools;

            _logger?.LogInformation("[CONTAINER] Tools sent to LLM this iteration ({Count}): {Tools}",
                visibleTools.Count,
                string.Join(", ", visibleTools.Select(t => t is AIFunction f
                    ? (f.AdditionalProperties?.TryGetValue("IsContainer", out var ic) == true && ic is true ? $"{f.Name} [CONTAINER]" : f.Name)
                    : t.ToString())));
        }

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 2: InjectSystemPrompt for active containers
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // Use ActiveContainerInstructions (supports both Toolkits and skills)
        var activeContainers = collapsingState.ActiveContainerInstructions;

        if (!activeContainers.IsEmpty && context.Options != null)
        {
            // Build rich container protocols section
            var protocolsSection = BuildContainerProtocolsSection(activeContainers, context.Options);

            // Inject with proper formatting - append AFTER original instructions
            if (!string.IsNullOrEmpty(protocolsSection))
            {
                var currentInstructions = context.Options.Instructions ?? string.Empty;
                context.Options.Instructions = string.IsNullOrEmpty(currentInstructions)
                    ? protocolsSection
                    : $"{currentInstructions}\n\n{protocolsSection}";
            }
        }

        // Toolkit-scoped middleware (015): dispatch BeforeIterationAsync to all active toolkit pipelines
        var activePipelines = collapsingState.ToolkitPipelines;
        if (!activePipelines.IsEmpty)
        {
            foreach (var pipeline in activePipelines.Values)
            {
                if (!pipeline.IsEmpty)
                    await pipeline.DispatchBeforeIterationAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // BEFORE TOOL EXECUTION: Detect container expansions (V2)
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before tools execute.
    /// Detects container expansions from tool calls and updates state with instruction contexts.
    /// V2: Moved from AfterIteration to BeforeToolExecution to have access to tool calls.
    /// </summary>
    public async Task BeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Skip if disabled or no tool calls
        if (!_config.Enabled || context.ToolCalls.Count == 0)
            return;

        // Get current state to check which containers are already expanded
        var currentState = context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();

        var containersToExpand = new HashSet<string>();
        var containerInstructions = new Dictionary<string, ContainerInstructionSet>();
        var recoveredCalls = new Dictionary<string, RecoveryInfo>();

        foreach (var toolCall in context.ToolCalls)
        {
            // 1. Standard Expansion Check (Is it a container?)
            // We use the existing DetectContainers logic just for this single tool call
            var (expansions, instructions) = DetectContainers(new[] { toolCall }, _initialTools);

            if (expansions.Count > 0)
            {
                foreach(var e in expansions) containersToExpand.Add(e);
                foreach(var i in instructions) containerInstructions[i.Key] = i.Value;

                // Check if this container was called with arguments (error case)
                if (toolCall.Arguments != null && !string.IsNullOrWhiteSpace(toolCall.Arguments.ToString()))
                {
                    // Container should be called with no arguments
                    _logger?.LogInformation("Recovery: Container '{Container}' called with arguments, marking for history rewriting", toolCall.Name);
                    foreach (var containerName in expansions)
                    {
                        recoveredCalls[toolCall.CallId] = new RecoveryInfo(
                            RecoveryType.ContainerWithArguments,
                            containerName,
                            toolCall.Name);
                    }
                }

                continue; // Found a match, move to next tool
            }

            // 2. Recovery Check A: Hidden Item? (e.g., "Add" -> "MathToolkit")
            if (_itemToContainerMap.TryGetValue(toolCall.Name, out var parentContainer))
            {
                // Check if container is already expanded - if so, this is a VALID call, not a recovery
                if (currentState.ExpandedContainers.Contains(parentContainer))
                {
                    _logger?.LogDebug("Container '{Container}' already expanded, '{Item}' is a valid call", parentContainer, toolCall.Name);
                    continue; // Not a recovery - container is expanded and function is visible
                }

                _logger?.LogInformation("Recovery: Auto-expanding '{Container}' for hidden item '{Item}'", parentContainer, toolCall.Name);
                containersToExpand.Add(parentContainer);
                recoveredCalls[toolCall.CallId] = new RecoveryInfo(
                    RecoveryType.HiddenItem,
                    parentContainer,
                    toolCall.Name);
                continue;
            }

            // 3. Recovery Check B: Qualified Name? (e.g., "MathToolkit.Add", "MathToolkit:Add", "Add-MathToolkit")
            // Check all known containers to see if any appear in the tool call name with word boundaries
            foreach (var containerName in _knownContainerNames)
            {
                if (LooksLikeQualifiedContainerCall(toolCall.Name, containerName))
                {
                    _logger?.LogInformation("Recovery: Auto-expanding '{Container}' for qualified call '{Item}'", containerName, toolCall.Name);
                    containersToExpand.Add(containerName);
                    recoveredCalls[toolCall.CallId] = new RecoveryInfo(
                        RecoveryType.QualifiedName,
                        containerName,
                        toolCall.Name);
                    break; // Found a match, stop checking other containers
                }
            }
        }

        if (containersToExpand.Count == 0 && recoveredCalls.Count == 0)
            return;

        // Update state
        context.UpdateMiddlewareState<ContainerMiddlewareState>(state =>
        {
            foreach (var container in containersToExpand)
            {
                state = state.WithExpandedContainer(container);
            }
            foreach (var (name, instr) in containerInstructions)
            {
                state = state.WithContainerInstructions(name, instr);
            }
            foreach (var (callId, recovery) in recoveredCalls)
            {
                state = state.WithRecoveredFunction(callId, recovery);
            }

            // Toolkit-scoped middleware (015): instantiate a scoped pipeline for each newly expanded
            // container that declares middleware via [Collapse(Middlewares = ...)] (§ factory path)
            // or builder-time DI instances via WithToolkit<T>(opts => opts.AddScopedMiddleware(...)) (§5B).
            // Only build if not already in state (skip re-expansions of already-pipelined toolkits).
            foreach (var containerName in containersToExpand)
            {
                if (state.ToolkitPipelines.ContainsKey(containerName))
                    continue; // already wired (persistent container across turns)

                var instances = new List<IAgentMiddleware>();

                // §factory path: attribute-declared parameterless factories
                if (_toolkitFactories != null
                    && _toolkitFactories.TryGetValue(containerName, out var factory)
                    && factory.CollapseMiddlewareFactories is { Count: > 0 } attrFactories)
                {
                    foreach (var f in attrFactories)
                        instances.Add(f());
                }

                // §5A: config-constructor factories (resolved from MiddlewareConfigs at build time — see §5A fields)
                if (_toolkitFactories != null
                    && _toolkitFactories.TryGetValue(containerName, out var factory5a)
                    && factory5a.CollapseMiddlewareConfigFactories is { Count: > 0 } configFactories
                    && _middlewareConfigs != null
                    && _middlewareConfigs.TryGetValue(containerName, out var configMap))
                {
                    foreach (var (middlewareTypeName, configElement) in configMap)
                    {
                        // Match by simple type name (last segment of fully-qualified name)
                        var matchingFactory = configFactories.FirstOrDefault(
                            cf => cf.MiddlewareTypeName.EndsWith(middlewareTypeName, StringComparison.OrdinalIgnoreCase)
                               || middlewareTypeName.EndsWith(cf.MiddlewareTypeName, StringComparison.OrdinalIgnoreCase));
                        if (matchingFactory != null)
                            instances.Add(matchingFactory.Factory(configElement));
                    }
                }

                // §5B: builder-time DI instances (appended after attribute-declared ones)
                if (_toolkitScopedMiddlewares != null
                    && _toolkitScopedMiddlewares.TryGetValue(containerName, out var diInstances))
                {
                    instances.AddRange(diInstances);
                }

                if (instances.Count > 0)
                    state = state.WithToolkitPipeline(containerName, new AgentMiddlewarePipeline(instances));
            }

            return state;
        });

        // Note: History rewriting happens in AfterMessageTurnAsync, not here
        // We want to teach the LLM the correct pattern for NEXT turn, not this iteration

        // Toolkit-scoped middleware (015): dispatch BeforeToolExecutionAsync to newly-activated pipelines
        if (_toolkitFactories != null && containersToExpand.Count > 0)
        {
            var updatedState = context.GetMiddlewareState<ContainerMiddlewareState>();
            if (updatedState != null)
            {
                foreach (var containerName in containersToExpand)
                {
                    if (updatedState.ToolkitPipelines.TryGetValue(containerName, out var pipeline) && !pipeline.IsEmpty)
                        await pipeline.DispatchBeforeToolExecutionAsync(context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // BEFORE PARALLEL BATCH: Dispatch to toolkit-scoped pipelines for functions in the batch
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before a batch of parallel function calls executes.
    /// Routes <c>BeforeParallelBatchAsync</c> to the toolkit-scoped pipeline of every toolkit
    /// represented in the batch .
    /// </summary>
    public async Task BeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
    {
        if (_toolkitFactories == null)
            return;

        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state == null || state.ToolkitPipelines.IsEmpty)
            return;

        // Collect the distinct toolkit pipelines that own at least one function in this batch
        HashSet<AgentMiddlewarePipeline>? seen = null;
        foreach (var fn in context.ParallelFunctions)
        {
            if (!_itemToContainerMap.TryGetValue(fn.FunctionName, out var toolkitName))
                continue;
            if (!state.ToolkitPipelines.TryGetValue(toolkitName, out var pipeline) || pipeline.IsEmpty)
                continue;

            seen ??= new HashSet<AgentMiddlewarePipeline>(ReferenceEqualityComparer.Instance);
            if (seen.Add(pipeline))
                await pipeline.DispatchBeforeParallelBatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // BEFORE FUNCTION: Handle recovered qualified name calls
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called before each function executes.
    /// For recovered qualified name calls (e.g., "MathToolkit:Add"), the function lookup will fail
    /// because no function is literally named "MathToolkit:Add". We need to look up the actual
    /// function (e.g., "Add") and provide guidance.
    /// Transparently informs user that error recovery occurred.
    /// </summary>
    public async Task BeforeFunctionAsync(
        BeforeFunctionContext context,
        CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
            return;

        // Only handle cases where function is null (lookup failed)
        if (context.Function != null)
        {
            // Toolkit-scoped middleware (015): dispatch to the owning toolkit's pipeline
            var dispatchState = context.GetMiddlewareState<ContainerMiddlewareState>();
            var fnName = context.Function.Name;
            if (dispatchState != null
                && fnName != null
                && _itemToContainerMap.TryGetValue(fnName, out var owningToolkit)
                && dispatchState.ToolkitPipelines.TryGetValue(owningToolkit, out var pipeline)
                && !pipeline.IsEmpty)
            {
                await pipeline.DispatchBeforeFunctionAsync(context, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        // Check if this is a recovered call
        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state?.RecoveredFunctionCalls.TryGetValue(context.FunctionCallId, out var recovery) != true)
            return; // Not a recovered call

        // Silent recovery - return empty result so the recovery is completely invisible
        // The LLM will see an empty response and naturally retry
        // History rewriting teaches the correct pattern for future turns (pure gaslighting)
        context.OverrideResult = string.Empty;
    }


    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // AFTER FUNCTION: Transform container expansion messages
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called after each function executes.
    /// For recovered function calls (hidden items or qualified names), add explanatory message.
    /// DISABLED: Testing if informational notes affect LLM learning.
    /// </summary>
    public async Task AfterFunctionAsync(
        AfterFunctionContext context,
        CancellationToken cancellationToken)
    {
        // Toolkit-scoped middleware (015): dispatch to the owning toolkit's pipeline (reverse order)
        if (context.Function != null)
        {
            var dispatchState = context.GetMiddlewareState<ContainerMiddlewareState>();
            var fnName = context.Function.Name;
            if (dispatchState != null
                && fnName != null
                && _itemToContainerMap.TryGetValue(fnName, out var owningToolkit)
                && dispatchState.ToolkitPipelines.TryGetValue(owningToolkit, out var pipeline)
                && !pipeline.IsEmpty)
            {
                await pipeline.DispatchAfterFunctionAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        // DISABLED: Testing without informational notes
        // Theory: History rewriting alone should be sufficient for teaching correct patterns
        // The "Note: X is part of Y" messages might be noise that doesn't help learning

        return;

        // ORIGINAL CODE (commented out for testing):
        /*
        if (!_config.Enabled || context.Function == null)
            return Task.CompletedTask;

        // Check if this function call was recovered (tracked in state from BeforeToolExecutionAsync)
        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state?.RecoveredFunctionCalls.TryGetValue(context.FunctionCallId, out var recovery) != true)
            return Task.CompletedTask; // Not a recovered call

        // Build explanation based on recovery type
        var explanation = recovery.Type switch
        {
            RecoveryType.HiddenItem =>
                $"ℹ️  Note: '{recovery.FunctionName}' is part of '{recovery.ContainerName}'. " +
                $"The container was automatically expanded to handle your request.",

            RecoveryType.QualifiedName =>
                $"ℹ️  Note: Detected qualified call '{recovery.FunctionName}'. " +
                $"Auto-expanded '{recovery.ContainerName}' container to handle your request.",

            _ => null
        };

        if (explanation == null)
            return Task.CompletedTask;

        // Prepend explanation to result
        if (context.Result is string resultMessage)
        {
            context.Result = $"{explanation}\n\n{resultMessage}";
        }
        else
        {
            context.Result = $"{explanation}\n\nResult: {context.Result}";
        }

        return Task.CompletedTask;
        */
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // AFTER ITERATION: No-op (filtering moved to BeforeIterationAsync)
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called after each iteration completes.
    /// NOTE: Container filtering happens in BeforeIterationAsync (before messages are sent to LLM).
    /// This hook is kept for potential future use.
    /// </summary>
    public async Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        // Container filtering moved to BeforeIterationAsync for immediate transparency

        // Toolkit-scoped middleware (015): dispatch AfterIterationAsync to all active toolkit pipelines (reverse order)
        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state != null && !state.ToolkitPipelines.IsEmpty)
        {
            // Dispatch in reverse registration order (values() order is insertion order in ImmutableDictionary)
            foreach (var pipeline in state.ToolkitPipelines.Values.Reverse())
            {
                if (!pipeline.IsEmpty)
                    await pipeline.DispatchAfterIterationAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // AFTER MESSAGE TURN: Cleanup (instruction clearing only - no TurnHistory filtering)
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called after the message turn completes.
    /// Clears SystemPrompt injections (if configured to not persist).
    /// </summary>
    /// <remarks>
    /// NOTE: We intentionally DO NOT remove container calls from TurnHistory.
    /// Container calls need to remain in permanent history so the LLM knows it expanded
    /// the container in previous turns. Without this context, the LLM will try to call
    /// hidden functions directly (e.g., "Add" instead of "MathToolkit" → "Add").
    ///
    /// Filtering happens in BeforeIterationAsync for within-turn transparency only.
    /// </remarks>
    public async Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
            return;

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 1: DISABLED - Container calls MUST remain in TurnHistory for cross-turn context
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // DISABLED: Removing containers from TurnHistory breaks cross-turn context.
        // The LLM needs to see that it called "MathToolkit" in Turn 1 to understand
        // that it needs to call "MathToolkit" again in Turn 2 before using "Multiply".
        //
        // var collapsingState = context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();
        //
        // _logger?.LogInformation("AfterMessageTurnAsync: ExpandedContainers count = {Count}, ContainersExpandedThisTurn count = {TurnCount}, TurnHistory count = {HistoryCount}",
        //     collapsingState.ExpandedContainers.Count,
        //     collapsingState.ContainersExpandedThisTurn.Count,
        //     context.TurnHistory?.Count ?? 0);
        //
        // if (collapsingState.ContainersExpandedThisTurn.Count > 0 && context.TurnHistory != null && context.TurnHistory.Count > 0)
        // {
        //     _logger?.LogInformation("AfterMessageTurnAsync: Removing container calls for: {Containers}",
        //         string.Join(", ", collapsingState.ContainersExpandedThisTurn));
        //
        //     var containersToRemove = collapsingState.ContainersExpandedThisTurn.ToHashSet(StringComparer.OrdinalIgnoreCase);
        //     RemoveContainerCallsFromTurnHistory_V2(context.TurnHistory, containersToRemove);
        // }
        // else
        // {
        //     _logger?.LogInformation("AfterMessageTurnAsync: No containers to remove (none were expanded this turn)");
        // }

        var collapsingState = context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 1.5: REINFORCEMENT - Rewrite qualified calls in TurnHistory for next turn learning
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // For all recovery types, rewrite history to show the CORRECT pattern
        // This teaches the LLM through reinforcement for the NEXT message turn
        if (collapsingState.RecoveredFunctionCalls.Any(r =>
                r.Value.Type == RecoveryType.QualifiedName ||
                r.Value.Type == RecoveryType.ContainerWithArguments ||
                r.Value.Type == RecoveryType.HiddenItem) &&
            context.TurnHistory != null)
        {
            RewriteQualifiedCallsInTurnHistory(context.TurnHistory, collapsingState);
        }

        //─────────────────────────────────────────────────────────────────────────────────────────────
        // STEP 2: ALWAYS clear system prompt injections at end of turn
        //─────────────────────────────────────────────────────────────────────────────────────────────

        // Always clear turn containers (turn-level state)
        var updatedCollapsing = collapsingState.ClearTurnContainers();

        // ALWAYS clear container instructions - they should be re-injected fresh each turn
        if (!collapsingState.ActiveContainerInstructions.IsEmpty)
        {
            updatedCollapsing = updatedCollapsing.ClearContainerInstructions();
        }

        // ALWAYS update state to ensure cleared state is persisted
        // Even if there were no changes, we need to write back the state
        // to ensure it's captured in checkpoints/session stores
        if (collapsingState.ContainersExpandedThisTurn.Count > 0 ||
            !collapsingState.ActiveContainerInstructions.IsEmpty ||
            !collapsingState.RecoveredFunctionCalls.IsEmpty ||
            !collapsingState.ToolkitPipelines.IsEmpty)
        {
            context.UpdateMiddlewareState<ContainerMiddlewareState>(_ => updatedCollapsing);
        }

        // Toolkit-scoped middleware (015): dispatch AfterMessageTurnAsync to active pipelines (reverse order)
        // Done AFTER state update so pipelines see the final state for the turn.
        if (!collapsingState.ToolkitPipelines.IsEmpty)
        {
            foreach (var pipeline in collapsingState.ToolkitPipelines.Values.Reverse())
            {
                if (!pipeline.IsEmpty)
                    await pipeline.DispatchAfterMessageTurnAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // ON ERROR: Route to toolkit-scoped pipelines
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Routes OnErrorAsync to active toolkit-scoped pipelines .
    /// <para>
    /// <b>Dispatch strategy:</b> <c>ErrorContext</c> does not carry a function name, so we cannot
    /// identify which specific toolkit owns the erroring call. For <c>ErrorSource.ToolCall</c>
    /// errors we dispatch to all active toolkit pipelines in reverse order — consistent with the
    /// After*-style unwinding model. For non-tool errors (model call, iteration, message turn)
    /// we still dispatch to all active pipelines so scoped middleware can do cross-cutting error
    /// handling (e.g. circuit breakers, per-toolkit telemetry).
    /// </para>
    /// </summary>
    public async Task OnErrorAsync(
        ErrorContext context,
        CancellationToken cancellationToken)
    {
        var state = context.GetMiddlewareState<ContainerMiddlewareState>();
        if (state == null || state.ToolkitPipelines.IsEmpty)
            return;

        // Dispatch to all active toolkit pipelines in reverse order (error unwinding)
        foreach (var pipeline in state.ToolkitPipelines.Values.Reverse())
        {
            if (!pipeline.IsEmpty)
                await pipeline.DispatchOnErrorAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // WRAP FUNCTION CALL: Route to toolkit-scoped pipeline (innermost wrapper)
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Routes WrapFunctionCallAsync to the owning toolkit's scoped pipeline .
    /// The toolkit pipeline wraps innermost — closest to the actual function call.
    /// ExecuteFunctionCallAsync is chain-based and does NOT call SetMiddlewareExecuting,
    /// so delegating to it here is safe even though we are inside the global pipeline's Execute*() call.
    /// </summary>
    public Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        if (_toolkitFactories == null)
            return handler(request);

        var stateKey = typeof(ContainerMiddlewareState).FullName!;
        var state = request.State.MiddlewareState.GetState<ContainerMiddlewareState>(stateKey);
        if (state == null)
            return handler(request);

        if (!_itemToContainerMap.TryGetValue(request.FunctionName, out var toolkitName))
            return handler(request);

        if (!state.ToolkitPipelines.TryGetValue(toolkitName, out var toolkitPipeline) || toolkitPipeline.IsEmpty)
            return handler(request);

        // Toolkit pipeline wraps the original handler — toolkit is innermost
        return toolkitPipeline.ExecuteFunctionCallAsync(request, handler, cancellationToken);
    }

    //═════════════════════════════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    //═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes stale container protocol sections from ChatOptions.Instructions.
    /// Handles session restoration where ChatOptions is reused across message turns.
    /// Finds and removes everything from the separator line before " ACTIVE CONTAINER PROTOCOLS" onwards.
    /// </summary>
    private static void RemoveStaleContainerProtocols(ChatOptions options)
    {
        var currentInstructions = options.Instructions;
        if (string.IsNullOrEmpty(currentInstructions))
            return;

        // The separator marker that appears before the protocols section
        var separatorMarker = "═════════════════════════════════════════════════════════════════════════════════════════════════";

        // Find the start of the protocols section by looking for the separator that comes before " ACTIVE"
        var parts = currentInstructions.Split([separatorMarker], StringSplitOptions.None);

        if (parts.Length <= 1)
            return; // No separator found, nothing to remove

        // Find which part contains the " ACTIVE CONTAINER PROTOCOLS" marker
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Contains(" ACTIVE CONTAINER PROTOCOLS"))
            {
                // Take everything BEFORE this separator (parts 0 through i-1)
                // This removes the separator, the marker, and everything after
                var cleanedInstructions = string.Join(separatorMarker, parts.Take(i)).TrimEnd();
                options.Instructions = cleanedInstructions;
                return;
            }
        }
    }

    /// <summary>
    /// Detects containers from tool calls (unified for Toolkits and skills).
    /// Extracts both FunctionResult and SystemPrompt.
    /// </summary>
    private static (
        HashSet<string> containerExpansions,
        Dictionary<string, ContainerInstructionSet> containerInstructions)
        DetectContainers(IReadOnlyList<FunctionCallContent> toolCalls, IList<AITool>? tools)
    {
        var containerExpansions = new HashSet<string>();
        var containerInstructions = new Dictionary<string, ContainerInstructionSet>();

        foreach (var toolCall in toolCalls)
        {
            // Find the function in the tool list
            var function = FindFunctionInList(toolCall.Name, tools);
            if (function == null) continue;

            // Check if it's a container
            if (function.AdditionalProperties?.TryGetValue("IsContainer", out var isContainerVal) != true ||
                isContainerVal is not bool isContainer || !isContainer)
            {
                continue;
            }

            // Check if it's a skill container (has both IsContainer=true AND IsSkill=true)
            var isSkill = function.AdditionalProperties?.TryGetValue("IsSkill", out var isSkillVal) == true
                && isSkillVal is bool isS && isS;

            // Determine container name
            string containerName;
            if (isSkill)
            {
                // Skill container - use function name
                containerName = function.Name ?? toolCall.Name;
            }
            else
            {
                // Toolkit container - use ToolkitName metadata or function name
                containerName = function.AdditionalProperties
                    ?.TryGetValue("ToolkitName", out var pnVal) == true && pnVal is string pn
                    ? pn
                    : toolCall.Name;
            }

            containerExpansions.Add(containerName);

            // Extract both contexts from metadata
            var funcResultCtx = ExtractStringMetadata(function, "FunctionResult");
            var sysPromptCtx = ExtractStringMetadata(function, "SystemPrompt");

            // Fallback to legacy "Instructions" for skills if SystemPrompt not present
            if (string.IsNullOrEmpty(sysPromptCtx) && isSkill)
            {
                sysPromptCtx = ExtractStringMetadata(function, "Instructions");
            }

            // Store instruction contexts
            if (funcResultCtx != null || sysPromptCtx != null)
            {
                containerInstructions[containerName] = new ContainerInstructionSet(funcResultCtx, sysPromptCtx);
            }
        }

        return (containerExpansions, containerInstructions);
    }

    /// <summary>
    /// Builds a rich, formatted container protocols section with metadata.
    /// Injects SystemPrompt for all containers (Toolkits + skills).
    /// </summary>
    private static string BuildContainerProtocolsSection(
        ImmutableDictionary<string, ContainerInstructionSet> activeContainers,
        ChatOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═════════════════════════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine(" ACTIVE CONTAINER PROTOCOLS (Execute ALL steps completely)");
        sb.AppendLine("═════════════════════════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Order alphabetically for consistency
        foreach (var (containerName, instructionSet) in activeContainers.OrderBy(kvp => kvp.Key))
        {
            // Only injectSystemPrompt (not FunctionResult)
            if (string.IsNullOrEmpty(instructionSet.SystemPrompt))
                continue;

            sb.AppendLine($"## {containerName}:");
            sb.AppendLine();

            // Find the container's AIFunction to extract metadata
            var containerFunction = options.Tools?.OfType<AIFunction>()
                .FirstOrDefault(f => f.Name == containerName);

            if (containerFunction != null)
            {
                // Add function list from metadata
                if (containerFunction.AdditionalProperties?.TryGetValue("ReferencedFunctions", out var functionsObj) == true
                    && functionsObj is string[] functions && functions.Length > 0)
                {
                    sb.AppendLine($"**Available functions:** {string.Join(", ", functions)}");
                    sb.AppendLine();
                }
                else if (containerFunction.AdditionalProperties?.TryGetValue("FunctionNames", out var funcNamesObj) == true
                    && funcNamesObj is string[] funcNames && funcNames.Length > 0)
                {
                    sb.AppendLine($"**Available functions:** {string.Join(", ", funcNames)}");
                    sb.AppendLine();
                }

            }

            // Add theSystemPrompt instructions
            sb.AppendLine(instructionSet.SystemPrompt);
            sb.AppendLine();
        }

        return sb.ToString();
    }


    /// <summary>
    /// Extracts string metadata from AdditionalProperties.
    /// </summary>
    private static string? ExtractStringMetadata(AIFunction function, string key)
    {
        if (function.AdditionalProperties?.TryGetValue(key, out var value) == true &&
            value is string str &&
            !string.IsNullOrWhiteSpace(str))
        {
            return str;
        }
        return null;
    }

    /// <summary>
    /// Finds an AIFunction by name in the tools list.
    /// </summary>
    private static AIFunction? FindFunctionInList(string? name, IList<AITool>? tools)
    {
        if (string.IsNullOrEmpty(name) || tools is not { Count: > 0 })
            return null;

        for (int i = 0; i < tools.Count; i++)
        {
            if (tools[i] is AIFunction af && af.Name == name)
                return af;
        }

        return null;
    }

    /// <summary>
    /// Filters [Collapse] container calls/results from the messages list in BeforeIterationContext.
    /// This implements "immediate transparency" - containers disappear even within the same turn.
    /// Only filters [Collapse] containers, not [Skill] containers.
    /// Also filters recovered hidden item calls.
    /// </summary>
    private void FilterCollapseContainersFromMessages(
        BeforeIterationContext context,
        ContainerMiddlewareState collapsingState)
    {
        // Identify which containers are [Collapse] vs [Skill]
        var collapseContainersToFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var containerName in collapsingState.ContainersExpandedThisTurn)
        {
            var containerFunction = FindContainerInInitialTools(containerName);
            if (containerFunction == null)
                continue;

            // Check if it's a [Collapse] container (IsCollapse=true OR just IsContainer=true without IsSkill)
            var isCollapse = containerFunction.AdditionalProperties?.TryGetValue("IsCollapse", out var collapseVal) == true
                && collapseVal is bool collapseFlag && collapseFlag;

            var isSkill = containerFunction.AdditionalProperties?.TryGetValue("IsSkill", out var skillVal) == true
                && skillVal is bool skillFlag && skillFlag;

            // Filter if it's a [Collapse] container OR a regular container (not a skill container)
            if (isCollapse || !isSkill)
            {
                collapseContainersToFilter.Add(containerName);
                _logger?.LogDebug("BeforeIterationAsync: Will filter [Collapse] container '{Container}' from messages (IsCollapse={IsCollapse}, IsSkill={IsSkill})",
                    containerName, isCollapse, isSkill);
            }
        }

        if (collapseContainersToFilter.Count == 0)
            return;

        _logger?.LogInformation("BeforeIterationAsync: Filtering {Count} [Collapse] containers from messages: {Containers}",
            collapseContainersToFilter.Count,
            string.Join(", ", collapseContainersToFilter));

        // First pass: Collect container CallIds
        var containerCallIds = new HashSet<string>();

        foreach (var message in context.Messages)
        {
            if (message.Role == ChatRole.Assistant)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionCallContent fcc && IsCallRelatedToContainers(fcc.Name, collapseContainersToFilter))
                    {
                        containerCallIds.Add(fcc.CallId);
                        _logger?.LogDebug("BeforeIterationAsync: Found container call '{Name}' with CallId '{CallId}'",
                            fcc.Name, fcc.CallId);
                    }
                }
            }
        }

        // Also add recovered hidden item calls to the filter list
        // All HiddenItem recoveries are failed calls (empty result) because:
        // - Successful calls don't trigger recovery (container already expanded check in BeforeToolExecutionAsync)
        // - Only the FIRST failed call (before container expansion) gets added to RecoveredFunctionCalls
        foreach (var (callId, recovery) in collapsingState.RecoveredFunctionCalls)
        {
            if (recovery.Type == RecoveryType.HiddenItem)
            {
                containerCallIds.Add(callId);
                _logger?.LogDebug("BeforeIterationAsync: Added recovered hidden item call '{Function}' (CallId: '{CallId}') to filter list",
                    recovery.FunctionName, callId);
            }
        }

        if (containerCallIds.Count == 0)
            return;

        _logger?.LogInformation("BeforeIterationAsync: Collected {Count} container CallIds to filter", containerCallIds.Count);

        // Second pass: Remove container calls and results
        for (int i = context.Messages.Count - 1; i >= 0; i--)
        {
            var message = context.Messages[i];

            if (message.Role == ChatRole.Assistant)
            {
                // Check if this message contains ANY container calls or recovered hidden item calls
                var hasContainerCall = message.Contents
                    .OfType<FunctionCallContent>()
                    .Any(fcc => IsCallRelatedToContainers(fcc.Name, collapseContainersToFilter) ||
                                containerCallIds.Contains(fcc.CallId));

                if (hasContainerCall)
                {
                    // Remove the ENTIRE assistant message (including text)
                    // The text (e.g., "Let me first see what files...") only makes sense WITH the call
                    // Leaving orphaned text confuses the LLM
                    _logger?.LogInformation("BeforeIterationAsync: Removed entire Assistant message containing container call");
                    context.Messages.RemoveAt(i);
                }
            }
            else if (message.Role == ChatRole.Tool)
            {
                // Filter out container function results using collected CallIds
                var nonContainerResults = message.Contents
                    .Where(c => c is not FunctionResultContent frc || !containerCallIds.Contains(frc.CallId))
                    .ToList();

                if (nonContainerResults.Count < message.Contents.Count)
                {
                    _logger?.LogInformation("BeforeIterationAsync: Removed {Count} container results from Tool message",
                        message.Contents.Count - nonContainerResults.Count);

                    if (nonContainerResults.Count == 0)
                    {
                        context.Messages.RemoveAt(i);
                    }
                    else
                    {
                        context.Messages[i] = new ChatMessage(ChatRole.Tool, nonContainerResults);
                    }
                }
            }
        }

        _logger?.LogInformation("BeforeIterationAsync: Filtered {Count} [Collapse] containers from {MessageCount} messages",
            collapseContainersToFilter.Count, context.Messages.Count);
    }

    /// <summary>
    /// Finds a container function by name in the initial tools list.
    /// Checks both Name and ToolkitName metadata.
    /// </summary>
    private AIFunction? FindContainerInInitialTools(string containerName)
    {
        if (string.IsNullOrEmpty(containerName))
            return null;

        foreach (var tool in _initialTools)
        {
            if (tool is not AIFunction af)
                continue;

            // Check if it's a container
            var isContainer = af.AdditionalProperties?.TryGetValue("IsContainer", out var val) == true
                && val is bool b && b;

            if (!isContainer)
                continue;

            // Check if name matches (either function name or ToolkitName)
            if (af.Name == containerName)
                return af;

            var ToolkitName = af.AdditionalProperties?.TryGetValue("ToolkitName", out var pnVal) == true
                && pnVal is string pn ? pn : null;

            if (ToolkitName == containerName)
                return af;
        }

        return null;
    }

    /// <summary>
    /// Extracts the function list from a container's metadata.
    /// Returns a comma-separated string of function names, or null if not found.
    /// </summary>
    private static string? ExtractFunctionList(AIFunction? containerFunction)
    {
        if (containerFunction == null)
            return null;

        // Try ReferencedFunctions first (new format)
        if (containerFunction.AdditionalProperties?.TryGetValue("ReferencedFunctions", out var refFuncsObj) == true
            && refFuncsObj is string[] refFuncs && refFuncs.Length > 0)
        {
            return string.Join(", ", refFuncs);
        }

        // Try FunctionNames fallback (old format)
        if (containerFunction.AdditionalProperties?.TryGetValue("FunctionNames", out var funcNamesObj) == true
            && funcNamesObj is string[] funcNames && funcNames.Length > 0)
        {
            return string.Join(", ", funcNames);
        }

        return null;
    }

    /// <summary>
    /// Checks if a function call IS a container (not a child function).
    /// Only matches:
    /// 1. Exact match: "MathToolkit" is in targetContainers
    /// 2. Qualified name: "MathToolkit.Add", "MathToolkit:Add", etc. (for cleanup of qualified calls)
    ///
    /// Does NOT match hidden items like "Add" - those should stay in history!
    /// </summary>
    private bool IsCallRelatedToContainers(string functionName, IReadOnlySet<string> targetContainers)
    {
        // 1. Exact Match - Direct container call
        if (targetContainers.Contains(functionName))
            return true;

        // 2. Qualified Match - Container name appears with word boundaries
        // Matches "MathToolkit.Add", "MathToolkit:Add", "Add-MathToolkit", etc.
        // but NOT bare "Add" (which should stay in history)
        foreach (var containerName in targetContainers)
        {
            if (LooksLikeQualifiedContainerCall(functionName, containerName))
                return true;
        }

        // NOTE: We deliberately DO NOT check _itemToContainerMap here.
        // Hidden items like "Add" should remain in history after expansion.
        // Only the container call itself ("MathToolkit") should be removed.

        return false;
    }

    /// <summary>
    /// Builds a map from item name to [Collapse] container name.
    /// Enables recovery for hidden items: maps "Add" to "MathToolkit".
    /// </summary>
    private static Dictionary<string, string> BuildItemToContainerMap(IList<AITool> tools)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var tool in tools.OfType<AIFunction>())
        {
            // Only map items inside Containers
            var isContainer = tool.AdditionalProperties?.TryGetValue("IsContainer", out var v) == true 
                && v is bool b && b;
            if (!isContainer) 
                continue;

            var containerName = tool.Name;
            var children = tool.AdditionalProperties?.TryGetValue("FunctionNames", out var c) == true 
                ? c as string[] 
                : null;

            if (children != null)
            {
                foreach (var child in children)
                {
                    // Handle "Toolkit.Child" -> Map "Child" to "Toolkit"
                    var simpleName = child.Contains('.') 
                        ? child.Substring(child.LastIndexOf('.') + 1) 
                        : child;
                    map[simpleName] = containerName;
                }
            }
        }
        
        return map;
    }

    /// <summary>
    /// Checks if a function call name looks like it contains a qualified reference to a container.
    /// Uses word boundary detection to avoid false positives (e.g., "Math" won't match "MathHelper").
    /// Handles any separator pattern (., /, :, -, _, spaces, etc.) by checking for non-alphanumeric boundaries.
    /// </summary>
    /// <param name="name">The function call name to check</param>
    /// <param name="containerName">The container name to look for</param>
    /// <returns>True if the name contains the container name at a word boundary</returns>
    private static bool LooksLikeQualifiedContainerCall(string name, string containerName)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(containerName))
            return false;

        if (string.Equals(name, containerName, StringComparison.OrdinalIgnoreCase))
            return false; // Exact match handled elsewhere

        // Check if container name appears with a non-alphanumeric boundary
        int idx = name.IndexOf(containerName, StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return false;

        // Verify it's a word boundary (not substring of another word)
        bool validStart = idx == 0 || !char.IsLetterOrDigit(name[idx - 1]);
        bool validEnd = idx + containerName.Length >= name.Length
                        || !char.IsLetterOrDigit(name[idx + containerName.Length]);

        return validStart && validEnd;
    }

    /// <summary>
    /// Rewrites container calls in TurnHistory to show the correct container pattern.
    /// Changes "MathToolkit:Add(args)" → "MathToolkit()" in history so LLM learns the correct behavior.
    /// Also handles "MathToolkit(args)" → "MathToolkit()" for containers called with arguments.
    /// </summary>
    /// <param name="turnHistory">The mutable TurnHistory list to modify</param>
    /// <param name="collapsingState">State containing recovered function calls</param>
    private void RewriteQualifiedCallsInTurnHistory(
        List<ChatMessage> turnHistory,
        ContainerMiddlewareState collapsingState)
    {
        if (turnHistory == null || turnHistory.Count == 0)
            return;

        // Find all recoveries that need history rewriting (all recovery types)
        var recoveriesNeedingRewrite = collapsingState.RecoveredFunctionCalls
            .Where(r => r.Value.Type == RecoveryType.QualifiedName ||
                        r.Value.Type == RecoveryType.ContainerWithArguments ||
                        r.Value.Type == RecoveryType.HiddenItem)
            .ToDictionary(r => r.Key, r => r.Value);

        if (recoveriesNeedingRewrite.Count == 0)
            return;

        // PHASE 1: Rewrite assistant messages (qualified name → container name, remove args)
        for (int i = 0; i < turnHistory.Count; i++)
        {
            var message = turnHistory[i];
            if (message.Role != ChatRole.Assistant)
                continue;

            // Check if this message contains any calls needing rewriting
            var hasRecoveryCalls = message.Contents
                .OfType<FunctionCallContent>()
                .Any(fcc => recoveriesNeedingRewrite.ContainsKey(fcc.CallId));

            if (!hasRecoveryCalls)
                continue;

            // Rewrite this message
            var newContents = new List<AIContent>();

            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fcc && recoveriesNeedingRewrite.TryGetValue(fcc.CallId, out var recovery))
                {
                    // Rewrite: "MathToolkit:Add" with args → "MathToolkit" with no args
                    // Or: "MathToolkit" with args → "MathToolkit" with no args
                    newContents.Add(new FunctionCallContent(
                        callId: fcc.CallId,
                        name: recovery.ContainerName,  // Just the container name
                        arguments: null));  // No arguments

                    _logger?.LogInformation(
                        "Rewrote {RecoveryType} call '{Original}' → '{Container}' (removed args) in TurnHistory",
                        recovery.Type, recovery.FunctionName, recovery.ContainerName);
                }
                else
                {
                    // Not a recovered call, keep as-is
                    newContents.Add(content);
                }
            }

            // Replace the message in TurnHistory
            turnHistory[i] = new ChatMessage(message.Role, newContents);
        }

        // PHASE 2: Rewrite tool messages (replace recovery message with container expansion message)
        for (int i = 0; i < turnHistory.Count; i++)
        {
            var message = turnHistory[i];
            if (message.Role != ChatRole.Tool)
                continue;

            // Check if this tool message contains results for any recoveries
            var newToolContents = new List<AIContent>();
            bool modified = false;

            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent frc && recoveriesNeedingRewrite.ContainsKey(frc.CallId))
                {
                    // This is a recovery message - replace it with container expansion message
                    var recovery = recoveriesNeedingRewrite[frc.CallId];

                    // Get the FunctionResult from the container instructions (already captured during detection)
                    var instructions = collapsingState.ActiveContainerInstructions.GetValueOrDefault(recovery.ContainerName);

                    string containerMessage;
                    if (instructions?.FunctionResult != null)
                    {
                        containerMessage = instructions.FunctionResult;
                    }
                    else
                    {
                        // Build fallback message with function list from container metadata
                        var containerFunction = FindContainerInInitialTools(recovery.ContainerName);
                        var functionList = ExtractFunctionList(containerFunction);

                        containerMessage = functionList != null
                            ? $"{recovery.ContainerName} expanded. Available functions: {functionList}\n\n" +
                              $"Note: In new message turns, call {recovery.ContainerName} again to access these functions."
                            : $"{recovery.ContainerName} expanded. Available functions are now visible.\n\n" +
                              $"Note: In new message turns, call {recovery.ContainerName} again to access these functions.";
                    }

                    newToolContents.Add(new FunctionResultContent(frc.CallId, containerMessage));
                    modified = true;

                    _logger?.LogInformation(
                        "Replaced {RecoveryType} message for '{Container}' with expansion instructions in TurnHistory",
                        recovery.Type, recovery.ContainerName);
                }
                else
                {
                    // Not a recovered call result, keep as-is
                    newToolContents.Add(content);
                }
            }

            // Replace tool message if we modified it
            if (modified)
            {
                turnHistory[i] = new ChatMessage(ChatRole.Tool, newToolContents);
            }
        }
    }

    /// <summary>
    /// Formats a list of messages for debug logging, showing role, text preview, and function call/result details.
    /// </summary>
    private static string FormatMessagesForLogging(IList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            sb.AppendLine($"  [{i}] {msg.Role}:");

            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent tc:
                        var textPreview = tc.Text?.Length > 100
                            ? tc.Text.Substring(0, 100) + "..."
                            : tc.Text;
                        sb.AppendLine($"       Text: \"{textPreview}\"");
                        break;

                    case FunctionCallContent fcc:
                        var argsPreview = fcc.Arguments != null
                            ? string.Join(", ", fcc.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                            : "<no args>";
                        if (argsPreview.Length > 100) argsPreview = argsPreview.Substring(0, 100) + "...";
                        sb.AppendLine($"       FunctionCall: {fcc.Name}({argsPreview}) [CallId: {fcc.CallId}]");
                        break;

                    case FunctionResultContent frc:
                        var resultPreview = frc.Result?.ToString() ?? "<null>";
                        if (resultPreview.Length > 100) resultPreview = resultPreview.Substring(0, 100) + "...";
                        sb.AppendLine($"       FunctionResult: [CallId: {frc.CallId}] => \"{resultPreview}\"");
                        break;

                    default:
                        sb.AppendLine($"       {content.GetType().Name}");
                        break;
                }
            }
        }
        return sb.ToString();
    }


}

/// <summary>
/// State for ContainerMiddleware. Tracks container expansions, instructions, and recovery operations.
/// Handles both Toolkits and Skills uniformly as "containers".
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety
/// for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var state = context.GetMiddlewareState&lt;ContainerMiddlewareState&gt;() ?? new();
/// var isExpanded = state.ExpandedContainers.Contains("FinancialToolkit");
///
/// // Update state
/// context.UpdateMiddlewareState&lt;ContainerMiddlewareState&gt;(s =>
///     s.WithExpandedContainer("FinancialToolkit"));
/// </code>
///
/// <para><b>Lifecycle:</b></para>
/// <para>
/// - ExpandedContainers persist across message turns (session-level state)
/// - ContainersExpandedThisTurn tracked during current turn, cleared at turn end
/// - ActiveContainerInstructions cleared at end of message turn
/// - RecoveredFunctionCalls tracked during turn, cleared at turn end
/// </para>
/// </remarks>
[MiddlewareState]
public sealed record ContainerMiddlewareState
{
    /// <summary>
    /// All expanded containers (Toolkits AND skills) across the entire session.
    /// Containers in this set have their member functions visible.
    /// Persists across message turns.
    /// </summary>
    public ImmutableHashSet<string> ExpandedContainers { get; init; }
        = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Containers expanded during the CURRENT message turn only.
    /// Used for cleanup in AfterMessageTurnAsync to remove container calls from TurnHistory.
    /// Cleared at end of each message turn.
    /// </summary>
    public ImmutableHashSet<string> ContainersExpandedThisTurn { get; init; }
        = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Active container instructions for prompt injection.
    /// Maps container name to its instruction contexts (FunctionResult + SystemPrompt).
    /// Cleared at end of message turn.
    /// </summary>
    public ImmutableDictionary<string, ContainerInstructionSet> ActiveContainerInstructions { get; init; }
        = ImmutableDictionary<string, ContainerInstructionSet>.Empty;

    /// <summary>
    /// Tracks recovered function calls (hidden items or qualified names).
    /// Maps CallId to recovery info for explanatory messages and history rewriting.
    /// Cleared at end of message turn.
    /// </summary>
    public ImmutableDictionary<string, RecoveryInfo> RecoveredFunctionCalls { get; init; }
        = ImmutableDictionary<string, RecoveryInfo>.Empty;

    /// <summary>
    /// Active scoped middleware pipelines for expanded toolkits .
    /// Key: toolkit name (same keys as <see cref="ExpandedContainers"/>).
    /// Value: pipeline containing middleware instances declared via <c>[Collapse(Middlewares = ...)]</c>
    ///        plus any builder-time additions from <c>WithToolkit&lt;T&gt;(opts =&gt; opts.AddScopedMiddleware(...))</c>.
    /// Populated at container expansion time in <c>BeforeToolExecutionAsync</c>.
    /// Cleared at turn end (when <c>PersistSystemPromptInjections = false</c>) or retained across
    /// turns (when <c>PersistSystemPromptInjections = true</c>).
    /// </summary>
    public ImmutableDictionary<string, AgentMiddlewarePipeline> ToolkitPipelines { get; init; }
        = ImmutableDictionary<string, AgentMiddlewarePipeline>.Empty;

    /// <summary>
    /// Records a container expansion (Toolkit or skill).
    /// Adds to both session-level ExpandedContainers and turn-level ContainersExpandedThisTurn.
    /// </summary>
    /// <param name="containerName">Name of the container being expanded</param>
    /// <returns>New state with container added to both sets</returns>
    public ContainerMiddlewareState WithExpandedContainer(string containerName)
    {
        return this with
        {
            ExpandedContainers = ExpandedContainers.Add(containerName),
            ContainersExpandedThisTurn = ContainersExpandedThisTurn.Add(containerName)
        };
    }

    /// <summary>
    /// Adds or updates container instructions.
    /// </summary>
    /// <param name="containerName">Name of the container</param>
    /// <param name="instructions">Instruction contexts to inject</param>
    /// <returns>New state with updated instructions</returns>
    public ContainerMiddlewareState WithContainerInstructions(string containerName, ContainerInstructionSet instructions)
    {
        return this with
        {
            ActiveContainerInstructions = ActiveContainerInstructions.SetItem(containerName, instructions)
        };
    }

    /// <summary>
    /// Records a recovered function call for later explanatory message injection.
    /// </summary>
    /// <param name="callId">Function call ID</param>
    /// <param name="recovery">Recovery information</param>
    /// <returns>New state with recovery info added</returns>
    public ContainerMiddlewareState WithRecoveredFunction(string callId, RecoveryInfo recovery)
    {
        return this with
        {
            RecoveredFunctionCalls = RecoveredFunctionCalls.SetItem(callId, recovery)
        };
    }

    /// <summary>
    /// Stores an active scoped middleware pipeline for an expanded toolkit.
    /// </summary>
    public ContainerMiddlewareState WithToolkitPipeline(string toolkitName, AgentMiddlewarePipeline pipeline)
        => this with { ToolkitPipelines = ToolkitPipelines.SetItem(toolkitName, pipeline) };

    /// <summary>
    /// Removes the scoped middleware pipeline for a toolkit (called at turn cleanup).
    /// </summary>
    public ContainerMiddlewareState WithoutToolkitPipeline(string toolkitName)
        => this with { ToolkitPipelines = ToolkitPipelines.Remove(toolkitName) };

    /// <summary>
    /// Clears all active container instructions (typically at end of message turn).
    /// </summary>
    /// <returns>New state with cleared instructions</returns>
    public ContainerMiddlewareState ClearContainerInstructions()
    {
        return this with
        {
            ActiveContainerInstructions = ImmutableDictionary<string, ContainerInstructionSet>.Empty
        };
    }

    /// <summary>
    /// Clears all container state at end of message turn.
    /// Resets expanded containers so LLM must re-expand them each turn.
    /// This ensures instructions are re-injected when containers are re-expanded.
    /// </summary>
    /// <returns>New state with all container tracking cleared</returns>
    public ContainerMiddlewareState ClearTurnContainers()
    {
        return this with
        {
            ExpandedContainers = ImmutableHashSet<string>.Empty,
            ContainersExpandedThisTurn = ImmutableHashSet<string>.Empty,
            RecoveredFunctionCalls = ImmutableDictionary<string, RecoveryInfo>.Empty,
            ToolkitPipelines = ImmutableDictionary<string, AgentMiddlewarePipeline>.Empty
        };
    }
}

/// <summary>
/// Instruction contexts for a container (Toolkit or skill).
/// Supports dual-injection: function result (ephemeral) + system prompt (persistent).
/// </summary>
public sealed record ContainerInstructionSet(
    string? FunctionResult,
    string? SystemPrompt
);

/// <summary>
/// Information about a recovered function call.
/// Used to track why a function triggered auto-expansion and provide explanatory messages.
/// </summary>
/// <param name="Type">Type of recovery that occurred</param>
/// <param name="ContainerName">Name of the container that was auto-expanded</param>
/// <param name="FunctionName">Original function name that triggered recovery</param>
public sealed record RecoveryInfo(
    RecoveryType Type,
    string ContainerName,
    string FunctionName
);

/// <summary>
/// Type of recovery operation.
/// </summary>
public enum RecoveryType
{
    /// <summary>Hidden item call (e.g., "Add" when container not expanded)</summary>
    HiddenItem,
    /// <summary>Qualified name call (e.g., "MathToolkit:Add", "MathToolkit.Add")</summary>
    QualifiedName,
    /// <summary>Container called with arguments (e.g., "MathToolkit(a: 5, b: 10)" should be "MathToolkit()")</summary>
    ContainerWithArguments
}