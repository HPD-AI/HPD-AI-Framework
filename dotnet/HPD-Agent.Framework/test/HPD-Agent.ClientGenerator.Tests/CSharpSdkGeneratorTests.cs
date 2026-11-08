using System.Diagnostics;
using System.Text.Json;
using HPD.Agent.ClientGenerator;

namespace HPD.Agent.ClientGenerator.Tests;

public sealed class CSharpSdkGeneratorTests
{
    [Fact]
    public async Task GeneratedClient_RoundTripsSessionInputAndTypedSseEvent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-csharp-generator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var contractPath = Path.Combine(root, "contract.json");
            await File.WriteAllTextAsync(contractPath, Contract);
            var runtimeCatalogPath = Path.Combine(root, "runtime-catalog.json");
            await File.WriteAllTextAsync(runtimeCatalogPath, RuntimeCatalog);
            var sdkPath = Path.Combine(root, "sdk");
            var manifest = await new CSharpSdkGenerator().GenerateAsync(
                new GeneratorOptions(
                    contractPath,
                    sdkPath,
                    "Contract.Client",
                    RuntimeCatalogFile: runtimeCatalogPath));

            Assert.Equal(8, manifest.OperationCount);
            Assert.Equal(21, manifest.ModelCount);
            Assert.Equal(7, manifest.EventCount);
            Assert.Equal(2, manifest.InputCount);
            Assert.Equal(1, manifest.RuntimeSchemaCount);
            Assert.Equal("1.0", manifest.IrVersion);
            Assert.StartsWith("sha256:", manifest.InputDigest, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", manifest.IrSha256, StringComparison.Ordinal);
            Assert.Equal("sha256:test-events", manifest.EventDigest);
            Assert.StartsWith("sha256:", manifest.PublicApiSha256, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(sdkPath, "hpd-client.generated.json")));
            Assert.True(File.Exists(Path.Combine(sdkPath, "sdk-ir.json")));
            Assert.True(File.Exists(Path.Combine(sdkPath, "public-api.txt")));
            Assert.True(File.Exists(Path.Combine(sdkPath, "runtime", "HPD-Agent.Client.csproj")));
            Assert.True(File.Exists(Path.Combine(sdkPath, "generated", "HpdAgentSdk.csproj")));
            Assert.Contains(
                "RuntimePayloadTenantTestTypedV1",
                await File.ReadAllTextAsync(Path.Combine(sdkPath, "public-api.txt")),
                StringComparison.Ordinal);
            using (var sdkIrDocument = JsonDocument.Parse(
                       await File.ReadAllTextAsync(Path.Combine(sdkPath, "sdk-ir.json"))))
            {
                var sdkIr = sdkIrDocument.RootElement;
                Assert.Equal("1.0", sdkIr.GetProperty("irVersion").GetString());
                Assert.Equal(
                    8,
                    sdkIr.GetProperty("operations").GetArrayLength());
                Assert.Equal(
                    1,
                    sdkIr.GetProperty("types")
                        .EnumerateArray()
                        .Count(type => type.GetProperty("componentId").GetString() == "InputA"));
                Assert.Contains(
                    sdkIr.GetProperty("unions").EnumerateArray(),
                    union => union.GetProperty("role").GetString() == "output-event");
                Assert.Contains(
                    sdkIr.GetProperty("capabilities").EnumerateArray(),
                    capability => capability.GetProperty("name").GetString() == "server-sent-events");
            }

            var changedContract = Contract.Replace(
                "\"createdAt\": { \"type\": \"string\" }",
                "\"createdAt\": { \"type\": \"string\" },\n                  \"extra\": { \"type\": \"string\" }",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(contractPath, changedContract);
            var generator = new CSharpSdkGenerator();
            var publicApiError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateAsync(new GeneratorOptions(
                    contractPath,
                    sdkPath,
                    "Contract.Client",
                    RuntimeCatalogFile: runtimeCatalogPath)));
            Assert.Contains("public API differs", publicApiError.Message, StringComparison.Ordinal);
            await generator.GenerateAsync(new GeneratorOptions(
                contractPath,
                sdkPath,
                "Contract.Client",
                VerifyBuild: false,
                AllowPublicApiChange: true,
                RuntimeCatalogFile: runtimeCatalogPath));

