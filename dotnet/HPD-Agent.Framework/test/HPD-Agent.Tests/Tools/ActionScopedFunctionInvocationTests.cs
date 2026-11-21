using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Tools;

public sealed class ActionScopedFunctionInvocationTests
{
    [Fact]
    public void ResolveAction_UsesSelectedPolicyAndPreservesDomainControlName()
    {
        using var document = JsonDocument.Parse("""
            {"request":{"action":"run","invocationMode":"background","value":3}}
            """);
        var arguments = new AIFunctionArguments
        {
            ["request"] = document.RootElement.GetProperty("request").Clone()
        };
        arguments.SetJson(document.RootElement.Clone());

        var resolved = AgentInvocationModes.ResolveAction(
            arguments, Contract(), out var sanitized, requestedMode: AgentInvocationMode.Background);

        Assert.Equal("run", resolved.Action);
        Assert.Equal(AgentInvocationMode.Background, resolved.RequestedMode);
        Assert.Equal(AgentInvocationMode.Background, resolved.Mode);
        Assert.Equal(AgentInvocationModePolicy.ModelChoice, resolved.Policy);
        Assert.Equal(AgentInvocationModeHandling.ToolBody, resolved.Handling);
        Assert.Equal("background", sanitized.GetJson().GetProperty("request").GetProperty("invocationMode").GetString());
        Assert.Equal(3, sanitized.GetJson().GetProperty("request").GetProperty("value").GetInt32());
    }

