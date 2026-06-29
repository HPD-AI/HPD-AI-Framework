using System.Net;
using System.Text;
using System.Text.Json;
using HPD.Agent.MultiAgent.AspNetCore.Tests.TestInfrastructure;

namespace HPD.Agent.MultiAgent.AspNetCore.Tests.Integration;

public class MultiAgentEndpointTests
{
    [Fact]
    public async Task GET_workflows_Returns_Only_MultiAgent_Workflows_By_Default()
    {
        using var server = new MultiAgentApiTestServer();
        await server.SeedWorkflowAsync("multi-agent-workflow");
        await server.SeedWorkflowAsync("generic-workflow", multiAgent: false);
        using var client = server.CreateClient();

        var response = await client.GetAsync("/multi-agent/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        var workflows = json.RootElement.GetProperty("workflows").EnumerateArray().ToList();
        workflows.Should().ContainSingle();
        workflows[0].GetProperty("workflowId").GetString().Should().Be("multi-agent-workflow");
        workflows[0].GetProperty("isMultiAgent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GET_workflows_Can_Include_Generic_Workflows()
    {
        using var server = new MultiAgentApiTestServer(options => options.IncludeGenericWorkflows = true);
        await server.SeedWorkflowAsync("multi-agent-workflow");
        await server.SeedWorkflowAsync("generic-workflow", multiAgent: false);
        using var client = server.CreateClient();

        var response = await client.GetAsync("/multi-agent/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        var workflowIds = json.RootElement
            .GetProperty("workflows")
            .EnumerateArray()
            .Select(item => item.GetProperty("workflowId").GetString())
            .ToList();
        workflowIds.Should().BeEquivalentTo(["multi-agent-workflow", "generic-workflow"]);
    }

    [Fact]
    public async Task POST_runs_Creates_Run_And_GET_runs_Lists_It()
    {
        using var server = new MultiAgentApiTestServer();
        await server.SeedWorkflowAsync("workflow-a");
        using var client = server.CreateClient();

        var startResponse = await PostJsonAsync(
            client,
            "/multi-agent/workflows/workflow-a/runs",
            """{"executionId":"run-a","startImmediately":false,"mode":"background","input":{"prompt":"hello"}}""");

        startResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var runResponse = await client.GetAsync("/multi-agent/workflows/workflow-a/runs/run-a");
        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var statusJson = await ReadJsonAsync(runResponse);
        statusJson.RootElement.GetProperty("executionId").GetString().Should().Be("run-a");
        statusJson.RootElement.GetProperty("status").GetString().Should().Be("Created");

        var listResponse = await client.GetAsync("/multi-agent/workflows/workflow-a/runs");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(listResponse);
        var runs = json.RootElement.GetProperty("runs").EnumerateArray().ToList();
        runs.Should().ContainSingle();
        runs[0].GetProperty("executionId").GetString().Should().Be("run-a");
    }

    [Fact]
    public async Task GET_events_Streams_Graph_Log_As_MultiAgent_Event()
    {
        using var server = new MultiAgentApiTestServer();
        await server.SeedWorkflowAsync("workflow-events");
        using var client = server.CreateClient();
        await PostJsonAsync(
            client,
            "/multi-agent/workflows/workflow-events/runs",
            """{"executionId":"run-events","startImmediately":false,"mode":"background"}""");

        var response = await client.GetAsync("/multi-agent/workflows/workflow-events/runs/run-events/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data:");
        body.Should().Contain("\"kind\":\"graph-log\"");
        body.Should().Contain("Execution created.");
    }

    [Fact]
    public async Task POST_approval_Returns_NotFound_For_Unknown_Suspension()
    {
        using var server = new MultiAgentApiTestServer();
        await server.SeedWorkflowAsync("workflow-approval");
        using var client = server.CreateClient();

        var response = await PostJsonAsync(
            client,
            "/multi-agent/workflows/workflow-approval/runs/run-a/approvals/missing-approval",
            """{"resumeValue":{"approved":true}}""");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string requestUri, string json)
    {
        return client.PostAsync(
            requestUri,
            new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
