using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.StructuredOutput;
using HPD.MultiAgent.Config;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Internal;

/// <summary>
/// Generic handler that wraps an Agent for execution within a graph.
/// Handles all output modes: String, Structured, Union, Handoff.
/// </summary>
internal sealed class AgentNodeHandler : IGraphNodeHandler<AgentGraphContext>
{
    private readonly string _nodeId;

    public string HandlerName { get; }

    public AgentNodeHandler(string nodeId)
    {
        _nodeId = nodeId;
        HandlerName = $"{nodeId}Handler";
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        AgentGraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get agent and options from context
        var agent = context.GetAgent(_nodeId);
        if (agent == null)
        {
            return new NodeExecutionResult.Failure(
                Exception: new InvalidOperationException($"Agent not found for node '{_nodeId}'"),
                Severity: ErrorSeverity.Fatal,
                IsTransient: false,
                Duration: stopwatch.Elapsed
            );
        }

        var options = context.GetAgentOptions(_nodeId) ?? new AgentNodeOptions();

        try
        {
            // Resolve input from upstream nodes
            var input = ResolveInput(inputs, options, context);

            // Run agent based on output mode
            var outputs = options.OutputMode switch
            {
                AgentOutputMode.String => await RunStringModeAsync(agent, context, input, options, cancellationToken),
                AgentOutputMode.Structured => await RunStructuredModeAsync(agent, context, input, options, cancellationToken),
                AgentOutputMode.Union => await RunUnionModeAsync(agent, context, input, options, cancellationToken),
                AgentOutputMode.Handoff => await RunHandoffModeAsync(agent, context, input, options, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown output mode: {options.OutputMode}")
            };

            // Evaluate any predicate-based edges originating from this node.
            // Writes synthetic boolean keys (e.g. "__predicate_nodeA_nodeB") into outputs
            // so the graph's FieldEquals conditions can route correctly.
            context.EvaluatePredicateEdges(_nodeId, outputs);

            // Check if approval is required
            if (options.Approval != null)
            {
                var approvalResult = await CheckApprovalAsync(
                    _nodeId, outputs, options, context, cancellationToken);

                if (approvalResult != null)
                {
                    // Return suspended or failure based on approval result
                    return approvalResult;
                }
            }

            stopwatch.Stop();

            return NodeExecutionResult.Success.Single(
                output: outputs,
                duration: stopwatch.Elapsed,
                metadata: new NodeExecutionMetadata()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NodeExecutionResult.Cancelled(
                Reason: CancellationReason.UserRequested,
                Message: "Agent execution was cancelled"
            );
        }
        catch (TimeoutException ex)
        {
            return new NodeExecutionResult.Failure(
                Exception: ex,
                Severity: ErrorSeverity.Transient,
                IsTransient: true,
                Duration: stopwatch.Elapsed,
                ErrorCode: "TIMEOUT"
            );
        }
        catch (HttpRequestException ex)
        {
            return new NodeExecutionResult.Failure(
                Exception: ex,
                Severity: ErrorSeverity.Transient,
                IsTransient: true,
                Duration: stopwatch.Elapsed,
                ErrorCode: "HTTP_ERROR"
            );
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult.Failure(
                Exception: ex,
                Severity: ErrorSeverity.Fatal,
                IsTransient: false,
                Duration: stopwatch.Elapsed
            );
        }
    }

    private static string ResolveInput(HandlerInputs inputs, AgentNodeOptions options, AgentGraphContext context)
    {
        // If template is specified, use it
        if (!string.IsNullOrEmpty(options.InputTemplate))
        {
            return RenderTemplate(options.InputTemplate, inputs);
        }

        // If specific input key is specified
        if (!string.IsNullOrEmpty(options.InputKey))
        {
            if (inputs.TryGet<string>(options.InputKey, out var value) && value != null)
            {
                return value;
            }
        }

        // Priority 1: Check for shared input (original workflow input available to all nodes)
        // This comes from SharedData and is the original user question/input
        if (inputs.TryGet<string>("shared.input", out var sharedInput) && !string.IsNullOrEmpty(sharedInput))
        {
            return sharedInput;
        }

        // Priority 2: Check common semantic keys from upstream outputs
        // These indicate intentional data passing (not routing metadata)
        var commonKeys = new[] { "question", "input", "message", "query", "prompt" };
        foreach (var key in commonKeys)
        {
            if (inputs.TryGet<string>(key, out var value) && value != null)
            {
                return value;
            }

            // Also try namespaced versions (e.g., "solver1.answer")
            var allMatching = inputs.GetAllMatching<string>($"*.{key}");
            if (allMatching.Count > 0 && !string.IsNullOrEmpty(allMatching[0]))
            {
                return allMatching[0];
            }
        }

        // Priority 3: Fall back to context's OriginalInput (legacy support)
        if (!string.IsNullOrEmpty(context.OriginalInput))
        {
            return context.OriginalInput;
        }

        // Priority 4: Last resort - get first string value from upstream outputs
        // This catches cases where upstream explicitly outputs data for downstream
        var allInputs = inputs.GetAll();
        foreach (var kvp in allInputs)
        {
            // Skip shared.* keys (already checked) and routing-like values
            if (kvp.Key.StartsWith("shared."))
                continue;

            if (kvp.Value is string strValue && !string.IsNullOrEmpty(strValue))
            {
                return strValue;
            }
        }

        return string.Empty;
    }

    private static string RenderTemplate(string template, HandlerInputs inputs)
    {
        // Simple template rendering - replace {{key}} with values
        // For full Handlebars support, would need a library
        var result = template;
        var allInputs = inputs.GetAll();

        foreach (var kvp in allInputs)
        {
            var placeholder = $"{{{{{kvp.Key}}}}}";
            var value = kvp.Value?.ToString() ?? "";
            result = result.Replace(placeholder, value);
        }

        return result;
    }

    private static async Task<Dictionary<string, object>> RunStringModeAsync(
        Agent.Agent agent,
        AgentGraphContext context,
        string input,
        AgentNodeOptions options,
        CancellationToken ct)
    {
        var response = new StringBuilder();
        var messages = new[] { new ChatMessage(ChatRole.User, input) };

        // Build per-invocation options (with fallback chat client from parent)
        var runConfig = BuildRunConfig(options, context);

        await foreach (var evt in agent.RunAsync(
            messages,
            options: runConfig,
            cancellationToken: ct))
        {
            // Bubble agent events up to graph event stream
            context.EventCoordinator?.Emit(evt);

            if (evt is TextDeltaEvent textEvt)
            {
                response.Append(textEvt.Text);
            }
        }

        var outputKey = options.OutputKey ?? "answer";
        return new Dictionary<string, object>
        {
            [outputKey] = response.ToString().Trim()
        };
    }

    private static async Task<Dictionary<string, object>> RunStructuredModeAsync(
        Agent.Agent agent,
        AgentGraphContext context,
        string input,
        AgentNodeOptions options,
        CancellationToken ct)
    {
        if (options.StructuredType == null)
        {
            throw new InvalidOperationException("StructuredType must be set for Structured output mode");
        }

        var messages = new[] { new ChatMessage(ChatRole.User, input) };
        var runConfig = BuildRunConfig(options, context);

        // Configure structured output
        runConfig.StructuredOutput = new StructuredOutputOptions
        {
            Mode = options.StructuredOutputMode == StructuredOutputMode.Native ? "native" : "tool",
            SchemaName = options.StructuredType.Name
        };

        object? result = null;

        await foreach (var evt in agent.RunAsync(
            messages,
            options: runConfig,
            cancellationToken: ct))
        {
            context.EventCoordinator?.Emit(evt);

            // Capture the final structured result using reflection
            // StructuredResultEvent<T> is generic, so we check by name and use reflection
            var evtType = evt.GetType();
            if (evtType.IsGenericType &&
                evtType.GetGenericTypeDefinition().Name.StartsWith("StructuredResultEvent"))
            {
                var isPartialProp = evtType.GetProperty("IsPartial");
                var valueProp = evtType.GetProperty("Value");

                if (isPartialProp != null && valueProp != null)
                {
                    var isPartial = (bool)(isPartialProp.GetValue(evt) ?? true);
                    if (!isPartial)
                    {
                        result = valueProp.GetValue(evt);
                    }
                }
            }
        }

        return FlattenToOutputs(result, options.StructuredType);
    }

    private static async Task<Dictionary<string, object>> RunUnionModeAsync(
        Agent.Agent agent,
        AgentGraphContext context,
        string input,
        AgentNodeOptions options,
        CancellationToken ct)
    {
        if (options.UnionTypes == null || options.UnionTypes.Length == 0)
        {
            throw new InvalidOperationException("UnionTypes must be set for Union output mode");
        }

        var messages = new[] { new ChatMessage(ChatRole.User, input) };
        var runConfig = BuildRunConfig(options, context);

        // Configure union output
        runConfig.StructuredOutput = new StructuredOutputOptions
        {
            Mode = options.StructuredOutputMode == StructuredOutputMode.Native ? "union" : "tool",
            UnionTypes = options.UnionTypes
        };

        object? result = null;
        Type? matchedType = null;

        await foreach (var evt in agent.RunAsync(
            messages,
            options: runConfig,
            cancellationToken: ct))
        {
            context.EventCoordinator?.Emit(evt);

            // Capture the final structured result using reflection
            var evtType = evt.GetType();
            if (evtType.IsGenericType &&
                evtType.GetGenericTypeDefinition().Name.StartsWith("StructuredResultEvent"))
            {
                var isPartialProp = evtType.GetProperty("IsPartial");
                var valueProp = evtType.GetProperty("Value");

                if (isPartialProp != null && valueProp != null)
                {
                    var isPartial = (bool)(isPartialProp.GetValue(evt) ?? true);
                    if (!isPartial)
                    {
                        result = valueProp.GetValue(evt);
                        matchedType = result?.GetType();
                    }
                }
            }
        }

        var outputs = FlattenToOutputs(result, matchedType);

        // Add matched_type for RouteByType() routing
        if (matchedType != null)
        {
            outputs["matched_type"] = matchedType.Name;
        }

        return outputs;
    }

    private static async Task<Dictionary<string, object>> RunHandoffModeAsync(
        Agent.Agent agent,
        AgentGraphContext context,
        string input,
        AgentNodeOptions options,
        CancellationToken ct)
    {
        if (options.HandoffTargets == null || options.HandoffTargets.Count == 0)
        {
            throw new InvalidOperationException(
                "Handoff mode requires at least one handoff target. Use WithHandoff() to configure targets.");
        }

        var messages = new[] { new ChatMessage(ChatRole.User, input) };
        string? selectedHandoff = null;
        var responseText = new StringBuilder();

        var runConfig = BuildRunConfig(options, context);

        // Generate and inject handoff tools via public AdditionalTools API
        var handoffTools = HandoffToolGenerator.CreateHandoffTools(options.HandoffTargets);
        if (handoffTools.Count > 0)
        {
            runConfig.AdditionalTools = handoffTools;

            // Force tool calling mode so the agent must call a handoff
            runConfig.ToolModeOverride = ChatToolMode.RequireAny;
        }

        // Append handoff instructions to system prompt
        var handoffInstructions = HandoffToolGenerator.CreateHandoffSystemPrompt(options.HandoffTargets);
        if (!string.IsNullOrEmpty(handoffInstructions))
        {
            runConfig.AdditionalSystemInstructions = string.IsNullOrEmpty(runConfig.AdditionalSystemInstructions)
                ? handoffInstructions
                : runConfig.AdditionalSystemInstructions + handoffInstructions;
        }

        await foreach (var evt in agent.RunAsync(
            messages,
            options: runConfig,
            cancellationToken: ct))
        {
            context.EventCoordinator?.Emit(evt);

            // Look for handoff tool calls
            if (evt is ToolCallStartEvent toolCall && toolCall.Name.StartsWith("handoff_to_"))
            {
                selectedHandoff = toolCall.Name.Replace("handoff_to_", "");
            }

            // Also capture any text response
            if (evt is TextDeltaEvent textEvt)
            {
                responseText.Append(textEvt.Text);
            }
        }

        if (selectedHandoff == null)
        {
            throw new InvalidOperationException(
                $"No handoff tool was called by the agent. Expected one of: {string.Join(", ", options.HandoffTargets.Keys.Select(k => $"handoff_to_{k}"))}");
        }

        var outputs = new Dictionary<string, object>
        {
            ["handoff_target"] = selectedHandoff
        };

        // Include any response text as well
        var responseStr = responseText.ToString().Trim();
        if (!string.IsNullOrEmpty(responseStr))
        {
            outputs["response"] = responseStr;
        }

        return outputs;
    }

    private static AgentRunConfig BuildRunConfig(AgentNodeOptions options, AgentGraphContext? graphContext = null)
    {
        var runConfig = new AgentRunConfig();

        if (!string.IsNullOrEmpty(options.AdditionalSystemInstructions))
        {
            runConfig.AdditionalSystemInstructions = options.AdditionalSystemInstructions;
        }

        if (options.ContextInstances != null && options.ContextInstances.Count > 0)
        {
            runConfig.ContextInstances = new Dictionary<string, IToolMetadata>();
            foreach (var kvp in options.ContextInstances)
            {
                if (kvp.Value is IToolMetadata metadata)
                {
                    runConfig.ContextInstances[kvp.Key] = metadata;
                }
            }
        }

        if (options.Timeout.HasValue)
        {
            runConfig.RunTimeout = options.Timeout.Value;
        }

        // Use fallback chat client from parent agent if available
        if (graphContext?.FallbackChatClient != null)
        {
            runConfig.OverrideChatClient = graphContext.FallbackChatClient;
        }

        return runConfig;
    }

    private static Dictionary<string, object> FlattenToOutputs(object? result, Type? type)
    {
        if (result == null)
        {
            return new Dictionary<string, object>();
        }

        var outputs = new Dictionary<string, object>();
        var targetType = type ?? result.GetType();

        // Flatten each property to a separate output key
        foreach (var prop in targetType.GetProperties())
        {
            var value = prop.GetValue(result);
            if (value != null)
            {
                // Use lowercase property name for consistency
                outputs[prop.Name.ToLowerInvariant()] = value;
            }
        }

        // Also include the full result
        outputs["result"] = result;

        return outputs;
    }

    private static async Task<NodeExecutionResult?> CheckApprovalAsync(
        string nodeId,
        Dictionary<string, object> outputs,
        AgentNodeOptions options,
        AgentGraphContext context,
        CancellationToken cancellationToken)
    {
        var approval = options.Approval;
        if (approval?.Condition == null)
            return null;

        // Build approval context
        var approvalContext = new ApprovalContext
        {
            NodeId = nodeId,
            Outputs = outputs,
            OriginalInput = context.OriginalInput,
            WorkflowData = context.WorkflowData
        };

        // Check if approval is needed
        if (!approval.Condition(approvalContext))
            return null;

        // Generate request ID and message
        var requestId = Guid.NewGuid().ToString();
        var message = approval.Message?.Invoke(approvalContext) ?? "Approval required";
        var description = approval.Description?.Invoke(approvalContext);

        // Emit approval request event
        context.EventCoordinator?.Emit(new NodeApprovalRequestEvent
        {
            RequestId = requestId,
            SourceName = $"AgentNode:{nodeId}",
            NodeId = nodeId,
            Message = message,
            Description = description,
            Metadata = outputs.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value)
        });

        // If timeout is zero, suspend indefinitely (for long-term approvals)
        if (approval.Timeout == TimeSpan.Zero)
        {
            return NodeExecutionResult.Suspended.ForHumanApproval(
                suspendToken: requestId,
                resumeValue: outputs,
                message: message
            );
        }

        // Wait for approval response via EventCoordinator
        if (context.EventCoordinator == null)
        {
            throw new InvalidOperationException(
                "EventCoordinator is required for approval workflows. Ensure the context has an EventCoordinator configured.");
        }

        try
        {
            var response = await context.EventCoordinator.WaitForResponseAsync<NodeApprovalResponseEvent>(
                requestId,
                approval.Timeout,
                cancellationToken);

            if (response.Approved)
            {
                // Approval granted - continue with original outputs
                // If resume data provided, merge it
                if (response.ResumeData is Dictionary<string, object> resumeData)
                {
                    foreach (var kvp in resumeData)
                    {
                        outputs[kvp.Key] = kvp.Value;
                    }
                }
                return null; // Continue to success
            }
            else
            {
                // Approval denied
                return new NodeExecutionResult.Skipped(
                    Reason: SkipReason.ManualSkip, // User explicitly denied
                    Message: response.Reason ?? "Approval denied"
                );
            }
        }
        catch (TimeoutException)
        {
            // Handle timeout based on configuration
            context.EventCoordinator.Emit(new NodeApprovalTimeoutEvent
            {
                RequestId = requestId,
                SourceName = $"AgentNode:{nodeId}",
                NodeId = nodeId,
                WaitedFor = approval.Timeout
            });

            return approval.TimeoutBehavior switch
            {
                ApprovalTimeoutBehavior.AutoApprove => null, // Continue as if approved
                ApprovalTimeoutBehavior.SuspendIndefinitely => NodeExecutionResult.Suspended.ForHumanApproval(
                    suspendToken: requestId,
                    resumeValue: outputs,
                    message: $"Approval timed out after {approval.Timeout.TotalMinutes} minutes"
                ),
                _ => new NodeExecutionResult.Failure(
                    Exception: new TimeoutException($"Approval timed out after {approval.Timeout.TotalMinutes} minutes"),
                    Severity: ErrorSeverity.Warning, // Recoverable - could be retried
                    IsTransient: false,
                    Duration: approval.Timeout,
                    ErrorCode: "APPROVAL_TIMEOUT"
                )
            };
        }
    }
}

