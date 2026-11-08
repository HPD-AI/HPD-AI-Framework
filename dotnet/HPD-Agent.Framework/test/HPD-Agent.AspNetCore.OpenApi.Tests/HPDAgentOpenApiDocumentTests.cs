using System.Net.Http.Json;
using HPD.Agent;
using HPD.Agent.AspNetCore.OpenApi;
using HPD.Agent.AspNetCore.Evaluations;
using HPD.Agent.EventComposition.AotFixture;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi.Tests;

public sealed class HPDAgentOpenApiDocumentTests
{
    [Fact]
    public async Task CanonicalDocument_DescribesHttpUploadSseAndStableOperationIds()
    {
        using var server = CreateServer();

        var document = await server.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(
                HPDAgentOpenApiExtensions.DefaultDocumentName)
            .GetOpenApiDocumentAsync();

        Assert.Equal("HPD Agent API", document.Info.Title);
        Assert.Contains(
            "GetSession",
            document.Paths["/sessions/{sessionId}"].Operations.Values.Select(operation => operation.OperationId));
        Assert.Contains(
            "GetAgentEventCatalog",
            document.Paths["/event-catalog"].Operations.Values.Select(operation => operation.OperationId));

        var stream = document.Paths[
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/events"]
            .Operations[HttpMethod.Get];
        Assert.Contains("text/event-stream", stream.Responses["200"].Content.Keys);
        Assert.Contains(stream.Parameters!, parameter =>
            parameter.Name == "after" && parameter.In == ParameterLocation.Query);
        Assert.Contains(stream.Parameters!, parameter =>
            parameter.Name == "Last-Event-ID" && parameter.In == ParameterLocation.Header);
        var dataSchema = Assert.IsType<OpenApiSchema>(
            Assert.IsType<OpenApiSchema>(
                stream.Responses["200"].Content["text/event-stream"].Schema)
            .Properties!["data"]);
        Assert.Equal(2, dataSchema.OneOf!.Count);
        var deliverySchema = Assert.IsType<OpenApiSchema>(dataSchema.OneOf[0]);
        var eventUnion = Assert.IsType<OpenApiSchema>(
            Assert.IsType<OpenApiSchema>(deliverySchema.Properties!["event"]));
        var eventComponentNames = eventUnion.OneOf!
            .OfType<OpenApiSchemaReference>()
            .Select(static reference => reference.Reference.Id)
            .ToArray();
        Assert.Contains(typeof(AotFixtureEvent).FullName, eventComponentNames);

        var input = document.Paths[
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/inputs"]
            .Operations[HttpMethod.Post];
        Assert.Contains("202", input.Responses.Keys);
        Assert.Contains("application/json", input.RequestBody!.Content!.Keys);
        Assert.Equal(
            7,
            Assert.IsType<OpenApiSchema>(
                input.RequestBody.Content["application/json"].Schema).OneOf!.Count);
        Assert.Contains("AgentRunConfig", document.Components!.Schemas!.Keys);

        var runtimeState = document.Paths[
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/state"]
            .Operations[HttpMethod.Get];
        var runtimeStateSchema = Assert.IsType<OpenApiSchema>(
            runtimeState.Responses["200"].Content["application/json"].Schema);
        var activeExecution = Assert.IsType<OpenApiSchema>(
            runtimeStateSchema.Properties!["activeExecution"]);
        Assert.True(activeExecution.Type!.Value.HasFlag(JsonSchemaType.Null));

        var upload = document.Paths["/sessions/{sid}/threads/{bid}/content"]
            .Operations[HttpMethod.Post];
        Assert.Contains("multipart/form-data", upload.RequestBody!.Content!.Keys);

        var download = document.Paths["/sessions/{sid}/threads/{bid}/content/{contentId}"]
            .Operations[HttpMethod.Get];
        Assert.Contains("application/octet-stream", download.Responses["200"].Content.Keys);
    }

    [Fact]
    public async Task MappedDocument_IsAvailableAtRuntimeAsOpenApi31()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/openapi/hpd-agent-v1.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal("3.1.1", json.GetProperty("openapi").GetString());
        Assert.Equal("hpd-agent-v1", json.GetProperty("x-hpd-document-name").GetString());
        Assert.True(json.GetProperty("paths").TryGetProperty(
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/events",
            out _));
    }

    [Fact]
    public async Task FullFeatureDocument_IncludesEvaluationsAndProviderProtocol()
    {
        using var server = CreateFullFeatureServer();

        var document = await server.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(
                HPDAgentOpenApiExtensions.DefaultDocumentName)
            .GetOpenApiDocumentAsync();

        Assert.Contains("GetScores", document.Paths["/evals/scores"].Operations.Values
            .Select(static operation => operation.OperationId));
        Assert.Contains("ListEvaluationDatasets", document.Paths["/evals/datasets"].Operations.Values
            .Select(static operation => operation.OperationId));
        Assert.Contains("full-feature.ListClientToolProviders", document.Paths["/client-tool-providers"].Operations.Values
            .Select(static operation => operation.OperationId));
        var connect = document.Paths["/client-tool-providers/connect"].Operations[HttpMethod.Get];
        Assert.Contains("101", connect.Responses.Keys);
        Assert.True(connect.Extensions!.ContainsKey("x-hpd-websocket-protocol"));
    }

    [Fact]
    public async Task WriteFullFeatureContractWhenRequested()
    {
        var output = System.Environment.GetEnvironmentVariable("HPD_FULL_FEATURE_OPENAPI_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;

        using var server = CreateFullFeatureServer();
        using var client = server.CreateClient();
        var json = await client.GetStringAsync("/openapi/hpd-agent-v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, json);
    }

    private static TestServer CreateServer()
    {
        _ = typeof(AotFixtureEvent);
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHPDAgentOpenApi();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHPDAgentOpenApi();
                    MapDocumentProbeEndpoints(endpoints);
                });
            });

        return new TestServer(builder);
    }

