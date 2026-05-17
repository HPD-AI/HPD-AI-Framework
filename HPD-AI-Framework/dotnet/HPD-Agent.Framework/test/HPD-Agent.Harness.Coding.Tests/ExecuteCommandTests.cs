using System.ComponentModel;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Harness.Coding.Tests;

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
        var method = typeof(CodingHarness).GetMethod(nameof(CodingHarness.ExecuteCommand));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(AIFunctionAttribute), inherit: false)
            .Should().ContainSingle();
        method.GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: false)
            .Should().ContainSingle();
        method.GetCustomAttributes(typeof(SandboxableAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public void ExecuteCommandEvents_AreOwnedByHarnessAssembly()
    {
        ((object)typeof(ExecuteCommandEvent).Assembly).Should().BeSameAs(typeof(CodingHarness).Assembly);
        ((object)typeof(ExecuteCommandProcessStartedEvent).Assembly).Should().BeSameAs(typeof(CodingHarness).Assembly);
        ((object)typeof(ExecuteCommandOutputChunkEvent).Assembly).Should().BeSameAs(typeof(CodingHarness).Assembly);
        ((object)typeof(ExecuteCommandProcessExitedEvent).Assembly).Should().BeSameAs(typeof(CodingHarness).Assembly);
    }

    [Fact]
    public void CodingHarnessJsonContext_RoundTripsExecuteCommandEvent()
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
            StreamId = "cmd_1"
        };

        var json = JsonSerializer.Serialize(
            evt,
            CodingHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            CodingHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);

        roundTrip.Should().NotBeNull();
        roundTrip!.CommandId.Should().Be("cmd_1");
        roundTrip.Category.Should().Be(ExecuteCommandCategory.Test);
        roundTrip.StreamId.Should().Be("cmd_1");
    }

    [Fact]
    public async Task ExecuteCommand_EmptyRunCommand_ReturnsValidationError()
    {
        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(new FakeSandboxedProcessRunner()),
            command: "   ");

        result.ToString().Should().Contain("<execute_command_error");
        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain("Run requires command.");
    }

    [Fact]
    public async Task ExecuteCommand_MissingWorkingDirectory_ReturnsValidationError()
    {
        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(new FakeSandboxedProcessRunner()),
            command: "dotnet test",
            workingDirectory: "missing");

        result.ToString().Should().Contain("kind=\"working_directory_not_found\"");
        result.ToString().Should().Contain(Path.Combine(_tempRoot, "missing"));
    }

    [Fact]
    public async Task ExecuteCommand_WorkingDirectoryFile_ReturnsValidationError()
    {
        var filePath = Path.Combine(_tempRoot, "not-a-directory.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(new FakeSandboxedProcessRunner()),
            command: "dotnet test",
            workingDirectory: filePath);

        result.ToString().Should().Contain("kind=\"working_directory_is_file\"");
        result.ToString().Should().Contain(filePath);
    }

    [Theory]
    [InlineData(ExecuteCommandAction.Run, "dotnet test", "cmd_1", null, false, 200, 0, "Run does not accept backgroundTaskId.")]
    [InlineData(ExecuteCommandAction.ListBackground, "dotnet test", null, null, false, 200, 0, "ListBackground accepts no command")]
    [InlineData(ExecuteCommandAction.ListBackground, null, null, null, false, 10, 0, "ListBackground accepts no command")]
    [InlineData(ExecuteCommandAction.ReadOutput, null, null, null, false, 200, 0, "ReadOutput requires backgroundTaskId.")]
    [InlineData(ExecuteCommandAction.ReadOutput, "tail", "cmd_1", null, false, 200, 0, "ReadOutput does not accept command")]
    [InlineData(ExecuteCommandAction.Stop, null, null, null, false, 200, 0, "Stop requires backgroundTaskId.")]
    [InlineData(ExecuteCommandAction.Stop, null, "cmd_1", null, false, 10, 0, "Stop does not accept command")]
    public async Task ExecuteCommand_InvalidActionArguments_ReturnValidationErrors(
        ExecuteCommandAction action,
        string? command,
        string? backgroundTaskId,
        string? workingDirectory,
        bool runInBackground,
        int tailLines,
        int delayMilliseconds,
        string expectedMessage)
    {
        var runner = new FakeSandboxedProcessRunner();

        var result = await new CodingHarness().ExecuteCommand(
            action: action,
            command: command,
            backgroundTaskId: backgroundTaskId,
            workingDirectory: workingDirectory,
            runInBackground: runInBackground,
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
        var runner = new FakeSandboxedProcessRunner();

        var result = await new CodingHarness().ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundTaskId: "cmd_1",
            timeoutMilliseconds: timeoutMilliseconds,
            tailLines: tailLines,
            delayMilliseconds: delayMilliseconds,
            context: CreateContext(runner));

        result.ToString().Should().Contain("kind=\"invalid_arguments\"");
        result.ToString().Should().Contain(expectedMessage);
        runner.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_UsesSandboxRunnerAndFormatsOutput()
    {
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "hello\n", stderr: "warn\n", exitCode: 0)
        };
        var harness = new CodingHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxInlineCommandOutputChars = 1024
        });

        var result = await harness.ExecuteCommand(
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
        runner.LastOptions.Should().NotBeNull();
        runner.LastOptions!.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_000));
        runner.LastOptions.InactivityTimeout.Should().Be(TimeSpan.FromMilliseconds(
            CodingHarnessDefaultExecuteCommandOptions.InactivityTimeoutMilliseconds));
        runner.LastOptions.EventCoordinator.Should().NotBeNull();
        runner.LastOptions.MaxCapturedBytesPerStream.Should().Be(1024);
        runner.LastCommand.Should().NotBeNull();
        runner.LastCommand!.Arguments.Should().Contain("printf hello");
        runner.LastCommand.WorkingDirectory.Should().Be(Directory.GetCurrentDirectory());
        runner.LastCommand.Environment.Should().ContainKey("PAGER");
        runner.LastCommand.Environment.Should().ContainKey("GIT_TERMINAL_PROMPT");
        runner.LastCommand.Environment["FOO"].Should().Be("bar");
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_CommitsArtifactsWhenSessionContentStoreExists()
    {
        var store = new InMemorySessionStore();
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "artifact stdout\n", stderr: "artifact stderr\n", exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner, store),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().Contain("artifact_path=\"/artifacts/commands/");
        xml.Should().Contain("content_id=");

        var contentStore = store.GetContentStore("session-1");
        contentStore.Should().NotBeNull();
        var artifacts = await contentStore!.QueryAsync("session-1", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
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
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "preview survives\n", exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner, new ThrowingSessionStore()),
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
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stderr: "failed\n", exitCode: 2)
        };

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "dotnet test");

        var xml = result.ToString();
        xml.Should().Contain("exit_code=\"2\"");
        xml.Should().Contain("Command failed with exit code 2.");
    }

    [Fact]
    public async Task ExecuteCommand_RgNoMatchesExitOne_IsNotInterpretedAsError()
    {
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "", exitCode: 1)
        };

        var result = await new CodingHarness().ExecuteCommand(
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
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: stdout, exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
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
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: CreateStream([0, 1, 2, 3, 4], "\0\u0001\u0002\u0003\u0004"), exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
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
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: CreateStream([0xFF, 0xFE, (byte)'o', (byte)'k'], text), exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "printf bytes");

        result.ToString().Should().Contain("<execute_command");
        result.ToString().Should().Contain("ok");
    }

    [Fact]
    public async Task ExecuteCommand_MissingRunner_FailsClosed()
    {
        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner: null),
            command: "dotnet test");

        result.ToString().Should().Contain("kind=\"missing_runner\"");
    }

    [Fact]
    public async Task ExecuteCommand_QuietSuccessForKnownSilentCommand_MarksNoOutputExpected()
    {
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "", stderr: "", exitCode: 0)
        };

        var result = await new CodingHarness().ExecuteCommand(
            context: CreateContext(runner),
            command: "mkdir -p src/generated");

        result.ToString().Should().Contain("no_output_expected=\"true\"");
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_ListAndRead_AreSessionScoped()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "server ready\n", exitCode: 0)
        };
        var registry = new TestBackgroundTaskRegistry();
        var harness = new CodingHarness();

        var start = await harness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            runInBackground: true);
        var startXml = start.ToString();

        startXml.Should().Contain("background=\"true\"");
        startXml.Should().Contain("background_task_id=\"");
        var taskId = ExtractAttribute(startXml!, "background_task_id");

        await registry.WhenIdleAsync();

        var list = await harness.ExecuteCommand(
            action: ExecuteCommandAction.ListBackground,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));
        var listXml = list.ToString();
        listXml.Should().Contain(taskId);
        listXml.Should().Contain("npm run dev");

        var otherSessionList = await harness.ExecuteCommand(
            action: ExecuteCommandAction.ListBackground,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: $"other-{Guid.NewGuid():N}"));
        otherSessionList.ToString().Should().Contain("count=\"0\"");

        var read = await harness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundTaskId: taskId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));
        var readXml = read.ToString();
        readXml.Should().Contain("server ready");
        readXml.Should().Contain("status=\"completed\"");
    }

    [Fact]
    public async Task ExecuteCommand_BackgroundRun_EnforcesActiveCommandCap()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<SandboxedProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeSandboxedProcessRunner
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry(startTasks: false);
        var harness = new CodingHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxActiveBackgroundCommands = 1
        });

        var first = await harness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            runInBackground: true);
        var second = await harness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            runInBackground: true);

        first.ToString().Should().Contain("background=\"true\"");
        second.ToString().Should().Contain("kind=\"background_limit_exceeded\"");
        runner.StartCalls.Should().Be(1);

        completion.SetResult(CreateResult(stdout: "", exitCode: 0));
    }

    [Fact]
    public async Task ExecuteCommand_StopBackgroundCommand_CallsProcessStop()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "stopped\n", exitCode: null, completionKind: SandboxedProcessCompletionKind.Stopped)
        };
        var registry = new TestBackgroundTaskRegistry(startTasks: false);
        var harness = new CodingHarness();

        var start = await harness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev",
            runInBackground: true);
        var taskId = ExtractAttribute(start.ToString()!, "background_task_id");

        var stop = await harness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundTaskId: taskId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        runner.LastHandle.Should().NotBeNull();
        runner.LastHandle!.StopCalls.Should().Be(1);
        stop.ToString().Should().Contain("completion_kind=\"stopped\"");

        var secondStop = await harness.ExecuteCommand(
            action: ExecuteCommandAction.Stop,
            backgroundTaskId: taskId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        runner.LastHandle.StopCalls.Should().Be(1);
        secondStop.ToString().Should().Contain("completion_kind=\"stopped\"");
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundCommand_AutoBackgroundsWithoutRespawning()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<SandboxedProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeSandboxedProcessRunner
        {
            Completion = completion.Task
        };
        var registry = new TestBackgroundTaskRegistry();
        var harness = new CodingHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            AutoBackgroundAfter = TimeSpan.FromMilliseconds(1)
        });

        var start = await harness.ExecuteCommand(
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId),
            command: "npm run dev");
        var startXml = start.ToString();

        startXml.Should().Contain("background=\"true\"");
        startXml.Should().Contain("auto_backgrounded=\"true\"");
        runner.StartCalls.Should().Be(1);

        completion.SetResult(CreateResult(stdout: "auto ready\n", exitCode: 0));
        await registry.WhenIdleAsync();

        var taskId = ExtractAttribute(startXml!, "background_task_id");
        var read = await harness.ExecuteCommand(
            action: ExecuteCommandAction.ReadOutput,
            backgroundTaskId: taskId,
            context: CreateContext(runner, backgroundTasks: registry, sessionId: sessionId));

        read.ToString().Should().Contain("auto ready");
    }

    [Fact]
    public async Task ExecuteCommand_OutputChunkBudget_DoesNotDropCommandResultOutput()
    {
        var runner = new FakeSandboxedProcessRunner
        {
            Result = CreateResult(stdout: "one\ntwo\nthree\n", exitCode: 0)
        };
        var harness = new CodingHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            MaxOutputChunkEventsPerCommand = 1,
            MaxOutputChunkEventsPerSecond = 100,
            MaxOutputChunkEventChars = 10
        });

        var result = await harness.ExecuteCommand(
            context: CreateContext(runner),
            command: "printf lots");

        result.ToString().Should().Contain("three");

        var budget = new ExecuteCommandEventState(new ExecuteCommandOptions
        {
            MaxOutputChunkEventsPerCommand = 1,
            MaxOutputChunkEventsPerSecond = 100
        });
        budget.Observe(SandboxedProcessStream.Stdout, 4).CombinedBytes.Should().Be(4);
        budget.TryReserveOutputEvent().Should().BeTrue();
        budget.TryReserveOutputEvent().Should().BeFalse();
        budget.OutputEventsSuppressed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCommand_ForegroundRun_EmitsCommandEvents()
    {
        var completion = new TaskCompletionSource<SandboxedProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeSandboxedProcessRunner
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

        var commandTask = new CodingHarness().ExecuteCommand(
            context: context,
            command: "dotnet test");

        await WaitForAsync(() => runner.LastOptions?.EventCoordinator is not null);
        runner.LastOptions!.EventCoordinator!.Emit(new SandboxedProcessOutputEvent
        {
            ProcessId = "process-1",
            Stream = SandboxedProcessStream.Stdout,
            Bytes = System.Text.Encoding.UTF8.GetBytes("event output\n")
        });
        await WaitForAsync(() => chunks.Count > 0);
        completion.SetResult(CreateResult(stdout: "event output\n", exitCode: 0));
        await commandTask;
        await WaitForAsync(() => chunks.Count > 0 && exited.Count > 0);

        started.Should().ContainSingle();
        chunks.Should().ContainSingle();
        exited.Should().ContainSingle();
        started[0].StreamId.Should().Be(started[0].CommandId);
        chunks[0].StreamId.Should().Be(started[0].CommandId);
        chunks[0].Text.Should().Contain("event output");
        chunks[0].Channel.Should().Be(EventChannel.Streaming);
        chunks[0].Kind.Should().Be(EventKind.Content);
        exited[0].Category.Should().Be(ExecuteCommandCategory.Test);
        exited[0].CombinedOutputBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteCommand_AutoBackgrounding_EmitsProgressAfterBudget()
    {
        var completion = new TaskCompletionSource<SandboxedProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeSandboxedProcessRunner
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
        var harness = new CodingHarness(null, null, executeCommandOptions: new ExecuteCommandOptions
        {
            AutoBackgroundAfter = TimeSpan.FromMilliseconds(5),
            ProgressAfter = TimeSpan.Zero
        });

        await harness.ExecuteCommand(
            context: context,
            command: "npm run dev");

        progress.Should().ContainSingle();
        completion.SetResult(CreateResult(stdout: "done\n", exitCode: 0));
        await registry.WhenIdleAsync();
    }

    [Fact]
    public void ExecuteCommand_ModelFacingContract_HasNoPolicyBypassArguments()
    {
        var parameterNames = typeof(CodingHarness)
            .GetMethod(nameof(CodingHarness.ExecuteCommand))!
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        parameterNames.Should().NotContain([
            "dangerouslyDisableSandbox",
            "requiresApproval",
            "safe",
            "dangerous",
            "sandbox",
            "permissionMode"
        ]);
    }

    private static FunctionExecutionContext CreateContext(
        ISandboxedProcessRunner? runner,
        ISessionStore? sessionStore = null,
        IAgentBackgroundTaskRegistry? backgroundTasks = null,
        string sessionId = "session-1")
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
        var branch = new Branch(sessionId) { Id = "branch-1" };
        var eventCoordinator = new EventCoordinator();
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator,
            session,
            branch,
            CancellationToken.None);
        if (runner is not null)
            agentContext.RuntimeCapabilities.Set<ISandboxedProcessRunner>(runner);

        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            harnessName: null,
            skillName: null,
            invocation: null);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = eventCoordinator,
            BackgroundTasks = backgroundTasks
        };

        return new FunctionExecutionContext(beforeContext, request);
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

    private static SandboxedProcessResult CreateResult(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        SandboxedProcessCompletionKind completionKind = SandboxedProcessCompletionKind.Completed)
        => CreateResult(CreateStream(stdout), CreateStream(stderr), exitCode, completionKind);

    private static SandboxedProcessResult CreateResult(
        SandboxedProcessStreamOutput? stdout = null,
        SandboxedProcessStreamOutput? stderr = null,
        int? exitCode = 0,
        SandboxedProcessCompletionKind completionKind = SandboxedProcessCompletionKind.Completed)
    {
        return new SandboxedProcessResult
        {
            ProcessId = "process-1",
            SystemProcessId = 123,
            ExitCode = exitCode,
            CompletionKind = completionKind,
            Output = new SandboxedProcessCapturedOutput
            {
                Stdout = stdout ?? CreateStream(""),
                Stderr = stderr ?? CreateStream(""),
                OutputDrainTimeout = TimeSpan.FromSeconds(2)
            }
        };
    }

    private static SandboxedProcessStreamOutput CreateStream(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return CreateStream(bytes, text);
    }

    private static SandboxedProcessStreamOutput CreateStream(byte[] bytes, string text)
    {
        return new SandboxedProcessStreamOutput
        {
            CapturedBytes = bytes,
            Text = text,
            BytesObserved = bytes.Length,
            BytesCaptured = bytes.Length,
            BytesDiscarded = 0,
            Truncated = false
        };
    }

    private sealed class FakeSandboxedProcessRunner : ISandboxedProcessRunner
    {
        public SandboxedProcessCommand? LastCommand { get; private set; }
        public SandboxConfigOverride? LastConfigOverride { get; private set; }
        public SandboxedProcessOptions? LastOptions { get; private set; }
        public FakeSandboxedProcessHandle? LastHandle { get; private set; }
        public int StartCalls { get; private set; }
        public SandboxedProcessResult Result { get; init; } = CreateResult(stdout: "");
        public Task<SandboxedProcessResult>? Completion { get; init; }
        public bool EmitOutputEvents { get; init; }
        public IReadOnlyList<string> OutputChunks { get; init; } = [];

        public Task<ISandboxedProcessHandle> StartAsync(
            SandboxedProcessCommand command,
            SandboxConfigOverride? configOverride = null,
            SandboxedProcessOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            LastCommand = command;
            LastConfigOverride = configOverride;
            LastOptions = options;
            LastHandle = new FakeSandboxedProcessHandle(command, options, Completion ?? Task.FromResult(Result));
            if (EmitOutputEvents && options?.EventCoordinator is not null)
            {
                foreach (var chunk in OutputChunks)
                {
                    options.EventCoordinator.Emit(new SandboxedProcessOutputEvent
                    {
                        ProcessId = Result.ProcessId,
                        Stream = SandboxedProcessStream.Stdout,
                        Bytes = System.Text.Encoding.UTF8.GetBytes(chunk)
                    });
                }
            }
            return Task.FromResult<ISandboxedProcessHandle>(LastHandle);
        }
    }

    private sealed class FakeSandboxedProcessHandle : ISandboxedProcessHandle
    {
        private readonly EventCoordinator _events = new();
        private readonly SandboxedProcessCommand _command;
        private readonly SandboxedProcessOptions? _options;
        private readonly Task<SandboxedProcessResult> _completion;

        public FakeSandboxedProcessHandle(
            SandboxedProcessCommand command,
            SandboxedProcessOptions? options,
            Task<SandboxedProcessResult> completion)
        {
            _command = command;
            _options = options;
            _completion = completion;
        }

        public int StopCalls { get; private set; }

        public string ProcessId => "process-1";
        public int? SystemProcessId => 123;
        public SandboxedProcessCommand Command => _command;
        public SandboxedProcessOptions Options => _options ?? new SandboxedProcessOptions();
        public IEventCoordinator Events => _options?.EventCoordinator ?? _events;
        public Task<SandboxedProcessResult> Completion => _completion;
        public Task StopAsync(SandboxedProcessStopReason reason = SandboxedProcessStopReason.Requested, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestBackgroundTaskRegistry(bool startTasks = true) : IAgentBackgroundTaskRegistry
    {
        private readonly List<Task> _tasks = [];

        public void RegisterBackgroundTask(Task task)
        {
            _tasks.Add(task);
        }

        public void RegisterBackgroundTask(Func<CancellationToken, Task> taskFactory)
        {
            if (startTasks)
                _tasks.Add(taskFactory(CancellationToken.None));
        }

        public void RegisterBackgroundTask(
            string name,
            FunctionInvocationSnapshot invocation,
            Func<FunctionBackgroundContext, CancellationToken, Task> taskFactory)
        {
            if (!startTasks)
                return;

            var backgroundContext = new FunctionBackgroundContext
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Name = name,
                Invocation = invocation,
                EventCoordinator = new EventCoordinator()
            };
            _tasks.Add(taskFactory(backgroundContext, CancellationToken.None));
        }

        public async Task WhenIdleAsync()
        {
            if (_tasks.Count > 0)
                await Task.WhenAll(_tasks);
        }
    }

    private sealed class ThrowingSessionStore : ISessionStore
    {
        private readonly IContentStore _contentStore = new ThrowingContentStore();

        public IContentStore? GetContentStore(string sessionId) => _contentStore;
        public Task<Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<Session?>(null);
        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default) => Task.FromResult<Branch?>(null);
        public Task SaveBranchAsync(string sessionId, Branch branch, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<string>> ListBranchIdsAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public Task DeleteBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<UncommittedTurn?>(null);
        public Task SaveUncommittedTurnAsync(UncommittedTurn turn, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ThrowingContentStore : IContentStore
    {
        public Task<string> PutAsync(
            string? scope,
            byte[] data,
            string contentType,
            ContentMetadata? metadata = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("commit blocked");

        public Task<ContentData?> GetAsync(
            string? scope,
            string contentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ContentData?>(null);

        public Task DeleteAsync(
            string? scope,
            string contentId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ContentInfo>> QueryAsync(
            string? scope = null,
            ContentQuery? query = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContentInfo>>([]);
    }
}
