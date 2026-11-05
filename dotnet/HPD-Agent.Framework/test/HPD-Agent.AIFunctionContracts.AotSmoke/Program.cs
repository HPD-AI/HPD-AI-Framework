using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using HPD.Agent.Serialization;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

var harness = new ContractSmokeHarness();
var function = ContractSmokeHarnessRegistration.CreateToolHarness(harness).Single();
var hpdFunction = (HPDAIFunctionFactory.HPDAIFunction)function;
if (hpdFunction.Replay != ReplayPolicy.Idempotent ||
    hpdFunction.OperationContract?.Actions["launch"].Replay != ReplayPolicy.Idempotent ||
    hpdFunction.OperationContract.Actions["continue"].Replay != ReplayPolicy.Never)
{
    return 8;
}

var requestSchema = function.JsonSchema.GetProperty("properties").GetProperty("request");
if (requestSchema.GetProperty("oneOf").GetArrayLength() != 2)
    return 1;
var branches = requestSchema.GetProperty("oneOf");
if (branches[0].GetProperty("properties").TryGetProperty("invocationMode", out _) ||
    !branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _))
    return 3;

using var document = JsonDocument.Parse("""{"request":{"action":"launch","target":"worker","retries":[1,2]}}""");
var arguments = new AIFunctionArguments();
arguments.SetJson(document.RootElement.Clone());
var result = await ((HPDAIFunctionFactory.HPDAIFunction)function).InvokeAsync(
    arguments,
    CreateContext(function),
    CancellationToken.None);

var subAgentFunction = SubAgentsFunctionFactory.Create([new SubAgentActionDescriptor
{
    ParentToolHarness = "AotSmokeHarness",
    RequiresToolHarnessActivation = false,
    Action = "reviewer",
    Description = "Reviews code.",
    CapabilityId = CapabilityId.Create("aot:reviewer"),
    Definition = SubAgent.FromConfig("reviewer", "reviewer-agent", "Reviews code.", new AgentConfig()),
    InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
    InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
    ContextPolicy = SubAgentContextPolicy.Fresh,
    RequiresPermission = true,
    BranchBinder = json => SubAgentGeneratedBranchBinder.Bind(json, allowContext: false)
}]);
if (!subAgentFunction.JsonSchema.GetRawText().Contains("reviewer", StringComparison.Ordinal))
    return 4;
using var subAgentBranch = JsonDocument.Parse("""{"input":"inspect"}""");
var subAgentBinding = SubAgentGeneratedBranchBinder.Bind(subAgentBranch.RootElement, allowContext: false);
if (subAgentBinding.Value is not BoundSubAgentStartAction { Input: "inspect", Context: null })
    return 6;
using var subAgentInvocationDocument = JsonDocument.Parse("""{"request":{"action":"reviewer","input":"inspect"}}""");
var subAgentArguments = new AIFunctionArguments();
subAgentArguments.SetJson(subAgentInvocationDocument.RootElement.Clone());
try
{
    _ = await ((HPDAIFunctionFactory.HPDAIFunction)subAgentFunction).InvokeAsync(
        subAgentArguments,
        CreateContext(subAgentFunction, subAgentInvocationDocument.RootElement.GetProperty("request")),
        CancellationToken.None);
    return 7;
}
catch (InvalidOperationException exception) when (
    exception.Message.Contains("subagent_creation_requires_session_store", StringComparison.Ordinal))
{
    // Verified action selection and the final generated branch binder executed. The
    // smoke context intentionally has no durable subagent runtime, so dispatch stops here.
}
var operationJson = JsonSerializer.Serialize<SubAgentActionResult>(new SubAgentOperationResult
{
    Status = SubAgentOperationStatus.Completed,
    Child = "reviewer-1",
    Output = "ok"
}, HPDJsonContext.Default.SubAgentActionResult);
var forkJson = JsonSerializer.Serialize(new ThreadForkResult
{
    OperationId = "fork-1",
    Source = new ThreadKey("s", "source"),
    Target = new ThreadKey("s", "target"),
    SourceBoundary = new ThreadJournalCursor(1, 2),
    SubAgentPolicy = SubAgentForkPolicy.Detach,
    Status = ThreadForkOperationStatus.Committed,
    Children = []
}, HPDJsonContext.Default.ThreadForkResult);
if (!operationJson.Contains("reviewer-1", StringComparison.Ordinal) ||
    !forkJson.Contains("fork-1", StringComparison.Ordinal))
    return 5;

var retryConfig = new ErrorHandlingConfig
{
    ModelRetries = new() { MaxRetries = 2 },
    FunctionRetries = new()
    {
        MaxRetries = 1,
        RetryDelay = TimeSpan.Zero,
        Exclude = new() { Harnesses = ["OtherHarness"] }
    }
};
var retryJson = HpdAgentConfigSerializer.Serialize(new AgentConfig
{
    Name = "AOT Retry",
    ErrorHandling = retryConfig
});
var restored = HpdAgentConfigSerializer.Deserialize(retryJson);
if (restored?.ErrorHandling?.ModelRetries.MaxRetries != 2 ||
    restored.ErrorHandling.FunctionRetries.MaxRetries != 1 ||
    restored.ErrorHandling.FunctionRetries.Exclude.Harnesses.Single() != "OtherHarness")
{
    return 9;
}

