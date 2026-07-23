using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class CodingHarnessGeneratedContractTests
{
    private static readonly IReadOnlyDictionary<string, AIFunction> Functions =
        CodingToolHarnessRegistration.CreateToolHarness(new CodingToolHarness())
            .ToDictionary(function => function.Name, StringComparer.Ordinal);

    [Fact]
    public void EveryCodingFunction_PublishesAClosedContractWithoutRuntimeParameters()
    {
        Functions.Keys.Should().Contain(
        [
            "EditFile",
            "ExecuteCommand",
            "GlobSearch",
            "Grep",
            "ListDirectory",
            "ReadFile",
            "WriteFile"
        ]);

        foreach (var functionName in new[] { "EditFile", "ExecuteCommand", "GlobSearch", "Grep", "ListDirectory", "ReadFile", "WriteFile" })
        {
            var function = Functions[functionName];
            var schema = function.JsonSchema;
            schema.GetProperty("type").GetString().Should().Be("object");
            schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
            var properties = schema.GetProperty("properties");
            properties.TryGetProperty("context", out _).Should().BeFalse();
            properties.TryGetProperty("cancellationToken", out _).Should().BeFalse();
        }
    }

    [Fact]
    public void EditFile_AdvertisesClosedReplacementItems()
    {
        var item = Properties("EditFile").GetProperty("edits").GetProperty("items");

        item.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        item.GetProperty("required").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("oldString", "newString");
        item.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().Equal("oldString", "newString", "replaceAll");
    }

    [Fact]
    public void ExecuteCommand_AdvertisesOneClosedBranchPerOperation()
    {
        var branches = Properties("ExecuteCommand").GetProperty("request").GetProperty("oneOf").EnumerateArray().ToArray();

        branches.Should().HaveCount(4);
        branches.Select(branch => branch.GetProperty("properties").GetProperty("action").GetProperty("const").GetString())
            .Should().Equal("run", "listBackground", "readOutput", "stop");
        branches.Should().OnlyContain(branch => !branch.GetProperty("additionalProperties").GetBoolean());

        var runProperties = branches[0].GetProperty("properties");
        runProperties.GetProperty("executionMode").GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("Synchronous", "Background");
        runProperties.GetProperty("environment").GetProperty("additionalProperties").GetProperty("type")
            .GetString().Should().Be("string");
    }

    [Fact]
    public void SearchAndListingFunctions_AdvertiseExactEnumsAndCollectionItems()
    {
        EnumValues("Grep", "outputMode").Should().Equal("FilesWithMatches", "Content", "Count");
        EnumValues("Grep", "caseMode").Should().Equal("Sensitive", "Insensitive", "Smart");
        Properties("Grep").GetProperty("includeGlobs").GetProperty("items").GetProperty("type")
            .GetString().Should().Be("string");

        EnumValues("ListDirectory", "kind").Should().Equal("All", "Files", "Directories");
        EnumValues("ListDirectory", "sortBy").Should().Equal("Name", "ModifiedTime", "Size", "Kind");
        EnumValues("ListDirectory", "sortDirection").Should().Equal("Ascending", "Descending");

        EnumValues("GlobSearch", "kind").Should().Equal("Files", "Directories", "All");
        EnumValues("GlobSearch", "sortBy").Should().Equal("Path", "ModifiedTime", "Recency", "Size", "Kind");
        EnumValues("GlobSearch", "sortDirection").Should().Equal("Ascending", "Descending");
    }

    [Theory]
    [InlineData("EditFile", "{\"path\":\"file.txt\",\"edits\":[{\"oldString\":\"a\",\"newString\":\"b\",\"unexpected\":true}]}", "edits[0].unexpected")]
    [InlineData("ExecuteCommand", "{\"request\":{\"action\":\"stop\",\"backgroundHandleId\":\"cmd_1\",\"command\":\"echo no\"}}", "request.command")]
    [InlineData("Grep", "{\"pattern\":\"TODO\",\"outputMode\":\"content\"}", "outputMode")]
    public async Task GeneratedBinding_RejectsAliasesWrongBranchesAndEnumCasing(
        string functionName,
        string json,
        string expectedProperty)
    {
        var function = Functions[functionName];
        using var document = JsonDocument.Parse(json);
        var arguments = new AIFunctionArguments();
        arguments.SetJson(document.RootElement.Clone());

        var result = await ((HPDAIFunctionFactory.HPDAIFunction)function)
            .InvokeAsync(arguments, CreateContext(function), CancellationToken.None);

        var error = ((JsonElement)result!).GetProperty("errors")[0];
        error.GetProperty("property").GetString().Should().Be(expectedProperty);
    }

    private static JsonElement Properties(string functionName) =>
        Functions[functionName].JsonSchema.GetProperty("properties");

    private static string?[] EnumValues(string functionName, string propertyName) =>
        Properties(functionName).GetProperty(propertyName).GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

    private static FunctionExecutionContext CreateContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "ContractTests");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "contract-tests") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "ContractTests",
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
            toolharnessName: nameof(CodingToolHarness),
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