            var consumerPath = Path.Combine(root, "consumer");
            Directory.CreateDirectory(consumerPath);
            await File.WriteAllTextAsync(
                Path.Combine(consumerPath, "Consumer.csproj"),
                ConsumerProject);
            await File.WriteAllTextAsync(
                Path.Combine(consumerPath, "Program.cs"),
                ConsumerProgram);

            var result = await RunAsync(
                "run",
                "--project",
                Path.Combine(consumerPath, "Consumer.csproj"),
                "--configuration",
                "Release",
                "--nologo");
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("consumer-ok", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput + await standardError;
        return new ProcessResult(process.ExitCode, output);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private const string ConsumerProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../sdk/generated/HpdAgentSdk.csproj" />
            <ProjectReference Include="../sdk/runtime/HPD-Agent.Client.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string ConsumerProgram =
        """"
        using System.Net;
        using System.Text;
        using System.Text.Json;
        using HPD.Agent.Client;
        using Contract.Client;

        var handler = new CaptureHandler();
        using var client = new HpdAgentClient(new HpdAgentClientOptions(
            new Uri("https://example.test"))
        {
            HttpClient = new HttpClient(handler),
            Responder = new HpdAgentResponderOptions
            {
                ResponderId = "browser-a",
                ResponderGroups = ["web-ui"]
            }
        });

        var session = await client.Sessions.GetSessionAsync("session/one");
        if (session.Id != "session-one")
            throw new InvalidOperationException("Session response was not hydrated.");
        if (!handler.LastUri!.AbsoluteUri.Contains("session%2Fone", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path parameter was not escaped: {handler.LastUri}");
        var missingSession = await client.Sessions.TryGetSessionAsync("missing");
        if (missingSession is not null)
            throw new InvalidOperationException("Declared 404 did not produce a null result.");
        var sessions = await client.Sessions.ListSessionsAsync(
            limit: 25,
            sortBy: "lastActivity");
        if (sessions.Single().Id != "session-one" ||
            !handler.LastUri!.Query.Contains("limit=25", StringComparison.Ordinal) ||
            !handler.LastUri.Query.Contains("sortBy=lastActivity", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("List-session query options were not serialized.");
        }

        await client.AgentRuntime.SubmitAgentInputAsync(
            "agent-one",
            "thread-one",
            "session-one",
            new InputA { Text = "hello", Mode = InputAMode.Fast });
        if (!handler.LastBody.Contains("\"type\":\"INPUT_A\"", StringComparison.Ordinal))
            throw new InvalidOperationException($"Input discriminator was not serialized: {handler.LastBody}");
        if (!handler.LastBody.Contains("\"mode\":\"fast\"", StringComparison.Ordinal))
            throw new InvalidOperationException($"Extensible enum was not serialized: {handler.LastBody}");

        var interaction = await client.AgentInteractions.RespondToAgentRequestAsync(
            "agent-one",
            "thread-one",
            "session-one",
            new PermissionResponseEvent
            {
                PermissionId = "permission-one",
                SourceName = "host",
                ChoiceId = "allow"
            });
        if (!interaction.IsAccepted ||
            interaction.ResponseStatus != HpdAgentResponseStatus.Accepted ||
            !handler.LastBody.Contains("\"type\":\"PERMISSION_RESPONSE\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Typed interaction response was not serialized: {handler.LastBody}");
        }

        await using var provider = client.ClientTools.ConnectClientToolProvider(
            new HpdAgentClientToolProviderIdentity("provider-one", "app-one"),
            new HpdAgentClientAppProvider("app-one"));
        if (provider.IsConnected)
            throw new InvalidOperationException("Provider configuration connected before ConnectAsync.");
        var permissionRequest = new PermissionRequestEvent
        {
            PermissionId = "permission-one",
            CallId = "call-permission",
            SourceName = "PermissionMiddleware",
            PermissionDomain = "acme.database-admin",
            Authority = "acme.db/write",
            Action = "write",
            PermissionResource = null,
            Reason = "Write the report?",
            Risk = PermissionRequestEventRisk.High,
            Choices =
            [
                new InlinePermissionRequestEventChoicesItem
                {
                    Id = "allow_once",
                    Label = "Allow once",
                    Kind = InlinePermissionRequestEventChoicesItemKind.Allow
                },
                new InlinePermissionRequestEventChoicesItem
                {
                    Id = "deny_once",
                    Label = "Deny",
                    Kind = InlinePermissionRequestEventChoicesItemKind.Deny
                }
            ],
            FunctionName = "write_file",
            Arguments = JsonSerializer.SerializeToElement(new { path = "/tmp/report.md" })
        };
        var prompt = HpdAgentPermissions.ToPrompt(permissionRequest);
        var approvedResponse = HpdAgentPermissions.CreateResponse(
            permissionRequest,
            new HpdAgentPermissionAnswer("permission-one", Approved: true, Reason: "once"));
        var deniedResponse = HpdAgentPermissions.CreateResponse(
            permissionRequest,
            new HpdAgentPermissionAnswer("permission-one", Approved: false));
        if (prompt.FunctionName != "write_file" ||
            approvedResponse.ChoiceId != "allow_once" ||
            approvedResponse.Feedback != "once" ||
            deniedResponse.ChoiceId != "deny_once")
        {
            throw new InvalidOperationException("Permission helpers did not preserve the simple answer contract.");
        }
        var providers = await client.ClientTools.ListClientToolProvidersAsync();
        var scores = await client.Evaluations.GetScoresAsync("quality");
        if (providers.Single().ClientRuntimeId != "runtime-one" ||
            scores.Single().EvaluatorName != "quality")
        {
            throw new InvalidOperationException("Provider or evaluation results were not hydrated.");
        }

        var delivered = false;
        var toolInvoked = false;
        var unknownEvents = new List<string>();
        var disposedCalled = false;
        var order = new List<string>();
        using var disposedSubscription = client.Live.On<EventA>(_ => disposedCalled = true);
        disposedSubscription.Dispose();
        using var typedSubscription = client.Live.On<EventA>(applicationEvent =>
        {
            if (applicationEvent.Value != "ok")
                throw new InvalidOperationException("Typed SSE event had the wrong value.");
            order.Add("typed");
            delivered = true;
        });
        using var anySubscription = client.Live.OnAny(delivery =>
        {
            if (delivery.Route.Origin.SessionId != "session-one")
                throw new InvalidOperationException("SSE route was not hydrated.");
            order.Add("any:" + delivery.Event.GetType().Name);
        });
        using var unknownSubscription = client.Live.OnUnknown(message =>
            unknownEvents.Add(
                message.Data.GetProperty("event").GetProperty("type").GetString()!));
        client.Live.Tools.Register(
            "host.sample.echo",
            (invocation, _) =>
            {
                toolInvoked = true;
                var value = invocation.Arguments.GetProperty("value").GetString();
                return ValueTask.FromResult(HpdAgentClientToolResult.Text("echo:" + value));
            });
        await client.Live.ObserveAsync(
            "agent-one",
            "thread-one",
            "session-one",
            after: new HpdAgentSseAppliedCursor(1, 0),
            reconnectOptions: HpdAgentSseReconnectOptions.Disabled);
        if (!delivered)
            throw new InvalidOperationException("No typed SSE event was delivered.");
        if (!toolInvoked)
            throw new InvalidOperationException("Client tool was not automatically dispatched.");
        if (disposedCalled)
            throw new InvalidOperationException("Disposed subscription was invoked.");
        if (unknownEvents.Count != 2 ||
            unknownEvents[0] != "FUTURE_EVENT" ||
            unknownEvents[1] != "EVENT_A")
            throw new InvalidOperationException(
                "Unknown or invalid-route events were not classified correctly: " +
                string.Join(",", unknownEvents));
        if (handler.ResponseCalls != 2)
            throw new InvalidOperationException(
                $"Targeted request for another responder was answered. Calls: {handler.ResponseCalls}");
        if (order.Count != 4 ||
            order[0] != "typed" ||
            order[1] != "any:EventA" ||
            order[2] != "any:ClientToolInvokeRequestEvent" ||
            order[3] != "any:ClientToolInvokeRequestEvent")
            throw new InvalidOperationException("Event callbacks were not ordered: " + string.Join(",", order));
        if (!handler.LastBody.Contains("\"type\":\"CLIENT_TOOL_INVOKE_OUTCOME\"", StringComparison.Ordinal) ||
            !handler.LastBody.Contains("\"outcome\":\"Completed\"", StringComparison.Ordinal) ||
            !handler.LastBody.Contains("echo:hello", StringComparison.Ordinal))
            throw new InvalidOperationException($"Client tool response was not normalized: {handler.LastBody}");

        var observedError = false;
        using var errorSubscription = client.Live.OnError(error =>
            observedError = error is HpdAgentJournalRebasedException);
        var rebaseRaised = false;
        try
        {
            await client.Live.ObserveAsync(
                "agent-one",
                "thread-one",
                "session-one",
                after: new HpdAgentSseAppliedCursor(1, 5),
                reconnectOptions: HpdAgentSseReconnectOptions.Disabled);
        }
        catch (HpdAgentJournalRebasedException)
        {
            rebaseRaised = true;
        }
        if (!rebaseRaised || !observedError)
            throw new InvalidOperationException("Journal rebase did not reach OnError.");

        var runtimeEvent = new RuntimeAgentLiveEvent
        {
            ExtensionNamespace = "tenant.test",
            SchemaName = "typed",
            SchemaVersion = 1,
            SchemaDigest = "sha256:b8c38e6b11ca2a13efd1b9f8b9dea43ecb368c280d328099f63e9e0a6910cfb2",
            Payload = JsonSerializer.SerializeToElement(new { value = "typed-value" })
        };
        var runtimePayload = HpdRuntimeExtensionPayloadCodec.Decode(runtimeEvent);
        if (runtimePayload is not RuntimePayloadTenantTestTypedV1 typed ||
            typed.Value != "typed-value")
        {
            throw new InvalidOperationException("Runtime payload was not decoded to its generated type.");
        }
        var runtimeInput = HpdRuntimeExtensionPayloadCodec.CreateInput(typed);
        if (runtimeInput.SchemaName != "typed" || runtimeInput.Payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Typed runtime payload was not encoded as an input envelope.");

        Console.WriteLine("consumer-ok");

        sealed class CaptureHandler : HttpMessageHandler
        {
            public Uri? LastUri { get; private set; }
            public string LastBody { get; private set; } = string.Empty;
            public int ResponseCalls { get; private set; }
            public int EventRequests { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri;
                if (request.Content is not null)
                    LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

                if (request.RequestUri!.AbsolutePath.EndsWith("/sessions/session%2Fone", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(HttpStatusCode.OK, """
                        {"id":"session-one","createdAt":"2026-01-01"}
                        """);
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/sessions/missing", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (request.RequestUri.AbsolutePath.EndsWith("/sessions", StringComparison.OrdinalIgnoreCase))
                    return Json(
                        HttpStatusCode.OK,
                        """[{"id":"session-one","createdAt":"2026-01-01T00:00:00Z"}]""");
                if (request.RequestUri.AbsolutePath.EndsWith("/inputs", StringComparison.Ordinal))
                    return Json(HttpStatusCode.Accepted, """{"status":"queued"}""");
                if (request.RequestUri.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal))
                {
                    ResponseCalls++;
                    return Json(
                        HttpStatusCode.OK,
                        """{"status":0,"requestId":"request-one","message":null,"accepted":true}""");
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/client-tool-providers", StringComparison.Ordinal))
                    return Json(
                        HttpStatusCode.OK,
                        """[{"clientRuntimeId":"runtime-one","connectionId":"connection-one"}]""");
                if (request.RequestUri.AbsolutePath.EndsWith("/evals/scores", StringComparison.Ordinal))
                    return Json(
                        HttpStatusCode.OK,
                        """[{"id":"score-one","evaluatorName":"quality"}]""");
                if (request.RequestUri.AbsolutePath.EndsWith("/events", StringComparison.Ordinal))
                {
                    EventRequests++;
                    if (EventRequests > 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                "event: thread-journal-rebased\n" +
                                "data: {\"previousGeneration\":1,\"currentGeneration\":2}\n\n",
                                Encoding.UTF8,
                                "text/event-stream")
                        };
                    }
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "event: agent-event\n" +
                            "id: 1:1\n" +
                            "data: {\"event\":{\"type\":\"EVENT_A\",\"value\":\"ok\"}," +
                            "\"route\":{\"origin\":{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}," +
                            "\"path\":[{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}]," +
                            "\"threadExecutionId\":null}}\n\n" +
                            "event: agent-event\n" +
                            "id: 1:2\n" +
                            "data: {\"event\":{\"type\":\"CLIENT_TOOL_INVOKE_REQUEST\"," +
                            "\"requestId\":\"request-one\",\"toolName\":\"host.sample.echo\"," +
                            "\"callId\":\"call-one\",\"arguments\":{\"value\":\"hello\"}}," +
                            "\"route\":{\"origin\":{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}," +
                            "\"path\":[{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}]," +
                            "\"threadExecutionId\":null}}\n\n" +
                            "event: agent-event\n" +
                            "id: 1:3\n" +
                            "data: {\"event\":{\"type\":\"FUTURE_EVENT\",\"value\":\"future\"}," +
                            "\"route\":{\"origin\":{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}," +
                            "\"path\":[{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}]," +
                            "\"threadExecutionId\":null}}\n\n" +
                            "event: agent-event\n" +
                            "id: 1:4\n" +
                            "data: {\"event\":{\"type\":\"CLIENT_TOOL_INVOKE_REQUEST\"," +
                            "\"requestId\":\"request-two\",\"toolName\":\"host.sample.echo\"," +
                            "\"callId\":\"call-two\",\"arguments\":{\"value\":\"other\"}," +
                            "\"responsePolicy\":1,\"target\":{\"responderId\":\"browser-b\"," +
                            "\"requiredCapabilities\":[\"client-tool:echo\"]}}," +
                            "\"route\":{\"origin\":{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}," +
                            "\"path\":[{\"sessionId\":\"session-one\",\"threadId\":\"thread-one\"}]," +
                            "\"threadExecutionId\":null}}\n\n" +
                            "event: agent-event\n" +
                            "id: 1:5\n" +
                            "data: {\"event\":{\"type\":\"EVENT_A\",\"value\":\"invalid-route\"}," +
                            "\"route\":{\"origin\":{\"sessionId\":\"session-one\",\"threadId\":\"   \"}," +
                            "\"path\":[{\"sessionId\":\"session-one\",\"threadId\":\"   \"}]," +
                            "\"threadExecutionId\":null}}\n\n",
                            Encoding.UTF8,
                            "text/event-stream")
                    };
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(request.RequestUri.AbsoluteUri)
                };
            }

            private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
                new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        """";

    internal const string Contract =
        """
        {
          "openapi": "3.1.1",
          "info": { "title": "Generator Fixture", "version": "1.0.0" },
          "paths": {
            "/sessions/{sessionId}": {
              "get": {
                "operationId": "GetSession",
                "tags": ["Sessions"],
                "parameters": [
                  {
                    "name": "sessionId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "200": {
                    "description": "Session",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/SessionDto" }
                      }
                    }
                  },
                  "404": { "description": "Session not found" }
                }
              }
            },
            "/sessions": {
              "get": {
                "operationId": "ListSessions",
                "tags": ["Sessions"],
                "parameters": [
                  { "name": "limit", "in": "query", "required": false, "schema": { "type": "integer" } },
                  { "name": "sortBy", "in": "query", "required": false, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": {
                    "description": "Sessions",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": { "$ref": "#/components/schemas/SessionDto" }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/inputs": {
              "post": {
                "operationId": "SubmitAgentInput",
                "tags": ["Agent Runtime"],
                "parameters": [
                  { "name": "agentId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "sid", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "bid", "in": "path", "required": true, "schema": { "type": "string" } }
                ],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "oneOf": [
                          { "$ref": "#/components/schemas/InputA" },
                          { "$ref": "#/components/schemas/RuntimeAgentInputEvent" }
                        ]
                      }
                    }
                  }
                },
                "responses": {
                  "202": {
                    "description": "Accepted",
                    "content": {
                      "application/json": {
                        "schema": { "type": "object", "additionalProperties": true }
                      }
                    }
                  }
                }
              }
            },
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/events": {
              "get": {
                "operationId": "ObserveLiveEventsWithSse",
                "tags": ["Agent Events"],
                "x-hpd-event-count": 7,
                "x-hpd-event-digest": "sha256:test-events",
                "parameters": [
                  { "name": "agentId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "sid", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "bid", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "after", "in": "query", "required": false, "schema": { "type": "string" } },
                  { "name": "Last-Event-ID", "in": "header", "required": false, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": {
                    "description": "Events",
                    "content": {
                      "text/event-stream": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "data": {
                              "oneOf": [
                                {
                                  "type": "object",
                                  "required": ["event", "route"],
                                  "properties": {
                                    "event": {
                                      "oneOf": [
                                        { "$ref": "#/components/schemas/EventA" },
                                        { "$ref": "#/components/schemas/RuntimeAgentLiveEvent" },
                                        { "$ref": "#/components/schemas/RuntimeAgentDurableEvent" },
                                        { "$ref": "#/components/schemas/ClientToolInvokeRequestEvent" },
                                        { "$ref": "#/components/schemas/ClientToolInvokeOutcomeEvent" },
                                        { "$ref": "#/components/schemas/PermissionRequestEvent" },
                                        { "$ref": "#/components/schemas/PermissionResponseEvent" }
                                      ]
                                    },
                                    "route": {
                                      "type": "object",
                                      "properties": {
                                        "origin": { "type": "object" },
                                        "path": { "type": "array", "items": { "type": "object" } },
                                        "threadExecutionId": { "type": ["null", "string"] }
                                      }
                                    }
                                  }
                                },
                                {
                                  "type": "object",
                                  "properties": {
                                    "previousGeneration": { "type": "integer" },
                                    "currentGeneration": { "type": "integer" }
                                  }
                                }
                              ]
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/agents/{agentId}/sessions/{sid}/threads/{bid}/responses": {
              "post": {
                "operationId": "RespondToAgentRequest",
                "tags": ["Agent Interactions"],
                "parameters": [
                  { "name": "agentId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "sid", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "bid", "in": "path", "required": true, "schema": { "type": "string" } }
                ],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "oneOf": [
                          { "$ref": "#/components/schemas/PermissionResponseEvent" },
                          { "$ref": "#/components/schemas/ClientToolInvokeOutcomeEvent" }
                        ]
                      }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Accepted",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "required": ["status", "requestId"],
                          "properties": {
                            "status": { "type": "integer" },
                            "requestId": { "type": "string" },
                            "message": { "type": ["null", "string"] },
                            "accepted": { "type": "boolean" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/client-tool-providers/connect": {
              "get": {
                "operationId": "ConnectClientToolProvider",
                "tags": ["Client Tools"],
                "x-hpd-websocket-protocol": "hpd.client-tool-provider.v2",
                "responses": {
                  "101": {
                    "description": "WebSocket protocol upgrade."
                  }
                }
              }
            },
            "/client-tool-providers": {
              "get": {
                "operationId": "ListClientToolProviders",
                "tags": ["Client Tools"],
                "parameters": [
                  { "name": "appProviderName", "in": "query", "required": false, "schema": { "type": "string" } },
                  { "name": "appKind", "in": "query", "required": false, "schema": { "type": "string" } },
                  { "name": "includeDisconnected", "in": "query", "required": false, "schema": { "type": "boolean" } }
                ],
                "responses": {
                  "200": {
                    "description": "Providers",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": { "$ref": "#/components/schemas/ClientToolProviderSnapshot" }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/evals/scores": {
              "get": {
                "operationId": "GetScores",
                "tags": ["Evaluations"],
                "parameters": [
                  { "name": "evaluatorName", "in": "query", "required": true, "schema": { "type": "string" } },
                  { "name": "from", "in": "query", "required": false, "schema": { "type": "string" } },
                  { "name": "to", "in": "query", "required": false, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": {
                    "description": "Scores",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": { "$ref": "#/components/schemas/ScoreRecord" }
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "SessionDto": {
                "type": "object",
                "required": ["id", "createdAt"],
                "properties": {
                  "id": { "type": "string" },
                  "createdAt": { "type": "string" }
                }
              },
              "ClientToolProviderSnapshot": {
                "type": "object",
                "required": ["clientRuntimeId", "connectionId"],
                "properties": {
                  "clientRuntimeId": { "type": "string" },
                  "connectionId": { "type": "string" },
                  "state": { "type": ["null", "string"] }
                }
              },
              "ScoreRecord": {
                "type": "object",
                "properties": {
                  "id": { "type": ["null", "string"] },
                  "evaluatorName": { "type": ["null", "string"] }
                }
              },
              "RuntimeAgentLiveEvent": {
                "type": "object",
                "required": ["type", "extensionNamespace", "schemaName", "schemaVersion", "schemaDigest", "payload"],
                "x-hpd-event-discriminator": "RUNTIME_AGENT_LIVE_EVENT",
                "properties": {
                  "type": { "type": "string", "enum": ["RUNTIME_AGENT_LIVE_EVENT"] },
                  "extensionNamespace": { "type": "string" },
                  "schemaName": { "type": "string" },
                  "schemaVersion": { "type": "integer" },
                  "schemaDigest": { "type": "string" },
                  "payload": {}
                }
              },
              "RuntimeAgentDurableEvent": {
                "type": "object",
                "required": ["type", "extensionNamespace", "schemaName", "schemaVersion", "schemaDigest", "payload"],
                "x-hpd-event-discriminator": "RUNTIME_AGENT_DURABLE_EVENT",
                "properties": {
                  "type": { "type": "string", "enum": ["RUNTIME_AGENT_DURABLE_EVENT"] },
                  "extensionNamespace": { "type": "string" },
                  "schemaName": { "type": "string" },
                  "schemaVersion": { "type": "integer" },
                  "schemaDigest": { "type": "string" },
                  "payload": {}
                }
              },
              "RuntimeAgentInputEvent": {
                "type": "object",
                "required": ["type", "extensionNamespace", "schemaName", "schemaVersion", "schemaDigest", "payload"],
                "properties": {
                  "type": { "type": "string", "enum": ["RUNTIME_AGENT_INPUT"] },
                  "extensionNamespace": { "type": "string" },
                  "schemaName": { "type": "string" },
                  "schemaVersion": { "type": "integer" },
                  "schemaDigest": { "type": "string" },
                  "payload": {}
                }
              },
              "InputA": {
                "type": "object",
                "required": ["type", "text", "mode"],
                "properties": {
                  "type": { "type": "string", "enum": ["INPUT_A"] },
                  "text": { "type": "string" },
                  "mode": { "type": "string", "enum": ["fast", "safe"] }
                }
              },
              "EventA": {
                "type": "object",
                "required": ["type", "value"],
                "x-hpd-event-discriminator": "EVENT_A",
                "properties": {
                  "type": { "type": "string", "enum": ["EVENT_A"] },
                  "value": { "type": "string" }
                }
              },
              "PermissionResponseEvent": {
                "type": "object",
                "required": ["type", "permissionId", "sourceName", "choiceId"],
                "x-hpd-event-discriminator": "PERMISSION_RESPONSE",
                "properties": {
                  "type": { "type": "string", "enum": ["PERMISSION_RESPONSE"] },
                  "permissionId": { "type": "string" },
                  "sourceName": { "type": "string" },
                  "choiceId": { "type": "string" },
                  "feedback": { "type": ["null", "string"] }
                }
              },
              "PermissionRequestEvent": {
                "type": "object",
                "required": ["type", "permissionId", "callId", "sourceName", "permissionDomain", "risk", "choices"],
                "x-hpd-event-discriminator": "PERMISSION_REQUEST",
                "properties": {
                  "type": { "type": "string", "enum": ["PERMISSION_REQUEST"] },
                  "permissionId": { "type": "string" },
                  "callId": { "type": "string" },
                  "sourceName": { "type": "string" },
                  "permissionDomain": { "type": "string" },
                  "authority": { "type": ["null", "string"] },
                  "action": { "type": ["null", "string"] },
                  "permissionResource": { "type": ["null", "object"] },
                  "reason": { "type": ["null", "string"] },
                  "risk": { "type": "string", "enum": ["Low", "Medium", "High", "Critical"] },
                  "choices": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "required": ["id", "label", "kind"],
                      "properties": {
                        "id": { "type": "string" },
                        "label": { "type": "string" },
                        "kind": { "type": "string", "enum": ["Allow", "Deny", "Feedback"] }
                      }
                    }
                  },
                  "functionName": { "type": ["null", "string"] },
                  "arguments": {}
                }
              },
              "ClientToolInvokeRequestEvent": {
                "type": "object",
                "required": ["type", "requestId", "toolName", "callId", "arguments"],
                "x-hpd-event-discriminator": "CLIENT_TOOL_INVOKE_REQUEST",
                "properties": {
                  "type": { "type": "string", "enum": ["CLIENT_TOOL_INVOKE_REQUEST"] },
                  "requestId": { "type": "string" },
                  "toolName": { "type": "string" },
                  "callId": { "type": "string" },
                  "arguments": {},
                  "description": { "type": ["null", "string"] },
                  "responsePolicy": { "type": "integer" },
                  "target": {
                    "type": "object",
                    "properties": {
                      "responderId": { "type": ["null", "string"] },
                      "responderGroup": { "type": ["null", "string"] },
                      "requiredCapabilities": {
                        "type": "array",
                        "items": { "type": "string" }
                      }
                    }
                  }
                }
              },
              "ClientToolInvokeOutcomeEvent": {
                "type": "object",
                "required": ["type", "requestId", "outcome"],
                "x-hpd-event-discriminator": "CLIENT_TOOL_INVOKE_OUTCOME",
                "properties": {
                  "type": { "type": "string", "enum": ["CLIENT_TOOL_INVOKE_OUTCOME"] },
                  "requestId": { "type": "string" },
                  "outcome": { "type": "string", "enum": ["Completed", "AcceptedBackground", "Rejected", "Failed"] },
                  "content": { "type": ["null", "array"] },
                  "errorMessage": { "type": ["null", "string"] },
                  "responderId": { "type": ["null", "string"] },
                  "responderGroup": { "type": ["null", "string"] },
                  "capabilities": { "type": "array", "items": { "type": "string" } }
                }
              }
            }
          }
        }
        """;

    private const string RuntimeCatalog =
        """
        {
          "schemas": [
            {
              "key": "tenant.test:typed@1",
              "namespace": "tenant.test",
              "name": "typed",
              "version": 1,
              "usage": 1,
              "payloadSchemaDigest": "sha256:b8c38e6b11ca2a13efd1b9f8b9dea43ecb368c280d328099f63e9e0a6910cfb2",
              "payloadSchema": {
                "type": "object",
                "required": ["value"],
                "properties": {
                  "value": { "type": "string" }
                }
              },
              "description": "Typed runtime payload."
            }
          ],
          "digest": "sha256:runtime-catalog"
        }
        """;
}
