// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ClientTools;

namespace HPD.Agent.Tests.ClientTools;

public sealed class ClientToolProviderContractsTests
{
    [Fact]
    public void ClientAppProviderReference_DeserializesFromString()
    {
        var reference = JsonSerializer.Deserialize(
            "\"penpot\"",
            HPDJsonContext.Default.ClientAppProviderReference);

        reference.Should().NotBeNull();
        reference!.Name.Should().Be("penpot");
        reference.BindingPolicy.Should().Be(ClientAppProviderBindingPolicy.Exclusive);
    }

    [Fact]
    public void ClientAppProviderReference_DeserializesFromObject()
    {
        const string json = """
            {
              "name": "code-server",
              "providerSelector": {
                "workspaceId": "current"
              },
              "harnesses": [
                "editor",
                {
                  "name": "terminal",
                  "tools": ["run_task", "read_terminal"],
                  "expanded": true
                }
              ],
              "bindingPolicy": "Optional"
            }
            """;

        var reference = JsonSerializer.Deserialize(
            json,
            HPDJsonContext.Default.ClientAppProviderReference);

        reference.Should().NotBeNull();
        reference!.Name.Should().Be("code-server");
        reference.ProviderSelector!.WorkspaceId.Should().Be("current");
        reference.Harnesses.Should().HaveCount(2);
        reference.Harnesses![0].Name.Should().Be("editor");
        reference.Harnesses[1].Name.Should().Be("terminal");
        reference.Harnesses[1].Tools.Should().Equal("run_task", "read_terminal");
        reference.Harnesses[1].Expanded.Should().BeTrue();
        reference.BindingPolicy.Should().Be(ClientAppProviderBindingPolicy.Optional);
    }

    [Fact]
    public async Task InMemoryClientToolProviderRegistry_TracksManifestHeartbeatAndDisconnect()
    {
        var registry = new InMemoryClientToolProviderRegistry();
        var identity = new ClientToolProviderIdentity
        {
            ProviderName = "penpot-plugin",
            AppKind = "penpot",
            InstanceId = "tab-1"
        };

        var connection = new TestProviderConnection(registry);
        var registration = await registry.RegisterConnectionAsync(identity, null, connection);
        connection.Attach(registration.ClientRuntimeId, registration.ConnectionId);

        registration.ClientRuntimeId.Should().StartWith("crt_penpot_");
        registration.ConnectionId.Should().StartWith("cpc_");

        var manifest = new ClientToolProviderManifest
        {
            Identity = identity,
            AppProvider = new ClientAppProviderDescriptor { Name = "penpot" },
            Readiness = ClientToolProviderReadiness.Ready,
            ClientToolHarnesses =
            [
                new clientToolHarnessDefinition(
                    "selection",
                    "Selection tools.",
                    [new ClientToolDefinition
                    {
                        Name = "penpot_get_selection",
                        Description = "Gets the current selection.",
                        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement
                    }],
                    StartCollapsed: false)
            ]
        };

        await registry.UpdateManifestAsync(
            registration.ClientRuntimeId,
            registration.ConnectionId,
            manifest);

        registry.TryGet(registration.ClientRuntimeId, out var snapshot).Should().BeTrue();
        snapshot.State.Should().Be(ClientToolProviderConnectionState.Ready);
        snapshot.Manifest!.ClientToolHarnesses.Should().ContainSingle();

        var binding = await registry.TryAcquireBindingAsync(
            new ClientAppProviderReference { Name = "penpot" },
            new ClientToolProviderBindingScope { AgentId = "agent", SessionId = "session", ThreadId = "thread" });
        binding.Should().NotBeNull();
        binding!.Lease.BindingId.Should().StartWith("bind_");
        binding.Provider.State.Should().Be(ClientToolProviderConnectionState.Bound);

        var sameBinding = await registry.TryAcquireBindingAsync(
            new ClientAppProviderReference { Name = "penpot" },
            new ClientToolProviderBindingScope { AgentId = "agent", SessionId = "session", ThreadId = "thread" });
        sameBinding!.Lease.BindingId.Should().Be(binding.Lease.BindingId);

        var conflictingBinding = await registry.TryAcquireBindingAsync(
            new ClientAppProviderReference { Name = "penpot" },
            new ClientToolProviderBindingScope { AgentId = "other", SessionId = "other-session", ThreadId = "thread" });
        conflictingBinding.Should().BeNull();

        await registry.RecordHeartbeatAsync(registration.ClientRuntimeId, registration.ConnectionId);
        registry.TryGet(registration.ClientRuntimeId, out var heartbeatSnapshot).Should().BeTrue();
        heartbeatSnapshot.LastHeartbeatAt.Should().NotBeNull();

        await registry.DisconnectAsync(registration.ClientRuntimeId, registration.ConnectionId);

        registry.TryGet(registration.ClientRuntimeId, out var disconnected).Should().BeTrue();
        disconnected.State.Should().Be(ClientToolProviderConnectionState.Disconnected);
        disconnected.BindingLease!.Status.Should().Be(ClientToolProviderBindingLeaseStatus.Disconnected);
        registry.List().Should().BeEmpty();
        registry.List(new ClientToolProviderQuery { IncludeDisconnected = true }).Should().ContainSingle();
    }

