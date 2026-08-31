using HPD.Agent.Middleware;
using HPD.Agent.ClientTools;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace HPD.Agent;

internal sealed record FunctionExecutionOutcome(
    string CallId,
    string? FunctionName,
    AIFunction? Function,
    object? Result,
    ToolResultPayload ResultPayload,
    Exception? Exception,
    bool WasBlocked,
    bool WasUnknown,
    bool WasOutputTool,
    bool ShouldTerminate,
    string? ToolHarnessName,
    ToolCallType? CallType,
    ToolResultMetadata ResultMetadata,
    ToolInvocationInfo? Invocation = null);

internal sealed record FunctionExecutionPreparation(
    FunctionCallContent FunctionCall,
    ToolInvocationInfo? Invocation,
    AIFunction? Function,
    IReadOnlyDictionary<string, object?> Arguments,
    ResolvedFunctionInvocation? ResolvedInvocation,
    BeforeFunctionContext? BeforeFunctionContext,
    FunctionExecutionOutcome? ImmediateOutcome,
    string? ToolHarnessName,
    ToolCallType? CallType);

internal sealed record FunctionBodyExecutionResult(
    FunctionExecutionPreparation Preparation,
    object? Result,
    ToolResultMetadata ResultMetadata,
    Exception? Exception);

internal interface IFunctionExecutionCore
{
    Task<FunctionExecutionOutcome> ExecuteFunctionAsync(
        FunctionCallContent functionCall,
        ChatOptions? options,
        AgentRunConfig runConfig,
        AgentContext agentContext,
        ToolInvocationInfo? invocation,
        CancellationToken cancellationToken);
}

internal sealed class FunctionExecutionCore : IFunctionExecutionCore
{
    private readonly AgentMiddlewarePipeline _middlewarePipeline;
    private readonly ErrorHandlingConfig? _errorHandlingConfig;
    private readonly IList<AITool>? _serverConfiguredTools;
    private readonly AgenticLoopConfig? _agenticLoopConfig;

    public FunctionExecutionCore(
        AgentMiddlewarePipeline middlewarePipeline,
        ErrorHandlingConfig? errorHandlingConfig = null,
        IList<AITool>? serverConfiguredTools = null,
        AgenticLoopConfig? agenticLoopConfig = null)
    {
        _middlewarePipeline = middlewarePipeline ?? throw new ArgumentNullException(nameof(middlewarePipeline));
        _errorHandlingConfig = errorHandlingConfig;
        _serverConfiguredTools = serverConfiguredTools;
        _agenticLoopConfig = agenticLoopConfig;
    }

    public Dictionary<string, AIFunction>? BuildMergedMap(IList<AITool>? requestTools) =>
        BuildMergedMap(_serverConfiguredTools, requestTools);

    internal static Dictionary<string, AIFunction>? BuildMergedMap(
        IList<AITool>? serverTools,
        IList<AITool>? requestTools)
    {
        if (serverTools is not { Count: > 0 } &&
            requestTools is not { Count: > 0 })
        {
            return null;
        }

        var map = new Dictionary<string, AIFunction>(StringComparer.Ordinal);

        if (serverTools is { Count: > 0 })
        {
            for (int i = 0; i < serverTools.Count; i++)
            {
                if (serverTools[i] is AIFunction af)
                {
                    map[af.Name] = af;
                }
            }
        }

        if (requestTools is { Count: > 0 })
        {
            for (int i = 0; i < requestTools.Count; i++)
            {
                if (requestTools[i] is AIFunction af)
                {
                    map[af.Name] = af;
                }
            }
        }

        return map.Count > 0 ? map : null;
    }

    public static AIFunction? FindFunction(
        string name,
        Dictionary<string, AIFunction>? map)
    {
        return map?.TryGetValue(name, out var func) == true ? func : null;
    }

