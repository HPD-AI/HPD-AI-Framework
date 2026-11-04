using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;
using EnvCapabilityId = HPD.Environment.Contracts.CapabilityId;

namespace HPD.Agent.Sandbox.Tests.Composition;

public sealed class EnvironmentSandboxBackendFactoryTests
{
    [Fact]
    public async Task Composer_creates_one_session_from_environment_resources()
    {
        await using var factory = CreateFactory();

        await using ISandboxSession session =
            await factory.CreateSessionAsync(ProcessOnlyRequest());

        Assert.Equal(SandboxBackendKind.HostProcess, session.Backend);
        Assert.Equal(
            HostProcessTestModule.ProviderId,
            session.Execution.ProcessProvider.ProviderId);
        Assert.Equal(
            ProviderId,
            session.Execution.ExecutionTarget.Route.ProviderId);
        Assert.Empty(session.Workspace.Roots);
        Assert.Single(
            await ((InMemoryEnvironmentRuntime)factory.Runtime!)
                .ListExecutionUnitsAsync());

        await session.DisposeAsync();

        Assert.Empty(
            await ((InMemoryEnvironmentRuntime)factory.Runtime!)
                .ListExecutionUnitsAsync());
    }

    [Fact]
    public async Task Unsupported_workspace_request_fails_before_resource_creation()
    {
        await using var factory = CreateFactory();
        var rootId = new WorkspaceRootId("workspace");
        SandboxSessionRequest request = ProcessOnlyRequest() with
        {
            Workspaces =
            [
                new SandboxWorkspaceRequest
                {
                    Source = new ContentSelector
                    {
                        Kind = ContentSelectorKind.HostPath,
                        HostPath = new HostPathSelection(
                            new HostPath("/workspace"),
                            HostPathKind.Directory,
                            RequireExists: false),
                    },
                    Root = new WorkspaceRootBinding
                    {
                        Id = rootId,
                        DisplayName = "Workspace",
                        DefaultExecutionPath =
                            new WorkspacePath(rootId, string.Empty),
                        Content =
                            new WorkspaceContentIdentity("workspace-content"),
                        PathSemantics =
                            new WorkspacePathSemantics(
                                true,
                                SymlinkEvaluationPolicy.DenySymlinks),
                    },
                    ExecutionPath =
                        new WorkspaceExecutionPath("/workspace"),
                },
            ],
        };

        await Assert.ThrowsAsync<SandboxCapabilityAdmissionException>(() =>
            factory.CreateSessionAsync(request).AsTask());
        Assert.Empty(
            await ((InMemoryEnvironmentRuntime)factory.Runtime!)
                .ListExecutionUnitsAsync());
    }

    private static ProviderId ProviderId =>
        HostProcessTestModule.ProviderId;

    private static SandboxSessionRequest ProcessOnlyRequest() =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Disabled,
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Unrestricted,
                    RequireProxyMediation = false,
                },
                Interactive =
                    ProcessInteractivePolicy.Default with
                    {
                        AllowStdin = false,
                    },
                Environment = new EnvironmentAccessPolicy
                {
                    StripUnlistedVariables = false,
                },
                Violations =
                    ProcessViolationPolicy.Default with
                    {
                        Action = ProcessViolationAction.ObserveOnly,
                    },
            },
        };

    private static EnvironmentSandboxBackendFactory CreateFactory() =>
        new(
            new EnvironmentSandboxBackendDefinition
            {
                Kind = SandboxBackendKind.HostProcess,
                Module = new HostProcessTestModule(),
                EnforcementLocation =
                    SandboxEnforcementLocation.Provider,
                HostSpecFactory = _ =>
                    new RuntimeHostSpec
                    {
                        PreferredProvider = ProviderId,
                        Platform =
                            new PlatformSpec("test", "test"),
                    },
            });

    private sealed class HostProcessTestModule : IProviderModule
    {
        public static ProviderId ProviderId { get; } =
            InMemoryEnvironmentProvider.InMemoryProviderId;

        private readonly InMemoryEnvironmentProviderModule _inner =
            new();

        public ProviderDescriptor Descriptor => _inner.Descriptor;

        public void Register(IProviderRegistrationBuilder builder)
        {
            _inner.Register(builder);
            builder.AddProviderCapabilityReporter(
                new SandboxCapabilityReporter());
        }

        public void RegisterJsonTypes(
            IProviderJsonTypeRegistry registry) =>
            _inner.RegisterJsonTypes(registry);
    }

    private sealed class SandboxCapabilityReporter
        : IProviderCapabilityReporter
    {
        private static readonly EnvCapabilityId[] RequiredIds =
        [
            StandardSandboxEnvironmentCapabilities.ProcessExecution,
            StandardSandboxEnvironmentCapabilities.ProcessOutput,
            StandardSandboxEnvironmentCapabilities.ProcessCancellation,
            StandardSandboxEnvironmentCapabilities.SessionCleanup,
        ];

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
            ProviderId providerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Report(providerId));

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
            ProviderId providerId,
            ProviderCapabilityQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Report(providerId));

        private static ProviderCapabilityReport Report(
            ProviderId providerId) =>
            new()
            {
                ProviderId = providerId,
                HostPlatform =
                    new PlatformSpec("test", "test"),
                Capabilities = RequiredIds
                    .Select(id => new CapabilityFact
                    {
                        Id = id,
                        Category =
                            new CapabilityCategory("sandbox"),
                        AppliesTo =
                            ProviderContractKind.ProcessInvocation,
                        State = CapabilityState.Supported,
                        Detail = "Test capability.",
                    })
                    .ToArray(),
            };
    }
}
