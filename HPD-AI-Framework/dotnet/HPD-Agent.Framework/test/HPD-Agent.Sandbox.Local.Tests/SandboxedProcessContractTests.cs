using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.Tooling;
using HPD.Sandbox.Local.Events;
using HPD.Sandbox.Local.State;
using HPD.Events;
using HPD.Events.Core;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public sealed class RuntimeCapabilityRegistryTests
{
    [Fact]
    public void Set_And_GetRequired_ReturnsCapability()
    {
        var registry = new RuntimeCapabilityRegistry();
        var capability = new TestCapability("runner");

        registry.Set<ITestCapability>(capability);

        registry.GetRequired<ITestCapability>().Should().BeSameAs(capability);
    }

    [Fact]
    public void TryGet_MissingCapability_ReturnsFalse()
    {
        var registry = new RuntimeCapabilityRegistry();

        var found = registry.TryGet<ITestCapability>(out var capability);

        found.Should().BeFalse();
        capability.Should().BeNull();
    }

    [Fact]
    public void GetRequired_MissingCapability_ThrowsUsefulError()
    {
        var registry = new RuntimeCapabilityRegistry();

        var act = () => registry.GetRequired<ITestCapability>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ITestCapability*");
    }

    [Fact]
    public void Seal_PreventsFurtherRegistrationButKeepsExistingCapabilitiesReadable()
    {
        var registry = new RuntimeCapabilityRegistry();
        var capability = new TestCapability("runner");

        registry.Set<ITestCapability>(capability);
        registry.Seal();

        registry.IsSealed.Should().BeTrue();
        registry.GetRequired<ITestCapability>().Should().BeSameAs(capability);
        registry.Invoking(r => r.Set<ITestCapability>(new TestCapability("other")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*sealed*ITestCapability*");
    }

    private interface ITestCapability;

    private sealed record TestCapability(string Name) : ITestCapability;
}

public sealed class SandboxedProcessCommandTests
{
    [Fact]
    public void Exec_CreatesArgvCommand()
    {
        var command = SandboxedProcessCommand.Exec("dotnet", ["test"], "/tmp");

        command.FileName.Should().Be("dotnet");
        command.Arguments.Should().Equal("test");
        command.WorkingDirectory.Should().Be("/tmp");
        command.Invoking(c => c.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsMissingExecFileName()
    {
        var command = new SandboxedProcessCommand
        {
            FileName = ""
        };

        command.Invoking(c => c.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*FileName*");
    }
}

public sealed class ShellSandboxCommandsTests
{
    [Fact]
    public void Posix_CreatesExplicitShellArgvCommand()
    {
        var command = ShellSandboxCommands.Posix("echo hello", workingDirectory: "/tmp");

        command.FileName.Should().Be("/bin/sh");
        command.Arguments.Should().Equal("-lc", "echo hello");
        command.WorkingDirectory.Should().Be("/tmp");
        command.Invoking(c => c.Validate()).Should().NotThrow();
    }

    [Fact]
    public void WindowsCmd_CreatesExplicitShellArgvCommand()
    {
        var command = ShellSandboxCommands.WindowsCmd("echo hello", workingDirectory: "C:\\tmp");

        command.FileName.Should().Be("cmd.exe");
        command.Arguments.Should().Equal("/c", "echo hello");
        command.WorkingDirectory.Should().Be("C:\\tmp");
        command.Invoking(c => c.Validate()).Should().NotThrow();
    }
}

public sealed class DefaultSandboxPolicyResolverTests
{
    [Fact]
    public void Resolve_EmptyOverrideInheritsGlobalConfig()
    {
        var resolver = new DefaultSandboxPolicyResolver();
        var global = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["github.com"],
            AllowWrite = [".", "./artifacts"],
            AllowPty = true
        };

        var resolved = resolver.Resolve(global, new SandboxConfigOverride());

        resolved.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        resolved.AllowedDomains.Should().Equal("github.com");
        resolved.AllowWrite.Should().BeEquivalentTo([".", "./artifacts"]);
        resolved.AllowPty.Should().BeTrue();
    }

    [Fact]
    public void Resolve_AppendsFilesystemAllowAndDenyLists()
    {
        var resolver = new DefaultSandboxPolicyResolver();
        var global = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Blocked,
            AllowWrite = ["."],
            DenyRead = ["~/.ssh"],
            DenyWrite = [".git/hooks"]
        };
        var function = new SandboxConfigOverride
        {
            NetworkMode = SandboxNetworkMode.Blocked,
            AllowWrite = ["./tmp"],
            DenyRead = ["~/.aws"],
            DenyWrite = [".npmrc"]
        };

        var resolved = resolver.Resolve(global, function);

        resolved.AllowWrite.Should().BeEquivalentTo([".", "./tmp"]);
        resolved.DenyRead.Should().BeEquivalentTo(["~/.ssh", "~/.aws"]);
        resolved.DenyWrite.Should().BeEquivalentTo([".git/hooks", ".npmrc"]);
    }

    [Fact]
    public void Resolve_ReplacesNetworkAllowListWithMostSpecificExplicitList()
    {
        var resolver = new DefaultSandboxPolicyResolver();
        var global = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["github.com"]
        };
        var function = new SandboxConfigOverride
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["nuget.org"]
        };

        var resolved = resolver.Resolve(global, function);

        resolved.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        resolved.AllowedDomains.Should().Equal("nuget.org");
    }

    [Fact]
    public void Resolve_PerCallOverrideWinsOverFunctionConfig()
    {
        var resolver = new DefaultSandboxPolicyResolver();
        var global = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["github.com"]
        };
        var function = new SandboxConfigOverride
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["nuget.org"]
        };
        var call = new SandboxConfigOverride
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["registry.npmjs.org"]
        };

        var resolved = resolver.Resolve(global, function, call);

        resolved.AllowedDomains.Should().Equal("registry.npmjs.org");
    }

    [Fact]
    public void Resolve_ValidatesMergedConfig()
    {
        var resolver = new DefaultSandboxPolicyResolver();
        var global = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Blocked
        };
        var invalid = new SandboxConfigOverride
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = []
        };

        var act = () => resolver.Resolve(global, invalid);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AllowedDomains*");
    }
}

