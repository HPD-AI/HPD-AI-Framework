using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugRealAdapterQualificationTests
{
    [RealAdapterFact("HPD_DEBUGPY_PYTHON")]
    [Trait("Category", "RealAdapter")]
    public async Task Debugpy_launch_stop_stack_continue_and_exit()
    {
        var python = System.Environment.GetEnvironmentVariable("HPD_DEBUGPY_PYTHON")!;

        var directory = Path.Combine(Path.GetTempPath(), "hpd-debugpy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var program = Path.Combine(directory, "smoke.py");
        await File.WriteAllTextAsync(program, "value = 40\nvalue += 2\nprint(value)\n");
        try
        {
            await using var transport = ProcessDebugProtocolTransport.Start(python, "-m", "debugpy.adapter");
            await using var client = new DebugProtocolClient(transport);
            var stopped = new TaskCompletionSource<StoppedEventBody>(TaskCreationOptions.RunContinuationsAsynchronously);
            var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var events = client.OnEvent(message =>
            {
                if (message.Event == "stopped")
                    stopped.TrySetResult(message.Body!.Value.Deserialize(DapJsonContext.Default.StoppedEventBody)!);
                if (message.Event == "terminated")
                    terminated.TrySetResult();
                return ValueTask.CompletedTask;
            });

            var capabilities = await client.InitializeAsync(
                new DebugInitializePolicy().Create("debugpy", new()),
                timeout: TimeSpan.FromSeconds(20));
            var descriptor = Descriptor();
            var configuration = new BuiltInDebugAdapterConfigurationComposer().ComposeLaunch(
                descriptor,
                new(program, directory, DebugTargetKind.SourceFile,
                    DebugAdapterProgramKind.SourceFile, StopOnEntry: true));
            var launch = client.SendAsync(
                DebugProtocolDescriptors.LaunchRequest,
                DebugProtocolArgumentComposer.Launch(configuration, noDebug: false),
                timeout: TimeSpan.FromSeconds(20)).AsTask();

            if (capabilities.SupportsConfigurationDoneRequest == true)
                await client.SendAsync(
                    DebugProtocolDescriptors.ConfigurationDoneRequest,
                    new ConfigurationDoneArguments(),
                    timeout: TimeSpan.FromSeconds(20));
            await launch;

            var stop = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(20));
            stop.ThreadId.Should().NotBeNull();
            var threads = await client.SendAsync(
                DebugProtocolDescriptors.ThreadsRequest,
                new DapNoArguments(),
                timeout: TimeSpan.FromSeconds(10));
            threads.Threads.Should().NotBeEmpty();
            var threadId = stop.ThreadId ?? threads.Threads[0].Id;
            var stack = await client.SendAsync(
                DebugProtocolDescriptors.StackTraceRequest,
                new StackTraceArguments { ThreadId = threadId, StartFrame = 0, Levels = 5 },
                timeout: TimeSpan.FromSeconds(10));
            stack.StackFrames.Should().NotBeEmpty();
            stack.StackFrames
                .Select(frame => frame.Source is null ? null : frame.Source.Path)
                .Should().Contain(program);

            await client.SendAsync(
                DebugProtocolDescriptors.ContinueRequest,
                new ContinueArguments { ThreadId = threadId },
                timeout: TimeSpan.FromSeconds(10));
            await terminated.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [RealAdapterFact("HPD_LLDB_DAP", "HPD_RUSTC")]
    [Trait("Category", "RealAdapter")]
    public async Task Lldb_dap_launch_stop_stack_continue_and_exit()
    {
        var adapter = System.Environment.GetEnvironmentVariable("HPD_LLDB_DAP")!;
        var rustc = System.Environment.GetEnvironmentVariable("HPD_RUSTC")!;

        var directory = Path.Combine(Path.GetTempPath(), "hpd-lldb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "smoke.rs");
        var program = Path.Combine(directory, "smoke");
        await File.WriteAllTextAsync(source, "fn main() { let value = 42; println!(\"{}\", value); }\n");
        try
        {
            using (var compiler = Process.Start(new ProcessStartInfo(rustc)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                ArgumentList = { "-g", "-o", program, source }
            }) ?? throw new InvalidOperationException("Failed to start rustc."))
            {
                await compiler.WaitForExitAsync();
                var diagnostics = await compiler.StandardError.ReadToEndAsync();
                compiler.ExitCode.Should().Be(0, diagnostics);
            }

            await QualifyLaunchAsync(
                ProcessDebugProtocolTransport.Start(adapter),
                LldbDescriptor(),
                program,
                directory);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Netcoredbg_launch_stop_stack_continue_and_exit()
    {
        var adapter = System.Environment.GetEnvironmentVariable("HPD_NETCOREDBG")!;
        var dotnet = System.Environment.GetEnvironmentVariable("HPD_DOTNET")!;

        var directory = Path.Combine(Path.GetTempPath(), "hpd-netcoredbg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Smoke.csproj");
        await File.WriteAllTextAsync(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.cs"),
            "var value = 42; System.Console.WriteLine(value);\n");
        try
        {
            using (var compiler = Process.Start(new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "build", project, "--nologo", "--verbosity", "quiet" }
            }) ?? throw new InvalidOperationException("Failed to start dotnet."))
            {
                await compiler.WaitForExitAsync();
                var diagnostics = await compiler.StandardError.ReadToEndAsync();
                compiler.ExitCode.Should().Be(0, diagnostics);
            }

            await QualifyLaunchAsync(
                ProcessDebugProtocolTransport.Start(adapter, "--interpreter=vscode"),
                NetcoredbgDescriptor(),
                Path.Combine(directory, "bin", "Debug", "net8.0", "Smoke.dll"),
                directory);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Netcoredbg_attaches_to_the_exact_vstest_handshake_process()
    {
        var adapter = System.Environment.GetEnvironmentVariable("HPD_NETCOREDBG")!;
        var dotnet = System.Environment.GetEnvironmentVariable("HPD_DOTNET")!;
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "HPD-Agent.Harness.Coding.Tests.csproj"));
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        foreach (var argument in new[]
                 {
                     "test", project,
                     "-f", "net10.0",
                     "--no-build",
                     "--no-restore",
                     "--nologo",
                     "--verbosity", "quiet",
                     "--filter",
                     "FullyQualifiedName~DebugExecutionPlanningV3Tests.Public_target_union_contains_only_v3_shapes"
                 })
            start.ArgumentList.Add(argument);
        start.Environment["VSTEST_HOST_DEBUG"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        using var runner = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start the VSTest runner.");
        try
        {
            var transcript = new List<string>();
            int? processId = null;
            using var readinessTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            while (!readinessTimeout.IsCancellationRequested)
            {
                var line = await runner.StandardOutput.ReadLineAsync(
                    readinessTimeout.Token);
                if (line is null)
                    break;
                transcript.Add(line);
                var observation = new VSTestHostDebugReadinessParser().Observe(
                    string.Join('\n', transcript),
                    DebugReadinessMultiplicity.ExactlyOne);
                if (observation.Status == DebugHostReadinessStatus.Invalid)
                    throw new InvalidOperationException(
                        "VSTest emitted an invalid host-debug readiness transcript.");
                if (observation.Ready is { } ready)
                {
                    processId = ready.SystemProcessId;
                    break;
                }
            }
            processId.Should().NotBeNull(
                $"readiness transcript was: {string.Join(" | ", transcript)}");

            await using var transport =
                ProcessDebugProtocolTransport.Start(adapter, "--interpreter=vscode");
            await using var client = new DebugProtocolClient(transport);
            var initialized = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var events = client.OnEvent(message =>
            {
                if (message.Event == "initialized")
                    initialized.TrySetResult();
                return ValueTask.CompletedTask;
            });
            var capabilities = await client.InitializeAsync(
                new DebugInitializePolicy().Create("netcoredbg", new()),
                timeout: TimeSpan.FromSeconds(20));
            var configuration =
                new BuiltInDebugAdapterConfigurationComposer().ComposeAttach(
                    NetcoredbgDescriptor(),
                    new(Path.GetDirectoryName(project)!,
                        processId.Value.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)));
            var attach = client.SendAsync(
                DebugProtocolDescriptors.AttachRequest,
                DebugProtocolArgumentComposer.Attach(configuration),
                timeout: TimeSpan.FromSeconds(20)).AsTask();
            await initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (capabilities.SupportsConfigurationDoneRequest == true)
                await client.SendAsync(
                    DebugProtocolDescriptors.ConfigurationDoneRequest,
                    new ConfigurationDoneArguments(),
                    timeout: TimeSpan.FromSeconds(20));
            await attach;

            await runner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            runner.ExitCode.Should().Be(0,
                await runner.StandardError.ReadToEndAsync());
        }
        finally
        {
            if (!runner.HasExited)
                runner.Kill(entireProcessTree: true);
        }
    }

    [RealAdapterFact("HPD_NODE", "HPD_JS_DEBUG_SERVER")]
    [Trait("Category", "RealAdapter")]
    public async Task Javascript_tcp_server_initializes()
    {
        var node = System.Environment.GetEnvironmentVariable("HPD_NODE")!;
        var adapter = System.Environment.GetEnvironmentVariable("HPD_JS_DEBUG_SERVER")!;

        await using var transport = await TcpServerDebugProtocolTransport.StartAsync(node, adapter);
        await using var client = new DebugProtocolClient(transport);
        var capabilities = await client.InitializeAsync(
            new DebugInitializePolicy().Create("javascript", new()),
            timeout: TimeSpan.FromSeconds(20));
        capabilities.Should().NotBeNull();
    }

    private static async Task QualifyLaunchAsync(
        IDebugProtocolTransport transport,
        DebugAdapterDescriptor descriptor,
        string program,
        string directory,
        DebugTargetKind targetKind = DebugTargetKind.Executable)
    {
        await using (transport)
        await using (var client = new DebugProtocolClient(transport))
        {
            var observedEvents = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource<StoppedEventBody>(TaskCreationOptions.RunContinuationsAsynchronously);
            var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var events = client.OnEvent(message =>
            {
                observedEvents.Enqueue(message.Event);
                if (message.Event == "initialized")
                    initialized.TrySetResult();
                if (message.Event == "stopped")
                    stopped.TrySetResult(message.Body!.Value.Deserialize(DapJsonContext.Default.StoppedEventBody)!);
                if (message.Event is "terminated" or "exited")
                    terminated.TrySetResult();
                return ValueTask.CompletedTask;
            });

            var capabilities = await client.InitializeAsync(
                new DebugInitializePolicy().Create(descriptor.Id, new()),
                timeout: TimeSpan.FromSeconds(20));
            var configuration = new BuiltInDebugAdapterConfigurationComposer().ComposeLaunch(
                descriptor,
                new(program, directory, targetKind,
                    targetKind == DebugTargetKind.SourceFile
                        ? DebugAdapterProgramKind.SourceFile
                        : DebugAdapterProgramKind.ExecutableFile,
                    StopOnEntry: true));
            var launch = client.SendAsync(
                DebugProtocolDescriptors.LaunchRequest,
                DebugProtocolArgumentComposer.Launch(configuration, noDebug: false),
                timeout: TimeSpan.FromSeconds(20)).AsTask();

            await initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (targetKind == DebugTargetKind.SourceFile)
                await client.SendAsync(
                    DebugProtocolDescriptors.SetBreakpointsRequest,
                    new SetBreakpointsArguments
                    {
                        Source = new Source { Path = program },
                        Breakpoints = [new SourceBreakpoint { Line = 2 }]
                    },
                    timeout: TimeSpan.FromSeconds(10));
            if (capabilities.SupportsConfigurationDoneRequest == true)
                await client.SendAsync(
                    DebugProtocolDescriptors.ConfigurationDoneRequest,
                    new ConfigurationDoneArguments(),
                    timeout: TimeSpan.FromSeconds(20));
            await launch;

            var stopOrExit = await Task.WhenAny(stopped.Task, terminated.Task)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (stopOrExit == terminated.Task)
                throw new InvalidOperationException(
                    $"Adapter terminated without stopping. Events: {string.Join(", ", observedEvents)}");
            var stop = await stopped.Task;
            var threads = await client.SendAsync(
                DebugProtocolDescriptors.ThreadsRequest,
                new DapNoArguments(),
                timeout: TimeSpan.FromSeconds(10));
            threads.Threads.Should().NotBeEmpty();
            var threadId = stop.ThreadId ?? threads.Threads[0].Id;
            var stack = await client.SendAsync(
                DebugProtocolDescriptors.StackTraceRequest,
                new StackTraceArguments { ThreadId = threadId, StartFrame = 0, Levels = 5 },
                timeout: TimeSpan.FromSeconds(10));
            stack.StackFrames.Should().NotBeEmpty();

            await client.SendAsync(
                DebugProtocolDescriptors.ContinueRequest,
                new ContinueArguments { ThreadId = threadId },
                timeout: TimeSpan.FromSeconds(10));
            await terminated.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private static DebugAdapterDescriptor Descriptor() => new()
    {
        Id = "debugpy",
        Languages = ["python"],
        FileExtensions = [".py"],
        RootMarkers = ["pyproject.toml"],
        TargetKinds = DebugTargetKind.SourceFile | DebugTargetKind.Process,
        ProgramKinds = DebugAdapterProgramKind.SourceFile,
        Provenance = new() { PackageId = "debugpy", PackageVersion = "1.8.21", AssemblyName = "qualification" }
    };

    private static DebugAdapterDescriptor LldbDescriptor() => new()
    {
        Id = "lldb-dap",
        Languages = ["rust", "c", "cpp"],
        FileExtensions = [".rs", ".c", ".cpp"],
        RootMarkers = ["Cargo.toml"],
        TargetKinds = DebugTargetKind.Executable | DebugTargetKind.Process,
        ProgramKinds = DebugAdapterProgramKind.ExecutableFile,
        Provenance = new() { PackageId = "xcode-lldb-dap", PackageVersion = "system", AssemblyName = "qualification" }
    };

    private static DebugAdapterDescriptor NetcoredbgDescriptor() => new()
    {
        Id = "netcoredbg",
        Languages = ["csharp", "fsharp", "visualbasic"],
        FileExtensions = [".cs", ".fs", ".vb"],
        RootMarkers = [".sln", ".csproj"],
        TargetKinds = DebugTargetKind.Executable | DebugTargetKind.ProjectDirectory | DebugTargetKind.Process,
        ProgramKinds = DebugAdapterProgramKind.ExecutableFile,
        Provenance = new() { PackageId = "netcoredbg", PackageVersion = "3.2.0-1092", AssemblyName = "qualification" }
    };

    internal sealed class ProcessDebugProtocolTransport : IDebugProtocolTransport
    {
        private readonly Process _process;
        private int _disposed;

        private ProcessDebugProtocolTransport(Process process) => _process = process;

        public static ProcessDebugProtocolTransport Start(string command, params string[] arguments)
        {
            var start = new ProcessStartInfo(command)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            return new(Process.Start(start) ?? throw new InvalidOperationException("Failed to start debug adapter."));
        }

        public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;

        public ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);

        public async ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _process.StandardInput.BaseStream.WriteAsync(buffer, cancellationToken);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }

        public async IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var read = await _process.StandardError.BaseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0) yield break;
                yield return new(buffer.AsMemory(0, read).ToArray(), 0, 0);
            }
        }

        public async ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return new(ProcessCompletionKind.Exited, _process.ExitCode);
        }

        public ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken = default)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await StopAsync(new(Reason: "TEST_DISPOSED")); } catch { }
            _process.Dispose();
        }
    }

    private sealed class TcpServerDebugProtocolTransport : IDebugProtocolTransport
    {
        private readonly Process _process;
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private int _disposed;

        private TcpServerDebugProtocolTransport(Process process, TcpClient client)
        {
            _process = process;
            _client = client;
            _stream = client.GetStream();
        }

        public static async Task<TcpServerDebugProtocolTransport> StartAsync(
            string node,
            string adapter,
            CancellationToken cancellationToken = default)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var start = new ProcessStartInfo(node)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(adapter);
            start.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(IPAddress.Loopback.ToString());
            var process = Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start JavaScript debug adapter.");
            var client = new TcpClient();
            try
            {
                Exception? lastError = null;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    try
                    {
                        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                        return new(process, client);
                    }
                    catch (SocketException error)
                    {
                        lastError = error;
                        await Task.Delay(100, cancellationToken);
                    }
                }
                throw new InvalidOperationException("JavaScript debug adapter did not accept a TCP connection.", lastError);
            }
            catch
            {
                client.Dispose();
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.Dispose();
                throw;
            }
        }

        public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_process.HasExited && _client.Connected;

        public ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _stream.ReadAsync(buffer, cancellationToken);

        public async ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        public async IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var read = await _process.StandardError.BaseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0) yield break;
                yield return new(buffer.AsMemory(0, read).ToArray(), 0, 0);
            }
        }

        public async ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return new(ProcessCompletionKind.Exited, _process.ExitCode);
        }

        public ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken = default)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stream.Dispose();
            _client.Dispose();
            try { await StopAsync(new(Reason: "TEST_DISPOSED")); } catch { }
            _process.Dispose();
        }
    }
}

internal sealed class RealAdapterFactAttribute : FactAttribute
{
    public RealAdapterFactAttribute(params string[] environmentVariables)
    {
        var missing = environmentVariables
            .Where(name =>
            {
                var value = System.Environment.GetEnvironmentVariable(name);
                return string.IsNullOrWhiteSpace(value) || !File.Exists(value);
            })
            .ToArray();
        if (missing.Length > 0)
            Skip = $"Real adapter prerequisites are unavailable: {string.Join(", ", missing)}";
    }
}
