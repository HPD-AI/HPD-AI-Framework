using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class FunctionExecutionUnionValidationTests
{
    [Fact]
    public async Task PreparationPreservesSelectedBranchError()
    {
        var error = await PrepareErrorAsync("""{"request":{"action":"launch"}}""");

        Assert.Equal("request.target", error.GetProperty("property").GetString());
        Assert.Equal("missing_required_property", error.GetProperty("error_code").GetString());
        Assert.False(error.TryGetProperty("attempted_value", out _));
        Assert.False(error.TryGetProperty("allowed_values", out _));
    }

    [Fact]
    public async Task PreparationPreservesUnknownActionMetadata()
    {
        var error = await PrepareErrorAsync("""{"request":{"action":"status"}}""");

        Assert.Equal("request.action", error.GetProperty("property").GetString());
        Assert.Equal("unknown_union_discriminator", error.GetProperty("error_code").GetString());
        Assert.Equal("status", error.GetProperty("attempted_value").GetString());
        Assert.Equal(
            ["launch", "listSessions"],
            error.GetProperty("allowed_values").EnumerateArray().Select(static value => value.GetString()!).ToArray());
    }

    [Fact]
    public async Task PreparationAcceptsKnownAction()
    {
        var preparation = await PrepareAsync("""{"request":{"action":"launch","target":"app.dll"}}""");

        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal("launch", preparation.ResolvedInvocation?.Action);
    }

    [Fact]
    public void ValidationErrorResponseSerializesUnionMetadataThroughAotContext()
    {
        using var attempted = JsonDocument.Parse("\"status\"");
        var response = new ValidationErrorResponse
        {
            Errors =
            [
                new ValidationError
                {
                    Property = "request.action",
                    ErrorCode = "unknown_union_discriminator",
                    ErrorMessage = "Unsupported discriminator value.",
                    AttemptedValue = attempted.RootElement.Clone(),
                    AllowedValues = ["launch", "listSessions"]
                }
            ]
        };

        var json = JsonSerializer.SerializeToElement(response, HPDJsonContext.Default.ValidationErrorResponse);
        var error = json.GetProperty("errors")[0];

        Assert.Equal("status", error.GetProperty("attempted_value").GetString());
        Assert.Equal(
            ["launch", "listSessions"],
            error.GetProperty("allowed_values").EnumerateArray().Select(static value => value.GetString()!).ToArray());
    }

    [Fact]
    public async Task DirectInvocationPreservesCustomValidatorMetadata()
    {
        using var attempted = JsonDocument.Parse("\"status\"");
        var customFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CustomValidation",
                Validator = (_, _) =>
                [
                    new ValidationError
                    {
                        Property = "request.action",
                        ErrorCode = "unknown_union_discriminator",
                        ErrorMessage = "Unsupported discriminator value.",
                        AttemptedValue = attempted.RootElement.Clone(),
                        AllowedValues = ["launch", "listSessions"]
                    }
                ]
            }));

        var result = await customFunction.InvokeAsync(
            new AIFunctionArguments(),
            CreateContext(customFunction),
            CancellationToken.None);
        var error = Assert.IsType<JsonElement>(result).GetProperty("errors")[0];

        Assert.Equal("status", error.GetProperty("attempted_value").GetString());
        Assert.Equal(
            ["launch", "listSessions"],
            error.GetProperty("allowed_values").EnumerateArray().Select(static value => value.GetString()!).ToArray());
    }

    private static async Task<JsonElement> PrepareErrorAsync(string arguments)
    {
        var preparation = await PrepareAsync(arguments);
        var result = Assert.IsType<JsonElement>(preparation.ImmediateOutcome?.Result);
        return result.GetProperty("errors")[0].Clone();
    }

    private static async Task<FunctionExecutionPreparation> PrepareAsync(string arguments)
    {
        var function = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "ActionTool",
                SchemaProvider = Schema,
                OperationContract = OperationContract()
            }));
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var context = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            session: null,
            thread: null,
            CancellationToken.None);
        var core = new FunctionExecutionCore(new AgentMiddlewarePipeline([]));
        var call = FunctionExecutionCore.NormalizeProviderFunctionCall(new FunctionCallContent(
            "call-1",
            function.Name,
            ParseArguments(arguments)));

        return await core.PrepareFunctionAsync(
            call,
            new ChatOptions { Tools = [function] },
            new AgentRunConfig(),
            context,
            invocation: null,
            CancellationToken.None,
            admit: false);
    }

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {
                      "type":"object",
                      "properties":{
                        "action":{"type":"string","const":"launch"},
                        "target":{"type":"string"}
                      },
                      "required":["action","target"],
                      "additionalProperties":false
                    },
                    {
                      "type":"object",
                      "properties":{
                        "action":{"type":"string","const":"listSessions"}
                      },
                      "required":["action"],
                      "additionalProperties":false
                    }
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """);
        return document.RootElement.Clone();
    }

    private static AIFunctionOperationContract OperationContract() => new()
    {
        ActionArgumentName = "request",
        Discriminator = "action",
        Actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal)
        {
            ["launch"] = Policy(),
            ["listSessions"] = Policy()
        }
    };

    private static AIFunctionActionPolicy Policy() => new()
    {
        InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
        InvocationModeHandling = AgentInvocationModeHandling.Runtime,
        Permission = new AIFunctionPermissionDeclaration
        {
            RequiresPermission = false,
            Authority = "test/action",
            Source = PermissionDeclarationSource.FrameworkDefault
        }
    };

    private static Dictionary<string, object?> ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => (object?)property.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static FunctionExecutionContext CreateContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "UnionTests");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "union-tests") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "UnionTests",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var before = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);
        return new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                Function = function,
                CallId = "call-1",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
    }
}
