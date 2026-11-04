namespace HPD.Environment.HostContainers.Tests.Sandboxing;

using FluentAssertions;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class HostContainersSandboxTests
    : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hpd-host-containers-sandbox",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Retained_container_composes_projection_workspace_and_process_access()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "file.txt"),
            "workspace-content\n");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, ".gitignore"),
            "ignored/\n");
        Directory.CreateDirectory(
            Path.Combine(workspace, "ignored"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "ignored", "search.txt"),
            "needle\n");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "search.txt"),
            "before\nneedle\nafter\n");
        string socket = Path.Combine(_root, "engine.sock");
        await File.WriteAllTextAsync(socket, string.Empty);
        string docker = await CreateFakeDockerAsync();
        var module = new HostContainersProviderModule(
            new HostContainersProviderOptions
            {
                EngineSocketPath = socket,
                DockerCliPath = docker,
                WorkloadStateRoot =
                    Path.Combine(_root, "provider-state"),
            });
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(module);
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ProviderCapabilityReport capabilities =
            await registry.GetCapabilitiesAsync(
                HostContainersProviderDescriptor.ProviderId);
        capabilities.Capabilities.Should().Contain(fact =>
            fact.Id ==
            StandardSandboxEnvironmentCapabilities
                .ProcessExecution &&
            fact.State == CapabilityState.Supported);
        capabilities.Capabilities.Should().Contain(fact =>
            fact.Id ==
            StandardSandboxEnvironmentCapabilities
                .ContentProjection &&
            fact.State == CapabilityState.Supported);
        capabilities.Capabilities.Should().Contain(fact =>
            fact.Id ==
            StandardWorkspaceEnvironmentCapabilities.Read &&
            fact.State == CapabilityState.Supported);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            host = await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    HostContainersProviderDescriptor.ProviderId,
                Platform =
                    HostContainersProviderDescriptor.CurrentPlatform(),
            });
        var hostRef = new ResourceRef<RuntimeHost>(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        var unitOptions = new HostContainersSandboxUnitOptions
        {
            Image = "hpd/test:latest",
            WorkingDirectory = new GuestPath("/workspace"),
            Mounts =
            [
                new HostContainersSandboxMount
                {
                    HostSource = new HostPath(workspace),
                    ContainerPath = new GuestPath("/workspace"),
                    Access = AccessMode.ReadWrite,
                },
            ],
        };
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = hostRef,
                }.WithSandbox(unitOptions));
        unit.Status.UnitPhase.Should().Be(
            ExecutionUnitPhase.Ready);
        var unitRef = new ResourceRef<ExecutionUnit>(
            unit.Metadata.Id,
            unit.Metadata.Scope,
            unit.Metadata.Generation);

        ResourceSnapshot<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> projection =
            await runtime.EnsureAsync(new ContentProjectionSpec
            {
                Source = new ContentSelector
                {
                    Kind = ContentSelectorKind.HostPath,
                    HostPath = new HostPathSelection(
                        new HostPath(workspace),
                        HostPathKind.Directory),
                },
                Target = new ContentProjectionTarget
                {
                    Host = hostRef,
                    Unit = unitRef,
                },
                View = new ProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath("/workspace"),
                },
                Role = ContentProjectionRole.Workspace,
                AccessMode = AccessMode.ReadWrite,
                SecurityPolicy =
                    ContentProjectionSecurityPolicy.Default with
                    {
                        AllowHostPathSource = true,
                        AllowDirectSourceMutation = true,
                    },
            });
        projection.Status.ProjectionPhase.Should().Be(
            ContentProjectionPhase.Projected);

        IWorkspaceFileStoreProvider fileStoreProvider =
            registry.WorkspaceFileStoreProviders.Single();
        await using WorkspaceFileStoreAttachment attachment =
            await fileStoreProvider.OpenAsync(
                new WorkspaceFileStoreSpec
                {
                    RootId = new WorkspaceRootId("workspace"),
                    Source = projection.Spec.Source,
                    ExecutionPath =
                        new WorkspaceExecutionPath("/workspace"),
                    Access = AccessMode.ReadWrite,
                    PathSemantics =
                        new WorkspacePathSemantics(
                            true,
                            SymlinkEvaluationPolicy.DenySymlinks),
                },
                unit.Status.Handle);
        var logical = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            "file.txt");
        WorkspaceTextSnapshot snapshot =
            (await attachment.Files.ReadTextAsync(
                logical,
                new WorkspaceTextReadOptions()))!;
        snapshot.Content.Should().Be("workspace-content\n");
        attachment.Paths.ToExecutionPath(logical).Value
            .Should().Be("/workspace/file.txt");
        WorkspaceSearchResultSet search =
            await attachment.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = new WorkspacePath(
                        new WorkspaceRootId("workspace"),
                        string.Empty),
                    Query = "needle",
                    BeforeContext = 1,
                    AfterContext = 1,
                });
        search.Results.Should().ContainSingle();
        search.Results[0].Path.RelativePath.Should().Be(
            "search.txt");
        search.Results[0].ContextLines
            .Select(line => line.Text)
            .Should().Equal("before", "after");

        ProcessInvocationResult process =
            await runtime.RunProcessAsync(new ProcessInvocationSpec
            {
                Target = unit.Status.Handle!.Value,
                Command = new ProcessCommandSpec
                {
                    FileName = "sh",
                    Arguments = ["-c", "cat file.txt"],
                    WorkingDirectory = "/workspace",
                },
                Isolation = ProcessIsolationPolicy.Default,
            });
        process.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(
                process.Output.Stdout.CapturedBytes.Span)
            .Should().Contain("container-ok");

        await runtime.ReleaseAsync(new ResourceRef<ContentProjection>(
            projection.Metadata.Id,
            projection.Metadata.Scope,
            projection.Metadata.Generation));
        await runtime.DeleteExecutionUnitAsync(unitRef);
        (await runtime.DeleteHostAsync()).Deleted.Should().BeTrue();

        (await File.ReadAllTextAsync(
            Path.Combine(_root, "docker.log")))
            .Should()
            .Contain("create")
            .And.Contain("--volume")
            .And.Contain("exec")
            .And.Contain("rm");
    }

    private async Task<string> CreateFakeDockerAsync()
    {
        string path = Path.Combine(_root, "docker");
        string log = Path.Combine(_root, "docker.log");
        string script = $$"""
            #!/bin/sh
            printf '%s\n' "$*" >> '{{log}}'
            mode=''
            while [ "$#" -gt 0 ]; do
              case "$1" in
                --host)
                  shift 2
                  ;;
                *)
                  mode="$1"
                  shift
                  break
                  ;;
              esac
            done
            case "$mode" in
              create)
                printf '%s\n' '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
                ;;
              exec)
                printf '%s\n' 'container-ok'
                ;;
              *)
                exit 0
                ;;
            esac
            """;
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        return path;
    }
}
