using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using HPD.Events;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Registry;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.AspNetCore.DependencyInjection;
using HPD.Graph.AspNetCore.EndpointMapping;
using HPD.Graph.Core.Artifacts;
using HPD.Graph.Core.Registry;
using HPD.Graph.Hosting.Data;
using HPD.Graph.Hosting.Lifecycle;
using HPD.Graph.Hosting.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HPD.Graph.Tests.V21;

public sealed class AspNetCoreWorkflowEndpointTests
{
    [Fact]
    public void MapHPDGraphWorkflows_MapsCorePhase8Routes()
    {
        using var app = CreateApp();

        var routes = GetEndpoints(app)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        routes.Should().Contain(
        [
            "/workflows/",
            "/workflows/handlers",
            "/workflows/{graphId}",
            "/workflows/{graphId}/execute",
            "/workflows/{graphId}/executions/{executionId}",
            "/workflows/{graphId}/executions/{executionId}/logs",
            "/workflows/{graphId}/executions/{executionId}/cancel",
            "/workflows/{graphId}/executions/{executionId}/suspended-nodes",
            "/workflows/{graphId}/resume/{suspendToken}",
            "/workflows/{graphId}/polling-status/{suspendToken}",
            "/workflows/scheduled",
            "/workflows/{graphId}/schedule"
        ]);
    }

    [Fact]
    public async Task WorkflowEndpoints_CreateListGetExecuteStatusCancelAndLogs()
    {
        using var app = CreateApp();
        var config = CreateConfig("graph-a", "Workflow A");

        var createResponse = await InvokeJsonAsync(
            app,
            "/workflows/",
            "POST",
            new CreateWorkflowRequest { Config = config },
            GraphHostingJsonSerializerContext.Default.CreateWorkflowRequest);

        createResponse.StatusCode.Should().Be(StatusCodes.Status201Created);
        var workflow = Deserialize<WorkflowDto>(
            createResponse.Body,
            GraphHostingJsonSerializerContext.Default.WorkflowDto);
        workflow.GraphId.Should().Be("graph-a");

        var listResponse = await InvokeAsync(app, "/workflows/", "GET");
        listResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        var list = Deserialize<WorkflowListResponse>(
            listResponse.Body,
            GraphHostingJsonSerializerContext.Default.WorkflowListResponse);
        list.Workflows.Should().ContainSingle(summary => summary.GraphId == "graph-a");

        var getResponse = await InvokeAsync(app, "/workflows/{graphId}", "GET", ("graphId", "graph-a"));
        getResponse.StatusCode.Should().Be(StatusCodes.Status200OK);

        var executeResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/execute",
            "POST",
            new ExecuteWorkflowRequest { ExecutionId = "exec-a", StartImmediately = false },
            GraphHostingJsonSerializerContext.Default.ExecuteWorkflowRequest,
            ("graphId", "graph-a"));

        executeResponse.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        var execution = Deserialize<WorkflowExecutionDto>(
            executeResponse.Body,
            GraphHostingJsonSerializerContext.Default.WorkflowExecutionDto);
        execution.Status.Should().Be(HPD.Graph.Abstractions.Storage.WorkflowExecutionStatus.Created);

