namespace HPD.Agent.ToolHarness.Coding.Tests.Workspace;

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.HostProcess;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

public sealed class SessionWorkspaceToolTests
{
    [Fact]
    public async Task Windows_coding_tools_share_the_AppContainer_workspace()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var project = Directory.CreateTempSubdirectory("hpd-coding-windows-");
        try
        {
            string input = Path.Combine(project.FullName, "project.txt");
            await File.WriteAllTextAsync(input, "original");
            await using var sandbox = new HostProcessSandboxMiddleware(
                options: new HostProcessSandboxOptions { Workspaces = [new() { Path = project.FullName }] });
            await sandbox.InitializeAsync();
            var session = sandbox.Session!;
            var resolver = new HPDOS.ToolHarnesses.Middleware.EnvironmentLanguageServerToolResolver(
                session.Execution, session.Workspace);
            string probe = Path.Combine(project.CreateSubdirectory("bin").FullName, "hpd-language-server-probe.exe");
            File.Copy(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"), probe);
            (await resolver.FindExecutableAsync(Path.GetFileName(probe), project.FullName)).Should().Be(probe);
            (await resolver.FindExecutableAsync("hpd-missing-language-server.exe", project.FullName)).Should().BeNull();
            var context = Context(session);
            var tools = CodingHarnessTestFactory.Create();

            var write = await tools.ExecuteCommand(
                new RunCommandOperation("Set-Content -LiteralPath project.txt -Value HPD_LIVE",
                    WorkingDirectory: "@workspace"), context);
            write.ExitCode.Should().Be(0, "{0}", write);
            (await tools.ReadFile("@workspace/project.txt", context)).ToString().Should().Contain("HPD_LIVE");
            (await tools.ExecuteCommand(new RunCommandOperation("Get-Content -LiteralPath project.txt",
                WorkingDirectory: "@workspace"), context)).ToString().Should().Contain("HPD_LIVE");
            (await File.ReadAllTextAsync(input)).Trim().Should().Be("HPD_LIVE");
            var systemCommand = await tools.ExecuteCommand(new RunCommandOperation(
                "cmd /d /c echo HPD_CMD",
                WorkingDirectory: "@workspace"), context);
            systemCommand.ExitCode.Should().Be(0, "{0}", systemCommand);
            systemCommand.ToString().Should().Contain("HPD_CMD");
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task File_and_search_tools_use_the_session_workspace_binding()
    {
        if (OperatingSystem.IsWindows())
            return;
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-coding-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var sandbox =
                new HostProcessSandboxMiddleware(
                    options: new HostProcessSandboxOptions
                    {
                        Workspaces =
                        [
                            new HostProcessWorkspaceOptions
                            {
                                Path = root,
                                RootId = "workspace",
                            },
                        ],
                        Isolation =
                            ProcessIsolationPolicy.Default with
                            {
                                Violations =
                                    ProcessViolationPolicy.Default with
                                    {
                                        Action =
                                            ProcessViolationAction.ObserveOnly,
                                    },
                            },
                    });
            await sandbox.InitializeAsync();
            ISandboxSession session = sandbox.Session!;
            FunctionExecutionContext context = Context(session);
            CodingToolHarness tools =
                CodingHarnessTestFactory.Create();

            string write = (await tools.WriteFile(
                "@workspace/src/hello.txt",
                "hello workspace\n",
                context)).ToString()!;
            write.Should().Contain(
                "path=\"src/hello.txt\"");
            (await File.ReadAllTextAsync(
                Path.Combine(root, "src", "hello.txt")))
                .Should().Be("hello workspace\n");

            string read = (await tools.ReadFile(
                "@workspace/src/hello.txt",
                context)).ToString()!;
            read.Should().Contain(
                "path=\"src/hello.txt\"");
            read.Should().Contain("1\thello workspace");

            string list = await tools.ListDirectory(
                "@workspace/src",
                context: context);
            list.Should().Contain("hello.txt");

            string glob = await tools.GlobSearch(
                "**/*.txt",
                "@workspace",
                context: context);
            glob.Should().Contain("src/hello.txt");

            string grep = await tools.Grep(
                "workspace",
                "@workspace",
                outputMode: GrepOutputMode.Content,
                context: context);
            grep.Should().Contain(
                "src/hello.txt");
            grep.Should().Contain("hello workspace");

            string created = await tools.EditFile(
                "@workspace/src/created.txt",
                [
                    new FileEditReplacement(
                        string.Empty,
                        "created by EditFile\n"),
                ],
                context);
            created.Should().NotContain("<error");
            (await File.ReadAllTextAsync(
                Path.Combine(root, "src", "created.txt")))
                .Should().Be("created by EditFile\n");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteCommand_uses_the_same_session_workspace_and_process_binding()
    {
        if (OperatingSystem.IsWindows())
            return;
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-coding-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "command.txt"),
            "command-content\n");
        try
        {
            await using var sandbox =
                new HostProcessSandboxMiddleware(
                    options: new HostProcessSandboxOptions
                    {
                        Workspaces =
                        [
                            new HostProcessWorkspaceOptions
                            {
                                Path = root,
                                RootId = "workspace",
                            },
                        ],
                        Isolation =
                            ProcessIsolationPolicy.Default with
                            {
                                Mode =
                                    ProcessIsolationMode.Disabled,
                                Network = new NetworkEgressPolicy
                                {
                                    Mode =
                                        NetworkEgressMode.Unrestricted,
                                    RequireProxyMediation = false,
                                },
                                Interactive =
                                    ProcessInteractivePolicy.Default with
                                    {
                                        AllowStdin = false,
                                    },
                                Environment =
                                    new EnvironmentAccessPolicy
                                    {
                                        StripUnlistedVariables = false,
                                    },
                                Violations =
                                    ProcessViolationPolicy.Default with
                                    {
                                        Action =
                                            ProcessViolationAction.ObserveOnly,
                                    },
                            },
                    });
            await sandbox.InitializeAsync();
            ISandboxSession session = sandbox.Session!;
            FunctionExecutionContext context = Context(session);
            CodingToolHarness tools =
                CodingHarnessTestFactory.Create();

            object result = await tools.ExecuteCommand(
                new RunCommandOperation(
                    "cat command.txt",
                    WorkingDirectory:
                        "@workspace"),
                context);
            result.ToString().Should().Contain(
                "command-content");
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
            config: new AgentConfig(),
            runtimeOptions: new AgentRuntimeOptions
            {
                EventComposition = AgentEventComposition.Create(
                [
                    CoreAgentEventModule.Fragment,
                    HPD_Agent_Harness_CodingGeneratedAgentEventModule.Fragment,
                ])
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
}