    [Fact]
    public async Task BindingRejectsAmbiguousTabsAndHonorsBrowserLaunchSelection()
    {
        var registry = new InMemoryClientToolProviderRegistry();
        var scope = new ClientToolProviderBindingScope
        {
            AgentId = "agent",
            SessionId = "session",
            ThreadId = "thread"
        };

        foreach (string launchId in new[] { "launch-1", "launch-2" })
        {
            var identity = new ClientToolProviderIdentity
            {
                ProviderName = "penpot-frontend",
                AppKind = "design-editor",
                InstanceId = $"runtime-{launchId}"
            };
            var connection = new TestProviderConnection(registry);
            ClientToolProviderConnectionRegistration registration =
                await registry.RegisterConnectionAsync(
                    identity,
                    RuntimeIdentity(launchId),
                    connection);
            connection.Attach(
                registration.ClientRuntimeId,
                registration.ConnectionId);
            await registry.UpdateManifestAsync(
                registration.ClientRuntimeId,
                registration.ConnectionId,
                Manifest(identity));
        }

        ClientAppProviderReference ambiguous =
            new() { Name = "penpot" };
        (await registry.TryAcquireBindingAsync(ambiguous, scope))
            .Should().BeNull();

        ClientAppProviderReference selected = new()
        {
            Name = "penpot",
            ProviderSelector = new ClientProviderSelector
            {
                AppInstallationId = "installation-1",
                BrowserLaunchSessionId = "launch-2"
            }
        };
        ClientToolProviderBindingResult? binding =
            await registry.TryAcquireBindingAsync(selected, scope);
        binding.Should().NotBeNull();
        binding!.Provider.RuntimeIdentity!.BrowserLaunchSessionId
            .Should().Be("launch-2");

        ClientToolProviderBindingResult? inherited =
            await registry.TryAcquireBindingAsync(ambiguous, scope);
        inherited!.Lease.BindingId.Should().Be(binding.Lease.BindingId);
    }