public sealed class LocalSandboxedProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesWrappedCommandAndCapturesOutput()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(PlatformShellCommand(PlatformEchoCommand("runner-ok")));

        result.ProcessId.Should().NotBeNullOrWhiteSpace();
        result.CompletionKind.Should().Be(SandboxedProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Trim().Should().Be("runner-ok");
        result.TimedOut.Should().BeFalse();
        result.Cancelled.Should().BeFalse();
        runner.ActiveProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_ReturnsHandleAndStreamsRuntimeEvents()
    {
        await using var runner = CreatePassthroughRunner();
        using var eventCoordinator = new EventCoordinator();
        var runtimeEvents = new List<SandboxedProcessRuntimeEvent>();
        using var subscription = eventCoordinator.Subscribe<SandboxedProcessRuntimeEvent>(evt =>
        {
            lock (runtimeEvents)
                runtimeEvents.Add(evt);

            return ValueTask.CompletedTask;
        });
        await using var handle = await runner.StartAsync(
            PlatformShellCommand(PlatformEchoCommand("stream-ok")),
            options: new SandboxedProcessOptions
            {
                EventCoordinator = eventCoordinator
            });

        var result = await handle.Completion;
        await WaitForRuntimeEventsAsync(runtimeEvents, expectedCount: 3);

        result.CompletionKind.Should().Be(SandboxedProcessCompletionKind.Completed);
        result.Output.Stdout.Text.Trim().Should().Be("stream-ok");
        handle.ProcessId.Should().Be(result.ProcessId);
        runtimeEvents.Should().ContainSingle(e => e is SandboxedProcessStartedEvent);
        runtimeEvents.OfType<SandboxedProcessOutputEvent>()
            .Any(output => output.Stream == SandboxedProcessStream.Stdout &&
                System.Text.Encoding.UTF8.GetString(output.Bytes.ToArray()).Contains("stream-ok"))
            .Should().BeTrue();
        runtimeEvents.OfType<SandboxedProcessExitedEvent>()
            .Should().ContainSingle(exited =>
                exited.CompletionKind == SandboxedProcessCompletionKind.Completed &&
                exited.ProcessId == result.ProcessId);
    }

    [Fact]
    public async Task StartAsync_StopAsyncStopsActiveProcess()
    {
        await using var runner = CreatePassthroughRunner();
        await using var handle = await runner.StartAsync(PlatformShellCommand(PlatformLongRunningCommand()));

        await WaitForActiveProcessAsync(runner);
        await handle.StopAsync();
        var result = await handle.Completion;

        result.CompletionKind.Should().Be(SandboxedProcessCompletionKind.Stopped);
        runner.ActiveProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ExecPreservesArgumentsWithSpaces()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(SandboxedProcessCommand.Exec(
            PlatformShellFileName(),
            PlatformShellArguments(PlatformEchoCommand("hello with spaces"))));

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Trim().Should().Be("hello with spaces");
    }

    [Fact]
    public async Task RunAsync_AppliesWorkingDirectory()
    {
        await using var runner = CreatePassthroughRunner();
        using var temp = new TemporaryDirectory();

        var result = await runner.RunAsync(PlatformShellCommand(
            PlatformPrintWorkingDirectoryCommand(),
            workingDirectory: temp.Path));

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Trim().Should().EndWith(System.IO.Path.GetFileName(temp.Path));
        Directory.Exists(result.Output.Stdout.Text.Trim()).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MergesEnvironmentOverrides()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(PlatformShellCommand(
            PlatformPrintEnvironmentCommand("HPD_SANDBOX_TEST_ENV"),
            environment: new Dictionary<string, string?>
            {
                ["HPD_SANDBOX_TEST_ENV"] = "env-ok"
            }));

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Trim().Should().Be("env-ok");
    }

    [Fact]
    public async Task RunAsync_WritesStandardInput()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformCatCommand()),
            options: new SandboxedProcessOptions
            {
                StandardInput = "stdin-ok"
            });

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Should().Be("stdin-ok");
    }

    [Fact]
    public async Task RunAsync_MergeStandardError_AppendsErrorToOutput()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformStdoutAndStderrCommand()),
            options: new SandboxedProcessOptions
            {
                MergeStandardError = true
            });

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Should().Contain("stdout-ok");
        result.Output.Stdout.Text.Should().Contain("stderr-ok");
        result.Output.Stderr.Text.Should().BeEmpty();
        result.Output.MergedStandardError.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MaxCapturedBytesPerStream_LimitsCapturedOutput()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformEchoCommand("1234567890")),
            options: new SandboxedProcessOptions
            {
                MaxCapturedBytesPerStream = 4
            });

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Should().Be("1234");
        result.Output.Stdout.BytesObserved.Should().BeGreaterThan(4);
        result.Output.Stdout.BytesCaptured.Should().Be(4);
        result.Output.Stdout.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MaxCapturedBytesPerStream_StillEmitsAllOutputEvents()
    {
        await using var runner = CreatePassthroughRunner();
        using var eventCoordinator = new EventCoordinator();
        var observedBytes = 0L;
        using var subscription = eventCoordinator.Subscribe<SandboxedProcessOutputEvent>(evt =>
        {
            if (evt.Stream == SandboxedProcessStream.Stdout)
                Interlocked.Add(ref observedBytes, evt.Bytes.Length);

            return ValueTask.CompletedTask;
        });

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformEchoCommand("1234567890")),
            options: new SandboxedProcessOptions
            {
                EventCoordinator = eventCoordinator,
                MaxCapturedBytesPerStream = 4
            });

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Should().Be("1234");
        await WaitForObservedBytesAsync(() => Interlocked.Read(ref observedBytes), result.Output.Stdout.BytesObserved);
        Interlocked.Read(ref observedBytes).Should().Be(result.Output.Stdout.BytesObserved);
        Interlocked.Read(ref observedBytes).Should().BeGreaterThan(result.Output.Stdout.BytesCaptured);
    }

    [Fact]
    public async Task RunAsync_OutputDrainTimeout_ReturnsPartialOutputWhenInheritedPipeStaysOpen()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(
            PlatformShellCommand("(sleep 1) & printf 'done\\n'"),
            options: new SandboxedProcessOptions
            {
                OutputDrainTimeout = TimeSpan.FromMilliseconds(100)
            });

        result.ExitCode.Should().Be(0);
        result.Output.Stdout.Text.Should().Be("done\n");
        result.Output.OutputDrainTimedOut.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndReturnsTimedOut()
    {
        await using var runner = CreatePassthroughRunner();

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformLongRunningCommand()),
            options: new SandboxedProcessOptions
            {
                Timeout = TimeSpan.FromMilliseconds(100)
            });

        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
        runner.ActiveProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessAndReturnsCancelled()
    {
        await using var runner = CreatePassthroughRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await runner.RunAsync(
            PlatformShellCommand(PlatformLongRunningCommand()),
            cancellationToken: cts.Token);

        result.Cancelled.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
        runner.ActiveProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var runner = CreatePassthroughRunner();
        await runner.DisposeAsync();

        var act = () => runner.RunAsync(PlatformShellCommand(PlatformEchoCommand("nope")));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_KillsActiveProcess()
    {
        var runner = CreatePassthroughRunner();
        var runTask = runner.RunAsync(PlatformShellCommand(PlatformLongRunningCommand()));

        await WaitForActiveProcessAsync(runner);

        await runner.DisposeAsync();
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));

        completed.Should().Be(runTask);
        _ = await runTask;
        runner.ActiveProcessCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_EmitsProcessLifecycleEvents()
    {
        var events = new List<AgentEvent>();
        await using var runner = CreatePassthroughRunner(events.Add);

        var result = await runner.RunAsync(PlatformShellCommand(PlatformEchoCommand("events-ok")));

        result.ExitCode.Should().Be(0);
        events.Should().ContainSingle(e => e is SandboxProcessStartingEvent);
        events.Should().ContainSingle(e => e is SandboxProcessStartedEvent);
        events.Should().ContainSingle(e => e is SandboxProcessCompletedEvent);
        events.OfType<SandboxProcessCompletedEvent>().Single().ExitCode.Should().Be(0);
        events.OfType<SandboxProcessEvent>().Select(e => e.ProcessId).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_AttachesViolationsRecordedDuringProcess()
    {
        var store = new SandboxViolationStore();
        store.Add(CreateViolation("before"));
        await using var runner = new LocalSandboxedProcessRunner(
            SandboxConfig.CreateDefault(),
            (invocation, _, _) =>
            {
                store.Add(CreateViolation("during"));
                return Task.FromResult(new SandboxedCommand(invocation.FileName, invocation.ArgumentList));
            },
            violationStore: store);

        var result = await runner.RunAsync(PlatformShellCommand(PlatformEchoCommand("violation-recorded")));

        result.ExitCode.Should().Be(0);
        result.Violations.Should().ContainSingle();
        result.Violations[0].Type.Should().Be(ViolationType.NetworkAccess.ToString());
        result.Violations[0].Message.Should().Be("during");
        result.Violations[0].Path.Should().Be("example.com:443");
    }

    [Fact]
    public async Task DisposeAsync_EmitsKilledEventForActiveProcess()
    {
        var events = new List<AgentEvent>();
        var runner = CreatePassthroughRunner(events.Add);
        var runTask = runner.RunAsync(PlatformShellCommand(PlatformLongRunningCommand()));

        await WaitForActiveProcessAsync(runner);

        await runner.DisposeAsync();
        _ = await runTask;

        events.Should().ContainSingle(e => e is SandboxProcessKilledEvent);
    }

    private static LocalSandboxedProcessRunner CreatePassthroughRunner(
        Action<AgentEvent>? eventSink = null,
        SandboxViolationStore? violationStore = null)
    {
        return new LocalSandboxedProcessRunner(
            SandboxConfig.CreateDefault(),
            (invocation, _, _) => Task.FromResult(new SandboxedCommand(invocation.FileName, invocation.ArgumentList)),
            eventSink: eventSink,
            violationStore: violationStore);
    }

    private static async Task WaitForActiveProcessAsync(LocalSandboxedProcessRunner runner)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (runner.ActiveProcessCount > 0)
                return;

            await Task.Delay(25);
        }

        throw new TimeoutException("The test process did not become active.");
    }

    private static async Task WaitForRuntimeEventsAsync(
        List<SandboxedProcessRuntimeEvent> events,
        int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (events)
            {
                if (events.Count >= expectedCount)
                    return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The expected process runtime events were not delivered.");
    }

    private static async Task WaitForObservedBytesAsync(Func<long> getObservedBytes, long expectedBytes)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (getObservedBytes() >= expectedBytes)
                return;

            await Task.Delay(25);
        }

        throw new TimeoutException("The expected process output bytes were not delivered.");
    }

    private static string PlatformEchoCommand(string text) =>
        OperatingSystem.IsWindows()
            ? $"echo {text}"
            : $"printf '%s\\n' '{text}'";

    private static string PlatformShellFileName() =>
        OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    private static string[] PlatformShellArguments(string command) =>
        OperatingSystem.IsWindows() ? ["/c", command] : ["-lc", command];

    private static SandboxedProcessCommand PlatformShellCommand(
        string command,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        ShellSandboxCommands.PlatformDefault(command, workingDirectory, environment);

    private static string PlatformPrintWorkingDirectoryCommand() =>
        OperatingSystem.IsWindows() ? "cd" : "pwd";

    private static string PlatformPrintEnvironmentCommand(string variableName) =>
        OperatingSystem.IsWindows()
            ? $"echo %{variableName}%"
            : $"printf '%s\\n' \"${variableName}\"";

    private static string PlatformCatCommand() =>
        OperatingSystem.IsWindows() ? "more" : "cat";

    private static string PlatformStdoutAndStderrCommand() =>
        OperatingSystem.IsWindows()
            ? "echo stdout-ok && echo stderr-ok 1>&2"
            : "printf 'stdout-ok\\n' && printf 'stderr-ok\\n' >&2";

    private static string PlatformLongRunningCommand() =>
        OperatingSystem.IsWindows()
            ? "ping -n 30 127.0.0.1 > nul"
            : "sleep 30";

    private static SandboxViolation CreateViolation(string message) => new()
    {
        Type = ViolationType.NetworkAccess,
        Message = message,
        Path = "example.com:443",
        Timestamp = DateTimeOffset.UtcNow
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hpd-sandbox-runner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