        var statusResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}",
            "GET",
            ("graphId", "graph-a"),
            ("executionId", "exec-a"));
        statusResponse.StatusCode.Should().Be(StatusCodes.Status200OK);

        var cancelResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}/cancel",
            "POST",
            ("graphId", "graph-a"),
            ("executionId", "exec-a"));
        cancelResponse.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var logResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}/logs",
            "GET",
            ("graphId", "graph-a"),
            ("executionId", "exec-a"));
        logResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        logResponse.ContentType.Should().Be("text/event-stream");
        logResponse.Body.Should().Contain("Execution created.");
        logResponse.Body.Should().Contain("Execution cancelled.");
    }

    [Fact]
    public async Task WorkflowEndpoints_UpdateDeleteAndMissingResources_ReturnExpectedStatusCodes()
    {
        using var app = CreateApp();
        await InvokeJsonAsync(
            app,
            "/workflows/",
            "POST",
            new CreateWorkflowRequest { Config = CreateConfig("graph-a", "Workflow A") },
            GraphHostingJsonSerializerContext.Default.CreateWorkflowRequest);

        var updateResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}",
            "PUT",
            new UpdateWorkflowRequest { Config = CreateConfig("graph-a", "Workflow A Updated") },
            GraphHostingJsonSerializerContext.Default.UpdateWorkflowRequest,
            ("graphId", "graph-a"));

        updateResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        var updated = Deserialize<WorkflowDto>(
            updateResponse.Body,
            GraphHostingJsonSerializerContext.Default.WorkflowDto);
        updated.Name.Should().Be("Workflow A Updated");

        var missingExecuteResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/execute",
            "POST",
            new ExecuteWorkflowRequest { ExecutionId = "missing-exec" },
            GraphHostingJsonSerializerContext.Default.ExecuteWorkflowRequest,
            ("graphId", "missing-graph"));
        missingExecuteResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        missingExecuteResponse.Body.Should().Contain("Workflow not found");

        var missingStatusResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}",
            "GET",
            ("graphId", "graph-a"),
            ("executionId", "missing-exec"));
        missingStatusResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var missingCancelResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}/cancel",
            "POST",
            ("graphId", "graph-a"),
            ("executionId", "missing-exec"));
        missingCancelResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        missingCancelResponse.Body.Should().Contain("Execution not found");

        var deleteResponse = await InvokeAsync(app, "/workflows/{graphId}", "DELETE", ("graphId", "graph-a"));
        deleteResponse.StatusCode.Should().Be(StatusCodes.Status204NoContent);

        var getAfterDeleteResponse = await InvokeAsync(app, "/workflows/{graphId}", "GET", ("graphId", "graph-a"));
        getAfterDeleteResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WorkflowEndpoints_InvalidJson_ReturnsBadRequest()
    {
        using var app = CreateApp();

        var response = await InvokeRawJsonAsync(app, "/workflows/", "POST", """{"config":""");

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task WorkflowEndpoints_SuspensionAndPollingMissingTokens_ReturnNotFound()
    {
        using var app = CreateApp();
        await InvokeJsonAsync(
            app,
            "/workflows/",
            "POST",
            new CreateWorkflowRequest { Config = CreateConfig("graph-a", "Workflow A") },
            GraphHostingJsonSerializerContext.Default.CreateWorkflowRequest);

        var suspendedNodesResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/executions/{executionId}/suspended-nodes",
            "GET",
            ("graphId", "graph-a"),
            ("executionId", "missing-exec"));
        suspendedNodesResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        suspendedNodesResponse.Body.Should().Be("[]");

        var resumeResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/resume/{suspendToken}",
            "POST",
            new ResumeSuspensionRequest { ResumeValue = "approved" },
            GraphHostingJsonSerializerContext.Default.ResumeSuspensionRequest,
            ("graphId", "graph-a"),
            ("suspendToken", "missing-token"));
        resumeResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        resumeResponse.Body.Should().Contain("NotFound");

        var pollingResponse = await InvokeAsync(
            app,
            "/workflows/{graphId}/polling-status/{suspendToken}",
            "GET",
            ("graphId", "graph-a"),
            ("suspendToken", "missing-token"));
        pollingResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WorkflowEndpoints_ReturnHandlersCatalog()
    {
        using var app = CreateApp();

        var response = await InvokeAsync(app, "/workflows/handlers", "GET");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        var catalog = Deserialize<HandlerCatalogResponse>(
            response.Body,
            GraphHostingJsonSerializerContext.Default.HandlerCatalogResponse);
        catalog.Handlers.Should().BeEmpty();
    }

    [Fact]
    public async Task SchedulingEndpoints_CreateListGetUpdateAndDeleteSchedule()
    {
        using var app = CreateApp();
        await InvokeJsonAsync(
            app,
            "/workflows/",
            "POST",
            new CreateWorkflowRequest { Config = CreateConfig("graph-a", "Workflow A") },
            GraphHostingJsonSerializerContext.Default.CreateWorkflowRequest);

        var createResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/schedule",
            "POST",
            new CreateScheduleRequest { Schedule = CreateSchedule() },
            GraphHostingJsonSerializerContext.Default.CreateScheduleRequest,
            ("graphId", "graph-a"));

        createResponse.StatusCode.Should().Be(StatusCodes.Status201Created);
        var created = Deserialize<ScheduledGraphDto>(
            createResponse.Body,
            GraphHostingJsonSerializerContext.Default.ScheduledGraphDto);
        created.GraphId.Should().Be("graph-a");
        created.NextRunAt.Should().NotBeNull();

        var listResponse = await InvokeAsync(app, "/workflows/scheduled", "GET");
        listResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        var list = Deserialize<ScheduledGraphListResponse>(
            listResponse.Body,
            GraphHostingJsonSerializerContext.Default.ScheduledGraphListResponse);
        list.Schedules.Should().ContainSingle(schedule => schedule.GraphId == "graph-a");

        var getResponse = await InvokeAsync(app, "/workflows/{graphId}/schedule", "GET", ("graphId", "graph-a"));
        getResponse.StatusCode.Should().Be(StatusCodes.Status200OK);

        var updateResponse = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/schedule",
            "PUT",
            new UpdateScheduleRequest
            {
                Schedule = new GraphScheduleConfig
                {
                    CronExpression = "0 4 * * *",
                    TimeZoneId = "UTC"
                },
                Enabled = false
            },
            GraphHostingJsonSerializerContext.Default.UpdateScheduleRequest,
            ("graphId", "graph-a"));

        updateResponse.StatusCode.Should().Be(StatusCodes.Status200OK);
        var updated = Deserialize<ScheduledGraphDto>(
            updateResponse.Body,
            GraphHostingJsonSerializerContext.Default.ScheduledGraphDto);
        updated.Enabled.Should().BeFalse();
        updated.NextRunAt.Should().BeNull();

        var deleteResponse = await InvokeAsync(app, "/workflows/{graphId}/schedule", "DELETE", ("graphId", "graph-a"));
        deleteResponse.StatusCode.Should().Be(StatusCodes.Status204NoContent);

        var missingResponse = await InvokeAsync(app, "/workflows/{graphId}/schedule", "GET", ("graphId", "graph-a"));
        missingResponse.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void AddHPDGraphAspNetCore_ChainsRegisteredJsonResolverContributors()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        var services = new ServiceCollection();
        services.AddSingleton<IGraphJsonTypeInfoResolverContributor>(new TestResolverContributor(resolver));

        services.AddHPDGraphAspNetCore();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        options.SerializerOptions.TypeInfoResolverChain.Should().Contain(resolver);
    }

    [Fact]
    public void AddHPDGraphAspNetCore_RegistersEventCoordinator()
    {
        var services = new ServiceCollection();

        services.AddHPDGraphAspNetCore();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEventCoordinator>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddHPDGraphWorkflowFromConfigFile_SeedsWorkflowDefinition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-graph-{Guid.NewGuid():N}.yaml");
        GraphConfigSerializer.WriteConfigFile(path, CreateConfig("seeded-graph", "Seeded Graph"));

        try
        {
            var services = new ServiceCollection();

            services.AddHPDGraphWorkflowFromConfigFile(path);

            await using var provider = services.BuildServiceProvider();
            foreach (var hostedService in provider.GetServices<IHostedService>())
                await hostedService.StartAsync(CancellationToken.None);

            var stored = await provider.GetRequiredService<GraphManager>()
                .GetDefinitionAsync("seeded-graph");

            stored.Should().NotBeNull();
            stored!.Name.Should().Be("Seeded Graph");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddHPDGraphMaterialization_RegistersDemandDrivenRegistries()
    {
        var services = new ServiceCollection();

        services.AddHPDGraphAspNetCore();
        services.AddHPDGraphMaterialization();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IArtifactRegistry>().Should().BeOfType<InMemoryArtifactRegistry>();
        var graphRegistry = provider.GetRequiredService<IGraphRegistry>();
        graphRegistry.Should().BeOfType<InMemoryGraphRegistry>();

        await provider.GetRequiredService<GraphManager>()
            .CreateDefinitionAsync(CreateConfig("graph-a", "Workflow A"));

        graphRegistry.GetGraph("graph-a").Should().NotBeNull();
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHPDGraphAspNetCore();
        var app = builder.Build();
        app.MapHPDGraphWorkflows();
        return app;
    }

    private static async Task<CapturedResponse> InvokeJsonAsync<T>(
        WebApplication app,
        string routePattern,
        string method,
        T body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        params (string Key, string Value)[] routeValues)
    {
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, body, jsonTypeInfo);
        stream.Position = 0;

        return await InvokeAsync(app, routePattern, method, routeValues, stream, "application/json");
    }

    private static async Task<CapturedResponse> InvokeAsync(
        WebApplication app,
        string routePattern,
        string method,
        params (string Key, string Value)[] routeValues)
    {
        return await InvokeAsync(app, routePattern, method, routeValues, requestBody: null, contentType: null);
    }

    private static async Task<CapturedResponse> InvokeRawJsonAsync(
        WebApplication app,
        string routePattern,
        string method,
        string json,
        params (string Key, string Value)[] routeValues)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return await InvokeAsync(
            app,
            routePattern,
            method,
            routeValues,
            new MemoryStream(bytes),
            "application/json");
    }

    private static async Task<CapturedResponse> InvokeAsync(
        WebApplication app,
        string routePattern,
        string method,
        (string Key, string Value)[] routeValues,
        Stream? requestBody,
        string? contentType)
    {
        var endpoint = GetEndpoints(app)
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal) &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services
        };
        context.Request.Method = method;
        context.Request.Body = requestBody ?? Stream.Null;
        if (contentType is not null)
        {
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature(canHaveBody: true));
            context.Request.ContentType = contentType;
            context.Request.ContentLength = requestBody?.Length;
        }

        foreach (var (key, value) in routeValues)
        {
            context.Request.RouteValues[key] = value;
        }

        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        return new CapturedResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            await reader.ReadToEndAsync());
    }

    private static IEnumerable<Endpoint> GetEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
    }

    private static T Deserialize<T>(
        string json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Deserialize(json, jsonTypeInfo)
            ?? throw new InvalidOperationException("Response JSON could not be deserialized.");
    }

    private static GraphConfig CreateConfig(string graphId, string name) => new()
    {
        GraphId = graphId,
        Name = name,
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

    private static GraphScheduleConfig CreateSchedule() => new()
    {
        CronExpression = "0 3 * * *",
        TimeZoneId = "UTC"
    };

    private sealed record CapturedResponse(int StatusCode, string? ContentType, string Body);

    private sealed class TestRequestBodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; } = canHaveBody;
    }

    private sealed class TestResolverContributor(IJsonTypeInfoResolver resolver) : IGraphJsonTypeInfoResolverContributor
    {
        public IJsonTypeInfoResolver Resolver { get; } = resolver;
    }
}
