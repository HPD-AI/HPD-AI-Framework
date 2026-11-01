using HPD.Agent.MCP;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Serialization;
using Microsoft.Extensions.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpAgentConfigurationTests
{
    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void InlineServersAndManifests_RoundTrip(HpdConfigFormat format)
    {
        var input = new AgentConfig
        {
            Mcp = new AgentMcpConfig
            {
                Servers =
                [
                    new McpServerConfig
                    {
                        Name = "inline",
                        Transport = "http",
                        Endpoint = new Uri("https://inline.example/mcp"),
                        RequiresPermission = true
                    }
                ],
                Manifests =
                [
                    new McpManifestReference
                    {
                        Path = "./mcp.json",
                        SourceName = "shared"
                    }
                ]
            }
        };

        var document = HpdAgentConfigSerializer.Serialize(input, format);
        var output = HpdAgentConfigSerializer.Deserialize(document, format)!;

        var server = Assert.Single(output.Mcp!.Servers);
        Assert.Equal("inline", server.Name);
        Assert.Equal("https://inline.example/mcp", server.Endpoint!.AbsoluteUri);
        var manifest = Assert.Single(output.Mcp.Manifests);
        Assert.Equal("./mcp.json", manifest.Path);
        Assert.Equal("shared", manifest.SourceName);
    }

    [Fact]
    public void Activator_NormalizesInlineAndFileSources()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-normalize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "shared-mcp.json");
        File.WriteAllText(
            manifestPath,
            """{"servers":[{"name":"shared","transport":"http","endpoint":"https://shared.example/mcp"}]}""");

        try
        {
            var registrations = McpConfigurationActivator.Instance.Create(
                new ResolvedAgentMcpConfig
                {
                    AgentId = "test-agent",
                    Servers =
                    [
                        new McpServerConfig
                        {
                            Name = "inline",
                            Transport = "http",
                            Endpoint = new Uri("https://inline.example/mcp")
                        }
                    ],
                    Manifests =
                    [
                        new ResolvedMcpManifestReference
                        {
                            Path = manifestPath,
                            SourceName = "shared"
                        }
                    ]
                },
                new McpConfigurationActivationContext { Builder = new AgentBuilder() });

            Assert.Collection(
                registrations,
                registration => Assert.Equal("mcp.inline:test-agent", registration.Factory.Id.Value),
                registration => Assert.Equal($"mcp.file:{manifestPath}", registration.Factory.Id.Value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StaleYamlFields_FailStrictly()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            HpdAgentConfigSerializer.Deserialize(
                """
                mcp:
                  servers:
                    - name: inline
                      transport: http
                      endpoint: https://inline.example/mcp
                      unsupported: true
                """,
                HpdConfigFormat.Yaml));
    }

    [Fact]
    public void FileBackedConfiguration_CapturesBaseDirectoryAndSourcePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "agent.yaml");
        File.WriteAllText(
            path,
            """
            mcp:
              manifests:
                - path: ./mcp.json
            """);

        try
        {
            var config = HpdAgentConfigSerializer.ReadFile(path)!;
            Assert.Equal(directory, config.SourceDirectory);
            Assert.Equal("mcp.manifests[0]", config.Mcp!.Manifests[0].SourcePath);
            Assert.NotNull(config.Mcp.Manifests[0].SourceLocations);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AgentBuilder_ActivatesInlineMcpConfiguration()
    {
        var config = new AgentConfig
        {
            EventComposition = TestEventComposition.Current,
            Mcp = new AgentMcpConfig
            {
                Servers =
                [
                    new McpServerConfig
                    {
                        Name = "disabled-inline",
                        Transport = "http",
                        Endpoint = new Uri("https://inline.example/mcp"),
                        Enabled = false
                    }
                ]
            }
        };

        await using var agent = await new AgentBuilder(config)
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .BuildAsync();

        var configuredMcp = Assert.IsType<AgentMcpConfig>(agent.Config.Mcp);
        Assert.Equal("disabled-inline", Assert.Single(configuredMcp.Servers).Name);
    }

    [Fact]
    public async Task RelativeManifestWithoutSourceDirectory_FailsClosed()
    {
        var config = new AgentConfig
        {
            EventComposition = TestEventComposition.Current,
            Mcp = new AgentMcpConfig
            {
                Manifests = [new McpManifestReference { Path = "./mcp.json" }]
            }
        };
        var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(config)
                .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
                .BuildAsync());

        Assert.Equal("McpManifestPathUnresolved", error.Code);
    }

    [Fact]
    public async Task RelativeManifest_WithHostSourceDirectory_Resolves()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-host-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "mcp.json"),
            """
            {
              "servers": [
                {
                  "name": "disabled",
                  "transport": "http",
                  "endpoint": "https://disabled.example/mcp",
                  "enabled": false
                }
              ]
            }
            """);

        try
        {
            var config = new AgentConfig
            {
                EventComposition = TestEventComposition.Current,
                SourceDirectory = directory,
                Mcp = new AgentMcpConfig
                {
                    Manifests = [new McpManifestReference { Path = "./mcp.json" }]
                }
            };

            await using var agent = await new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .BuildAsync();

            Assert.Single(agent.Config.Mcp!.Manifests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingManifestPath_FailsWithConfigurationDiagnostic()
    {
        var config = HpdAgentConfigSerializer.Deserialize(
            """
            mcp:
              manifests:
                - sourceName: missing-path
            """,
            HpdConfigFormat.Yaml)!;
        config.EventComposition = TestEventComposition.Current;

        var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .BuildAsync());

        Assert.Equal("McpManifestPathRequired", error.Code);
        Assert.Equal("mcp.manifests[0].path", error.Path);
        Assert.NotNull(error.LineNumber);
    }

    [Fact]
    public async Task AgentBuilder_LoadsAndInvokesInlineAndManifestServers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-e2e-{Guid.NewGuid():N}");
        var configDirectory = Path.Combine(directory, "config");
        Directory.CreateDirectory(configDirectory);
        var agentConfigPath = Path.Combine(directory, "agent.yaml");
        File.WriteAllText(
            agentConfigPath,
            """
            name: DeclarativeMcpAgent
            mcp:
              servers:
                - name: inline
                  transport: http
                  endpoint: https://inline.test/mcp
              manifests:
                - path: ./config/mcp.json
                  sourceName: external
            """);
        File.WriteAllText(
            Path.Combine(configDirectory, "mcp.json"),
            """
            {
              "servers": [
                {
                  "name": "external",
                  "transport": "http",
                  "endpoint": "https://external.test/mcp"
                }
              ]
            }
            """);

        try
        {
            var config = HpdAgentConfigSerializer.ReadFile(agentConfigPath)!;
            config.EventComposition = TestEventComposition.Current;
            var handler = new DeclarativeMcpHandler();
            using var inlineHttp = new HttpClient(handler, disposeHandler: false);
            using var externalHttp = new HttpClient(handler, disposeHandler: false);
            var chat = new FakeChatClient();
            config.Clients.Chat ??= new ChatClientConfig();
            config.Clients.Chat.Override =
                ClientOverride<IChatClient>.Borrow(chat, "test", "local", "test.chat");
            chat.EnqueueToolCalls(
                ("inline_echo", "call-inline", new Dictionary<string, object?>()),
                ("external_echo", "call-external", new Dictionary<string, object?>()));
            chat.EnqueueTextResponse("Both MCP tools completed.");

            await using (var agent = await new AgentBuilder(config)
                .WithChatClient(chat)
                .WithMcpOptions(options =>
                    options.HttpClientFactory = server => server.Name == "inline"
                        ? inlineHttp
                        : externalHttp)
                .BuildAsync())
            {
                var result = await agent.RunAsync("Invoke both MCP servers.");

                Assert.Equal("Both MCP tools completed.", result.Text);
                Assert.Contains("https://inline.test/mcp:inline_echo", handler.InvokedTools);
                Assert.Contains("https://external.test/mcp:external_echo", handler.InvokedTools);
                Assert.Equal(2, handler.ToolsListRequests);
                Assert.All(handler.ToolCallBodies, body =>
                {
                    Assert.DoesNotContain("__raw_json__", body, StringComparison.Ordinal);
                    Assert.DoesNotContain("__json_serializer_options__", body, StringComparison.Ordinal);
                    Assert.DoesNotContain("__hpd_bound_arguments__", body, StringComparison.Ordinal);
                });
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateServerAcrossInlineAndManifest_FailsBeforeSourceLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-duplicate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "mcp.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "servers": [
                {
                  "name": "shared",
                  "transport": "http",
                  "endpoint": "https://file.test/mcp"
                }
              ]
            }
            """);

        try
        {
            var config = new AgentConfig
            {
                EventComposition = TestEventComposition.Current,
                Mcp = new AgentMcpConfig
                {
                    Servers =
                    [
                        new McpServerConfig
                        {
                            Name = "shared",
                            Transport = "http",
                            Endpoint = new Uri("https://inline.test/mcp")
                        }
                    ],
                    Manifests = [new McpManifestReference { Path = manifestPath }]
                }
            };

            var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
                new AgentBuilder(config)
                    .WithChatClient(new FakeChatClient())
                    .BuildAsync());

            Assert.Equal("McpServerNameConflict", error.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateModelFacingToolName_FailsWithMcpDiagnostic()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-mcp-tool-conflict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "mcp.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "servers": [
                {
                  "name": "external",
                  "transport": "http",
                  "endpoint": "https://external.test/mcp"
                }
              ]
            }
            """);
        var handler = new DeclarativeMcpHandler(sameToolName: true);
        using var inlineHttp = new HttpClient(handler, disposeHandler: false);
        using var externalHttp = new HttpClient(handler, disposeHandler: false);

        try
        {
            var config = new AgentConfig
            {
                EventComposition = TestEventComposition.Current,
                Mcp = new AgentMcpConfig
                {
                    Servers =
                    [
                        new McpServerConfig
                        {
                            Name = "inline",
                            Transport = "http",
                            Endpoint = new Uri("https://inline.test/mcp")
                        }
                    ],
                    Manifests = [new McpManifestReference { Path = manifestPath }]
                }
            };

            var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
                new AgentBuilder(config)
                    .WithChatClient(new FakeChatClient())
                    .WithMcpOptions(options =>
                        options.HttpClientFactory = server => server.Name == "inline"
                            ? inlineHttp
                            : externalHttp)
                    .BuildAsync());

            Assert.Equal("McpToolNameConflict", error.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidInlineServer_ReportsYamlSourceLocation()
    {
        var config = HpdAgentConfigSerializer.Deserialize(
            """
            mcp:
              servers:
                - name: invalid
                  transport: http
                  endpoint: relative
            """,
            HpdConfigFormat.Yaml)!;
        config.EventComposition = TestEventComposition.Current;

        var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .BuildAsync());

        Assert.Equal("McpInlineServerInvalid", error.Code);
        Assert.Equal("mcp.servers[0]", error.Path);
        Assert.NotNull(error.LineNumber);
        Assert.NotNull(error.ColumnNumber);
    }

    private sealed class DeclarativeMcpHandler(bool sameToolName = false) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<string> _invokedTools = [];
        private readonly List<string> _toolCallBodies = [];
        private int _toolsListRequests;

        internal IReadOnlyList<string> InvokedTools
        {
            get { lock (_gate) return _invokedTools.ToArray(); }
        }

        internal int ToolsListRequests => Volatile.Read(ref _toolsListRequests);
        internal IReadOnlyList<string> ToolCallBodies
        {
            get { lock (_gate) return _toolCallBodies.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            var serverName = request.RequestUri!.Host == "inline.test" ? "inline" : "external";
            var toolName = sameToolName ? "echo" : serverName + "_echo";

            return method switch
            {
                "server/discover" => JsonResponse(
                    root,
                    """
                    {"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"__SERVER__","version":"1"}},"ttlMs":0,"cacheScope":"private"}
                    """.Replace("__SERVER__", serverName, StringComparison.Ordinal)),
                "tools/list" => ListTools(root, toolName),
                "tools/call" => InvokeTool(root, request.RequestUri, toolName, body),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{method}'.")
            };
        }

        private HttpResponseMessage ListTools(JsonElement root, string toolName)
        {
            Interlocked.Increment(ref _toolsListRequests);
            return JsonResponse(
                root,
                """
                {"resultType":"complete","tools":[{"name":"__TOOL__","description":"__TOOL__","inputSchema":{"type":"object","properties":{}}}],"ttlMs":0,"cacheScope":"private"}
                """.Replace("__TOOL__", toolName, StringComparison.Ordinal));
        }

        private HttpResponseMessage InvokeTool(
            JsonElement root,
            Uri endpoint,
            string expectedTool,
            string body)
        {
            var actualTool = root.GetProperty("params").GetProperty("name").GetString();
            Assert.Equal(expectedTool, actualTool);
            lock (_gate)
            {
                _invokedTools.Add($"{endpoint}:{actualTool}");
                _toolCallBodies.Add(body);
            }
            return JsonResponse(
                root,
                """
                {"resultType":"complete","content":[{"type":"text","text":"__TOOL__ result"}]}
                """.Replace("__TOOL__", actualTool, StringComparison.Ordinal));
        }

        private static HttpResponseMessage JsonResponse(JsonElement request, string result)
        {
            var id = request.GetProperty("id").GetRawText();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"jsonrpc":"2.0","id":__ID__,"result":__RESULT__}"""
                        .Replace("__ID__", id, StringComparison.Ordinal)
                        .Replace("__RESULT__", result, StringComparison.Ordinal),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
