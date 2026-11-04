namespace HPD.Agent.Sandbox.HostContainers.Tests;

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

public sealed class HostContainersSandboxAdapterTests
{
    [SkippableFact]
    [Trait("Category", "RealAdapter")]
    public async Task Real_engine_runs_file_tools_and_processes_in_one_retained_container()
    {
        Skip.If(OperatingSystem.IsWindows(),
            "Retained Host Containers sandbox acceptance requires a Unix engine socket.");
        string? image = System.Environment.GetEnvironmentVariable(
            "HPD_CONTAINER_IMAGE");
        Skip.If(string.IsNullOrWhiteSpace(image),
            "Set HPD_CONTAINER_IMAGE to an existing local test image.");
        string? socket = new[]
        {
            System.Environment.GetEnvironmentVariable(
                "HPD_CONTAINER_ENGINE_SOCKET"),
            "/var/run/docker.sock",
            Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.UserProfile),
                ".docker",
                "run",
                "docker.sock"),
        }.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path));
        Skip.If(socket is null,
            "Set HPD_CONTAINER_ENGINE_SOCKET to a local Docker-compatible Unix socket.");
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-real-container-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var middleware =
                new HostContainersSandboxMiddleware(
                    new HostContainersSandboxOptions
                    {
                        Image = image!,
                        EngineSocketPath = socket!,
                        DockerCliPath =
                            System.Environment.GetEnvironmentVariable(
                                "HPD_DOCKER_CLI"),
                        WorkloadStateRoot =
                            Path.Combine(root, "provider"),
                        Workspaces =
                        [
                            new HostContainersWorkspaceOptions
                            {
                                SourceKind =
                                    HostContainersWorkspaceSourceKind
                                        .ProviderSource,
                                ProviderSourceId =
                                    "real-container-provider-workspace",
                                ContainerPath =
                                    "/tmp/provider-workspace",
                            },
                        ],
                    });
            await middleware.InitializeAsync();
            ISandboxSession session = middleware.Session!;
            var logical = new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "from-tools.txt");
            (await session.Workspace.Files.WriteTextAsync(
                logical,
                "written-by-tools\n",
                WorkspaceWriteCondition.RequireCreate))
                .Outcome.Should().Be(
                    WorkspaceMutationOutcome.Succeeded);

            ProcessInvocationResult result =
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
                                "cat /tmp/provider-workspace/from-tools.txt",
                            ],
                            WorkingDirectory =
                                "/tmp/provider-workspace",
                        },
                        Isolation =
                            session.Execution.Isolation,
                    });
            result.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(
                    result.Output.Stdout.CapturedBytes.Span)
                .Should().Contain("written-by-tools");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Adapter_publishes_retained_container_workspace_and_process_binding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-agent-containers-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "workspace.txt"),
            "container-workspace\n");
        try
        {
            string socket = Path.Combine(root, "engine.sock");
            await File.WriteAllTextAsync(socket, string.Empty);
            string docker = await CreateFakeDockerAsync(
                root,
                workspace);
            await using var middleware =
                new HostContainersSandboxMiddleware(
                    new HostContainersSandboxOptions
                    {
                        Image = "hpd/test:latest",
                        EngineSocketPath = socket,
                        DockerCliPath = docker,
                        WorkloadStateRoot =
                            Path.Combine(root, "provider"),
                        Workspaces =
                        [
                            new HostContainersWorkspaceOptions
                            {
                                Path = workspace,
                                RootId = "workspace",
                                ContainerPath = "/workspace",
                            },
                        ],
                    });
            await middleware.InitializeAsync();
            ISandboxSession session = middleware.Session!;
            session.Backend.Should().Be(
                SandboxBackendKind.HostContainers);
            session.Workspace.Roots.Should().ContainSingle();
            var logical = new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "written.txt");
            WorkspaceMutationResult write =
                await session.Workspace.Files.WriteTextAsync(
                    logical,
                    "written-by-agent\n",
                    WorkspaceWriteCondition.RequireCreate);
            write.Outcome.Should().Be(
                WorkspaceMutationOutcome.Succeeded);
            session.Workspace.Paths.ToExecutionPath(logical).Value
                .Should().Be("/workspace/written.txt");

            ProcessInvocationResult process =
                await session.Execution.EnvironmentRuntime!
                    .RunProcessAsync(new ProcessInvocationSpec
                    {
                        Target =
                            session.Execution.ExecutionTarget,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "cat",
                            Arguments = ["workspace.txt"],
                            WorkingDirectory = "/workspace",
                        },
                        Isolation =
                            session.Execution.Isolation,
                    });
            process.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(
                    process.Output.Stdout.CapturedBytes.Span)
                .Should().Contain("container-workspace");

            await middleware.DisposeAsync();
            middleware.Session.Should().BeNull();
            (await File.ReadAllTextAsync(
                Path.Combine(root, "docker.log")))
                .Should()
                .Contain("create")
                .And.Contain("--volume")
                .And.Contain("exec")
                .And.Contain("rm");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Provider_owned_workspace_uses_container_process_lane_without_a_bind_mount()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-agent-provider-workspace-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "existing.txt"),
            "before\nneedle alpha\nother\nafter\n");
        File.SetLastWriteTimeUtc(
            Path.Combine(workspace, "existing.txt"),
            DateTime.UtcNow.AddMinutes(-1));
        try
        {
            string socket = Path.Combine(root, "engine.sock");
            await File.WriteAllTextAsync(socket, string.Empty);
            string docker = await CreateFakeDockerAsync(
                root,
                workspace);
            await using var middleware =
                new HostContainersSandboxMiddleware(
                    new HostContainersSandboxOptions
                    {
                        Image = "hpd/test:latest",
                        EngineSocketPath = socket,
                        DockerCliPath = docker,
                        WorkloadStateRoot =
                            Path.Combine(root, "provider"),
                        Workspaces =
                        [
                            new HostContainersWorkspaceOptions
                            {
                                SourceKind =
                                    HostContainersWorkspaceSourceKind
                                        .ProviderSource,
                                ProviderSourceId =
                                    "test-provider-volume",
                                RootId = "workspace",
                                ContainerPath = workspace,
                            },
                        ],
                    });
            await middleware.InitializeAsync();
            ISandboxSession session = middleware.Session!;
            CodingToolHarness tools =
                new(new TestHarnessConfig());
            FunctionExecutionContext context = Context(session);
            string written = (await tools.WriteFile(
                "@workspace/tool-created.txt",
                "tool-created needle\n",
                context)).ToString()!;
            written.Should().Contain(
                "path=\"tool-created.txt\"");
            (await tools.ListDirectory(
                "@workspace",
                context: context))
                .Should().Contain("tool-created.txt");
            (await tools.GlobSearch(
                "*.txt",
                "@workspace",
                context: context))
                .Should().Contain("tool-created.txt");
            (await tools.Grep(
                "needle",
                "@workspace",
                outputMode: GrepOutputMode.Content,
                context: context))
                .Should().Contain("tool-created.txt");

            var logical = new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "created.txt");
            WorkspaceMutationResult write =
                await session.Workspace.Files.WriteTextAsync(
                    logical,
                    "created through provider storage\n",
                    WorkspaceWriteCondition.RequireCreate);
            write.Outcome.Should().Be(
                WorkspaceMutationOutcome.Succeeded);
            (await File.ReadAllTextAsync(
                Path.Combine(workspace, "created.txt")))
                .Should().Be("created through provider storage\n");

            WorkspaceSearchResultSet search =
                await session.Workspace.Files.SearchTextAsync(
                    new WorkspaceSearchRequest
                    {
                        Root = new WorkspacePath(
                            new WorkspaceRootId("workspace"),
                            string.Empty),
                        Query = "needle",
                        GlobPattern = "existing.txt",
                        BeforeContext = 1,
                        AfterContext = 1,
                    });
            search.Results.Should().ContainSingle();
            search.Results[0].ContextLines
                .Select(line => line.Text)
                .Should().Equal("before", "other");

            WorkspaceSearchResultSet multiline =
                await session.Workspace.Files.SearchTextAsync(
                    new WorkspaceSearchRequest
                    {
                        Root = new WorkspacePath(
                            new WorkspaceRootId("workspace"),
                            string.Empty),
                        Query = @"alpha\s+other",
                        Multiline = true,
                        GlobPattern = "existing.txt",
                    });
            multiline.Results.Should().ContainSingle();

            ProcessInvocationResult process =
                await session.Execution.EnvironmentRuntime!
                    .RunProcessAsync(new ProcessInvocationSpec
                    {
                        Target =
                            session.Execution.ExecutionTarget,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "cat",
                            Arguments = ["created.txt"],
                            WorkingDirectory = workspace,
                        },
                        Isolation =
                            session.Execution.Isolation,
                    });
            process.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(
                    process.Output.Stdout.CapturedBytes.Span)
                .Should().Contain(
                    "created through provider storage");

            await middleware.DisposeAsync();
            (await File.ReadAllTextAsync(
                Path.Combine(root, "docker.log")))
                .Should()
                .Contain("exec")
                .And.NotContain("--volume");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Copy_in_out_workspace_materializes_and_finalizes_without_bind_mounting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-agent-copy-workspace-" + Guid.NewGuid().ToString("N"));
        string hostWorkspace = Path.Combine(root, "host");
        string containerWorkspace = Path.Combine(root, "container");
        Directory.CreateDirectory(hostWorkspace);
        Directory.CreateDirectory(containerWorkspace);
        await File.WriteAllTextAsync(
            Path.Combine(hostWorkspace, "source.txt"),
            "copied-into-environment\n");
        try
        {
            string socket = Path.Combine(root, "engine.sock");
            await File.WriteAllTextAsync(socket, string.Empty);
            string docker = await CreateFakeDockerAsync(
                root,
                containerWorkspace);
            await using var middleware =
                new HostContainersSandboxMiddleware(
                    new HostContainersSandboxOptions
                    {
                        Image = "hpd/test:latest",
                        EngineSocketPath = socket,
                        DockerCliPath = docker,
                        WorkloadStateRoot =
                            Path.Combine(root, "provider"),
                        Workspaces =
                        [
                            new HostContainersWorkspaceOptions
                            {
                                SourceKind =
                                    HostContainersWorkspaceSourceKind
                                        .CopyInOut,
                                Path = hostWorkspace,
                                RootId = "workspace",
                                ContainerPath = containerWorkspace,
                            },
                        ],
                    });
            await middleware.InitializeAsync();
            ISandboxSession session = middleware.Session!;
            (await File.ReadAllTextAsync(
                Path.Combine(containerWorkspace, "source.txt")))
                .Should().Be("copied-into-environment\n");

            WorkspaceMutationResult write =
                await session.Workspace.Files.WriteTextAsync(
                    new WorkspacePath(
                        new WorkspaceRootId("workspace"),
                        "result.txt"),
                    "copied-back-to-host\n",
                    WorkspaceWriteCondition.RequireCreate);
            write.Outcome.Should().Be(
                WorkspaceMutationOutcome.Succeeded);
            File.Exists(
                Path.Combine(hostWorkspace, "result.txt"))
                .Should().BeFalse(
                    "copy-out occurs before session disposal");

            await middleware.DisposeAsync();
            (await File.ReadAllTextAsync(
                Path.Combine(hostWorkspace, "result.txt")))
                .Should().Be("copied-back-to-host\n");
            (await File.ReadAllTextAsync(
                Path.Combine(root, "docker.log")))
                .Should().NotContain("--volume");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FunctionExecutionContext Context(
        ISandboxSession session)
    {
        var config = new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>(),
            },
        };
        var state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-thread",
            "test-agent");
        var agentContext = new AgentContext(
            "test-agent",
            "test-thread",
            state,
            new EventCoordinator(),
            new Session("test-session"),
            new HPD.Agent.Thread(
                "test-session",
                "test-thread"),
            CancellationToken.None,
            config: new AgentConfig
            {
                EventComposition = AgentEventComposition.Create(
                [
                    CoreAgentEventModule.Fragment,
                    GeneratedAgentEventModule_HPD_Agent_Harness_Coding_ab3285cb.Fragment,
                ]),
            });
        var before = agentContext.AsBeforeFunction(
            null,
            "test-call",
            new Dictionary<string, object?>(),
            config,
            "CodingToolHarness");
        var context = new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                Function = AIFunctionFactory.Create(
                    () => "test",
                    "test"),
                CallId = before.FunctionCallId,
                Arguments =
                    new Dictionary<string, object?>(),
                State = state,
                RunConfig = config,
                EventCoordinator =
                    agentContext.EventCoordinator,
            });
        context.RuntimeCapabilities.Set(session.Workspace);
        context.RuntimeCapabilities.Set(session.Execution);
        return context;
    }

    private sealed class TestHarnessConfig
        : IHarnessConfig<CodingHarnessConfig>
    {
        private readonly CodingHarnessConfig _value = new();

        public CodingHarnessConfig AgentDefaults => _value;

        public CodingHarnessConfig For(
            IHarnessConfigurationContext context) =>
            _value;
    }

    private static async Task<string> CreateFakeDockerAsync(
        string root,
        string workspace)
    {
        string path = Path.Combine(root, "docker");
        string log = Path.Combine(root, "docker.log");
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
                workdir='{{workspace}}'
                command_mode=''
                while [ "$#" -gt 0 ]; do
                  case "$1" in
                    --interactive|--tty|-i|-t)
                      shift
                      ;;
                    --workdir)
                      workdir="$2"
                      shift 2
                      ;;
                    --user|--env|-u|-e)
                      shift 2
                      ;;
                    0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef)
                      shift
                      break
                      ;;
                    *)
                      break
                      ;;
                  esac
                done
                if [ "$workdir" = '/workspace' ]; then
                  workdir='{{workspace}}'
                fi
                cd "$workdir" || exit 91
                exec "$@"
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