    [Fact]
    public void CreateActionSchema_PreservesActionDomainSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "request":{"oneOf":[
                  {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
                  {"type":"object","properties":{"action":{"type":"string","const":"run"}},"required":["action"],"additionalProperties":false}
                ]}
              }
            }
            """);

        var schema = AgentInvocationModes.CreateActionSchema(document.RootElement, Contract());
        var branches = schema.GetProperty("properties").GetProperty("request").GetProperty("oneOf");

        Assert.False(branches[0].GetProperty("properties").TryGetProperty("invocationMode", out _));
        Assert.False(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
    }

    [Fact]
    public void CreateActionSchema_ResolvesBoundedDocumentLocalReferences()
    {
        using var document = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{"request":{"$ref":"#/$defs/request"}},
              "$defs":{
                "request":{"oneOf":[{"$ref":"#/$defs/read"},{"$ref":"#/$defs/run"}]},
                "read":{"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
                "run":{"type":"object","properties":{"action":{"type":"string","const":"run"}},"required":["action"],"additionalProperties":false}
              }
            }
            """);

        var schema = AgentInvocationModes.CreateActionSchema(document.RootElement, Contract());
        var branches = schema.GetProperty("properties").GetProperty("request").GetProperty("oneOf");

        Assert.False(schema.TryGetProperty("$defs", out _));
        Assert.False(branches[0].GetProperty("properties").TryGetProperty("invocationMode", out _));
        Assert.False(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
        AgentInvocationModes.ValidateActionSchema(schema, Contract());
    }

    [Fact]
    public void CreateActionSchema_RejectsExternalReferences()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"$ref":"https://example.test/action.json"}}}
            """);

        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.CreateActionSchema(document.RootElement, Contract()));

        Assert.Contains("document-local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_RegistersFlattenedLocalReferenceContractEndToEnd()
    {
        JsonElement Schema()
        {
            using var document = JsonDocument.Parse("""
                {
                  "type":"object",
                  "properties":{"request":{"$ref":"#/$defs/request"}},
                  "required":["request"],"additionalProperties":false,
                  "$defs":{
                    "request":{"oneOf":[{"$ref":"#/$defs/read"},{"$ref":"#/$defs/run"}]},
                    "read":{"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
                    "run":{"type":"object","properties":{"action":{"type":"string","const":"run"}},"required":["action"],"additionalProperties":false}
                  }
                }
                """);
            return document.RootElement.Clone();
        }

        var function = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "ReferencedAction",
                SchemaProvider = Schema,
                OperationContract = Contract()
            }));

        Assert.False(function.JsonSchema.TryGetProperty("$defs", out _));
        Assert.True(function.JsonSchema.GetProperty("properties").GetProperty("invocation").GetProperty("properties").TryGetProperty("mode", out _));
        Assert.NotNull(function.CanonicalInputContract);
    }

    [Fact]
    public async Task Preparation_UsesCanonicalValuesInsteadOfDivergentDictionary()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"value":{"type":"integer"},"label":{"type":"string","default":"defaulted"}},"required":["value"],"additionalProperties":false}""");
        var function = HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions { Name = "canonical", SchemaProvider = () => schema.RootElement.Clone() });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var context = new global::HPD.Agent.Middleware.AgentContext("AgentA", "conversation-1", state,
            new global::HPD.Events.Core.EventCoordinator(), session: null, thread: null, CancellationToken.None);
        var core = new FunctionExecutionCore(new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));
        using var json = JsonDocument.Parse("""{"value":1}""");
        var args = new AIFunctionArguments { ["value"] = 9 };
        args.SetJson(json.RootElement.Clone());

        var preparation = await core.PrepareFunctionAsync(new FunctionCallContent("call-1", function.Name, args),
            new ChatOptions { Tools = [function] }, new AgentRunConfig(), context, invocation: null,
            CancellationToken.None, admit: false);

        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal(1, Assert.IsType<JsonElement>(preparation.Arguments["value"]).GetInt32());
        Assert.Equal("defaulted", Assert.IsType<JsonElement>(preparation.Arguments["label"]).GetString());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)preparation.Arguments)["value"] = 3);
    }

    [Theory]
    [InlineData("synchronous", AgentInvocationMode.Synchronous)]
    [InlineData("background", AgentInvocationMode.Background)]
    public async Task ModelChoiceEnvelope_PreparesDomainArgumentsAndResolvedMode(string mode, AgentInvocationMode expected)
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"invocationMode":{"type":"string"}},"required":["invocationMode"],"additionalProperties":false}""");
        var function = HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "choice", SchemaProvider = () => schema.RootElement.Clone(),
                InvocationModePolicy = AgentInvocationModePolicy.ModelChoice,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody
            });
        Assert.True(function.JsonSchema.GetProperty("properties").TryGetProperty("arguments", out _));
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var context = new global::HPD.Agent.Middleware.AgentContext("AgentA", "conversation-1", state,
            new global::HPD.Events.Core.EventCoordinator(), session: null, thread: null, CancellationToken.None);
        var core = new FunctionExecutionCore(new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));
        using var input = JsonDocument.Parse("{\"arguments\":{\"invocationMode\":\"domain\"},\"invocation\":{\"mode\":\"" + mode + "\"}}");
        var args = new AIFunctionArguments();
        args.SetJson(input.RootElement.Clone());
        var call = new FunctionCallContent("call-1", function.Name, args);

        var preparation = await core.PrepareFunctionAsync(call, new ChatOptions { Tools = [function] },
            new AgentRunConfig(), context, invocation: null, CancellationToken.None, admit: false);

        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal(expected, preparation.ResolvedInvocation!.Mode);
        Assert.Equal("domain", ((JsonElement)preparation.Arguments["invocationMode"]!).GetString());
        Assert.False(preparation.Arguments.ContainsKey("invocation"));
    }

    [Theory]
    [InlineData("run", "background", false)]
    [InlineData("read", "background", true)]
    [InlineData("read", null, false)]
    public async Task ActionEnvelope_EnforcesSelectedBranchPolicy(string action, string? mode, bool rejected)
    {
        using var schema = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"oneOf":[
              {"type":"object","properties":{"action":{"type":"string","const":"read"},"invocationMode":{"type":"string"}},"required":["action","invocationMode"],"additionalProperties":false},
              {"type":"object","properties":{"action":{"type":"string","const":"run"},"invocationMode":{"type":"string"}},"required":["action","invocationMode"],"additionalProperties":false}
            ]}},"required":["request"],"additionalProperties":false}
            """);
        var function = HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions { Name = "action", SchemaProvider = () => schema.RootElement.Clone(), OperationContract = Contract() });
        var payload = mode is null
            ? "{\"arguments\":{\"request\":{\"action\":\"" + action + "\",\"invocationMode\":\"domain\"}}}"
            : "{\"arguments\":{\"request\":{\"action\":\"" + action + "\",\"invocationMode\":\"domain\"}},\"invocation\":{\"mode\":\"" + mode + "\"}}";
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var context = new global::HPD.Agent.Middleware.AgentContext("AgentA", "conversation-1", state,
            new global::HPD.Events.Core.EventCoordinator(), session: null, thread: null, CancellationToken.None);
        var core = new FunctionExecutionCore(new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));
        var call = FunctionExecutionCore.NormalizeProviderFunctionCall(new FunctionCallContent("call-1", function.Name, ParsedArguments(payload)));

        var preparation = await core.PrepareFunctionAsync(call, new ChatOptions { Tools = [function] },
            new AgentRunConfig(), context, invocation: null, CancellationToken.None, admit: false);

        if (rejected)
        {
            Assert.NotNull(preparation.ImmediateOutcome);
            return;
        }
        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal(action, preparation.ResolvedInvocation!.Action);
        Assert.Equal(mode == "background" ? AgentInvocationMode.Background : AgentInvocationMode.Synchronous,
            preparation.ResolvedInvocation.Mode);
        Assert.Equal("domain", ((JsonElement)preparation.Arguments["request"]!).GetProperty("invocationMode").GetString());
    }

    [Fact]
    public async Task Preparation_CanonicalizesParsedProviderArgumentsBeforeActionDispatch()
    {
        JsonElement Schema()
        {
            using var document = JsonDocument.Parse("""
                {
                  "type":"object",
                  "properties":{"request":{"oneOf":[
                    {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
                    {"type":"object","properties":{"action":{"type":"string","const":"run"},"value":{"type":"integer"}},"required":["action","value"],"additionalProperties":false}
                  ]}},
                  "required":["request"],"additionalProperties":false
                }
                """);
            return document.RootElement.Clone();
        }

        var function = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "ActionTool",
                SchemaProvider = Schema,
                OperationContract = Contract()
            }));
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var agentContext = new global::HPD.Agent.Middleware.AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new global::HPD.Events.Core.EventCoordinator(),
            session: null,
            thread: null,
            CancellationToken.None);
        var core = new FunctionExecutionCore(
            new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));
        var call = FunctionExecutionCore.NormalizeProviderFunctionCall(new FunctionCallContent(
            "call-1",
            function.Name,
            ParsedArguments("""{"arguments":{"request":{"action":"run","value":2}}}""")));

        var preparation = await core.PrepareFunctionAsync(
            call,
            new ChatOptions { Tools = [function] },
            new AgentRunConfig(),
            agentContext,
            invocation: null,
            CancellationToken.None,
            admit: false);

        Assert.Null(preparation.ImmediateOutcome);
        Assert.Equal("run", preparation.ResolvedInvocation?.Action);
        Assert.Equal(
            FunctionArgumentIngressProvenance.Canonicalized,
            preparation.ResolvedInvocation?.IngressProvenance);
    }

    [Fact]
    public async Task Preparation_RejectsUnmarkedAdHocDictionaryIngress()
    {
        JsonElement Schema()
        {
            using var document = JsonDocument.Parse("""
                {
                  "type":"object",
                  "properties":{"request":{"oneOf":[
                    {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
                    {"type":"object","properties":{"action":{"type":"string","const":"run"},"value":{"type":"integer"}},"required":["action","value"],"additionalProperties":false}
                  ]}},
                  "required":["request"],"additionalProperties":false
                }
                """);
            return document.RootElement.Clone();
        }

        var function = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "ActionTool",
                SchemaProvider = Schema,
                OperationContract = Contract()
            }));
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var agentContext = new global::HPD.Agent.Middleware.AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new global::HPD.Events.Core.EventCoordinator(),
            session: null,
            thread: null,
            CancellationToken.None);
        var core = new FunctionExecutionCore(new global::HPD.Agent.Middleware.AgentMiddlewarePipeline([]));
        var call = new FunctionCallContent(
            "call-1",
            function.Name,
            ParsedArguments("""{"request":{"action":"run","value":2}}"""));

        var preparation = await core.PrepareFunctionAsync(
            call,
            new ChatOptions { Tools = [function] },
            new AgentRunConfig(),
            agentContext,
            invocation: null,
            CancellationToken.None,
            admit: false);

        var result = Assert.IsType<JsonElement>(preparation.ImmediateOutcome?.Result);
        Assert.Equal("raw_json_required", result.GetProperty("errors")[0].GetProperty("error_code").GetString());
    }

    [Fact]
    public void ResolveAction_RejectsUnknownActionWithoutFallback()
    {
        using var document = JsonDocument.Parse("""{"request":{"action":"delete"}}""");
        var arguments = new AIFunctionArguments { ["request"] = document.RootElement.GetProperty("request").Clone() };
        arguments.SetJson(document.RootElement.Clone());

        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ResolveAction(arguments, Contract(), out _));

        Assert.Contains("Unknown function action", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAction_MissingControlDefaultsToSynchronous()
    {
        using var document = JsonDocument.Parse("""{"request":{"action":"run","value":1}}""");
        var arguments = Arguments(document);

        var resolved = AgentInvocationModes.ResolveAction(
            arguments, Contract(), out _, arguments.GetIngressProvenance());

        Assert.Null(resolved.RequestedMode);
        Assert.Equal(AgentInvocationMode.Synchronous, resolved.Mode);
        Assert.Equal(FunctionArgumentIngressProvenance.Original, resolved.IngressProvenance);
    }

    [Fact]
    public void ResolveAction_RejectsMissingDiscriminator()
    {
        using var document = JsonDocument.Parse("""{"request":{"value":1}}""");
        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ResolveAction(Arguments(document), Contract(), out _));
        Assert.Contains("action", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveAction_SynchronousOnlyRejectsBackgroundRequest()
    {
        using var document = JsonDocument.Parse("""
            {"request":{"action":"read","invocationMode":"background"}}
            """);
        Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ResolveAction(Arguments(document), Contract(), out _,
                requestedMode: AgentInvocationMode.Background));
    }

    [Fact]
    public void ProviderCanonicalWriterMarksCanonicalizedIngress()
    {
        var providerArguments = new Dictionary<string, object?>
        {
            ["request"] = JsonSerializer.SerializeToElement(new { action = "run", value = 2 })
        };

        var arguments = FunctionExecutionCore.CreateInvocationArguments(providerArguments);
        var resolved = AgentInvocationModes.ResolveAction(
            arguments, Contract(), out _, arguments.GetIngressProvenance());

        Assert.Equal(FunctionArgumentIngressProvenance.Canonicalized, resolved.IngressProvenance);
    }

    [Fact]
    public void FunctionArgumentSerializationSupportsAIFunctionArgumentsContract()
    {
        var arguments = FunctionExecutionCore.CreateInvocationArguments(
            ParsedArguments("""{"request":{"action":"run","value":2}}"""));
        var call = new FunctionCallContent("call-1", "ActionTool", arguments);

        var serialized = FunctionCallArgumentSerializer.Serialize(call);
        var canonicalized = ContentExtractor.Canonicalize([call]);

        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("run", document.RootElement
            .GetProperty("request").GetProperty("action").GetString());
        Assert.Contains(serialized, canonicalized, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateActionSchema_RejectsRecursiveReference()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"$ref":"#/$defs/request"}},"$defs":{"request":{"$ref":"#/$defs/request"}}}
            """);
        Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.CreateActionSchema(document.RootElement, Contract()));
    }

    [Fact]
    public void CreateActionSchema_RejectsReferenceSiblings()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"$ref":"#/$defs/request","description":"ambiguous"}},"$defs":{"request":{"oneOf":[]}}}
            """);
        Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.CreateActionSchema(document.RootElement, Contract()));
    }

    [Fact]
    public void ValidateActionSchema_AllowsRequiredDomainInvocationMode()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"oneOf":[
              {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
              {"type":"object","properties":{"action":{"type":"string","const":"run"},"invocationMode":{"type":"string","enum":["synchronous","background"]}},"required":["action","invocationMode"],"additionalProperties":false}
            ]}},"required":["request"],"additionalProperties":false}
            """);
        AgentInvocationModes.ValidateActionSchema(document.RootElement, Contract());
    }

    [Fact]
    public void ValidateActionSchema_RejectsOptionalDiscriminator()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"oneOf":[
              {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
              {"type":"object","properties":{"action":{"type":"string","const":"run"},"invocationMode":{"type":"string","enum":["synchronous","background"]}},"required":[],"additionalProperties":false}
            ]}},"required":["request"],"additionalProperties":false}
            """);
        Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ValidateActionSchema(document.RootElement, Contract()));
    }

    private static AIFunctionArguments Arguments(JsonDocument document)
    {
        var arguments = new AIFunctionArguments
        {
            ["request"] = document.RootElement.GetProperty("request").Clone()
        };
        arguments.SetJson(document.RootElement.Clone());
        return arguments;
    }

    private static AIFunctionOperationContract Contract() => new()
    {
        ActionArgumentName = "request",
        Discriminator = "action",
        Actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal)
        {
            ["read"] = new()
            {
                InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.Runtime,
                Permission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = false,
                    Authority = "read",
                    Source = PermissionDeclarationSource.FrameworkDefault
                }
            },
            ["run"] = new()
            {
                InvocationModePolicy = AgentInvocationModePolicy.ModelChoice,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                Permission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = false,
                    Authority = "run",
                    Source = PermissionDeclarationSource.FrameworkDefault
                }
            }
        }
    };

    private static Dictionary<string, object?> ParsedArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => (object?)property.Value.Clone(),
            StringComparer.Ordinal);
    }
}
