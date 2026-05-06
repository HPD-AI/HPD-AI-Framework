using System.Text.Json;
using FluentAssertions;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.AspNetCore.Serialization;
using HPDAgent.Graph.Core.Config;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Serialization;

namespace HPD.Graph.Tests.V21;

public sealed class GraphAotSmokeTests
{
    [Fact]
    public void SourceGeneratedContexts_CoverConfigHostingAndAspNetCoreDtos()
    {
        var config = CreateConfig();
        var configJson = JsonSerializer.Serialize(config, GraphConfigJsonSerializerContext.Default.GraphConfig);
        var roundTrip = JsonSerializer.Deserialize(configJson, GraphConfigJsonSerializerContext.Default.GraphConfig);

        roundTrip.Should().NotBeNull();
        roundTrip!.GraphId.Should().Be("aot-smoke");
        roundTrip.ToGraph().Nodes.Should().Contain(node => node.Id == "work");

        var execute = new ExecuteWorkflowRequest { ExecutionId = "exec-aot" };
        var executeJson = JsonSerializer.Serialize(execute, GraphHostingJsonSerializerContext.Default.ExecuteWorkflowRequest);
        JsonSerializer.Deserialize(executeJson, GraphHostingJsonSerializerContext.Default.ExecuteWorkflowRequest)!
            .ExecutionId.Should().Be("exec-aot");

        var resume = new ResumeSuspensionRequest { ResumeValue = "approved" };
        var resumeJson = JsonSerializer.Serialize(resume, GraphAspNetCoreJsonSerializerContext.Default.ResumeSuspensionRequest);
        JsonSerializer.Deserialize(resumeJson, GraphAspNetCoreJsonSerializerContext.Default.ResumeSuspensionRequest)!
            .ResumeValue.Should().NotBeNull();
    }

    [Fact]
    public void SourceGeneratedResolverChain_CanBeMadeReadOnly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolverChain.Add(GraphConfigJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(GraphHostingJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(GraphAspNetCoreJsonSerializerContext.Default);

        options.MakeReadOnly();

        options.IsReadOnly.Should().BeTrue();
        JsonSerializer.Serialize(CreateConfig(), options).Should().Contain("aot-smoke");
    }

    private static GraphConfig CreateConfig() => new()
    {
        GraphId = "aot-smoke",
        Name = "AOT Smoke",
        Nodes = new Dictionary<string, NodeConfig>
        {
            ["work"] = new()
            {
                Id = "work",
                Name = "Work",
                Type = NodeKindConfig.Handler,
                HandlerName = "work"
            }
        },
        Edges =
        [
            new EdgeConfig { From = "START", To = "work" },
            new EdgeConfig { From = "work", To = "END" }
        ]
    };
}
