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
}
