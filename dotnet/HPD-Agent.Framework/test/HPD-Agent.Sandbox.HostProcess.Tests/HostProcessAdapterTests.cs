using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox.HostProcess;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess;
using HPD.Environment.Runtime;
using Xunit;

namespace HPD.Agent.Sandbox.HostProcess.Tests;

public sealed class HostProcessAdapterTests
{
    [SkippableFact]
    public async Task Composed_session_shares_one_workspace_between_files_and_processes()
    {
        Skip.If(OperatingSystem.IsWindows(), "Host Process workspace projection currently requires a POSIX execution path.");

        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-agent-host-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var factory =
                new EnvironmentSandboxBackendFactory(
                    new EnvironmentSandboxBackendDefinition
                    {
                        Kind = SandboxBackendKind.HostProcess,
                        Module = new HostProcessProviderModule(),
                        EnforcementLocation =
                            SandboxEnforcementLocation.Host,
                        HostSpecFactory = _ =>
                            new RuntimeHostSpec
                            {
                                PreferredProvider =
                                    HostProcessProviderDescriptor.ProviderId,
                                Platform =
                                    HostProcessProviderDescriptor.CurrentPlatform(),
                            },
                    });
            var rootId = new WorkspaceRootId("workspace");
            var commandPolicy = new ProcessIsolationPolicy
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
                Violations = ProcessViolationPolicy.Default with
                {
                    Action = ProcessViolationAction.ObserveOnly,
                },
            };
            await using ISandboxSession session =
                await factory.CreateSessionAsync(
                    new SandboxSessionRequest
                    {
                        Isolation = commandPolicy,
                        Workspaces =
                        [
                            new SandboxWorkspaceRequest
                            {
                                Source = new ContentSelector
                                {
                                    Kind = ContentSelectorKind.HostPath,
                                    HostPath = new HostPathSelection(
                                        new HostPath(root),
                                        HostPathKind.Directory),
                                },
                                Root = new WorkspaceRootBinding
                                {
                                    Id = rootId,
                                    DisplayName = "Workspace",
                                    Access = AccessMode.ReadWrite,
                                    DefaultExecutionPath =
                                        new WorkspacePath(
                                            rootId,
                                            string.Empty),
                                    Content =
                                        new WorkspaceContentIdentity(
                                            "workspace-content"),
                                    PathSemantics =
                                        new WorkspacePathSemantics(
                                            OperatingSystem.IsLinux(),
                                            SymlinkEvaluationPolicy.DenySymlinks),
                                },
                                ExecutionPath =
                                    new WorkspaceExecutionPath(root),
                                SecurityPolicy =
                                    ContentProjectionSecurityPolicy.Default with
                                    {
                                        AllowHostPathSource = true,
                                        AllowDirectSourceMutation = true,
                                    },
                            },
                        ],
                    });
            var logicalPath = new WorkspacePath(
                rootId,
                "shared.txt");

            WorkspaceMutationResult write =
                await session.Workspace.Files.WriteTextAsync(
                    logicalPath,
                    "shared-content\n",
                    WorkspaceWriteCondition.RequireCreate);

            write.Outcome.Should().Be(
                WorkspaceMutationOutcome.Succeeded);
            session.Workspace.Roots.Should().ContainSingle();
            session.Workspace.Paths.ToExecutionPath(logicalPath)
                .Value.Should().Be(
                    Path.Combine(root, "shared.txt"));

            ProcessInvocationResult process =
                await session.Execution.EnvironmentRuntime!
                    .RunProcessAsync(new ProcessInvocationSpec
                    {
                        Target =
                            session.Execution.ExecutionTarget,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "/bin/sh",
                            Arguments =
                            [
                                "-c",
                                "cat shared.txt",
                            ],
                            WorkingDirectory = root,
                        },
                        Isolation = commandPolicy,
                    });
            process.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(
                    process.Output.Stdout.CapturedBytes.Span)
                .Should().Be("shared-content\n");
            (await session.Workspace.Files.ReadTextAsync(
                logicalPath,
                new WorkspaceTextReadOptions()))!
                .Content.Should().Be("shared-content\n");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Adapter_publishes_real_environment_execution_binding()
    {
        Skip.If(OperatingSystem.IsWindows(), "Host Process sandboxing is unavailable on Windows.");

        await using var middleware =
            new HostProcessSandboxMiddleware(options: ObservationOnlyOptions());
        await middleware.InitializeAsync();

        middleware.IsInitialized.Should().BeTrue();
        middleware.Registry.Should().NotBeNull();
        middleware.Runtime.Should().NotBeNull();
        RuntimeProcessExecutionBinding? binding =
            middleware.ExecutionBinding;
        binding.Should().NotBeNull();
        RuntimeProcessExecutionBinding requiredBinding = binding!;
        requiredBinding.EnvironmentId.Should().Be(
            HostProcessProviderDescriptor.ProviderId.Value);
        requiredBinding.ProcessProvider.ProviderId.Should().Be(
            HostProcessProviderDescriptor.ProviderId);
        requiredBinding.ExecutionTarget.Route.ProviderId.Should().Be(
            HostProcessProviderDescriptor.ProviderId);
        requiredBinding.ExecutionTarget.Route.Segments.Should().ContainSingle(
            segment =>
                segment.Kind ==
                TargetRouteSegmentKind.ExecutionUnit);
    }

    [SkippableFact]
    public async Task Adapter_disposal_releases_unit_and_host_ownership()
    {
        Skip.If(OperatingSystem.IsWindows(), "Host Process sandboxing is unavailable on Windows.");

        var middleware =
            new HostProcessSandboxMiddleware(options: ObservationOnlyOptions());
        await middleware.InitializeAsync();
        InMemoryEnvironmentRuntime runtime =
            (InMemoryEnvironmentRuntime)middleware.Runtime!;

        await middleware.DisposeAsync();

        (await runtime.ListExecutionUnitsAsync()).Should().BeEmpty();
        middleware.ExecutionBinding.Should().BeNull();
        middleware.Registry.Should().BeNull();
        middleware.Runtime.Should().BeNull();
    }

    private static HostProcessSandboxOptions ObservationOnlyOptions() =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default with
            {
                Violations = ProcessViolationPolicy.Default with
                {
                    Action = ProcessViolationAction.ObserveOnly,
                },
            },
        };
}