    private static TestServer CreateFullFeatureServer()
    {
        _ = typeof(AotFixtureEvent);
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHPDAgentOpenApi();
                services.AddHPD(hpd =>
                    hpd.UseProviderComposition(ProviderComposition.Create([])));
                services.AddHPDAgentEvaluations();
                services.AddHPDAgentHost("full-feature");
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHPDAgentOpenApi();
                    endpoints.MapGroup("").MapHPDAgentHostApi("full-feature");
                    endpoints.MapHPDAgentEvaluationApi();
                });
            });
        return new TestServer(builder);
    }

    private static void MapDocumentProbeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/sessions/{sessionId}",
                (string sessionId) => TypedResults.Ok(
                    new SessionDto(
                        sessionId,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch,
                        new Dictionary<string, object>())))
            .WithName("GetSession")
            .WithSummary("Get session metadata by ID")
            .Produces<SessionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet(
                "/event-catalog",
                () => TypedResults.Ok(new AgentEventCatalog
                {
                    Events = [],
                    Digest = "sha256:probe"
                }))
            .WithName("GetAgentEventCatalog")
            .WithSummary("Get the immutable agent event compatibility catalog");

        endpoints.MapGet(
                "/agents/{agentId}/sessions/{sid}/threads/{bid}/events",
                () => TypedResults.Empty)
            .WithName("ObserveLiveEventsWithSse")
            .WithSummary("Replay durable thread events and observe all live runtime events using SSE");

        endpoints.MapPost(
                "/agents/{agentId}/sessions/{sid}/threads/{bid}/inputs",
                (System.Text.Json.JsonElement input) =>
                    TypedResults.Accepted(string.Empty, new InputSubmissionDto("started")))
            .WithName("SubmitAgentInput")
            .WithSummary("Submit an agent input event to the runtime");

        endpoints.MapGet(
                "/agents/{agentId}/sessions/{sid}/threads/{bid}/state",
                () => TypedResults.Ok(
                    new ThreadRuntimeStateDto(
                        new HPD.Agent.ThreadJournalCursor(1, 0),
                        null,
                        [])))
            .WithName("GetAgentThreadRuntimeState")
            .WithSummary("Get one authoritative thread history and active-run snapshot");

        endpoints.MapPost(
                "/sessions/{sid}/threads/{bid}/content",
                () => TypedResults.Created(
                    "/sessions/s/threads/t/content/c",
                    new ContentDto("c", "v1", "text/plain", 1, DateTimeOffset.UnixEpoch.ToString("O"))))
            .WithName("UploadContent")
            .WithSummary("Upload content (multipart/form-data)");

        endpoints.MapGet(
                "/sessions/{sid}/threads/{bid}/content/{contentId}",
                () => TypedResults.File([1], "application/octet-stream", "file.bin"))
            .WithName("DownloadContent")
            .WithSummary("Download content (returns binary content)");
    }
}