    public bool IsOutputToolByName(string? functionName, IList<AITool>? tools)
    {
        if (string.IsNullOrEmpty(functionName))
            return false;

        var functionMap = BuildMergedMap(tools);
        var function = FindFunction(functionName, functionMap);
        return IsOutputTool(function);
    }

    public static bool IsOutputTool(AIFunction? function)
    {
        return function?.AdditionalProperties?.TryGetValue("Kind", out var kind) == true
               && kind?.ToString() == "Output";
    }

    public string? LookupToolHarnessName(string? functionName, IList<AITool>? tools)
    {
        if (string.IsNullOrEmpty(functionName))
            return null;

        var functionMap = BuildMergedMap(tools);
        var function = FindFunction(functionName, functionMap);
        return LookupToolHarnessName(function);
    }

    public static string? LookupToolHarnessName(AIFunction? function)
    {
        if (function == null)
            return null;

        if (function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var parentToolHarness) == true
            && parentToolHarness is string pt)
        {
            return pt;
        }

        if (function.AdditionalProperties?.TryGetValue("ToolHarnessName", out var toolharnessName) == true
            && toolharnessName is string tn)
        {
            return tn;
        }

        return null;
    }

    public ToolCallType? LookupToolCallType(string? functionName, IList<AITool>? tools)
    {
        if (string.IsNullOrEmpty(functionName))
            return null;

        var functionMap = BuildMergedMap(tools);
        var function = FindFunction(functionName, functionMap);
        return LookupToolCallType(function);
    }

    public static ToolCallType? LookupToolCallType(AIFunction? function)
    {
        if (function?.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey,
                out var typedValue) == true &&
            typedValue is HPDCapabilityMetadata metadata)
        {
            return metadata.Kind switch
            {
                HPDCapabilityKind.SkillActivation => ToolCallType.Skill,
                HPDCapabilityKind.SubAgent => ToolCallType.SubAgent,
                HPDCapabilityKind.MultiAgent => ToolCallType.MultiAgent,
                HPDCapabilityKind.Mcp => ToolCallType.McpServer,
                HPDCapabilityKind.OpenApi => ToolCallType.OpenApi,
                HPDCapabilityKind.Function or
                HPDCapabilityKind.SkillResource or
                HPDCapabilityKind.SkillScript or
                HPDCapabilityKind.ToolHarnessActivation => ToolCallType.Function,
                _ => null
            };
        }

        if (function?.AdditionalProperties?.TryGetValue("CapabilityType", out var capType) != true
            || capType is not string capTypeStr)
            return null;

        return capTypeStr switch
        {
            "Function"   => ToolCallType.Function,
            "Skill"      => ToolCallType.Skill,
            "SubAgent"   => ToolCallType.SubAgent,
            "MultiAgent" => ToolCallType.MultiAgent,
            "MCPServer"  => ToolCallType.McpServer,
            "OpenApi"    => ToolCallType.OpenApi,
            _            => null,
        };
    }

    public async Task<FunctionExecutionOutcome> ExecuteFunctionAsync(
        FunctionCallContent functionCall,
        ChatOptions? options,
        AgentRunConfig runConfig,
        AgentContext agentContext,
        ToolInvocationInfo? invocation,
        CancellationToken cancellationToken)
    {
        var preparation = await PrepareFunctionAsync(
            functionCall,
            options,
            runConfig,
            agentContext,
            invocation,
            cancellationToken).ConfigureAwait(false);

        if (preparation.ImmediateOutcome is { } immediateOutcome)
            return immediateOutcome;

        var bodyResult = await ExecuteFunctionBodyAsync(
            preparation,
            agentContext,
            cancellationToken).ConfigureAwait(false);

        return await CompleteFunctionAsync(
            bodyResult,
            runConfig,
            agentContext,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<FunctionExecutionPreparation> PrepareFunctionAsync(
        FunctionCallContent functionCall,
        ChatOptions? options,
        AgentRunConfig runConfig,
        AgentContext agentContext,
        ToolInvocationInfo? invocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(functionCall.Name))
        {
            var outcome = new FunctionExecutionOutcome(
                functionCall.CallId,
                functionCall.Name,
                Function: null,
                Result: null,
                ResultPayload: ToolResultPayload.FromResult(null),
                Exception: null,
                WasBlocked: false,
                WasUnknown: false,
                WasOutputTool: false,
                ShouldTerminate: false,
                ToolHarnessName: null,
                CallType: null,
                ResultMetadata: new ToolResultMetadata(),
                Invocation: invocation);

            return new FunctionExecutionPreparation(
                functionCall,
                invocation,
                Function: null,
                Arguments: new Dictionary<string, object?>(),
                ResolvedInvocation: null,
                BeforeFunctionContext: null,
                ImmediateOutcome: outcome,
                ToolHarnessName: null,
                CallType: null);
        }

        var functionMap = BuildMergedMap(options?.Tools);
        var function = FindFunction(functionCall.Name, functionMap);
        var toolharnessName = LookupToolHarnessName(function);
        var callType = LookupToolCallType(function);

        if (IsOutputTool(function))
        {
            var outcome = new FunctionExecutionOutcome(
                functionCall.CallId,
                functionCall.Name,
                function,
                Result: null,
                ResultPayload: ToolResultPayload.FromResult(null),
                Exception: null,
                WasBlocked: false,
                WasUnknown: false,
                WasOutputTool: true,
                ShouldTerminate: false,
                ToolHarnessName: toolharnessName,
                CallType: callType,
                ResultMetadata: new ToolResultMetadata(),
                Invocation: invocation);

            return new FunctionExecutionPreparation(
                functionCall,
                invocation,
                function,
                Arguments: new Dictionary<string, object?>(),
                ResolvedInvocation: null,
                BeforeFunctionContext: null,
                ImmediateOutcome: outcome,
                ToolHarnessName: toolharnessName,
                CallType: callType);
        }

        if (function == null && _agenticLoopConfig?.TerminateOnUnknownCalls == true)
        {
            agentContext.UpdateState(s => s with { IsTerminated = true });

            var outcome = new FunctionExecutionOutcome(
                functionCall.CallId,
                functionCall.Name,
                Function: null,
                Result: null,
                ResultPayload: ToolResultPayload.FromResult(null),
                Exception: null,
                WasBlocked: false,
                WasUnknown: true,
                WasOutputTool: false,
                ShouldTerminate: true,
                ToolHarnessName: null,
                CallType: null,
                ResultMetadata: new ToolResultMetadata(),
                Invocation: invocation);

            return new FunctionExecutionPreparation(
                functionCall,
                invocation,
                Function: null,
                Arguments: new Dictionary<string, object?>(),
                ResolvedInvocation: null,
                BeforeFunctionContext: null,
                ImmediateOutcome: outcome,
                ToolHarnessName: null,
                CallType: null);
        }

        IReadOnlyDictionary<string, object?> arguments = (IReadOnlyDictionary<string, object?>?)(functionCall.Arguments ?? new Dictionary<string, object?>())
            ?? new Dictionary<string, object?>();
        ResolvedFunctionInvocation? resolvedInvocation = null;
        if (function is HPDAIFunctionFactory.HPDAIFunction hpdFunction)
        {
            var source = functionCall.Arguments as AIFunctionArguments
                ?? CreateInvocationArguments(arguments);
            if (hpdFunction.HPDOptions.OperationContract is { } operationContract)
            {
                resolvedInvocation = AgentInvocationModes.ResolveAction(
                    source, operationContract, out var sanitized);
                arguments = sanitized
                    .Where(pair => pair.Key != AIFunctionArgumentsExtensions.JsonKey &&
                        pair.Key != AIFunctionArgumentsExtensions.JsonSerializerOptionsKey &&
                        pair.Key != AIFunctionArgumentsExtensions.BoundArgumentsKey)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
            else
            {
                var sanitized = AgentInvocationModes.CreateSanitizedArguments(source, out var requested);
                var mode = AgentInvocationModes.Resolve(hpdFunction.HPDOptions.InvocationModePolicy, requested);
                resolvedInvocation = new ResolvedFunctionInvocation
                {
                    RequestedMode = requested,
                    Mode = mode,
                    Policy = hpdFunction.HPDOptions.InvocationModePolicy,
                    Handling = hpdFunction.HPDOptions.InvocationModeHandling
                };
                arguments = sanitized
                    .Where(pair => pair.Key != AIFunctionArgumentsExtensions.JsonKey &&
                        pair.Key != AIFunctionArgumentsExtensions.JsonSerializerOptionsKey &&
                        pair.Key != AIFunctionArgumentsExtensions.BoundArgumentsKey)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
        }

        agentContext.RuntimeCapabilities.TryGet<IClientToolOperationRegistry>(
            out var clientToolOperations);
        var beforeFunctionContext = agentContext.AsBeforeFunction(
            function: function!,
            callId: functionCall.CallId,
            arguments: arguments,
            runConfig: runConfig,
            invocationMode: resolvedInvocation,
            toolharnessName: toolharnessName,
            skillName: null,
            invocation: invocation,
            clientToolOperations: clientToolOperations);

        await _middlewarePipeline.ExecuteBeforeFunctionAsync(
            beforeFunctionContext, cancellationToken).ConfigureAwait(false);

        if (beforeFunctionContext.BlockExecution)
        {
            var outcome = new FunctionExecutionOutcome(
                functionCall.CallId,
                functionCall.Name,
                function,
                Result: beforeFunctionContext.OverrideResult ?? "Permission denied",
                ResultPayload: ToolResultPayload.FromResult(beforeFunctionContext.OverrideResult ?? "Permission denied"),
                Exception: null,
                WasBlocked: true,
                WasUnknown: function == null,
                WasOutputTool: false,
                ShouldTerminate: false,
                ToolHarnessName: toolharnessName,
                CallType: callType,
                ResultMetadata: new ToolResultMetadata(),
                Invocation: invocation);

            return new FunctionExecutionPreparation(
                functionCall,
                invocation,
                function,
                arguments,
                resolvedInvocation,
                beforeFunctionContext,
                ImmediateOutcome: outcome,
                ToolHarnessName: toolharnessName,
                CallType: callType);
        }

        return new FunctionExecutionPreparation(
            functionCall,
            invocation,
            function,
            arguments,
            resolvedInvocation,
            beforeFunctionContext,
            ImmediateOutcome: null,
            ToolHarnessName: toolharnessName,
            CallType: callType);
    }

    internal async Task<FunctionBodyExecutionResult> ExecuteFunctionBodyAsync(
        FunctionExecutionPreparation preparation,
        AgentContext agentContext,
        CancellationToken cancellationToken)
    {
        if (preparation.ImmediateOutcome is { } immediateOutcome)
        {
            return new FunctionBodyExecutionResult(
                preparation,
                immediateOutcome.Result,
                immediateOutcome.ResultMetadata,
                immediateOutcome.Exception);
        }

        var functionCall = preparation.FunctionCall;
        var beforeFunctionContext = preparation.BeforeFunctionContext
            ?? throw new InvalidOperationException("Function preparation is missing BeforeFunction context.");

        try
        {
            if (preparation.Function is null)
            {
                var notFoundResult = beforeFunctionContext.OverrideResult
                    ?? $"Function '{functionCall.Name ?? "Unknown"}' not found.";
                return new FunctionBodyExecutionResult(
                    preparation,
                    notFoundResult,
                    new ToolResultMetadata(),
                    Exception: null);
            }

            var resultMetadata = new ToolResultMetadata();
            var functionRequest = new Middleware.FunctionRequest
            {
                ExecutionContext = agentContext,
                Function = preparation.Function,
                CallId = functionCall.CallId,
                Arguments = preparation.Arguments,
                State = agentContext.State,
                RunConfig = beforeFunctionContext.RunConfig,
                Invocation = preparation.Invocation,
                InvocationMode = preparation.ResolvedInvocation,
                ResultMetadata = resultMetadata,
                ToolHarnessName = preparation.ToolHarnessName,
                SkillName = null,
                EventCoordinator = agentContext.EventCoordinator,
                EventPublisher = agentContext.PublishAsync,
                StructEvents = agentContext.StructEvents
            };

            var backgroundGate = preparation.ResolvedInvocation?.Mode == AgentInvocationMode.Background
                ? new SemaphoreSlim(1, 1)
                : null;
            object? committedBackgroundResult = null;
            JsonElement committedBackgroundRequest = default;
            var backgroundCommitted = false;
            var executionResult = await _middlewarePipeline.ExecuteFunctionCallAsync(
                functionRequest,
                coreHandler: async (req) =>
                {
                    VerifyPreparedAuthority(preparation, req);
                    if (backgroundGate is null)
                        return await InvokePreparedCoreAsync(req).ConfigureAwait(false);
                    await backgroundGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var canonicalRequest = JsonSerializer.SerializeToElement(
                            req.Arguments, HPDToolArgumentBinder.DefaultSerializerOptions);
                        if (backgroundCommitted)
                        {
                            if (!JsonElement.DeepEquals(committedBackgroundRequest, canonicalRequest))
                                throw new InvalidOperationException("background_invocation_already_committed");
                            return committedBackgroundResult;
                        }
                        var result = await InvokePreparedCoreAsync(req).ConfigureAwait(false);
                        committedBackgroundRequest = canonicalRequest.Clone();
                        committedBackgroundResult = result;
                        backgroundCommitted = true;
                        return result;
                    }
                    finally { backgroundGate.Release(); }

                    async Task<object?> InvokePreparedCoreAsync(Middleware.FunctionRequest effectiveRequest)
                    {
                        var functionContext = new FunctionExecutionContext(beforeFunctionContext, effectiveRequest);
                        var args = CreateInvocationArguments(
                            effectiveRequest.Arguments,
                            preparation.ResolvedInvocation is null
                                ? functionCall.Arguments as AIFunctionArguments
                                : null);
                        return effectiveRequest.Function is HPDAIFunctionFactory.HPDAIFunction hpdFunction
                            ? await hpdFunction.InvokeAsync(args, functionContext, cancellationToken).ConfigureAwait(false)
                            : await effectiveRequest.Function.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            return new FunctionBodyExecutionResult(
                preparation,
                executionResult,
                resultMetadata,
                Exception: null);
        }
        catch (Exception ex)
        {
            var errorResult = $"Error executing function '{functionCall.Name}': {ex.Message}";
            return new FunctionBodyExecutionResult(
                preparation,
                errorResult,
                new ToolResultMetadata(),
                ex);
        }
    }

    private static void VerifyPreparedAuthority(
        FunctionExecutionPreparation preparation,
        Middleware.FunctionRequest request)
    {
        if (preparation.Function is not HPDAIFunctionFactory.HPDAIFunction prepared ||
            prepared.HPDOptions.OperationContract is not { } contract)
            return;
        if (!ReferenceEquals(preparation.Function, request.Function))
            throw new InvalidOperationException("function_authority_drift: wrapping middleware replaced the authorized function.");
        if (!preparation.Arguments.TryGetValue(contract.ActionArgumentName, out var expected) ||
            !request.Arguments.TryGetValue(contract.ActionArgumentName, out var actual))
            throw new InvalidOperationException("function_authority_drift: wrapping middleware removed the authorized action.");
        var expectedJson = ToCanonicalElement(expected);
        var actualJson = ToCanonicalElement(actual);
        if (!JsonElement.DeepEquals(expectedJson, actualJson) ||
            ContainsExactProperty(actualJson, "invocationMode"))
            throw new InvalidOperationException("function_authority_drift: wrapping middleware changed the authorized action.");
    }

    private static JsonElement ToCanonicalElement(object? value) => value switch
    {
        JsonElement element => element.Clone(),
        JsonDocument document => document.RootElement.Clone(),
        _ => JsonSerializer.SerializeToElement(value, HPDToolArgumentBinder.DefaultSerializerOptions)
    };

    private static bool ContainsExactProperty(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.Ordinal));

    internal static AIFunctionArguments CreateInvocationArguments(
        IReadOnlyDictionary<string, object?> arguments,
        AIFunctionArguments? sourceArguments = null)
    {
        var invocationArguments = new AIFunctionArguments(
            new Dictionary<string, object?>(arguments));
        var sourceJson = sourceArguments?.GetJson() ?? default;
        invocationArguments.SetJson(sourceJson.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(
                arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                HPDJsonContext.Default.DictionaryStringObject)
            : sourceJson.Clone());
        return invocationArguments;
    }

    internal async Task<FunctionExecutionOutcome> CompleteFunctionAsync(
        FunctionBodyExecutionResult bodyResult,
        AgentRunConfig runConfig,
        AgentContext agentContext,
        CancellationToken cancellationToken)
    {
        var preparation = bodyResult.Preparation;
        var functionCall = preparation.FunctionCall;

        if (preparation.ImmediateOutcome is { } immediateOutcome)
            return immediateOutcome;

        if (bodyResult.Exception is { } ex)
        {
            await agentContext.PublishAsync(new MiddlewareErrorEvent(
                "FunctionExecution",
                $"Error executing function '{functionCall.Name}': {ex.Message}") { Exception = ex });

            var errorContext = agentContext.AsError(
                error: ex,
                source: ErrorSource.ToolCall,
                iteration: agentContext.State.Iteration);

            try
            {
                await _middlewarePipeline.ExecuteOnErrorAsync(errorContext, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original function exception if error handlers fail.
            }
        }

        var afterFunctionContext = agentContext.AsAfterFunction(
            function: preparation.Function,
            callId: functionCall.CallId,
            result: bodyResult.Result,
            exception: bodyResult.Exception,
            runConfig: runConfig,
            invocationMode: preparation.ResolvedInvocation,
            toolharnessName: preparation.ToolHarnessName,
            skillName: null,
            invocation: preparation.Invocation,
            resultMetadata: bodyResult.ResultMetadata);

        try
        {
            await _middlewarePipeline.ExecuteAfterFunctionAsync(
                afterFunctionContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception afterEx)
        {
            await agentContext.PublishAsync(new MiddlewareErrorEvent(
                "AfterFunctionMiddleware",
                $"Error in AfterFunction middleware: {afterEx.Message}") { Exception = afterEx });
        }

        return new FunctionExecutionOutcome(
            functionCall.CallId,
            functionCall.Name,
            preparation.Function,
            Result: afterFunctionContext.Result,
            ResultPayload: ToolResultPayload.FromResult(afterFunctionContext.Result),
            Exception: afterFunctionContext.Exception,
            WasBlocked: false,
            WasUnknown: preparation.Function == null,
            WasOutputTool: false,
            ShouldTerminate: false,
            ToolHarnessName: preparation.ToolHarnessName,
            CallType: preparation.CallType,
            ResultMetadata: afterFunctionContext.ResultMetadata,
            Invocation: preparation.Invocation);
    }

    public static FunctionResultContent ToFunctionResultContent(FunctionExecutionOutcome outcome)
    {
        return new FunctionResultContent(outcome.CallId, outcome.Result)
        {
            Exception = outcome.Exception
        };
    }
}
