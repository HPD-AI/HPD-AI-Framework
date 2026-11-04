namespace HPD.Environment.Virtualization.Apple.Tests.Workspaces;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime.Workspaces;
using HPD.Environment.Virtualization.Apple.Workspaces;
using Xunit;

public sealed class AppleVirtualizationWorkspaceFileStoreTests
    : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hpd-apple-workspace",
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
    public async Task Guest_file_store_reads_writes_lists_and_searches_through_process_lane()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        await using var store =
            new ProcessWorkspaceFileStore(
                new WorkspaceRootId("workspace"),
                _root,
                AccessMode.ReadWrite,
                new WorkspacePathSemantics(
                    true,
                    SymlinkEvaluationPolicy.DenySymlinks),
                new LocalShellProcessProvider(),
                UnitHandle(),
                "AppleVirtualization");
        var logical = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            "src/file.txt");
        WorkspaceMutationResult write =
            await store.WriteTextAsync(
                logical,
                "guest line\nneedle\n",
                WorkspaceWriteCondition.RequireCreate);
        write.Outcome.Should().Be(
            WorkspaceMutationOutcome.Succeeded);
        (await File.ReadAllTextAsync(
            Path.Combine(_root, "src", "file.txt")))
            .Should().Be("guest line\nneedle\n");
        store.ToExecutionPath(logical).Value
            .Should().Be(_root + "/src/file.txt");

        WorkspaceTextSnapshot text =
            (await store.ReadTextAsync(
                logical,
                new WorkspaceTextReadOptions()))!;
        text.Content.Should().Be("guest line\nneedle\n");
        text.TotalLines.Should().Be(2);
        text.Version.ContentDigest.Should().NotBeNull();

        WorkspaceBinarySnapshot binary =
            (await store.ReadBytesAsync(
                logical,
                new WorkspaceByteReadOptions
                {
                    Offset = 6,
                    MaxBytes = 4,
                }))!;
        Encoding.UTF8.GetString(binary.Content.Span)
            .Should().Be("line");

        WorkspaceListResultSet listing =
            await store.ListAsync(
                new WorkspacePath(
                    new WorkspaceRootId("workspace"),
                    string.Empty),
                new WorkspaceListOptions
                {
                    Recursive = true,
                    GlobPattern = "src/*.txt",
                });
        listing.Entries.Should().ContainSingle(entry =>
            entry.Path == logical);

        WorkspaceSearchResultSet search =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = new WorkspacePath(
                        new WorkspaceRootId("workspace"),
                        "src"),
                    Query = "needle",
                });
        search.Results.Should().ContainSingle();
        search.Results[0].Path.Should().Be(logical);
        search.Results[0].Line.Should().Be(2);

        WorkspaceMutationResult stale =
            await store.WriteTextAsync(
                logical,
                "stale",
                new WorkspaceWriteCondition
                {
                    ExpectedVersion = new WorkspaceContentVersion(
                        null,
                        new Digest("sha256", "not-current"),
                        null,
                        default),
                });
        stale.Outcome.Should().Be(
            WorkspaceMutationOutcome.Stale);
    }

    [Fact]
    public async Task Guest_search_and_listing_preserve_portable_filter_semantics()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "ignored"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".gitignore"),
            "ignored/\n*.log\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "file.txt"),
            "before\nneedle alpha\nother\nafter\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "second.txt"),
            "needle\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "nested", "deep.txt"),
            "needle\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "ignored", "file.txt"),
            "needle\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "debug.log"),
            "needle\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".hidden.txt"),
            "needle\n");
        await using var store =
            new ProcessWorkspaceFileStore(
                new WorkspaceRootId("workspace"),
                _root,
                AccessMode.ReadOnly,
                new WorkspacePathSemantics(
                    true,
                    SymlinkEvaluationPolicy.DenySymlinks),
                new LocalShellProcessProvider(),
                UnitHandle(),
                "AppleVirtualization");
        var root = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            string.Empty);

        WorkspaceSearchResultSet files =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    OutputMode =
                        WorkspaceSearchOutputMode.FilesWithMatches,
                });
        files.Files.Should().BeEquivalentTo(new[]
        {
            new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "src/file.txt"),
            new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "src/nested/deep.txt"),
            new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "src/second.txt"),
        });

        WorkspaceSearchResultSet context =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    GlobPattern = "src/file.txt",
                    BeforeContext = 1,
                    AfterContext = 1,
                    MaxDepth = 2,
                });
        context.Results.Should().ContainSingle();
        context.Results[0].ContextLines.Select(line => line.Text)
            .Should().Equal("before", "other");

        WorkspaceSearchResultSet multiline =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = @"alpha\s+other",
                    Multiline = true,
                    ExcludeGlobs =
                        ["ignored/**", "*.log", ".hidden.txt"],
                });
        multiline.Results.Should().ContainSingle();

        WorkspaceListResultSet listing =
            await store.ListAsync(
                root,
                new WorkspaceListOptions
                {
                    Recursive = true,
                    GlobPattern = "**/*.txt",
                    MaxDepth = 2,
                    MaxEntries = 1,
                });
        listing.Status.Should().Be(WorkspaceListStatus.Truncated);
        listing.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Process_backed_managed_fallback_preserves_advanced_search_contract()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Directory.CreateDirectory(
            Path.Combine(_root, "src", "nested"));
        Directory.CreateDirectory(
            Path.Combine(_root, "src", "ignored"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", ".gitignore"),
            "ignored/\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "nested", "match.txt"),
            "before\nneedle alpha\nother\nafter\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "src", "ignored", "hidden.txt"),
            "needle\n");
        await using var store =
            new ProcessWorkspaceFileStore(
                new WorkspaceRootId("workspace"),
                _root,
                AccessMode.ReadOnly,
                new WorkspacePathSemantics(
                    true,
                    SymlinkEvaluationPolicy.DenySymlinks),
                new LocalShellProcessProvider(),
                UnitHandle(),
                "AppleVirtualization",
                enableRipgrep: false);
        var root = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            string.Empty);

        WorkspaceSearchResultSet context =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    BeforeContext = 1,
                    AfterContext = 1,
                });
        context.Results.Should().ContainSingle();
        context.Results[0].ContextLines
            .Select(line => line.Text)
            .Should().Equal("before", "other");

        WorkspaceSearchResultSet multiline =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = @"alpha\s+other",
                    Multiline = true,
                    GlobPattern = "**/*.txt",
                });
        multiline.Results.Should().ContainSingle();
        multiline.Results[0].LineText.Should().Contain("other");

        WorkspaceSearchResultSet counts =
            await store.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = root,
                    Query = "needle",
                    OutputMode = WorkspaceSearchOutputMode.Count,
                });
        counts.Counts.Should().ContainSingle();
        counts.Counts[0].Count.Should().Be(1);
    }

    private static TargetHandle<ExecutionUnit> UnitHandle() =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind("execution-unit"),
                Scope = new ResourceScope("test"),
                ProviderId =
                    AppleVirtualizationProviderDescriptor.ProviderId,
            },
            TargetHandleLifetime.Lease,
            TargetHandleAuthority.Observe |
            TargetHandleAuthority.Control |
            TargetHandleAuthority.Invoke);

    private sealed class LocalShellProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId =>
            AppleVirtualizationProviderDescriptor.ProviderId;

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            var handle = new LocalShellHandle(
                spec,
                output);
            return ValueTask.FromResult<IProcessInvocationHandle>(
                handle);
        }

        public async ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            await using IProcessInvocationHandle handle =
                await StartAsync(
                    spec,
                    output,
                    cancellationToken).ConfigureAwait(false);
            return await handle.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The test provider only waits through the returned handle.");

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            EmptyOutput(cancellationToken);

        private static async IAsyncEnumerable<ProcessOutputChunk>
            EmptyOutput(
                [EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class LocalShellHandle
        : IProcessInvocationHandle
    {
        private readonly Process _process;
        private readonly Task<byte[]> _stdout;
        private readonly Task<byte[]> _stderr;
        private readonly IProcessOutputSink? _sink;

        public LocalShellHandle(
            ProcessInvocationSpec spec,
            IProcessOutputSink? sink)
        {
            Spec = spec;
            _sink = sink;
            _process = Start(spec);
            _stdout = ReadAllAsync(
                _process.StandardOutput.BaseStream);
            _stderr = ReadAllAsync(
                _process.StandardError.BaseStream);
            Handle = new TargetHandle<ProcessInvocation>(
                new TargetRoute
                {
                    Kind = new TargetKind("process"),
                    Scope = spec.Target.Route.Scope,
                    ProviderId = spec.Target.Route.ProviderId,
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Observe |
                TargetHandleAuthority.Control |
                TargetHandleAuthority.Invoke);
        }

        public TargetHandle<ProcessInvocation> Handle { get; }

        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; }

        public async ValueTask WriteStdinAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default) =>
            await _process.StandardInput.BaseStream.WriteAsync(
                bytes,
                cancellationToken).ConfigureAwait(false);

        public ValueTask CloseStdinAsync(
            CancellationToken cancellationToken = default)
        {
            _process.StandardInput.Close();
            return ValueTask.CompletedTask;
        }

        public ValueTask SignalAsync(
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeTerminalAsync(
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<ProcessInvocationResult> WaitAsync(
            CancellationToken cancellationToken = default)
        {
            if (Spec.Io.StandardInput.Kind == ProcessInputKind.None)
            {
                _process.StandardInput.Close();
            }
            await _process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
            byte[] stdout = await _stdout.ConfigureAwait(false);
            byte[] stderr = await _stderr.ConfigureAwait(false);
            if (_sink is not null && stdout.Length > 0)
            {
                await _sink.OnOutputAsync(
                    new ProcessOutputChunk(
                        Handle,
                        ProcessOutputStream.Stdout,
                        1,
                        DateTimeOffset.UtcNow,
                        stdout,
                        ProcessOutputChunkFlags.None),
                    cancellationToken).ConfigureAwait(false);
            }
            return new ProcessInvocationResult
            {
                ProcessId = new ResourceId<ProcessInvocation>(
                    Guid.NewGuid().ToString("N")),
                ProviderProcessId =
                    $"test:{_process.Id}",
                ExitCode = _process.ExitCode,
                CompletionKind =
                    _process.ExitCode == 0
                        ? ProcessCompletionKind.Completed
                        : ProcessCompletionKind.Exited,
                Output = new ProcessCapturedOutput
                {
                    Stdout = new ProcessStreamOutput
                    {
                        CapturedBytes = stdout,
                        BytesObserved = stdout.Length,
                        BytesCaptured = stdout.Length,
                    },
                    Stderr = new ProcessStreamOutput
                    {
                        CapturedBytes = stderr,
                        BytesObserved = stderr.Length,
                        BytesCaptured = stderr.Length,
                    },
                },
            };
        }

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            ProcessInvocationResult result =
                await WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            yield return new ProcessOutputChunk(
                Handle,
                ProcessOutputStream.Stdout,
                1,
                DateTimeOffset.UtcNow,
                result.Output.Stdout.CapturedBytes,
                ProcessOutputChunkFlags.Final);
        }

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }

        private static Process Start(
            ProcessInvocationSpec spec)
        {
            var start = new ProcessStartInfo
            {
                FileName = spec.Command.FileName,
                WorkingDirectory =
                    spec.Command.WorkingDirectory ??
                    Path.GetTempPath(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in spec.Command.Arguments)
            {
                start.ArgumentList.Add(argument);
            }
            var process = new Process
            {
                StartInfo = start,
            };
            process.Start();
            return process;
        }

        private static async Task<byte[]> ReadAllAsync(
            Stream stream)
        {
            using var output = new MemoryStream();
            await stream.CopyToAsync(output).ConfigureAwait(false);
            return output.ToArray();
        }
    }
}
