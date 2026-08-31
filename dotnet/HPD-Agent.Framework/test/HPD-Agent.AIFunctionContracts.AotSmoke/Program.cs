using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

var harness = new ContractSmokeHarness();
var function = ContractSmokeHarnessRegistration.CreateToolHarness(harness).Single();
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

return result as string == "worker:2" && harness.InvocationCount == 1 ? 0 : 2;

static global::HPD.Agent.Middleware.FunctionExecutionContext CreateContext(AIFunction function)
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
            EventCoordinator = agentContext.EventCoordinator
        });
}

public sealed partial class ContractSmokeHarness
{
    public int InvocationCount { get; private set; }

    [AIFunction]
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
    InvocationModeHandling = AIFunctionActionInvocationModeHandling.ToolBody)]
public sealed record ContinueRequest(string DebugTreeId, int? ThreadId = null) : OperationRequest;