    [Fact]
    public async Task RevocationFenceRejectsAuthorizedRegistrationAndClosesConnectedProvider()
    {
        var registry = new InMemoryClientToolProviderRegistry();
        var identity = new ClientToolProviderIdentity
        {
            ProviderName = "penpot-frontend",
            AppKind = "design-editor",
            InstanceId = "runtime-1"
        };
        var connected = new TestProviderConnection(registry);
        ClientToolProviderRuntimeIdentity generationFive =
            RuntimeIdentity("launch-5") with
            {
                WorkloadGeneration = 5
            };
        ClientToolProviderConnectionRegistration registration =
            await registry.RegisterConnectionAsync(
                identity,
                generationFive,
                connected);

        await registry.AdvanceRevocationFenceAsync(
            new ClientToolProviderRevocationFence(
                "installation-1",
                6));
        await registry.RevokeAsync(
            new ClientProviderSelector
            {
                AppInstallationId = "installation-1"
            },
            "App stopped.");

        connected.CloseReasons.Should().ContainSingle("App stopped.");
        registry.TryGet(registration.ClientRuntimeId, out var revoked)
            .Should().BeTrue();
        revoked.State.Should()
            .Be(ClientToolProviderConnectionState.Revoked);

        var authorizedBeforeStop =
            new TestProviderConnection(registry);
        Func<Task> commit = async () =>
            await registry.RegisterConnectionAsync(
                identity,
                RuntimeIdentity("launch-6") with
                {
                    WorkloadGeneration = 6
                },
                authorizedBeforeStop);
        await commit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*revoked before it committed*");

        var afterStop = new TestProviderConnection(registry);
        ClientToolProviderConnectionRegistration next =
            await registry.RegisterConnectionAsync(
                identity,
                RuntimeIdentity("launch-7") with
                {
                    WorkloadGeneration = 7
                },
                afterStop);
        next.ConnectionId.Should().StartWith("cpc_");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationAttemptsEverySocketCloseAfterCleanupCancellation(
        bool cancelBeforeTransition)
    {
        var registry = new InMemoryClientToolProviderRegistry();
        using var cleanup = new CancellationTokenSource();
        var first = new TestProviderConnection(
            registry,
            onClose: cleanup.Cancel);
        var second = new TestProviderConnection(
            registry,
            onClose: cleanup.Cancel);

        await registry.RegisterConnectionAsync(
            new ClientToolProviderIdentity
            {
                ProviderName = "penpot-frontend",
                AppKind = "design-editor",
                InstanceId = "runtime-close-1"
            },
            RuntimeIdentity("launch-close-1"),
            first);
        await registry.RegisterConnectionAsync(
            new ClientToolProviderIdentity
            {
                ProviderName = "penpot-frontend",
                AppKind = "design-editor",
                InstanceId = "runtime-close-2"
            },
            RuntimeIdentity("launch-close-2"),
            second);
        if (cancelBeforeTransition)
            cleanup.Cancel();

        int revoked = await registry.RevokeAsync(
            new ClientProviderSelector
            {
                AppInstallationId = "installation-1"
            },
            "App stopped.",
            cleanup.Token);

        revoked.Should().Be(2);
        first.CloseReasons.Should().ContainSingle("App stopped.");
        second.CloseReasons.Should().ContainSingle("App stopped.");
    }

    [Fact]
    public async Task InMemoryClientToolProviderRegistry_InvokeToolWaitsForProviderOutcome()
    {
        var registry = new InMemoryClientToolProviderRegistry();
        var identity = new ClientToolProviderIdentity
        {
            ProviderName = "code-server-extension",
            AppKind = "code-server",
            InstanceId = "workspace-1"
        };
        var connection = new TestProviderConnection(registry);
        var registration = await registry.RegisterConnectionAsync(identity, null, connection);
        connection.Attach(registration.ClientRuntimeId, registration.ConnectionId);

        await registry.UpdateManifestAsync(
            registration.ClientRuntimeId,
            registration.ConnectionId,
            new ClientToolProviderManifest
            {
                Identity = identity,
                AppProvider = new ClientAppProviderDescriptor { Name = "code-server" },
                Readiness = ClientToolProviderReadiness.Ready,
                ClientToolHarnesses =
                [
                    new clientToolHarnessDefinition(
                        "editor",
                        "Editor tools.",
                        [new ClientToolDefinition
                        {
                            Name = "get_selected_text",
                            Description = "Gets selected text.",
                            ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement
                        }],
                        StartCollapsed: false)
                ]
            });

        var binding = await registry.TryAcquireBindingAsync(
            new ClientAppProviderReference { Name = "code-server" },
            new ClientToolProviderBindingScope { AgentId = "agent", SessionId = "session", ThreadId = "thread" });
        binding.Should().NotBeNull();

        var outcome = await registry.InvokeToolAsync(
            new ClientToolProviderInvocationRequest
            {
                RequestId = "req_1",
                CallId = "call_1",
                Arguments = new Dictionary<string, object?>(),
                ResolvedInvocationMode = AgentInvocationMode.Synchronous,
                Binding = new ClientToolProviderToolBinding
                {
                    BindingId = binding!.Lease.BindingId,
                    ClientRuntimeId = registration.ClientRuntimeId,
                    ConnectionId = registration.ConnectionId,
                    AppProviderName = "code-server",
                    HarnessName = "editor",
                    ProviderToolName = "get_selected_text",
                    VisibleToolName = "code_server_editor_get_selected_text"
                }
            },
            TimeSpan.FromSeconds(5));

        outcome.Outcome.Should().Be(ClientToolInvokeOutcomeKind.Completed);
        connection.LastInvocation!.BindingId.Should().Be(binding.Lease.BindingId);
        connection.LastInvocation!.ToolName.Should().Be("get_selected_text");
        connection.LastInvocation.VisibleToolName.Should().Be("code_server_editor_get_selected_text");
    }

    [Fact]
    public async Task InMemoryClientToolProviderRegistry_PreservesStructuredBackgroundError()
    {
        var registry = new InMemoryClientToolProviderRegistry();
        var identity = new ClientToolProviderIdentity
        {
            ProviderName = "code-server-extension",
            AppKind = "code-server",
            InstanceId = "workspace-1"
        };
        var connection = new TestProviderConnection(registry);
        var registration = await registry.RegisterConnectionAsync(identity, null, connection);
        connection.Attach(registration.ClientRuntimeId, registration.ConnectionId);

        await registry.UpdateManifestAsync(
            registration.ClientRuntimeId,
            registration.ConnectionId,
            new ClientToolProviderManifest
            {
                Identity = identity,
                AppProvider = new ClientAppProviderDescriptor { Name = "code-server" },
                Readiness = ClientToolProviderReadiness.Ready,
                ClientToolHarnesses =
                [
                    new clientToolHarnessDefinition(
                        "export",
                        "Export tools.",
                        [new ClientToolDefinition
                        {
                            Name = "export_selection",
                            Description = "Exports selected content.",
                            ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement
                        }],
                        StartCollapsed: false)
                ]
            });

        var binding = await registry.TryAcquireBindingAsync(
            new ClientAppProviderReference { Name = "code-server" },
            new ClientToolProviderBindingScope { AgentId = "agent", SessionId = "session", ThreadId = "thread" });
        binding.Should().NotBeNull();

        var operation = registry.RegisterBackgroundOperation(
            new ClientToolProviderBackgroundOperationDescriptor
            {
                Binding = new ClientToolProviderToolBinding
                {
                    BindingId = binding!.Lease.BindingId,
                    ClientRuntimeId = registration.ClientRuntimeId,
                    ConnectionId = registration.ConnectionId,
                    AppProviderName = "code-server",
                    HarnessName = "export",
                    ProviderToolName = "export_selection",
                    VisibleToolName = "code_server_export_export_selection"
                },
                ClientOperationId = "op_1",
                ToolName = "code_server_export_export_selection",
                RequestId = "req_1",
                CallId = "call_1",
                SessionId = "session",
                ThreadId = "thread"
            });

        registry.TryResolveBackgroundOperationOutcome(
            registration.ClientRuntimeId,
            registration.ConnectionId,
            new ClientToolProviderBackgroundOperationOutcomeMessage
            {
                BindingId = binding.Lease.BindingId,
                ClientOperationId = "op_1",
                State = ClientToolBackgroundOperationOutcomeState.Faulted,
                Error = new ClientToolError
                {
                    Kind = "stale_context",
                    Message = "Document context changed.",
                    Retryable = true,
                    CurrentContext = new ClientToolProviderContext
                    {
                        DocumentId = "doc-2",
                        AppStateVersion = "43"
                    },
                    Metadata = new Dictionary<string, object?>
                    {
                        ["expectedRevision"] = "42"
                    }
                },
                Metadata = new Dictionary<string, string> { ["artifactId"] = "file_1" }
            }).Should().BeTrue();

        var result = await operation.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        result.State.Should().Be(ClientToolBackgroundOperationOutcomeState.Faulted);
        result.Error.Should().NotBeNull();
        result.Error!.Kind.Should().Be("stale_context");
        result.Error.Retryable.Should().BeTrue();
        result.Error.CurrentContext!.DocumentId.Should().Be("doc-2");
        result.Error.Metadata.Should().Contain("expectedRevision", "42");
        result.Metadata.Should().Contain("artifactId", "file_1");
    }

    private static ClientToolProviderManifest Manifest(
        ClientToolProviderIdentity identity) =>
        new()
        {
            Identity = identity,
            AppProvider = new ClientAppProviderDescriptor
            {
                Name = "penpot"
            },
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
                            Description = "Inspects the design.",
                            ParametersSchema = JsonDocument.Parse(
                                """{"type":"object","properties":{},"additionalProperties":false}""")
                                .RootElement
                        }
                    ],
                    StartCollapsed: false)
            ]
        };

    private static ClientToolProviderRuntimeIdentity RuntimeIdentity(
        string browserLaunchSessionId) =>
        new()
        {
            AppId = "io.penpot.penpot",
            AppRevision = "revision-1",
            InstallationId = "installation-1",
            WorkloadId = "frontend",
            WorkloadGeneration = 1,
            EndpointId = "web",
            PublicationId = "publication-1",
            PublicationGeneration = 1,
            LaunchSurfaceId = "workspace",
            BrowserLaunchSessionId = browserLaunchSessionId,
            BrowserLaunchSessionGeneration = 1,
            ProviderConnectionGeneration = 1,
            Origin = "https://penpot.example"
        };

    private sealed class TestProviderConnection : IClientToolProviderConnection
    {
        private readonly IClientToolProviderRegistry _registry;
        private readonly Action? _onClose;
        private string? _clientRuntimeId;
        private string? _connectionId;

        public TestProviderConnection(
            IClientToolProviderRegistry registry,
            Action? onClose = null)
        {
            _registry = registry;
            _onClose = onClose;
        }

        public ClientToolProviderInvokeToolMessage? LastInvocation { get; private set; }
        public List<string> CloseReasons { get; } = [];

        public void Attach(string clientRuntimeId, string connectionId)
        {
            _clientRuntimeId = clientRuntimeId;
            _connectionId = connectionId;
        }

        public ValueTask SendInvocationAsync(
            ClientToolProviderInvokeToolMessage message,
            CancellationToken cancellationToken)
        {
            LastInvocation = message;
            _registry.TryResolveInvocationOutcome(
                _clientRuntimeId!,
                _connectionId!,
                new ClientToolProviderInvokeOutcomeMessage
                {
                    BindingId = message.BindingId,
                    InvocationId = message.InvocationId,
                    RequestId = message.RequestId,
                    Outcome = ClientToolInvokeOutcomeKind.Completed,
                    Content = [new TextContent("selected text")]
                });
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(
            string reason,
            CancellationToken cancellationToken = default)
        {
            CloseReasons.Add(reason);
            _onClose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
