using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.OpenApi;
using HPDAgent.Graph.Connectors.OpenApi.Builders;
using HPDAgent.Graph.Connectors.OpenApi.Catalog;
using HPDAgent.Graph.Connectors.OpenApi.DependencyInjection;
using HPDAgent.Graph.Connectors.OpenApi.Descriptors;
using HPDAgent.Graph.Connectors.OpenApi.Handlers;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HPDGraph.Tests.V21;

public sealed class ConnectorOpenApiBridgeTests
{
    [Fact]
    public void OperationCatalog_ListsAndLooksUpOperationsByConnector()
    {
        var operation = CreateCreateWidgetOperation();
        var catalog = new OpenApiOperationCatalog(
        [
            new OpenApiOperationRegistration("widgets", operation)
        ]);

        catalog.GetOperation("widgets", "createWidget").Should().BeSameAs(operation);
        catalog.ListOperations("widgets").Should().ContainSingle().Which.Should().BeSameAs(operation);
        catalog.GetOperation("missing", "createWidget").Should().BeNull();
    }

    [Fact]
    public async Task OperationCatalogLoader_ProjectsParsedSpecsIntoConnectorRegistrations()
    {
        var json = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Widgets", "version": "1.0.0" },
          "servers": [{ "url": "https://api.example.test" }],
          "paths": {
            "/widgets/{widgetId}": {
              "get": {
                "operationId": "getWidget",
                "parameters": [
                  {
                    "name": "widgetId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "200": {
                    "description": "OK",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": { "id": { "type": "string" } }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var spec = await new OpenApiDocumentParser()
            .ParseAsync(stream, new OpenApiCoreConfig());

        var registrations = OpenApiOperationCatalogLoader.FromParsedSpec("widgets", spec);

        registrations.Should().ContainSingle(r =>
            r.ConnectorId == "widgets" &&
            r.Operation.Id == "getWidget" &&
            r.Operation.Path == "/widgets/{widgetId}");
    }

    [Fact]
    public void ArgumentBuilder_MergesPathQueryHeaderAndBodyValues()
    {
        var builder = new OpenApiArgumentBuilder();

        builder.Path("accountId", "acct_1");
        builder.Query("expand", new[] { "owner", "items" });
        builder.Header("x-trace-id", "trace");
        builder.Body("name", "Roadrunner");

        var args = builder.Build();

        args["accountId"].Should().Be("acct_1");
        args["expand"].Should().BeEquivalentTo(new[] { "owner", "items" });
        args["x-trace-id"].Should().Be("trace");
        args["name"].Should().Be("Roadrunner");
    }

    [Fact]
    public async Task GenericHandler_ExecutesOperationWithConnectionAuthAndArguments()
    {
        var operation = CreateCreateWidgetOperation();
        var http = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"w_1","ok":true}""", Encoding.UTF8, "application/json")
        });
        var client = new HttpClient(http);
        var connectionProvider = new TestOpenApiConnectionProvider();
        var handler = new OpenApiCallOperationHandler(
            new OpenApiOperationCatalog([new OpenApiOperationRegistration("widgets", operation)]),
            connectionProvider,
            [new TestOpenApiConnectionAdapter(client)]);

        var graph = new GraphBuilder()
            .WithName("openapi")
            .AddHandlerNode("call", "Call", OpenApiCallOperationHandler.Name, node => node.WithConfig(
                JsonSerializer.SerializeToElement(
                    new OpenApiCallOperationConfig
                    {
                        ConnectorId = "widgets",
                        ConnectionId = "widgets-main",
                        OperationId = "createWidget",
                        Arguments = JsonSerializer.SerializeToElement(new
                        {
                            accountId = "acct_1",
                            verbose = true
                        })
                    },
                    OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig)))
            .Build();
        var context = new GraphContext("exec-openapi", graph, EmptyServiceProvider.Instance);
        context.SetCurrentNode("call");
        var inputs = new HandlerInputs();
        inputs.Add("name", "Roadrunner");

        var result = await handler.ExecuteAsync(context, inputs);

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        success.PortOutputs[0].Should().ContainKey("response");
        http.Request.Should().NotBeNull();
        http.Request!.RequestUri!.ToString().Should().Be("https://api.example.test/accounts/acct_1/widgets?verbose=True");
        http.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        http.Request.Headers.Authorization.Parameter.Should().Be("secret-token");
        http.Body.Should().Contain("\"name\":\"Roadrunner\"");
    }

    [Fact]
    public async Task GenericHandler_ReturnsOpenApiErrorsAsOutputData()
    {
        var operation = CreateCreateWidgetOperation();
        var client = new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message":"bad widget"}""", Encoding.UTF8, "application/json")
        }));
        var handler = new OpenApiCallOperationHandler(
            new OpenApiOperationCatalog([new OpenApiOperationRegistration("widgets", operation)]),
            new TestOpenApiConnectionProvider(),
            [new TestOpenApiConnectionAdapter(client)]);
        var graph = new GraphBuilder()
            .WithName("openapi-error")
            .AddHandlerNode("call", "Call", OpenApiCallOperationHandler.Name, node => node.WithConfig(
                JsonSerializer.SerializeToElement(
                    new OpenApiCallOperationConfig
                    {
                        ConnectorId = "widgets",
                        ConnectionId = "widgets-main",
                        OperationId = "createWidget",
                        Arguments = JsonSerializer.SerializeToElement(new
                        {
                            accountId = "acct_1",
                            name = "bad"
                        })
                    },
                    OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig)))
            .Build();
        var context = new GraphContext("exec-openapi-error", graph, EmptyServiceProvider.Instance);
        context.SetCurrentNode("call");

        var result = await handler.ExecuteAsync(context, new HandlerInputs());

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        var error = success.PortOutputs[0]["error"].Should().BeOfType<OpenApiErrorResponse>().Subject;
        error.StatusCode.Should().Be(400);
        error.UserMessage.Should().Be("bad widget");
    }

