using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Tools;

public sealed class ActionScopedFunctionInvocationTests
{
    [Fact]
    public void ResolveAction_UsesSelectedPolicyAndSanitizesNestedControl()
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
            arguments, Contract(), out var sanitized);

        Assert.Equal("run", resolved.Action);
        Assert.Equal(AgentInvocationMode.Background, resolved.RequestedMode);
        Assert.Equal(AgentInvocationMode.Background, resolved.Mode);
        Assert.Equal(AgentInvocationModePolicy.ModelChoice, resolved.Policy);
        Assert.Equal(AgentInvocationModeHandling.ToolBody, resolved.Handling);
        Assert.False(sanitized.GetJson().GetProperty("request").TryGetProperty("invocationMode", out _));
        Assert.Equal(3, sanitized.GetJson().GetProperty("request").GetProperty("value").GetInt32());
    }

    [Fact]
    public void CreateActionSchema_OffersChoiceOnlyOnModelChoiceBranch()
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
        Assert.True(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
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
        Assert.True(branches[1].GetProperty("properties").TryGetProperty("invocationMode", out _));
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
        Assert.Contains("invocationMode", function.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.NotNull(function.CanonicalInputContract);
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

        var resolved = AgentInvocationModes.ResolveAction(arguments, Contract(), out _);

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
            AgentInvocationModes.ResolveAction(Arguments(document), Contract(), out _));
    }

    [Fact]
    public void SharedRootSanitizerMatchesNativeModeSemantics()
    {
        var source = new Dictionary<string, object?>
        {
            ["invocationMode"] = JsonSerializer.SerializeToElement("background"),
            ["value"] = 4
        };

        var sanitized = AgentInvocationModes.CreateSanitizedArgumentDictionary(source, out var requested);

        Assert.Equal(AgentInvocationMode.Background, requested);
        Assert.False(sanitized.ContainsKey("invocationMode"));
        Assert.Equal(4, sanitized["value"]);
        Assert.Equal(AgentInvocationMode.Background,
            AgentInvocationModes.Resolve(AgentInvocationModePolicy.ModelChoice, requested));
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
    public void ValidateActionSchema_RejectsRequiredInvocationControl()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"request":{"oneOf":[
              {"type":"object","properties":{"action":{"type":"string","const":"read"}},"required":["action"],"additionalProperties":false},
              {"type":"object","properties":{"action":{"type":"string","const":"run"},"invocationMode":{"type":"string","enum":["synchronous","background"]}},"required":["action","invocationMode"],"additionalProperties":false}
            ]}},"required":["request"],"additionalProperties":false}
            """);
        Assert.Throws<InvalidOperationException>(() =>
            AgentInvocationModes.ValidateActionSchema(document.RootElement, Contract()));
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
                InvocationModeHandling = AgentInvocationModeHandling.Runtime
            },
            ["run"] = new()
            {
                InvocationModePolicy = AgentInvocationModePolicy.ModelChoice,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody
            }
        }
    };
}
