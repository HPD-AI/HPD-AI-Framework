using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess;
using HPD.Environment.Runtime;
using Xunit;

namespace HPD.Agent.Sandbox.HostProcess.Tests;

public sealed class HostProcessAdapterTests
{
    [SkippableFact]
    public async Task Windows_commands_and_file_tools_share_the_live_workspace()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var project = Directory.CreateTempSubdirectory("hpd-project-");
        var reference = Directory.CreateTempSubdirectory("hpd-reference-");
        try
        {
            string original = Path.Combine(project.FullName, "project.txt");
            File.WriteAllText(original, "original");
            string referenceFile = Path.Combine(reference.FullName, "reference.txt");
            File.WriteAllText(referenceFile, "reference");
            string[] ReadAceIdentities() => System.IO.FileSystemAclExtensions.GetAccessControl(
                    project, System.Security.AccessControl.AccessControlSections.Access)
                .GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
                .Cast<System.Security.AccessControl.FileSystemAccessRule>()
                .Select(rule => rule.IdentityReference.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] originalAces = ReadAceIdentities();
            await using var middleware = new EnvironmentSandboxMiddleware(new HostProcessProviderModule(), ObservationOnlyOptions() with
            {
                Workspaces = [
                    SandboxWorkspaceRequest.HostDirectory(project.FullName),
                    SandboxWorkspaceRequest.HostDirectory(reference.FullName, AccessMode.ReadOnly, rootId: "reference"),
                ],
            });
            await middleware.InitializeAsync();
            ReadAceIdentities().Should().NotEqual(originalAces);
            var session = middleware.Session!;
            var path = new WorkspacePath(new WorkspaceRootId("workspace"), "project.txt");
            string cwd = session.Workspace.Paths.ToExecutionPath(session.Workspace.WorkingDirectory).Value;
            async Task<ProcessInvocationResult> RunAsync(string command, string? directory = null) =>
                await session.Execution.ProcessProvider.RunAsync(new ProcessInvocationSpec
                {
                    Target = session.Execution.ExecutionTarget,
                    Isolation = session.Execution.Isolation,
                    Command = new ProcessCommandSpec
                    {
                        FileName = Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
                        Arguments = ["/d", "/c", command],
                        WorkingDirectory = directory ?? cwd,
                    },
                });

            (await RunAsync("echo edited>project.txt")).ExitCode.Should().Be(0);
            (await session.Workspace.Files.ReadTextAsync(path, new WorkspaceTextReadOptions()))!
                .Content.Trim().Should().Be("edited");
            var second = await RunAsync("type project.txt");
            second.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(second.Output.Stdout.CapturedBytes.Span).Trim().Should().Be("edited");
            File.ReadAllText(original).Trim().Should().Be("edited");
            var readOnly = await RunAsync("type reference.txt", reference.FullName);
            readOnly.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(readOnly.Output.Stdout.CapturedBytes.Span).Should().Contain("reference");
            await RunAsync("echo overwritten>reference.txt", reference.FullName);
            await RunAsync("del /q reference.txt", reference.FullName);
            File.ReadAllText(referenceFile).Should().Be("reference");
            await middleware.DisposeAsync();
            ReadAceIdentities().Should().Equal(originalAces);
            cwd.Should().Be(project.FullName);
            Directory.Exists(cwd).Should().BeTrue();
        }
        finally
        {
            project.Delete(recursive: true);
            reference.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public async Task Windows_admits_a_supported_policy_without_workspace_bindings()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        await using var middleware = new EnvironmentSandboxMiddleware(new HostProcessProviderModule(), ObservationOnlyOptions());
        await middleware.InitializeAsync();
        middleware.Session.Should().NotBeNull();
    }

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
            new EnvironmentSandboxMiddleware(new HostProcessProviderModule(), ObservationOnlyOptions());
        await middleware.InitializeAsync();

        middleware.IsInitialized.Should().BeTrue();
        middleware.Registry.Should().NotBeNull();
        middleware.Runtime.Should().NotBeNull();
        RuntimeProcessExecutionBinding? binding =
            middleware.Session?.Execution;
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
            new EnvironmentSandboxMiddleware(new HostProcessProviderModule(), ObservationOnlyOptions());
        await middleware.InitializeAsync();
        InMemoryEnvironmentRuntime runtime =
            (InMemoryEnvironmentRuntime)middleware.Runtime!;

        await middleware.DisposeAsync();

        (await runtime.ListExecutionUnitsAsync()).Should().BeEmpty();
        middleware.Session?.Execution.Should().BeNull();
        middleware.Registry.Should().BeNull();
        middleware.Runtime.Should().BeNull();
    }

    private static SandboxSessionRequest ObservationOnlyOptions() =>
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

    [SkippableFact]
    public async Task Switching_workspace_keeps_previous_binding_available_and_reuses_it_on_return()
    {
        var first = Directory.CreateTempSubdirectory("hpd-workspace-a-");
        var second = Directory.CreateTempSubdirectory("hpd-workspace-b-");
        try
        {
            var firstRun = new HPD.Agent.AgentRunConfig();
            var secondRun = new HPD.Agent.AgentRunConfig { Workspace = new AgentWorkspaceReference("second") };
            await File.WriteAllTextAsync(Path.Combine(first.FullName, "project.txt"), "first");
            await File.WriteAllTextAsync(Path.Combine(second.FullName, "project.txt"), "second");
            var firstRequest = ObservationOnlyOptions() with
            {
                Workspaces = [SandboxWorkspaceRequest.HostDirectory(first.FullName)],
            };
            var secondRequest = ObservationOnlyOptions() with
            {
                Workspaces = [SandboxWorkspaceRequest.HostDirectory(second.FullName)],
            };
            await using var middleware = new EnvironmentSandboxMiddleware(
                new HostProcessProviderModule(), firstRequest,
                (workspace, _) => ValueTask.FromResult(workspace.Id == "second"
                    ? secondRequest with { Workspaces = [SandboxWorkspaceRequest.HostDirectory(second.FullName)] }
                    : throw new InvalidOperationException("Unknown workspace.")));
            await middleware.InitializeAsync(firstRun);
            var original = middleware.Session!;
            await middleware.InitializeAsync(secondRun);
            middleware.Session.Should().NotBeSameAs(original);
            var secondSession = middleware.Session;
            await middleware.InitializeAsync(secondRun);
            middleware.Session.Should().BeSameAs(secondSession);

            var path = new WorkspacePath(new WorkspaceRootId("workspace"), "project.txt");
            (await original.Workspace.Files.ReadTextAsync(path, new WorkspaceTextReadOptions()))!
                .Content.Should().Be("first");
            (await middleware.Session!.Workspace.Files.ReadTextAsync(path, new WorkspaceTextReadOptions()))!
                .Content.Should().Be("second");
            await middleware.InitializeAsync(firstRun);
            middleware.Session.Should().BeSameAs(original);
        }
        finally
        {
            first.Delete(recursive: true);
            second.Delete(recursive: true);
        }
    }
}