    [Fact]
    public void DescriptorFactory_MapsOperationsToGraphAndConnectorDescriptors()
    {
        var operation = CreateCreateWidgetOperation();

        var handler = OpenApiDescriptorFactory.CreateHandlerDescriptor("widgets", operation);
        var action = OpenApiDescriptorFactory.CreateConnectorActionDescriptor("widgets", operation);

        handler.HandlerName.Should().Be("widgets.createWidget");
        handler.Metadata["openapi.method"].Should().Be("POST");
        handler.Inputs.Should().Contain(i => i.Name == "accountId" && i.Required);
        handler.Inputs.Should().Contain(i => i.Name == "name" && i.Required);
        action.ActionType.Should().Be("widgets.createWidget");
        action.HandlerName.Should().Be(OpenApiCallOperationHandler.Name);
        action.Fields.Should().Contain(f => f.Name == "name" && f.Required);
    }

    [Fact]
    public void DependencyInjection_RegistersOperationsCatalogAndDescriptorCatalog()
    {
        var operation = CreateCreateWidgetOperation();
        using var provider = new ServiceCollection()
            .AddOpenApiOperations("widgets", [operation])
            .BuildServiceProvider();

        provider.GetRequiredService<IOpenApiOperationCatalog>()
            .GetOperation("widgets", "createWidget")
            .Should()
            .BeSameAs(operation);
        provider.GetRequiredService<IOpenApiDescriptorCatalog>()
            .GetActions()
            .Should()
            .ContainSingle(a => a.ActionType == "widgets.createWidget");
    }

    [Fact]
    public void GraphBuilderExtension_AddsGenericOpenApiOperationNode()
    {
        var graph = new GraphBuilder()
            .WithName("openapi-builder")
            .AddOpenApiOperationNode("call", "Call", new OpenApiCallOperationConfig
            {
                ConnectorId = "widgets",
                ConnectionId = "widgets-main",
                OperationId = "createWidget"
            })
            .Build();

        var node = graph.GetNode("call");
        node.HandlerName.Should().Be(OpenApiCallOperationHandler.Name);
        node.Config.Should().NotBeNull();
        var config = JsonSerializer.Deserialize(
            node.Config!.Value.GetRawText(),
            OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig);
        config!.OperationId.Should().Be("createWidget");
    }

    private static RestApiOperation CreateCreateWidgetOperation()
        => new()
        {
            Id = "createWidget",
            Method = HttpMethod.Post,
            Path = "/accounts/{accountId}/widgets",
            ServerUrl = "https://api.example.test",
            Description = "Create a widget.",
            Parameters =
            [
                new RestApiParameter
                {
                    Name = "accountId",
                    Location = RestApiParameterLocation.Path,
                    IsRequired = true,
                    Type = "string"
                },
                new RestApiParameter
                {
                    Name = "verbose",
                    Location = RestApiParameterLocation.Query,
                    Type = "boolean"
                }
            ],
            Payload = new RestApiPayload
            {
                MediaType = "application/json",
                Properties =
                [
                    new RestApiPayloadProperty
                    {
                        Name = "name",
                        Type = "string",
                        IsRequired = true
                    }
                ]
            }
        };

    private sealed class TestOpenApiConnectionProvider : IConnectionProvider
    {
        public Task<ResolvedConnection?> ResolveAsync(
            string connectionId,
            CancellationToken ct = default)
            => Task.FromResult<ResolvedConnection?>(new ResolvedConnection
            {
                ConnectionId = connectionId,
                ConnectionType = "widgets.bearer",
                AppId = "widgets",
                Secrets = new Dictionary<string, string>
                {
                    ["token"] = "secret-token"
                }
            });
    }

    private sealed class TestOpenApiConnectionAdapter : IOpenApiConnectionAdapter
    {
        private readonly HttpClient _client;

        public TestOpenApiConnectionAdapter(HttpClient client) => _client = client;

        public bool CanAdapt(ResolvedConnection connection)
            => connection.ConnectionType == "widgets.bearer";

        public OpenApiCoreConfig CreateConfig(
            ResolvedConnection connection,
            CancellationToken ct = default)
        {
            return new OpenApiCoreConfig
            {
                HttpClient = _client,
                AuthCallback = (request, _) =>
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        connection.Secrets["token"]);
                    return Task.CompletedTask;
                }
            };
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            => _responseFactory = responseFactory;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responseFactory(request);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