var retryMiddleware = new RetryMiddleware(restored.ErrorHandling);
var retryAttempts = 0;
var retryResult = await retryMiddleware.WrapFunctionCallAsync(
    RetryRequest(function),
    _ =>
    {
        retryAttempts++;
        if (retryAttempts == 1)
            throw new InvalidOperationException("transient");
        return Task.FromResult<object?>("retried");
    },
    CancellationToken.None);
if (!string.Equals(retryResult as string, "retried", StringComparison.Ordinal) || retryAttempts != 2)
    return 10;

var actionAttempts = 0;
try
{
    _ = await retryMiddleware.WrapFunctionCallAsync(
        RetryRequest(function, "continue"),
        _ =>
        {
            actionAttempts++;
            throw new InvalidOperationException("must not replay");
        },
        CancellationToken.None);
    return 11;
}
catch (InvalidOperationException exception) when (
    exception.Message == "must not replay" && actionAttempts == 1)
{
}

return result as string == "worker:2" && harness.InvocationCount == 1 ? 0 : 2;

static FunctionRequest RetryRequest(AIFunction function, string? action = null)
{
    var state = AgentLoopState.InitialSafe([], "retry-run", "retry-conversation", "RetryAgent");
    return new FunctionRequest
    {
        Function = function,
        CallId = "retry-call",
        Arguments = new Dictionary<string, object?>(),
        State = state,
        InvocationMode = action is null
            ? null
            : new ResolvedFunctionInvocation
            {
                Action = action,
                Mode = AgentInvocationMode.Synchronous,
                Policy = AgentInvocationModePolicy.SynchronousOnly,
                Handling = AgentInvocationModeHandling.Runtime
            }
    };
}

static global::HPD.Agent.Middleware.FunctionExecutionContext CreateContext(
    AIFunction function,
    JsonElement? validatedAction = null)
{
    var state = AgentLoopState.InitialSafe([], "aot-run", "aot-conversation", "AotAgent");
    var session = new global::HPD.Agent.Session("aot-session");
    var thread = new global::HPD.Agent.Thread("aot-session", "AotAgent") { Id = "aot-thread" };
    var agentContext = new global::HPD.Agent.Middleware.AgentContext(
        "AotAgent",
        "aot-conversation",
        state,
        new EventCoordinator(),
        session,
        thread,
        CancellationToken.None);
    var before = agentContext.AsBeforeFunction(
        function,
        "aot-call",
        new Dictionary<string, object?>(),
        new AgentRunConfig(),
        toolharnessName: null,
        skillName: null);
    return new global::HPD.Agent.Middleware.FunctionExecutionContext(
        before,
        new FunctionRequest
        {
            Function = function,
            CallId = "aot-call",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = agentContext.EventCoordinator,
            InvocationMode = validatedAction is { } action
                ? new ResolvedFunctionInvocation
                {
                    Action = "reviewer",
                    Mode = AgentInvocationMode.Synchronous,
                    Policy = AgentInvocationModePolicy.SynchronousOnly,
                    Handling = AgentInvocationModeHandling.ToolBody,
                    ValidatedAction = new ValidatedFunctionAction
                    {
                        Action = "reviewer",
                        CanonicalJson = action.Clone()
                    },
                    IngressProvenance = FunctionArgumentIngressProvenance.Original
                }
                : null
        });
}

public sealed partial class ContractSmokeHarness
{
    public int InvocationCount { get; private set; }

    [AIFunction(Replay = ReplayPolicy.Idempotent)]
    public string Execute(OperationRequest request)
    {
        InvocationCount++;
        return request switch
        {
            LaunchRequest launch => $"{launch.Target}:{launch.Retries.Count}",
            ContinueRequest continueRequest => continueRequest.DebugTreeId,
            _ => throw new InvalidOperationException("Unsupported request.")
        };
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(LaunchRequest), "launch")]
[JsonDerivedType(typeof(ContinueRequest), "continue")]
public abstract record OperationRequest;

[AIFunctionAction("launch")]
public sealed record LaunchRequest(string Target, IReadOnlyList<int> Retries) : OperationRequest;

[AIFunctionAction("continue",
    InvocationModePolicy = AIFunctionActionInvocationModePolicy.ModelChoice,
    InvocationModeHandling = AIFunctionActionInvocationModeHandling.ToolBody,
    Replay = ActionReplayPolicy.Never)]
public sealed record ContinueRequest(string DebugTreeId, int? ThreadId = null) : OperationRequest;
