using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Tools;

public class HPDAIFunctionFactoryResultMarshallingTests
{
    [Fact]
    public async Task HPDAIFunction_InvokeAsync_ProvidesFunctionExecutionContext()
    {
        FunctionExecutionContext? capturedContext = null;
        var function = HPDAIFunctionFactory.Create(
            (_, context, _) =>
            {
                capturedContext = context;
                return Task.FromResult<object?>("ok");
            },
            new HPDAIFunctionFactoryOptions { Name = "CaptureContext" });
        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        var context = CreateContext(function, callId: "call-42");

        await hpdFunction.InvokeAsync(new AIFunctionArguments(), context, CancellationToken.None);

        Assert.Same(context, capturedContext);
        Assert.Equal("call-42", capturedContext!.FunctionCallId);
    }

    [Fact]
    public async Task HPDAIFunction_InvokeAsync_PassesCancellationToken()
    {
        CancellationToken capturedToken = default;
        var function = HPDAIFunctionFactory.Create(
            (_, _, cancellationToken) =>
            {
                capturedToken = cancellationToken;
                return Task.FromResult<object?>("ok");
            },
            new HPDAIFunctionFactoryOptions { Name = "CaptureToken" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(function), cts.Token);

        Assert.True(capturedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task HPDAIFunction_InvokeAsync_PreservesArgumentsJson()
    {
        JsonElement capturedJson = default;
        var function = HPDAIFunctionFactory.Create(
            (args, _, _) =>
            {
                capturedJson = args.GetJson().Clone();
                return Task.FromResult<object?>("ok");
            },
            new HPDAIFunctionFactoryOptions { Name = "CaptureJson" });
        using var document = JsonDocument.Parse("""{ "query": "x" }""");
        var arguments = new AIFunctionArguments();
        arguments.SetJson(document.RootElement.Clone());

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        await hpdFunction.InvokeAsync(arguments, CreateContext(function), CancellationToken.None);

        Assert.Equal("x", capturedJson.GetProperty("query").GetString());
    }

    [Fact]
    public async Task HPDAIFunction_InvokeAsync_ValidationRunsBeforeInvocation()
    {
        var invoked = false;
        var function = HPDAIFunctionFactory.Create(
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult<object?>("should not run");
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = "NeedsInput",
                Validator = (_, _) =>
                [
                    new ValidationError
                    {
                        Property = "input",
                        ErrorMessage = "Required property 'input' is missing.",
                        ErrorCode = "missing_required_property"
                    }
                ]
            });

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        var result = await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(function), CancellationToken.None);

        Assert.False(invoked);
        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("validation_error", json.GetProperty("error_type").GetString());
    }

    [Fact]
    public async Task HPDAIFunction_InvokeAsync_WithoutFunctionExecutionContext_ThrowsClearError()
    {
        var function = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("ok"),
            new HPDAIFunctionFactoryOptions { Name = "RequiresContext" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await function.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));

        Assert.Contains("FunctionExecutionContext", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_WithDeclaredPocoResult_UsesConfiguredJsonContext()
    {
        var function = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(new WeatherResult("Chicago", 72)),
            new HPDAIFunctionFactoryOptions
            {
                Name = "GetWeather",
                ResultType = typeof(WeatherResult),
                SerializerOptions = ResultMarshallingJsonContext.Default.Options
            });

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        var result = await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(function), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("Chicago", json.GetProperty("city").GetString());
        Assert.Equal(72, json.GetProperty("temperature").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_WithStringResult_PreservesStringResult()
    {
        var function = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("done"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "Echo",
                ResultType = typeof(string),
                SerializerOptions = ResultMarshallingJsonContext.Default.Options
            });

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        var result = await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(function), CancellationToken.None);

        Assert.Equal("done", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task InvokeAsync_WithValidationErrors_ReturnsJsonValidationPayload()
    {
        var function = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>("should not run"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "NeedsInput",
                ResultType = typeof(string),
                Validator = (_, _) =>
                [
                    new ValidationError
                    {
                        Property = "input",
                        ErrorMessage = "Required property 'input' is missing.",
                        ErrorCode = "missing_required_property"
                    }
                ]
            });

        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        var result = await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(function), CancellationToken.None);

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("validation_error", json.GetProperty("error_type").GetString());
        Assert.Equal("input", json.GetProperty("errors")[0].GetProperty("property").GetString());
    }

    [Fact]
    public void BindValue_WithEnumString_ParsesCaseInsensitiveEnum()
    {
        using var document = JsonDocument.Parse("""{ "mode": "content" }""");

        var mode = HPDToolArgumentBinder.BindRequired<SearchMode>(
            document.RootElement,
            "mode",
            HPDToolArgumentBinder.DefaultSerializerOptions);

        Assert.Equal(SearchMode.Content, mode);
    }

    [Fact]
    public void BindValue_WithNumberForString_ThrowsValidationException()
    {
        using var document = JsonDocument.Parse("""{ "input": 123 }""");

        var ex = Assert.Throws<HPDToolArgumentException>(() =>
            HPDToolArgumentBinder.BindRequired<string>(
                document.RootElement,
                "input",
                HPDToolArgumentBinder.DefaultSerializerOptions));

        Assert.Equal("input", ex.PropertyName);
        Assert.Equal("type_conversion_error", ex.ErrorCode);
    }

    [Fact]
    public void ValidateNoUnmappedProperties_WithDisallow_RejectsUnexpectedProperty()
    {
        using var document = JsonDocument.Parse("""{ "input": "hello", "extra": true }""");
        var options = new JsonSerializerOptions(HPDToolArgumentBinder.DefaultSerializerOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        var ex = Assert.Throws<HPDToolArgumentException>(() =>
            HPDToolArgumentBinder.ValidateNoUnmappedProperties(
                document.RootElement,
                options,
                "input"));

        Assert.Equal("extra", ex.PropertyName);
        Assert.Equal("unmapped_property", ex.ErrorCode);
    }

    [Fact]
    public void ValidateNoUnmappedProperties_WithDefaultOptions_AllowsUnexpectedProperty()
    {
        using var document = JsonDocument.Parse("""{ "input": "hello", "extra": true }""");

        HPDToolArgumentBinder.ValidateNoUnmappedProperties(
            document.RootElement,
            HPDToolArgumentBinder.DefaultSerializerOptions,
            "input");
    }

    private static FunctionExecutionContext CreateContext(AIFunction function, string callId = "call-1")
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var branch = new global::HPD.Agent.Branch("session-1") { Id = "branch-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            branch,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            callId,
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            harnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = callId,
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
    }
}

internal sealed record WeatherResult(string City, int Temperature);

internal enum SearchMode
{
    Files,
    Content
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WeatherResult))]
internal sealed partial class ResultMarshallingJsonContext : JsonSerializerContext;
