using System.Net.WebSockets;
using System.Text.Json;
using HPD.Agent.ClientTools;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

public sealed class ClientToolProviderWebSocketTests
{
    [Fact]
    public async Task ProtocolV2ProviderCanRegisterManifest()
    {
        using var factory = new TestWebApplicationFactory();
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/client-tool-providers/connect"),
            CancellationToken.None);

        await SendAsync(
            socket,
            new ClientToolProviderHelloMessage
            {
                Identity = new ClientToolProviderIdentity
                {
                    ProviderName = "browser-test",
                    AppKind = "design-editor",
                    InstanceId = "test-tab"
                }
            },
            HPDJsonContext.Default.ClientToolProviderHelloMessage);

        var welcome = await ReceiveAsync(
            socket,
            HPDJsonContext.Default.ClientToolProviderWelcomeMessage);

        await SendAsync(
            socket,
            new ClientToolProviderManifestMessage
            {
                AppProvider = new ClientAppProviderDescriptor { Name = "penpot" },
                Readiness = ClientToolProviderReadiness.Ready,
                ClientToolHarnesses =
                [
                    new clientToolHarnessDefinition(
                        "design",
                        "Design tools.",
                        [
                            new ClientToolDefinition
                            {
                                Name = "inspect",
                                Description = "Inspects the active design.",
                                ParametersSchema = JsonDocument.Parse(
                                    """{"type":"object","properties":{},"additionalProperties":false}""")
                                    .RootElement
                            }
                        ],
                        StartCollapsed: false)
                ]
            },
            HPDJsonContext.Default.ClientToolProviderManifestMessage);

        var registry = factory.Server.Services.GetRequiredService<IClientToolProviderRegistry>();
        await WaitUntilAsync(() =>
            registry.TryGet(welcome.ClientRuntimeId, out var provider)
            && provider.State == ClientToolProviderConnectionState.Ready);

        Assert.True(registry.TryGet(welcome.ClientRuntimeId, out var snapshot));
        var harness = Assert.Single(snapshot.Manifest!.ClientToolHarnesses);
        var tool = Assert.Single(harness.Tools);
        Assert.Equal("inspect", tool.Name);
    }

    [Fact]
    public async Task AuthorizedConnectionUsesServerRuntimeIdentity()
    {
        var runtimeIdentity = CreateRuntimeIdentity();
        using var factory = new TestWebApplicationFactory(services =>
            services.AddSingleton<IClientToolProviderConnectionAuthorizer>(
                new AllowAuthorizer(runtimeIdentity)));
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/authorized-client-tool-providers/connect"),
            CancellationToken.None);

        await SendAsync(
            socket,
            new ClientToolProviderHelloMessage
            {
                Identity = new ClientToolProviderIdentity
                {
                    ProviderName = "penpot-frontend",
                    AppKind = "design-editor",
                    InstanceId = "untrusted-tab-id",
                    InstallationId = "spoofed-installation"
                }
            },
            HPDJsonContext.Default.ClientToolProviderHelloMessage);

        var welcome = await ReceiveAsync(
            socket,
            HPDJsonContext.Default.ClientToolProviderWelcomeMessage);
        var registry = factory.Server.Services
            .GetRequiredService<IClientToolProviderRegistry>();

        Assert.True(registry.TryGet(welcome.ClientRuntimeId, out var snapshot));
        Assert.Equal(runtimeIdentity, snapshot.RuntimeIdentity);
        Assert.Contains("installation-1", welcome.ClientRuntimeId);
        Assert.DoesNotContain("spoofed", welcome.ClientRuntimeId);
    }

    [Fact]
    public async Task AuthorizedConnectionRejectsProviderIdentityMismatch()
    {
        using var factory = new TestWebApplicationFactory(services =>
            services.AddSingleton<IClientToolProviderConnectionAuthorizer>(
                new AllowAuthorizer(CreateRuntimeIdentity())));
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/authorized-client-tool-providers/connect"),
            CancellationToken.None);

        await SendAsync(
            socket,
            new ClientToolProviderHelloMessage
            {
                Identity = new ClientToolProviderIdentity
                {
                    ProviderName = "malicious-provider",
                    AppKind = "design-editor",
                    InstanceId = "malicious-tab"
                }
            },
            HPDJsonContext.Default.ClientToolProviderHelloMessage);

        var error = await ReceiveAsync(
            socket,
            HPDJsonContext.Default.ClientToolProviderErrorMessage);
        Assert.Equal("provider_identity_mismatch", error.Code);
        Assert.Empty(factory.Server.Services
            .GetRequiredService<IClientToolProviderRegistry>()
            .List());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static async Task SendAsync<T>(
        WebSocket socket,
        T message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private static async Task<T> ReceiveAsync<T>(
        WebSocket socket,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        payload.Position = 0;
        return (await JsonSerializer.DeserializeAsync(payload, typeInfo))!;
    }

    private static ClientToolProviderRuntimeIdentity CreateRuntimeIdentity() =>
        new()
        {
            AppId = "penpot",
            AppRevision = "revision-1",
            InstallationId = "installation-1",
            WorkloadId = "frontend",
            WorkloadGeneration = 4,
            EndpointId = "web",
            PublicationId = "publication-1",
            PublicationGeneration = 7,
            LaunchSurfaceId = "workspace",
            BrowserLaunchSessionId = "browser-launch-1",
            BrowserLaunchSessionGeneration = 2,
            ProviderConnectionGeneration = 1,
            Origin = "https://penpot.example"
        };

    private sealed class AllowAuthorizer(
        ClientToolProviderRuntimeIdentity runtimeIdentity) :
        IClientToolProviderConnectionAuthorizer
    {
        public ValueTask<ClientToolProviderConnectionAuthorization?> AuthorizeAsync(
            HttpContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ClientToolProviderConnectionAuthorization?>(
                new ClientToolProviderConnectionAuthorization
                {
                    RuntimeIdentity = runtimeIdentity,
                    ExpectedProviderName = "penpot-frontend",
                    ExpectedAppKind = "design-editor"
                });
    }
}
