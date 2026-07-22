using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class ExecuteCommandTests : IDisposable
{
    private readonly string _originalCwd = Directory.GetCurrentDirectory();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-execute-command-tests-{Guid.NewGuid():N}");

    public ExecuteCommandTests()
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCurrentDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ExecuteCommand_HasExpectedToolAttributes()
    {
        var method = typeof(CodingToolHarness).GetMethod(nameof(CodingToolHarness.ExecuteCommand));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(AIFunctionAttribute), inherit: false)
            .Should().ContainSingle();
        method.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().BeEmpty("ExecuteCommand is guarded by ExecuteCommandPermissionMiddleware, not generic function-level permission");
        method.GetCustomAttributes(inherit: false)
            .Select(attribute => attribute.GetType().Name)
            .Should().NotContain(name => name.Contains("Sandbox", StringComparison.Ordinal));

        var collapse = typeof(CodingToolHarness)
            .GetCustomAttributes(typeof(CollapseAttribute), inherit: false)
            .Should().ContainSingle()
            .Subject as CollapseAttribute;

        collapse!.Middlewares.Should().Contain(typeof(ExecuteCommandPermissionMiddleware));
    }

    [Fact]
    public void ExecuteCommandEvents_AreOwnedByToolHarnessAssembly()
    {
        ((object)typeof(ExecuteCommandEvent).Assembly).Should().BeSameAs(typeof(CodingToolHarness).Assembly);
        ((object)typeof(ExecuteCommandProcessStartedEvent).Assembly).Should().BeSameAs(typeof(CodingToolHarness).Assembly);
        ((object)typeof(ExecuteCommandOutputChunkEvent).Assembly).Should().BeSameAs(typeof(CodingToolHarness).Assembly);
        ((object)typeof(ExecuteCommandProcessExitedEvent).Assembly).Should().BeSameAs(typeof(CodingToolHarness).Assembly);
    }

    [Fact]
    public void CodingToolHarnessJsonContext_RoundTripsExecuteCommandEvent()
    {
        var evt = new ExecuteCommandProcessStartedEvent
        {
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd_1",
            Command = "dotnet test",
            BaseCommand = "dotnet",
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            Shell = "/bin/zsh",
            StartedAt = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero),
            Background = false,
            AutoBackgroundEligible = true,
            ProcessId = 123,
            TimeoutMilliseconds = 120_000,
            EventFlowId = "cmd_1"
        };

        var json = JsonSerializer.Serialize(
            evt,
            CodingToolHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            CodingToolHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);

        roundTrip.Should().NotBeNull();
        roundTrip!.CommandId.Should().Be("cmd_1");
        roundTrip.Category.Should().Be(ExecuteCommandCategory.Test);
        roundTrip.EventFlowId.Should().Be("cmd_1");
    }

    [Fact]
    public void CodingToolHarnessJsonContext_RoundTripsExecuteCommandPermissionRequestEvent()
    {
        var sandbox = new ExecuteCommandSandboxPolicy();
        var workspace = new ExecuteCommandPermissionWorkspaceScope
        {
            RootId = "default",
            RootPath = _tempRoot,
            RelativeWorkingDirectory = "."
        };
        var shell = new ExecuteCommandShellScope
        {
            Executable = "/bin/zsh",
            Family = ExecuteCommandShellFamily.Zsh
        };
        var rule = new ExecuteCommandPermissionRule
        {
            Id = "rule-1",
            RuleSchemaVersion = ExecuteCommandPermissionAnalyzerVersions.RuleSchema,
            AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
            NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
            Behavior = ExecuteCommandPermissionBehavior.Allow,
            MatchKind = ExecuteCommandPermissionMatchKind.Prefix,
            Pattern = "git status",
            Shell = shell,
            RequestedSandboxFingerprint = sandbox.Canonicalize(_tempRoot),
            Workspace = workspace,
            MinimumTrustLevel = ExecuteCommandAnalysisTrustLevel.Simple
        };
        var proposal = new PrefixAllowRuleProposal
        {
            Rule = rule,
            UserLabel = "Always allow similar commands",
            Prefix = new SafeCommandPrefix("git status")
        };
        var plan = new SimpleCommandPermissionPlan
        {
            AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
            NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
            Fingerprint = new PermissionFingerprint("fp"),
            Action = ExecuteCommandAction.Run,
            Command = new RawCommandText("git status -sb"),
            NormalizedCommand = new NormalizedCommandText("git status -sb"),
            Shell = shell,
            WorkingDirectory = _tempRoot,
            Workspace = workspace,
            RequestedSandbox = sandbox,
            FilesystemEffects = [],
            NetworkEffects = [],
            StartsInBackground = false,
            Risk = ExecuteCommandPermissionRisk.None,
            CommandPlan = new ExecuteCommandSubcommandPlan
            {
                Text = "git status -sb",
                Argv = ["git", "status", "-sb"],
                BaseCommand = "git",
                SafePrefix = "git status",
                Risk = ExecuteCommandPermissionRisk.None,
                TrustLevel = ExecuteCommandAnalysisTrustLevel.Simple,
                Readiness = ExecuteCommandPolicyReadiness.PrefixAllowAllowed
            },
            ExactAllowRule = new ExactAllowRuleProposal
            {
                Rule = rule with { MatchKind = ExecuteCommandPermissionMatchKind.Exact, Pattern = "git status -sb" },
                UserLabel = "Always allow this exact command"
            },
            PrefixAllowRule = proposal,
            SuggestedRules = [proposal]
        };
        var evt = new ExecuteCommandPermissionRequestEvent(
            "perm-1",
            "ExecuteCommandPermissionMiddleware",
            "call-1",
            plan,
            [],
            new ExecuteCommandPermissionRuleDiagnostics(null, null, [], [], []),
            [
                new AllowOnceChoice
                {
                    Id = "allow_once",
                    Label = "Allow once"
                },
                new PersistRuleChoice
                {
                    Id = "allow_similar",
                    Label = "Always allow similar commands",
                    Proposal = proposal
                }
            ]);

        var json = JsonSerializer.Serialize(
            evt,
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRequestEvent);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRequestEvent);

        roundTrip.Should().NotBeNull();
        roundTrip!.Plan.Should().BeOfType<SimpleCommandPermissionPlan>();
        roundTrip.AvailableChoices.Should().Contain(choice => choice is PersistRuleChoice);
    }

    [Fact]
    public async Task ExecuteCommand_EmptyRunCommand_ReturnsValidationError()
    {
        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(new FakeProcessProvider()),
            command: "   ");

        result.ToString().Should().Contain("<execute_command_error");
        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain("Run requires command.");
    }

    [Fact]
    public async Task ExecuteCommand_MissingWorkingDirectory_ReturnsValidationError()
    {
        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(new FakeProcessProvider()),
            command: "dotnet test",
            workingDirectory: "missing");

        result.ToString().Should().Contain("kind=\"working_directory_not_found\"");
        result.ToString().Should().Contain(Path.Combine(_tempRoot, "missing"));
    }

    [Fact]
    public async Task ExecuteCommand_InvalidWorkspaceShape_ReturnsWorkspaceValidationError()
    {
        var workspaceJson = $$"""
        {
          "defaultRootId": "default",
          "roots": [
            { "id": "default", "path": "{{JsonEscape(_tempRoot)}}" }
          ]
        }
        """;

        using var document = JsonDocument.Parse(workspaceJson);
        var runConfig = new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = document.RootElement.Clone()
            }
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(new FakeProcessProvider(), runConfig: runConfig),
            command: "ls");

        result.ToString().Should().Contain("kind=\"invalid_workspace\"");
        result.ToString().Should().Contain("Workspace 'version' is required.");
    }

    [Fact]
    public async Task ExecuteCommand_WorkingDirectoryFile_ReturnsValidationError()
    {
        var filePath = Path.Combine(_tempRoot, "not-a-directory.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(new FakeProcessProvider()),
            command: "dotnet test",
            workingDirectory: filePath);

        result.ToString().Should().Contain("kind=\"working_directory_is_file\"");
        result.ToString().Should().Contain(filePath);
    }

    [Fact]
    public async Task ExecuteCommand_DefaultWorkingDirectory_UsesWorkspaceDefaultRoot()
    {
        var docsRoot = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var runner = new FakeProcessProvider();

        await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, runConfig: CreateWorkspaceRunConfig(_tempRoot, docsRoot)),
            command: "pwd");

        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Command.WorkingDirectory.Should().Be(Path.GetFullPath(_tempRoot));
    }

    [Fact]
    public async Task ExecuteCommand_DefaultSandboxConfig_UsesIsolatedProcessMode()
    {
        var runner = new FakeProcessProvider();

        await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "dotnet test");

        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Isolation.Mode.Should().Be(ProcessIsolationMode.Isolated);
    }

    [Fact]
    public async Task ExecuteCommand_RunConfigSandboxDisabled_DisablesProcessIsolation()
    {
        var runner = new FakeProcessProvider();
        var runConfig = CreateWorkspaceRunConfig();
        runConfig.ContextOverrides![ExecuteCommandSandboxPolicy.ContextKey] = new ExecuteCommandSandboxPolicy
        {
            Mode = ExecuteCommandIsolationMode.Disabled
        };

        await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, runConfig: runConfig),
            command: "dotnet test");

        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Isolation.Mode.Should().Be(ProcessIsolationMode.Disabled);
    }

    [Fact]
    public async Task ExecuteCommand_RunConfigSandboxBooleanFalse_ReturnsValidationError()
    {
        var runner = new FakeProcessProvider();
        var runConfig = CreateWorkspaceRunConfig();
        runConfig.ContextOverrides![ExecuteCommandSandboxPolicy.ContextKey] = false;

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, runConfig: runConfig),
            command: "dotnet test");

        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain("ExecuteCommand sandbox policy must be an ExecuteCommandSandboxPolicy object.");
        runner.LastSpec.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteCommand_RunConfigSandboxPolicy_CompilesGrantsIntoProcessIsolationPolicy()
    {
        var runner = new FakeProcessProvider();
        var runConfig = CreateWorkspaceRunConfig();
        runConfig.ContextOverrides![ExecuteCommandSandboxPolicy.ContextKey] = new ExecuteCommandSandboxPolicy
        {
            Filesystem =
            [
                new ExecuteCommandPathGrant { Kind = ExecuteCommandPathGrantKind.Read, Path = "readonly" },
                new ExecuteCommandPathGrant { Kind = ExecuteCommandPathGrantKind.Write, Path = "/tmp/hpd-write" }
            ],
            Network = new ExecuteCommandNetworkGrant
            {
                Mode = ExecuteCommandNetworkMode.Filtered,
                AllowedDomains = ["registry.npmjs.org"],
                DeniedDomains = ["169.254.169.254"]
            },
            Interactive = new ExecuteCommandInteractiveGrant
            {
                AllowPty = true,
                AllowLocalBinding = true,
                AllowedMachLookups = ["com.apple.securityd"]
            }
        };

        await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, runConfig: runConfig),
            command: "npm install");

        runner.LastSpec.Should().NotBeNull();
        var expectedReadPath = Path.GetFullPath(Path.Combine(runner.LastSpec!.Command.WorkingDirectory!, "readonly"));
        runner.LastSpec!.Isolation.Mode.Should().Be(ProcessIsolationMode.Isolated);
        runner.LastSpec.Isolation.Filesystem.Rules.Should().Contain(rule =>
            rule.Kind == PathAccessRuleKind.AllowRead &&
            rule.Path.Value == expectedReadPath);
        runner.LastSpec.Isolation.Filesystem.Rules.Should().Contain(rule =>
            rule.Kind == PathAccessRuleKind.AllowWrite &&
            rule.Path.Value == "/tmp/hpd-write");
        runner.LastSpec.Isolation.Network.Mode.Should().Be(NetworkEgressMode.Filtered);
        runner.LastSpec.Isolation.Network.AllowedDomains.Should().ContainSingle(rule => rule.Pattern == "registry.npmjs.org");
        runner.LastSpec.Isolation.Network.DeniedDomains.Should().ContainSingle(rule => rule.Pattern == "169.254.169.254");
        runner.LastSpec.Isolation.Interactive.AllowPty.Should().BeTrue();
        runner.LastSpec.Isolation.Interactive.AllowLocalBinding.Should().BeTrue();
        runner.LastSpec.Isolation.Interactive.AllowedMachLookups.Should().ContainSingle("com.apple.securityd");
    }

    [Fact]
    public async Task ExecuteCommandPermissionBoundary_DeniedCommandDoesNotReachProcessProvider()
    {
        var runner = new FakeProcessProvider();

        var result = await InvokeExecuteCommandThroughPermissionBoundaryAsync(
            runner,
            "git status -sb",
            _ => "deny");

        result.ToString().Should().Contain("<execute_command_permission_denied");
        runner.StartCalls.Should().Be(0);
        runner.LastSpec.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteCommandPermissionBoundary_AllowOnceReachesProcessProvider()
    {
        var runner = new FakeProcessProvider();

        var result = await InvokeExecuteCommandThroughPermissionBoundaryAsync(
            runner,
            "git status -sb",
            _ => "allow_once");

        result.ToString().Should().Contain("<execute_command");
        runner.StartCalls.Should().Be(1);
        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Command.Arguments.Should().Contain("git status -sb");
    }

    [Fact]
    public async Task ExecuteCommandPermissionBoundary_LocalBindingSandboxApprovalRetriesWithLocalBinding()
    {
        var runner = new FakeProcessProvider
        {
            Results =
            [
                CreateResult(
                    stderr: "Error: listen EPERM: operation not permitted 127.0.0.1:3000",
                    exitCode: 1),
                CreateResult(stdout: "Server running")
            ]
        };

        var coordinator = new EventCoordinator();
        using var permissionSubscription = RespondToPermissionRequests(coordinator, _ => "allow_once");
        using var sandboxSubscription = coordinator.Subscribe<ExecuteCommandSandboxCapabilityRequestEvent>(request =>
        {
            request.Capability.Should().Be(ExecuteCommandSandboxCapabilityKind.LocalBinding);
            request.Amendment.Should().BeOfType<AllowLocalBindingAmendment>();
            var result = coordinator.Respond(new ExecuteCommandSandboxCapabilityResponseEvent(
                request.RequestId,
                request.SourceName,
                true));
            result.Accepted.Should().BeTrue(result.Message);
            return ValueTask.CompletedTask;
        });

        var function = CreateExecuteCommandFunction();
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "test-agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            coordinator,
            session,
            thread,
            CancellationToken.None);
        agentContext.RuntimeCapabilities.Set<IProcessProvider>(runner);
        var runConfig = CreateWorkspaceRunConfig();
        var arguments = new Dictionary<string, object?> { ["command"] = "node server.js" };
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            arguments,
            runConfig,
            toolharnessName: nameof(CodingToolHarness),
            skillName: null);

        await new ExecuteCommandPermissionMiddleware()
            .BeforeFunctionAsync(beforeContext, CancellationToken.None)
            .ConfigureAwait(false);

        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = arguments,
            State = agentContext.State,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator
        };
        var executionContext = new FunctionExecutionContext(beforeContext, request);

        var result = await new CodingToolHarness()
            .ExecuteCommand(context: executionContext, command: "node server.js")
            .ConfigureAwait(false);

        result.ToString().Should().Contain("Server running");
        runner.StartCalls.Should().Be(2);
        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Isolation.Interactive.AllowLocalBinding.Should().BeTrue();
    }

    [Fact]
    public void ExecuteCommandPermissionBoundary_SandboxModeChangesPermissionFingerprint()
    {
        var sandboxed = ExecuteCommandPermissionMiddleware.ExecuteCommandPermissionAnalyzer.Analyze(
            new Dictionary<string, object?> { ["command"] = "npm install" },
            CreateWorkspaceRunConfig(),
            new ExecuteCommandOptions(),
            new ExecuteCommandShellScope { Executable = "zsh", Family = ExecuteCommandShellFamily.Zsh });
        var unsandboxedConfig = CreateWorkspaceRunConfig();
        unsandboxedConfig.ContextOverrides![ExecuteCommandSandboxPolicy.ContextKey] = new ExecuteCommandSandboxPolicy
        {
            Mode = ExecuteCommandIsolationMode.Disabled
        };
        var unsandboxed = ExecuteCommandPermissionMiddleware.ExecuteCommandPermissionAnalyzer.Analyze(
            new Dictionary<string, object?> { ["command"] = "npm install" },
            unsandboxedConfig,
            new ExecuteCommandOptions(),
            new ExecuteCommandShellScope { Executable = "zsh", Family = ExecuteCommandShellFamily.Zsh });

        sandboxed.Fingerprint.Should().NotBe(unsandboxed.Fingerprint);
        sandboxed.Risk.Should().NotHaveFlag(ExecuteCommandPermissionRisk.Unsandboxed);
        unsandboxed.Risk.Should().HaveFlag(ExecuteCommandPermissionRisk.Unsandboxed);
    }

    [Fact]
    public async Task ExecuteCommand_RunConfigSandboxDictionary_ReturnsValidationError()
    {
        var runConfig = CreateWorkspaceRunConfig();
        runConfig.ContextOverrides![ExecuteCommandSandboxPolicy.ContextKey] = new Dictionary<string, object?>
        {
            ["mode"] = "disabled"
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(new FakeProcessProvider(), runConfig: runConfig),
            command: "dotnet test");

        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain("ExecuteCommand sandbox policy must be an ExecuteCommandSandboxPolicy object.");
    }

    [Fact]
    public async Task ExecuteCommand_RootQualifiedWorkingDirectory_UsesSelectedRoot()
    {
        var docsRoot = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var runner = new FakeProcessProvider();

        await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, runConfig: CreateWorkspaceRunConfig(_tempRoot, docsRoot)),
            command: "pwd",
            workingDirectory: "@docs");

        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Command.WorkingDirectory.Should().Be(Path.GetFullPath(docsRoot));
    }

    [Fact]
    public async Task ExecuteCommand_WorkingDirectoryOutsideWorkspace_Rejects()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"hpd-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var runner = new FakeProcessProvider();

            var result = await new CodingToolHarness().ExecuteCommand(
                context: CreateContext(runner, runConfig: CreateWorkspaceRunConfig(_tempRoot)),
                command: "pwd",
                workingDirectory: outside);

            result.ToString().Should().Contain("kind=\"working_directory_not_found\"");
            result.ToString().Should().Contain("outside the configured workspace");
            runner.StartCalls.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [InlineData(ExecuteCommandAction.Run, "dotnet test", "cmd_1", null, false, 200, 0, "Run does not accept backgroundHandleId.")]
    [InlineData(ExecuteCommandAction.ListBackground, "dotnet test", null, null, false, 200, 0, "ListBackground accepts no command")]
    [InlineData(ExecuteCommandAction.ListBackground, null, null, null, false, 10, 0, "ListBackground accepts no command")]
    [InlineData(ExecuteCommandAction.ReadOutput, null, null, null, false, 200, 0, "ReadOutput requires backgroundHandleId.")]
    [InlineData(ExecuteCommandAction.ReadOutput, "tail", "cmd_1", null, false, 200, 0, "ReadOutput does not accept command")]
    [InlineData(ExecuteCommandAction.Stop, null, null, null, false, 200, 0, "Stop requires backgroundHandleId.")]
    [InlineData(ExecuteCommandAction.Stop, null, "cmd_1", null, false, 10, 0, "Stop does not accept command")]
    public async Task ExecuteCommand_InvalidActionArguments_ReturnValidationErrors(
        ExecuteCommandAction action,
        string? command,
        string? backgroundHandleId,
        string? workingDirectory,
        bool startsInBackground,
        int tailLines,
        int delayMilliseconds,
        string expectedMessage)
    {
        var runner = new FakeProcessProvider();

        var result = await new CodingToolHarness().ExecuteCommand(
            action: action,
            command: command,
            backgroundHandleId: backgroundHandleId,
            workingDirectory: workingDirectory,
            invocationMode: startsInBackground ? "background" : null,
            tailLines: tailLines,
            delayMilliseconds: delayMilliseconds,
            context: CreateContext(runner));

        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain(expectedMessage);
        runner.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 200, 0, "timeoutMilliseconds must be positive.")]
    [InlineData(1_800_001, 200, 0, "timeoutMilliseconds must be less than or equal to 1800000.")]
    [InlineData(1_000, 0, 0, "tailLines must be between 1 and 2000.")]
    [InlineData(1_000, 2_001, 0, "tailLines must be between 1 and 2000.")]
    [InlineData(1_000, 200, -1, "delayMilliseconds must be between 0 and 10000.")]
    [InlineData(1_000, 200, 10_001, "delayMilliseconds must be between 0 and 10000.")]
    public async Task ExecuteCommand_InvalidNumericArguments_ReturnValidationErrors(
        int timeoutMilliseconds,
        int tailLines,
        int delayMilliseconds,
        string expectedMessage)
    {
        var runner = new FakeProcessProvider();

        var result = await new CodingToolHarness().ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: "cmd_1",
            timeoutMilliseconds: timeoutMilliseconds,
            tailLines: tailLines,
            delayMilliseconds: delayMilliseconds,
            context: CreateContext(runner));

        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain(expectedMessage);
        runner.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_UsesProcessProviderAndFormatsOutput()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "hello\n", stderr: "warn\n", exitCode: 0)
        };
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxInlineCommandOutputChars = 1024
        });

        var result = await toolharness.ExecuteCommand(
            context: CreateContext(runner),
            command: "printf hello",
            timeoutMilliseconds: 1_000,
            environment: new Dictionary<string, string> { ["FOO"] = "bar" });

        var xml = result.ToString();
        xml.Should().Contain("<execute_command");
        xml.Should().Contain("command=\"printf hello\"");
        xml.Should().Contain($"cwd=\"{Directory.GetCurrentDirectory()}\"");
        xml.Should().Contain("exit_code=\"0\"");
        xml.Should().Contain("completion_kind=\"completed\"");
        xml.Should().Contain("hello");
        xml.Should().Contain("warn");
        xml.Should().Contain("local_path=");
        xml.Should().Contain("<combined_output");

        runner.StartCalls.Should().Be(1);
        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Policy.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_000));
        runner.LastOutputSink.Should().NotBeNull();
        runner.LastSpec.Io.StandardOutput.MaxCapturedBytes.Should().BeGreaterThan(0);
        runner.LastSpec.Command.Arguments.Should().Contain("printf hello");
        runner.LastSpec.Command.WorkingDirectory.Should().Be(Directory.GetCurrentDirectory());
        runner.LastSpec.Command.Environment.Should().ContainKey("PAGER");
        runner.LastSpec.Command.Environment.Should().ContainKey("GIT_TERMINAL_PROMPT");
        runner.LastSpec.Command.Environment["FOO"].Should().Be("bar");
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_SanitizesTerminalControlSequences()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "\u001B[31mwarning\u001B[0m\u0007\n", exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "printf warning");

        var xml = result.ToString();
        xml.Should().Contain("warning");
        xml.Should().NotContain("\u001B");
        xml.Should().NotContain("[31m");
        xml.Should().NotContain("\u0007");
        XDocument.Parse(xml).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_CommitsArtifactsWhenSessionContentStoreExists()
    {
        var store = new InMemorySessionStore();
        var contentStore = new InMemoryContentStore();
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "artifact stdout\n", stderr: "artifact stderr\n", exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, store, contentStore: contentStore),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().NotContain("artifact_path=");
        xml.Should().Contain("content_id=");

        var artifacts = await contentStore.QueryAsync(ContentScope.Create("session-1"), new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["kind"] = "artifact" }
        });

        artifacts.Select(item => item.Name).Should().Contain(name => name.EndsWith("/stdout.txt", StringComparison.Ordinal));
        artifacts.Select(item => item.Name).Should().Contain(name => name.EndsWith("/stderr.txt", StringComparison.Ordinal));
        artifacts.Select(item => item.Name).Should().Contain(name => name.EndsWith("/combined.log", StringComparison.Ordinal));
        artifacts.Select(item => item.Name).Should().Contain(name => name.EndsWith("/metadata.json", StringComparison.Ordinal));
        foreach (var artifact in artifacts)
        {
            artifact.Tags.Should().NotBeNull();
            artifact.Tags!["artifact-kind"].Should().Be("execute_command_output");
            artifact.Tags["command-id"].Should().StartWith("cmd_");
            artifact.Tags["cwd"].Should().Be(Directory.GetCurrentDirectory());
        }
    }

    [Fact]
    public async Task ExecuteCommand_OutputStoreCommitFailure_ReturnsStructuredWarning()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "preview survives\n", exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner, contentStore: new ThrowingContentStore()),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().Contain("preview survives");
        xml.Should().Contain("<output_store");
        xml.Should().Contain("content_store_available=\"true\"");
        xml.Should().Contain("warning=\"Failed to commit command output artifacts: commit blocked\"");
        xml.Should().Contain("local_path=");
    }

    [Fact]
    public void ExecuteCommandOutputStore_RejectsLocalPathEscape()
    {
        var root = Path.Combine(_tempRoot, "spool", "cmd_1");
        var sibling = Path.Combine(_tempRoot, "spool", "cmd_10", "combined.log");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
        File.WriteAllText(sibling, "outside");

        ExecuteCommandOutputStoreSession.IsPathUnderDirectory(root, Path.Combine(root, "combined.log"))
            .Should().BeTrue();
        ExecuteCommandOutputStoreSession.IsPathUnderDirectory(root, sibling)
            .Should().BeFalse();
        var act = () => ExecuteCommandOutputStoreSession.ValidateLocalOutputPath(root, sibling);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Output path is outside the command spool directory.");
    }

    [Fact]
    public async Task ExecuteCommand_NonzeroExit_ReturnsResultNotException()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stderr: "failed\n", exitCode: 2)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().Contain("exit_code=\"2\"");
        xml.Should().Contain("Command failed with exit code 2.");
    }

    [Fact]
    public async Task ExecuteCommand_RgNoMatchesExitOne_IsNotInterpretedAsError()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "", exitCode: 1)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "rg MissingSymbol src");

        var xml = result.ToString();
        xml.Should().Contain("exit_code=\"1\"");
        xml.Should().Contain("is_error=\"false\"");
        xml.Should().Contain("not considered an error for rg");
    }

    [Fact]
    public async Task ExecuteCommand_LargeOutput_PreservesHeadAndTailPreview()
    {
        var stdout = "HEAD\n" + new string('x', 40_000) + "\nTAIL";
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: stdout, exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().Contain("HEAD");
        xml.Should().Contain("TAIL");
        xml.Should().Contain("chars omitted");
        xml.Should().Contain("truncated=\"true\"");
    }

    [Fact]
    public async Task ExecuteCommand_BinaryLookingOutput_IsSummarized()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: CreateStream([0, 1, 2, 3, 4], "\0\u0001\u0002\u0003\u0004"), exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "cat image.png");

        var xml = result.ToString();
        xml.Should().Contain("binary=\"true\"");
        xml.Should().Contain("Binary-looking output omitted from model result.");
    }

    [Fact]
    public async Task ExecuteCommand_InvalidUtf8Output_DoesNotCrashFormatting()
    {
        var text = System.Text.Encoding.UTF8.GetString([0xFF, 0xFE, (byte)'o', (byte)'k']);
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: CreateStream([0xFF, 0xFE, (byte)'o', (byte)'k'], text), exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "printf bytes");

        result.ToString().Should().Contain("<execute_command");
        result.ToString().Should().Contain("ok");
    }

    [Fact]
    public async Task ExecuteCommand_MissingRunner_FailsClosed()
    {
        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner: null),
            command: "dotnet test");

        result.ToString().Should().Contain("kind=\"missing_runner\"");
    }

    [Fact]
    public async Task ExecuteCommand_QuietSuccessForKnownSilentCommand_MarksNoOutputExpected()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "", stderr: "", exitCode: 0)
        };

        var result = await new CodingToolHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "mkdir -p src/generated");

        result.ToString().Should().Contain("no_output_expected=\"true\"");
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_ListAndRead_AreSessionScoped()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry();
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(100)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            invocationMode: "background");
        var startXml = start.ToString();

        startXml.Should().Contain("background=\"true\"");
        startXml.Should().Contain("background_handle_id=\"");
        startXml.Should().Contain("startup_status=\"launched_not_verified\"");
        startXml.Should().Contain("Background start only means the process launched.");
        startXml.Should().Contain("action=\"ReadOutput\"");
        var handleId = ExtractAttribute(startXml!, "background_handle_id");

        var list = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ListBackground,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));
        var listXml = list.ToString();
        listXml.Should().Contain(handleId);
        listXml.Should().Contain("npm run dev");

        var otherSessionList = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ListBackground,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: $"other-{Guid.NewGuid():N}"));
        otherSessionList.ToString().Should().Contain("count=\"0\"");

        completion.SetResult(CreateResult(stdout: "server ready\n", exitCode: 0));
        await registry.WhenIdleAsync();

        var read = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));
        var readXml = read.ToString();
        readXml.Should().Contain("server ready");
        readXml.Should().Contain("status=\"completed\"");
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRead_SanitizesTerminalControlSequences()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider { Completion = completion.Task };
        var registry = new TestBackgroundTaskRegistry();
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });
        var context = CreateContext(runner, backgroundTasks: registry, sessionId: sessionId);

        var start = await toolharness.ExecuteCommand(
            context: context,
            command: "python3 app.py",
            invocationMode: "background");
        var handleId = ExtractAttribute(start.ToString()!, "background_handle_id");
        completion.SetResult(CreateResult(stderr: "\u001B[31mserver warning\u001B[0m\u0007\n", exitCode: 0));
        await registry.WhenIdleAsync();

        var read = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: handleId,
            context: context);

        var xml = read.ToString();
        xml.Should().Contain("server warning");
        xml.Should().NotContain("\u001B");
        xml.Should().NotContain("[31m");
        xml.Should().NotContain("\u0007");
        XDocument.Parse(xml).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_ImmediateExitReturnsCommandResult()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(
                stderr: "PermissionError: [Errno 1] Operation not permitted\n",
                exitCode: 1)
        };
        var registry = new TestBackgroundTaskRegistry();
        var context = CreateContext(runner, backgroundTasks: registry, sessionId: sessionId);
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(100)
        });

        var result = await toolharness.ExecuteCommand(
            context: context,
            command: "python3 -m http.server 8080",
            invocationMode: "background");
        var xml = result.ToString();

        xml.Should().NotContain("background_handle_id=\"");
        xml.Should().Contain("exit_code=\"1\"");
        xml.Should().Contain("PermissionError");
        xml.Should().Contain("Command failed with exit code 1.");
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_LocalBindingSandboxApprovalRetriesAsBackground()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var serverCompletion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completions =
            [
                Task.FromResult(CreateResult(
                    stderr: "PermissionError: [Errno 1] Operation not permitted\n  self.socket.bind(self.server_address)\n",
                    exitCode: 1)),
                serverCompletion.Task
            ]
        };
        var registry = new TestBackgroundTaskRegistry();
        var coordinator = new EventCoordinator();
        using var sandboxSubscription = coordinator.Subscribe<ExecuteCommandSandboxCapabilityRequestEvent>(request =>
        {
            request.Capability.Should().Be(ExecuteCommandSandboxCapabilityKind.LocalBinding);
            var result = coordinator.Respond(new ExecuteCommandSandboxCapabilityResponseEvent(
                request.RequestId,
                request.SourceName,
                true));
            result.Accepted.Should().BeTrue(result.Message);
            return ValueTask.CompletedTask;
        });
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, eventCoordinator: coordinator, sessionId: sessionId),
            command: "python3 -m http.server 8000",
            invocationMode: "background");
        var startXml = start.ToString();

        startXml.Should().Contain("background=\"true\"");
        startXml.Should().Contain("background_handle_id=\"");
        runner.StartCalls.Should().Be(2);
        runner.LastSpec.Should().NotBeNull();
        runner.LastSpec!.Isolation.Interactive.AllowLocalBinding.Should().BeTrue();

        serverCompletion.SetResult(CreateResult(stdout: "serving\n", exitCode: 0));
        await registry.WhenIdleAsync();
    }

    [Fact]
    public async Task ExecuteCommand_ReadOutput_RequiresSameSessionBackgroundOwnership()
    {
        var ownerSessionId = $"session-{Guid.NewGuid():N}";
        var otherSessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry();
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: ownerSessionId),
            command: "npm run dev",
            invocationMode: "background");
        var handleId = ExtractAttribute(start.ToString()!, "background_handle_id");

        completion.SetResult(CreateResult(stdout: "private output\n", exitCode: 0));
        await registry.WhenIdleAsync();

        var otherSessionRead = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: otherSessionId));
        var ownerRead = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: ownerSessionId));

        otherSessionRead.ToString().Should().Contain("kind=\"background_task_not_found\"");
        otherSessionRead.ToString().Should().Contain("Background command was not found for this session.");
        ownerRead.ToString().Should().Contain("private output");
        runner.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteCommand_Stop_RequiresSameSessionBackgroundOwnership()
    {
        var ownerSessionId = $"session-{Guid.NewGuid():N}";
        var otherSessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry(startTasks: false);
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: ownerSessionId),
            command: "npm run dev",
            invocationMode: "background");
        var handleId = ExtractAttribute(start.ToString()!, "background_handle_id");

        var otherSessionStop = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: otherSessionId));

        otherSessionStop.ToString().Should().Contain("kind=\"background_task_not_found\"");
        runner.LastHandle.Should().NotBeNull();
        runner.LastHandle!.StopCalls.Should().Be(0);

        completion.SetResult(CreateResult(stdout: "stopped\n", exitCode: null, completionKind: ProcessCompletionKind.Stopped));
        var ownerStop = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: ownerSessionId));

        ownerStop.ToString().Should().Contain("completion_kind=\"stopped\"");
        runner.LastHandle.StopCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(ExecuteCommandAction.ListBackground, null)]
    [InlineData(ExecuteCommandAction.ReadOutput, "cmd_missing")]
    [InlineData(ExecuteCommandAction.Stop, "cmd_missing")]
    public async Task ExecuteCommandPermissionBoundary_NonRunActionsDoNotPromptOrStartNewProcess(
        ExecuteCommandAction action,
        string? backgroundHandleId)
    {
        var runner = new FakeProcessProvider();
        var promptCount = 0;
        var coordinator = new EventCoordinator();
        using var subscription = coordinator.Subscribe<ExecuteCommandPermissionRequestEvent>(_ =>
        {
            promptCount++;
            return ValueTask.CompletedTask;
        });
        var function = CreateExecuteCommandFunction();
        var runConfig = CreateWorkspaceRunConfig();
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            coordinator,
            new Session("session-1"),
            new Thread("session-1", "test-agent") { Id = "thread-1" },
            CancellationToken.None);
        agentContext.RuntimeCapabilities.Set<IProcessProvider>(runner);
        var arguments = new Dictionary<string, object?> { ["action"] = action };
        if (backgroundHandleId is not null)
            arguments["backgroundHandleId"] = backgroundHandleId;

        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            arguments,
            runConfig,
            toolharnessName: nameof(CodingToolHarness),
            skillName: null);
        await new ExecuteCommandPermissionMiddleware()
            .BeforeFunctionAsync(beforeContext, CancellationToken.None)
            .ConfigureAwait(false);

        beforeContext.BlockExecution.Should().BeFalse();
        promptCount.Should().Be(0);
        var executionContext = new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = "call-1",
                Arguments = arguments,
                State = agentContext.State,
                RunConfig = runConfig,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = coordinator
            });

        var result = await new CodingToolHarness()
            .ExecuteCommand(
                action: action,
                backgroundHandleId: backgroundHandleId,
                context: executionContext)
            .ConfigureAwait(false);

        result.ToString().Should().NotContain("<execute_command_permission_denied");
        runner.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_EnforcesActiveCommandCap()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry(startTasks: false);
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxActiveBackgroundCommands = 1,
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });

        var first = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            invocationMode: "background");
        var second = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            invocationMode: "background");

        first.ToString().Should().Contain("background=\"true\"");
        second.ToString().Should().Contain("kind=\"background_limit_exceeded\"");
        runner.StartCalls.Should().Be(1);

        completion.SetResult(CreateResult(stdout: "", exitCode: 0));
    }

    [Fact]
    public async Task ExecuteCommand_StopBackgroundCommand_CallsProcessStop()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry(startTasks: false);
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            BackgroundStartSettleDelay = TimeSpan.FromMilliseconds(1)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            invocationMode: "background");
        var handleId = ExtractAttribute(start.ToString()!, "background_handle_id");

        completion.SetResult(CreateResult(stdout: "stopped\n", exitCode: null, completionKind: ProcessCompletionKind.Stopped));
        var stop = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        runner.LastHandle.Should().NotBeNull();
        runner.LastHandle!.StopCalls.Should().Be(1);
        stop.ToString().Should().Contain("completion_kind=\"stopped\"");
        registry.LastDescriptor.Should().NotBeNull();
        registry.LastDescriptor!.Metadata.Should().ContainKey(BackgroundTaskNotificationMetadataKeys.SuppressNotification)
            .WhoseValue.Should().Be("true");
        registry.LastDescriptor.Metadata.Should().ContainKey(BackgroundTaskNotificationMetadataKeys.SuppressNotificationReason)
            .WhoseValue.Should().Be("handled-by-foreground-stop");

        var secondStop = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        runner.LastHandle.StopCalls.Should().Be(1);
        secondStop.ToString().Should().Contain("completion_kind=\"stopped\"");
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundCommand_AutoBackgroundsWithoutRespawning()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry();
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            AutoBackgroundAfter = TimeSpan.FromMilliseconds(1)
        });

        var start = await toolharness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev");
        var startXml = start.ToString();

        startXml.Should().Contain("background=\"true\"");
        startXml.Should().Contain("auto_backgrounded=\"true\"");
        runner.StartCalls.Should().Be(1);

        completion.SetResult(CreateResult(stdout: "auto ready\n", exitCode: 0));
        await registry.WhenIdleAsync();

        var handleId = ExtractAttribute(startXml!, "background_handle_id");
        var read = await toolharness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundHandleId: handleId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        read.ToString().Should().Contain("auto ready");
    }

    [Fact]
    public async Task ExecuteCommand_OutputChunkBudget_DoesNotDropCommandResultOutput()
    {
        var runner = new FakeProcessProvider
        {
            Result = CreateResult(stdout: "one\ntwo\nthree\n", exitCode: 0)
        };
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxOutputChunkEventsPerCommand = 1,
            MaxOutputChunkEventsPerSecond = 100,
            MaxOutputChunkEventChars = 10
        });

        var result = await toolharness.ExecuteCommand(
            context: CreateContext(runner),
            command: "printf lots");

        result.ToString().Should().Contain("three");

        var budget = new ExecuteCommandEventState(new ExecuteCommandOptions
        {
            MaxOutputChunkEventsPerCommand = 1,
            MaxOutputChunkEventsPerSecond = 100
        });
        budget.Observe(ProcessOutputStream.Stdout, 4).CombinedBytes.Should().Be(4);
        budget.TryReserveOutputEvent().Should().BeTrue();
        budget.TryReserveOutputEvent().Should().BeFalse();
        budget.OutputEventsSuppressed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_EmitsCommandEvents()
    {
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var context = CreateContext(runner);
        var started = new List<ExecuteCommandProcessStartedEvent>();
        var chunks = new List<ExecuteCommandOutputChunkEvent>();
        var exited = new List<ExecuteCommandProcessExitedEvent>();
        using var startedSub = context.EventCoordinator!.Subscribe<ExecuteCommandProcessStartedEvent>(evt =>
        {
            started.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var chunkSub = context.EventCoordinator.Subscribe<ExecuteCommandOutputChunkEvent>(evt =>
        {
            chunks.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var exitedSub = context.EventCoordinator.Subscribe<ExecuteCommandProcessExitedEvent>(evt =>
        {
            exited.Add(evt);
            return ValueTask.CompletedTask;
        });

        var commandTask = new CodingToolHarness().ExecuteCommand(
            context: context,
            command: "dotnet test");

        await WaitForAsync(() => runner.LastOutputSink is not null);
        await runner.LastOutputSink!.OnOutputAsync(CreateOutputChunk("event output\n"));
        await WaitForAsync(() => chunks.Count > 0);
        completion.SetResult(CreateResult(stdout: "event output\n", exitCode: 0));
        await commandTask;
        await WaitForAsync(() => chunks.Count > 0 && exited.Count > 0);

        started.Should().ContainSingle();
        chunks.Should().ContainSingle();
        exited.Should().ContainSingle();
        started[0].EventFlowId.Should().Be(started[0].CommandId);
        chunks[0].EventFlowId.Should().Be(started[0].CommandId);
        chunks[0].Text.Should().Contain("event output");
        chunks[0].Channel.Should().Be(EventChannel.Streaming);
        chunks[0].Kind.Should().Be(EventKind.Content);
        exited[0].Category.Should().Be(ExecuteCommandCategory.Test);
        exited[0].CombinedOutputBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteCommand_AutoBackgrounding_EmitsProgressAfterBudget()
    {
        var completion = new TaskCompletionSource<ProcessInvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessProvider
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry();
        var context = CreateContext(
            runner,
            backgroundTasks: registry,
            sessionId: $"session-{Guid.NewGuid():N}");
        var progress = new List<ExecuteCommandProgressEvent>();
        using var sub = context.EventCoordinator!.Subscribe<ExecuteCommandProgressEvent>(evt =>
        {
            progress.Add(evt);
            return ValueTask.CompletedTask;
        });
        var toolharness = new CodingToolHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            AutoBackgroundAfter = TimeSpan.FromMilliseconds(5),
            ProgressAfter = TimeSpan.Zero
        });

        await toolharness.ExecuteCommand(
            context: context,
            command: "npm run dev");

        await WaitForAsync(() => progress.Count > 0);
        progress.Should().ContainSingle();
        completion.SetResult(CreateResult(stdout: "done\n", exitCode: 0));
        await registry.WhenIdleAsync();
    }

    [Fact]
    public void ExecuteCommand_ModelFacingContract_HasNoPolicyBypassArguments()
    {
        var parameterNames = typeof(CodingToolHarness)
            .GetMethod(nameof(CodingToolHarness.ExecuteCommand))!
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        parameterNames.Should().NotContain([
            "dangerouslyDisableIsolation",
            "requiresApproval",
            "safe",
            "dangerous",
            "sandbox",
            "permissionMode"
        ]);
    }

    private static FunctionExecutionContext CreateContext(
        IProcessProvider? runner,
        ISessionStore? sessionStore = null,
        IAgentBackgroundTaskRegistry? backgroundTasks = null,
        IAgentBackgroundHandleRegistry? backgroundHandles = null,
        string sessionId = "session-1",
        AgentRunConfig? runConfig = null,
        IContentStore? contentStore = null,
        EventCoordinator? eventCoordinator = null)
    {
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "ExecuteCommand",
                Description = "Test function"
            });

        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session(sessionId) { Store = sessionStore };
        var thread = new Thread(sessionId, "test-agent") { Id = "thread-1" };
        eventCoordinator ??= new EventCoordinator();
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator,
            session,
            thread,
            CancellationToken.None,
            contentStore: contentStore);
        if (runner is not null)
            agentContext.RuntimeCapabilities.Set<IProcessProvider>(runner);

        runConfig ??= CreateWorkspaceRunConfig();
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            runConfig,
            toolharnessName: null,
            skillName: null,
            invocation: null);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = eventCoordinator,
            BackgroundTasks = backgroundTasks,
            BackgroundHandles = backgroundHandles ?? backgroundTasks as IAgentBackgroundHandleRegistry
        };

        return new FunctionExecutionContext(beforeContext, request);
    }

    private async Task<object> InvokeExecuteCommandThroughPermissionBoundaryAsync(
        FakeProcessProvider runner,
        string command,
        Func<ExecuteCommandPermissionRequestEvent, string> choiceSelector,
        AgentRunConfig? runConfig = null)
    {
        runConfig ??= CreateWorkspaceRunConfig();
        var coordinator = new EventCoordinator();
        using var subscription = RespondToPermissionRequests(coordinator, choiceSelector);
        var function = CreateExecuteCommandFunction();
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "test-agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            coordinator,
            session,
            thread,
            CancellationToken.None);
        agentContext.RuntimeCapabilities.Set<IProcessProvider>(runner);

        var arguments = new Dictionary<string, object?> { ["command"] = command };
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            arguments,
            runConfig,
            toolharnessName: nameof(CodingToolHarness),
            skillName: null);
        await new ExecuteCommandPermissionMiddleware()
            .BeforeFunctionAsync(beforeContext, CancellationToken.None)
            .ConfigureAwait(false);

        if (beforeContext.BlockExecution)
            return beforeContext.OverrideResult!;

        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = arguments,
            State = agentContext.State,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator
        };
        var executionContext = new FunctionExecutionContext(beforeContext, request);

        return await new CodingToolHarness()
            .ExecuteCommand(context: executionContext, command: command)
            .ConfigureAwait(false);
    }

    private static AIFunction CreateExecuteCommandFunction()
        => AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = nameof(CodingToolHarness.ExecuteCommand),
                Description = "Test ExecuteCommand function"
            });

    private static IDisposable RespondToPermissionRequests(
        EventCoordinator coordinator,
        Func<ExecuteCommandPermissionRequestEvent, string> choiceSelector)
        => coordinator.Subscribe<ExecuteCommandPermissionRequestEvent>(request =>
        {
            var choiceId = choiceSelector(request);
            request.AvailableChoices.Should().Contain(choice => choice.Id == choiceId);
            var result = coordinator.Respond(new ExecuteCommandPermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                choiceId));
            result.Accepted.Should().BeTrue(result.Message);
            return ValueTask.CompletedTask;
        });

    private static AgentRunConfig CreateWorkspaceRunConfig(string? defaultRoot = null, string? docsRoot = null)
    {
        var cwd = Path.GetFullPath(defaultRoot ?? Directory.GetCurrentDirectory());
        var roots = new List<AgentWorkspaceRoot>
        {
            new("default", cwd)
        };

        if (docsRoot is not null)
            roots.Add(new AgentWorkspaceRoot("docs", Path.GetFullPath(docsRoot), "Docs"));

        return new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    cwd,
                    roots)
            }
        };
    }

    private static string ExtractAttribute(string xml, string name)
    {
        var marker = $"{name}=\"";
        var start = xml.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += marker.Length;
        var end = xml.IndexOf('"', start);
        end.Should().BeGreaterThan(start);
        return xml[start..end];
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().Should().BeTrue();
    }

    private static ProcessInvocationResult CreateResult(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        ProcessCompletionKind completionKind = ProcessCompletionKind.Completed)
        => CreateResult(CreateStream(stdout), CreateStream(stderr), exitCode, completionKind);

    private static ProcessInvocationResult CreateResult(
        ProcessStreamOutput? stdout = null,
        ProcessStreamOutput? stderr = null,
        int? exitCode = 0,
        ProcessCompletionKind completionKind = ProcessCompletionKind.Completed)
    {
        return new ProcessInvocationResult
        {
            SystemProcessId = 123,
            ExitCode = exitCode,
            CompletionKind = completionKind,
            Output = new ProcessCapturedOutput
            {
                Stdout = stdout ?? CreateStream(""),
                Stderr = stderr ?? CreateStream(""),
                MergedStandardError = false,
                OutputDrainTimedOut = false,
                OutputDrainTimeout = TimeSpan.FromSeconds(2)
            }
        };
    }

    private static ProcessStreamOutput CreateStream(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return CreateStream(bytes, text);
    }

    private static ProcessStreamOutput CreateStream(byte[] bytes, string text)
    {
        return new ProcessStreamOutput
        {
            CapturedBytes = bytes,
            BytesObserved = bytes.Length,
            BytesCaptured = bytes.Length,
            BytesDiscarded = 0,
            Truncated = false
        };
    }

    private static ProcessOutputChunk CreateOutputChunk(string text) =>
        new(
            CreateProcessHandle(),
            ProcessOutputStream.Stdout,
            1,
            DateTimeOffset.UtcNow,
            System.Text.Encoding.UTF8.GetBytes(text),
            ProcessOutputChunkFlags.None);

    private static TargetHandle<ProcessInvocation> CreateProcessHandle() =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind("test.process"),
                Scope = new ResourceScope("test"),
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId { get; } = new("test.process-provider");
        public ProcessInvocationSpec? LastSpec { get; private set; }
        public IProcessOutputSink? LastOutputSink { get; private set; }
        public FakeProcessHandle? LastHandle { get; private set; }
        public int StartCalls { get; private set; }
        public ProcessInvocationResult Result { get; init; } = CreateResult(stdout: "");
        public IReadOnlyList<ProcessInvocationResult>? Results { get; init; }
        public Task<ProcessInvocationResult>? Completion { get; init; }
        public IReadOnlyList<Task<ProcessInvocationResult>>? Completions { get; init; }
        public bool EmitOutputEvents { get; init; }
        public IReadOnlyList<string> OutputChunks { get; init; } = [];

        public async ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            LastSpec = spec;
            LastOutputSink = output;
            var result = Results is { Count: > 0 }
                ? Results[Math.Min(StartCalls - 1, Results.Count - 1)]
                : Result;
            var completion = Completions is { Count: > 0 }
                ? Completions[Math.Min(StartCalls - 1, Completions.Count - 1)]
                : Completion ?? Task.FromResult(result);
            LastHandle = new FakeProcessHandle(spec, completion);
            if (EmitOutputEvents && output is not null)
            {
                foreach (var chunk in OutputChunks)
                {
                    await output.OnOutputAsync(CreateOutputChunk(chunk), cancellationToken).ConfigureAwait(false);
                }
            }
            return LastHandle;
        }

        public async ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            await using var handle = await StartAsync(spec, output, cancellationToken).ConfigureAwait(false);
            return await handle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => new(Result);
        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeProcessHandle : IProcessInvocationHandle
    {
        private readonly ProcessInvocationSpec _spec;
        private readonly Task<ProcessInvocationResult> _completion;

        public FakeProcessHandle(
            ProcessInvocationSpec spec,
            Task<ProcessInvocationResult> completion)
        {
            _spec = spec;
            _completion = completion;
        }

        public int StopCalls { get; private set; }

        public TargetHandle<ProcessInvocation> Handle { get; } = CreateProcessHandle();
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec => _spec;

        public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) => await _completion.ConfigureAwait(false);
        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestBackgroundTaskRegistry(bool startTasks = true) : IAgentBackgroundTaskRegistry, IAgentBackgroundHandleRegistry
    {
        private readonly List<Task> _tasks = [];
        private readonly Dictionary<string, RegisteredBackgroundHandle> _handles = new(StringComparer.Ordinal);
        public BackgroundTaskDescriptor? LastDescriptor { get; private set; }

        public BackgroundTaskRegistration RegisterBackgroundTask(
            BackgroundTaskDescriptor descriptor,
            Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
        {
            LastDescriptor = descriptor;

            if (!startTasks)
                return new BackgroundTaskRegistration("task-1", descriptor.Name, descriptor.SourceKind);

            var backgroundContext = new BackgroundTaskContext
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Descriptor = descriptor,
                EventCoordinator = new EventCoordinator()
            };
            _tasks.Add(taskFactory(backgroundContext, CancellationToken.None));
            return new BackgroundTaskRegistration(backgroundContext.TaskId, descriptor.Name, descriptor.SourceKind);
        }

        public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(
            BackgroundHandleDescriptor descriptor,
            IBackgroundHandle handle,
            CancellationToken cancellationToken = default)
        {
            var handleId = descriptor.HandleId ?? Guid.NewGuid().ToString("N");
            var normalized = descriptor with
            {
                HandleId = handleId,
                SourceId = descriptor.SourceId ?? handleId
            };
            var registered = new RegisteredBackgroundHandle(
                handleId,
                normalized,
                handle,
                DateTimeOffset.UtcNow);
            _handles.Add(handleId, registered);
            return ValueTask.FromResult(new BackgroundHandleRegistration(
                handleId,
                descriptor.Name,
                descriptor.Kind,
                descriptor.SourceKind));
        }

        public bool TryGetHandle(
            string handleId,
            BackgroundHandleScope scope,
            out RegisteredBackgroundHandle handle)
        {
            if (_handles.TryGetValue(handleId, out var registered) &&
                (scope.SessionId is null ||
                 string.Equals(registered.Descriptor.SessionId, scope.SessionId, StringComparison.Ordinal)) &&
                (scope.ThreadId is null ||
                 string.Equals(registered.Descriptor.ThreadId, scope.ThreadId, StringComparison.Ordinal)))
            {
                handle = registered;
                return true;
            }

            handle = null!;
            return false;
        }

        public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query)
            => _handles.Values
                .Where(handle =>
                    (query.SessionId is null ||
                     string.Equals(handle.Descriptor.SessionId, query.SessionId, StringComparison.Ordinal)) &&
                    (query.ThreadId is null ||
                     string.Equals(handle.Descriptor.ThreadId, query.ThreadId, StringComparison.Ordinal)) &&
                    (query.Kind is null || handle.Descriptor.Kind == query.Kind) &&
                    (query.SourceKind is null || handle.Descriptor.SourceKind == query.SourceKind))
                .ToList();

        public async Task WhenIdleAsync()
        {
            if (_tasks.Count > 0)
                await Task.WhenAll(_tasks);
        }
    }

    private sealed class ThrowingSessionStore : ISessionStore
    {
        public Task<Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<Session?>(null);
        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Thread?> ProjectThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default) => Task.FromResult<Thread?>(null);
        public ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(ThreadKey thread, IReadOnlyList<AgentEvent> events, ThreadAppendCondition condition = default, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ThreadEventAppendResult(
                events,
                new ThreadJournalCursor(1, 0),
                new ThreadJournalCursor(1, events.Count)));
        public ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(ThreadKey thread, IReadOnlyList<AgentEvent> events, ThreadJournalCursor expectedCursor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ThreadJournalReplaceResult(
                events,
                expectedCursor,
                new ThreadJournalCursor(expectedCursor.Generation + 1, events.Count)));
        public ValueTask<ThreadDescriptor?> GetThreadAsync(ThreadKey thread, CancellationToken cancellationToken = default) => ValueTask.FromResult<ThreadDescriptor?>(null);
        public ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(ThreadKey thread, CancellationToken cancellationToken = default) => ValueTask.FromResult<ThreadEventHead?>(null);
        public async IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(string sessionId, ThreadListRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public async IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(ThreadKey thread, ThreadEventReadRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public async IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(ThreadKey thread, ThreadJournalCursor after, ThreadObservationOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public Task DeleteThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ThrowingContentStore : IContentStore
    {
        public ValueTask<ContentInfo> WriteAsync(
            ContentScope scope,
            Stream data,
            ContentMetadata metadata,
            ContentWriteOptions options,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("commit blocked");

        public ValueTask<ContentReadResult?> OpenReadAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ContentReadResult?>(null);

        public ValueTask<Uri?> CreateReadUriAsync(
            ContentAddress address,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Uri?>(null);

        public ValueTask<ContentInfo?> StatAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ContentInfo?>(null);

        public ValueTask DeleteAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(
            ContentScope scope,
            ContentQuery? query = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ContentInfo>>([]);
    }
}
