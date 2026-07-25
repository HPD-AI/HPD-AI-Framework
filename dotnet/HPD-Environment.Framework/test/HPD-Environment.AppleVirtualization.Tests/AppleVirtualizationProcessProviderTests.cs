namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Processes;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationProcessProviderTests
{
    [Fact]
    public async Task Run_streams_stdout_and_returns_captured_bytes()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 0x48, 0x50, 0x44 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.ExitCode.Should().Be(0);
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(0x48, 0x50, 0x44);
        result.Output.Stdout.BytesObserved.Should().Be(3);
        result.Output.Stdout.BytesCaptured.Should().Be(3);
        result.Output.Stdout.Truncated.Should().BeFalse();
        sink.Chunks.Should().ContainSingle().Which.Stream.Should().Be(ProcessOutputStream.Stdout);
        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            AppleVirtualizationHelperOperation.ProcessWait);
    }

    [Fact]
    public async Task Run_streams_stderr_and_preserves_stream_identity()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stderr, new byte[] { 0x65, 0x72, 0x72 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink);

        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal(0x65, 0x72, 0x72);
        result.Output.Stderr.BytesObserved.Should().Be(3);
        sink.Chunks.Should().ContainSingle().Which.Stream.Should().Be(ProcessOutputStream.Stderr);
    }

    [Fact]
    public async Task Run_merges_stderr_into_stdout_when_requested()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                MergeStandardError = true,
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stderr, new byte[] { 0x6d, 0x65, 0x72, 0x67, 0x65 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec, sink);

        result.Output.MergedStandardError.Should().BeTrue();
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(0x6d, 0x65, 0x72, 0x67, 0x65);
        result.Output.Stderr.BytesObserved.Should().Be(0);
        sink.Chunks.Should().ContainSingle().Which.Stream.Should().Be(ProcessOutputStream.Stdout);
    }

    [Fact]
    public async Task Run_preserves_nonzero_exit_as_exited_result()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stderr, new byte[] { 0x21 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 7));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.ExitCode.Should().Be(7);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_preserves_timed_out_wait_result()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessResult("process-1", ProcessInvocationPhase.Stopped, ProcessCompletionKind.TimedOut, exitCode: null));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec);

        result.CompletionKind.Should().Be(ProcessCompletionKind.TimedOut);
        result.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task Run_truncates_capture_without_truncating_streamed_chunk()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with
                {
                    MaxCapturedBytes = 3,
                },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1, 2, 3, 4, 5 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec, sink);

        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 2, 3);
        result.Output.Stdout.BytesObserved.Should().Be(5);
        result.Output.Stdout.BytesCaptured.Should().Be(3);
        result.Output.Stdout.BytesDiscarded.Should().Be(2);
        result.Output.Stdout.Truncated.Should().BeTrue();
        sink.Chunks.Should().ContainSingle().Which.Bytes.ToArray().Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Run_reapplies_capture_bounds_when_output_arrives_only_in_wait_result()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with { MaxCapturedBytes = 3 },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(
            AppleVirtualizationHelperOperation.ProcessStart,
            "process-1",
            ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            "process-1",
            ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessResult(
            "process-1",
            ProcessInvocationPhase.Exited,
            ProcessCompletionKind.Exited,
            exitCode: 0,
            output: new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput
                {
                    CapturedBytes = new byte[] { 1, 2, 3, 4, 5 },
                    BytesObserved = 5,
                    BytesCaptured = 5,
                },
                Stderr = new ProcessStreamOutput(),
            }));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec);

        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 2, 3);
        result.Output.Stdout.BytesObserved.Should().Be(5);
        result.Output.Stdout.BytesCaptured.Should().Be(3);
        result.Output.Stdout.BytesDiscarded.Should().Be(2);
        result.Output.Stdout.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Run_applies_independent_stdout_and_stderr_bounds_to_wait_result()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with { MaxCapturedBytes = 2 },
                StandardError = fixture.Spec.Io.StandardError with { MaxCapturedBytes = 3 },
            },
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(
            fixture,
            spec,
            CapturedOutput(
                stdout: new byte[] { 1, 2, 3, 4 },
                stderr: new byte[] { 5, 6, 7, 8, 9 }));

        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 2);
        result.Output.Stdout.BytesObserved.Should().Be(4);
        result.Output.Stdout.BytesCaptured.Should().Be(2);
        result.Output.Stdout.BytesDiscarded.Should().Be(2);
        result.Output.Stdout.Truncated.Should().BeTrue();
        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal(5, 6, 7);
        result.Output.Stderr.BytesObserved.Should().Be(5);
        result.Output.Stderr.BytesCaptured.Should().Be(3);
        result.Output.Stderr.BytesDiscarded.Should().Be(2);
        result.Output.Stderr.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Run_wait_result_capture_disabled_discards_without_introducing_truncation()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with { Capture = false },
            },
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(
            fixture,
            spec,
            CapturedOutput(stdout: new byte[] { 1, 2, 3 }, stderr: ReadOnlyMemory<byte>.Empty));

        result.Output.Stdout.CapturedBytes.ToArray().Should().BeEmpty();
        result.Output.Stdout.BytesObserved.Should().Be(3);
        result.Output.Stdout.BytesCaptured.Should().Be(0);
        result.Output.Stdout.BytesDiscarded.Should().Be(3);
        result.Output.Stdout.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Run_wait_result_zero_capture_bound_marks_observed_output_truncated()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with
                {
                    Capture = true,
                    MaxCapturedBytes = 0,
                },
            },
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(
            fixture,
            spec,
            CapturedOutput(stdout: new byte[] { 1, 2 }, stderr: ReadOnlyMemory<byte>.Empty));

        result.Output.Stdout.CapturedBytes.ToArray().Should().BeEmpty();
        result.Output.Stdout.BytesObserved.Should().Be(2);
        result.Output.Stdout.BytesCaptured.Should().Be(0);
        result.Output.Stdout.BytesDiscarded.Should().Be(2);
        result.Output.Stdout.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Run_wait_result_preserves_upstream_truncation_and_output_drain_fields()
    {
        var fixture = CreateFixture();
        TimeSpan drainTimeout = TimeSpan.FromMilliseconds(735);
        ProcessCapturedOutput output = CapturedOutput(
            stdout: new byte[] { 1 },
            stderr: ReadOnlyMemory<byte>.Empty,
            stdoutTruncated: true) with
        {
            OutputDrainTimedOut = true,
            OutputDrainTimeout = drainTimeout,
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(fixture, fixture.Spec, output);

        result.Output.Stdout.Truncated.Should().BeTrue();
        result.Output.OutputDrainTimedOut.Should().BeTrue();
        result.Output.OutputDrainTimeout.Should().Be(drainTimeout);
    }

    [Fact]
    public async Task Run_wait_result_preserves_guest_merged_output_and_empty_stderr_accounting()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with { MergeStandardError = true },
        };
        ProcessCapturedOutput output = CapturedOutput(
            stdout: new byte[] { 1, 5, 2, 6 },
            stderr: ReadOnlyMemory<byte>.Empty) with
        {
            MergedStandardError = true,
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(fixture, spec, output);

        result.Output.MergedStandardError.Should().BeTrue();
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 5, 2, 6);
        result.Output.Stdout.BytesObserved.Should().Be(4);
        result.Output.Stderr.CapturedBytes.ToArray().Should().BeEmpty();
        result.Output.Stderr.BytesObserved.Should().Be(0);
        result.Output.Stderr.BytesCaptured.Should().Be(0);
        result.Output.Stderr.BytesDiscarded.Should().Be(0);
        result.Output.Stderr.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Run_wait_result_never_discards_separate_stderr_when_guest_did_not_merge()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with { MergeStandardError = true },
        };

        ProcessInvocationResult result = await RunWithWaitOutputAsync(
            fixture,
            spec,
            CapturedOutput(stdout: new byte[] { 1 }, stderr: new byte[] { 2 }));

        result.Output.MergedStandardError.Should().BeFalse();
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1);
        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal(2);
        result.Output.Stderr.BytesObserved.Should().Be(1);
    }

    [Fact]
    public async Task Run_captures_output_event_returned_directly_from_read_output_response()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 0x64, 0x69, 0x72, 0x65, 0x63, 0x74 }, final: true, sequence: 8));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink);

        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(0x64, 0x69, 0x72, 0x65, 0x63, 0x74);
        result.Output.Stdout.BytesObserved.Should().Be(6);
        sink.Chunks.Should().ContainSingle();
        sink.Chunks[0].Sequence.Should().Be(8);
        sink.Chunks[0].Flags.Should().HaveFlag(ProcessOutputChunkFlags.Final);
    }

    [Fact]
    public async Task Run_preserves_output_chunk_order_sequences_and_final_flags()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1, 2 }, sequence: 41));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stderr, new byte[] { 3, 4 }, final: true, sequence: 42));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink);

        sink.Chunks.Select(chunk => chunk.Sequence).Should().Equal(41, 42);
        sink.Chunks[0].Stream.Should().Be(ProcessOutputStream.Stdout);
        sink.Chunks[1].Stream.Should().Be(ProcessOutputStream.Stderr);
        sink.Chunks[1].Flags.Should().HaveFlag(ProcessOutputChunkFlags.Final);
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 2);
        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal(3, 4);
    }

    [Fact]
    public async Task Run_honors_max_captured_bytes_per_stream_independently()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with { MaxCapturedBytes = 2 },
                StandardError = fixture.Spec.Io.StandardError with { MaxCapturedBytes = 1 },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1, 2, 3 }, sequence: 1));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stderr, new byte[] { 4, 5, 6 }, final: true, sequence: 2));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec);

        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1, 2);
        result.Output.Stdout.BytesObserved.Should().Be(3);
        result.Output.Stdout.BytesCaptured.Should().Be(2);
        result.Output.Stdout.BytesDiscarded.Should().Be(1);
        result.Output.Stdout.Truncated.Should().BeTrue();
        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal(4);
        result.Output.Stderr.BytesObserved.Should().Be(3);
        result.Output.Stderr.BytesCaptured.Should().Be(1);
        result.Output.Stderr.BytesDiscarded.Should().Be(2);
        result.Output.Stderr.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Run_preserves_output_drain_timeout_from_wait_result_after_capture()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessResult("process-1", ProcessInvocationPhase.Exited, ProcessCompletionKind.Exited, exitCode: 0, outputDrainTimedOut: true));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec);

        result.Output.OutputDrainTimedOut.Should().BeTrue();
        result.Output.OutputDrainTimeout.Should().Be(fixture.Spec.Policy.OutputDrainTimeout);
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(1);
    }

    [Fact]
    public async Task Read_output_sends_bounded_request_and_completes_at_chunk_limit()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        for (int i = 0; i < 1030; i++)
        {
            fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { (byte)(i % 255) }, final: i == 1029, sequence: i + 1));
        }

        var chunks = new List<ProcessOutputChunk>();
        await foreach (ProcessOutputChunk chunk in handle.ReadOutputAsync())
        {
            chunks.Add(chunk);
        }

        AppleVirtualizationProcessLifecycleRequest request = fixture.Helper.Requests[^1].ProcessLifecycleRequest!;
        request.ProcessId.Should().Be("process-1");
        request.OutputLimit.Should().Be(1024);
        chunks.Should().HaveCount(1024);
        chunks[0].Sequence.Should().Be(1);
        chunks[^1].Sequence.Should().Be(1024);
    }

    [Fact]
    public async Task Read_output_uses_last_observed_sequence_on_subsequent_reads()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1 }, sequence: 7));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        await foreach (ProcessOutputChunk _ in handle.ReadOutputAsync())
        {
        }

        await foreach (ProcessOutputChunk _ in handle.ReadOutputAsync())
        {
        }

        fixture.Helper.Requests[^1].ProcessLifecycleRequest!.AfterOutputSequence.Should().Be(7);
    }

    [Fact]
    public async Task Signal_sends_process_signal_request()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        await fixture.Provider.SignalAsync(handle.Handle, new ProcessSignal("SIGTERM"));

        fixture.Helper.Requests[^1].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessSignal);
        fixture.Helper.Requests[^1].ProcessSignalRequest!.ProcessId.Should().Be("process-1");
        fixture.Helper.Requests[^1].ProcessSignalRequest!.Signal.Name.Should().Be("SIGTERM");
    }

    [Fact]
    public async Task Stop_sends_process_stop_request()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        await handle.StopAsync(new ProcessStopRequest(StopKind.GracefulThenKill, Reason: "test-stop", GracePeriod: TimeSpan.FromSeconds(1)));

        fixture.Helper.Requests[^1].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStop);
        fixture.Helper.Requests[^1].ProcessStopRequest!.ProcessId.Should().Be("process-1");
        fixture.Helper.Requests[^1].ProcessStopRequest!.Kind.Should().Be(StopKind.GracefulThenKill);
        fixture.Helper.Requests[^1].ProcessStopRequest!.Reason.Should().Be("test-stop");
    }

    [Fact]
    public async Task Stop_after_process_exited_is_idempotent_without_helper_dispatch()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        await handle.WaitAsync();
        int requestCount = fixture.Helper.Requests.Count;

        await handle.StopAsync(new ProcessStopRequest(StopKind.GracefulThenKill, Reason: "already-exited"));

        fixture.Helper.Requests.Should().HaveCount(requestCount);
    }

    [Fact]
    public async Task Retained_process_status_and_release_use_the_exact_provider_resource()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(
            AppleVirtualizationHelperOperation.ProcessStart,
            "process-1",
            ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        ResourceRef<ProcessInvocation> resource = handle.Resource!.Value;

        ProcessInvocationStatus running = await fixture.Provider.GetStatusAsync(handle.Handle);
        _ = await handle.WaitAsync();
        ProcessInvocationStatus exited = await fixture.Provider.GetStatusAsync(handle.Handle);
        await fixture.Provider.ReleaseAsync(resource);

        running.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);
        exited.ProcessPhase.Should().Be(ProcessInvocationPhase.Exited);
        fixture.Ledger.TryGetProcessInvocation(resource).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Signal_after_process_exited_is_structured_without_helper_dispatch()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        await handle.WaitAsync();
        int requestCount = fixture.Helper.Requests.Count;

        Func<Task> act = async () => await handle.SignalAsync(new ProcessSignal("SIGTERM"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.ProcessAlreadyExited:*");
        fixture.Helper.Requests.Should().HaveCount(requestCount);
    }

    [Fact]
    public async Task Wait_process_not_found_error_returns_structured_faulted_result()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessError(
            AppleVirtualizationHelperOperation.ProcessWait,
            "AppleVirtualization.ProcessNotFound",
            "process not found"));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.Faulted);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessNotFound");
    }

    [Fact]
    public async Task Wait_vm_stopped_error_returns_stopped_result()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessError(
            AppleVirtualizationHelperOperation.ProcessWait,
            "AppleVirtualization.VmStoppedDuringProcess",
            "vm stopped during process"));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.Stopped);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.VmStoppedDuringProcess");
    }

    [Fact]
    public async Task Run_sends_policy_timeout_to_wait_request_and_preserves_timed_out_result()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Policy = fixture.Spec.Policy with { Timeout = TimeSpan.FromSeconds(9) },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessResult("process-1", ProcessInvocationPhase.Stopped, ProcessCompletionKind.TimedOut, exitCode: null));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec);

        result.CompletionKind.Should().Be(ProcessCompletionKind.TimedOut);
        fixture.Helper.Requests[^1].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessWait);
        fixture.Helper.Requests[^1].ProcessLifecycleRequest!.Timeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task Run_cancellation_stops_process_and_returns_cancelled_result()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 1 }, final: true));
        fixture.Helper.EnqueueResponse(ProcessResult("process-1", ProcessInvocationPhase.Stopped, ProcessCompletionKind.Stopped, exitCode: null));
        using var cts = new CancellationTokenSource();
        var sink = new CancellingProcessOutputSink(cts);

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink, cts.Token);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Cancelled);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessRunCancelled");
        fixture.Helper.Requests[^1].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStop);
        fixture.Helper.Requests[^1].ProcessStopRequest!.Reason.Should().Be("run-cancelled");
    }

    [Fact]
    public async Task Signal_unsupported_helper_error_is_deterministic_and_recorded()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessError(
            AppleVirtualizationHelperOperation.ProcessSignal,
            "AppleVirtualization.ProcessSignalUnsupported",
            "unsupported signal"));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        Func<Task> act = async () => await handle.SignalAsync(new ProcessSignal("SIGUSR2"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.ProcessSignalUnsupported:*");
    }

    [Fact]
    public async Task Resize_terminal_reports_unsupported_without_sending_helper_operation()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        Func<Task> act = async () => await fixture.Provider.ResizeTerminalAsync(handle.Handle, new TerminalSpec(120, 40));

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("AppleVirtualization.ProcessResizeUnsupported:*");
        fixture.Helper.Requests.Should().ContainSingle();
        fixture.Helper.Requests[0].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStart);
        fixture.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessResize);
    }

    [Fact]
    public async Task Start_maps_process_spec_to_helper_start_request()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Identity = new ProcessIdentitySpec("hpd", "hpd", ["staff"]),
            Limits = new ProcessLimitSpec(ProcessCount: 4, MemoryBytes: 1024 * 1024, CpuTime: TimeSpan.FromSeconds(3)),
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(spec);

        handle.Resource.Should().NotBeNull();
        handle.Resource!.Value.Id.Value.Should().Be("process-1");
        AppleVirtualizationProcessStartRequest request = fixture.Helper.Requests.Single().ProcessStartRequest!;
        request.ProcessId.Should().Be("process-1");
        request.UnitId.Should().Be("unit-1");
        request.Command.FileName.Should().Be("uname");
        request.Command.Arguments.Should().Equal("-a");
        request.Command.WorkingDirectory.Should().Be("/workspace");
        request.Identity.Should().Be(spec.Identity);
        request.Limits.Should().Be(spec.Limits);
        request.Io.Should().Be(spec.Io);
        request.Policy.Should().Be(spec.Policy);
        request.SandboxPlan.Should().BeNull();
        request.RequireVerifiedProjection.Should().BeFalse();
    }

    [Fact]
    public async Task Start_with_required_isolation_sends_guest_sandbox_plan()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        new PathAccessRule
                        {
                            Kind = PathAccessRuleKind.AllowWrite,
                            Path = new HostPath("/workspace"),
                        },
                    ],
                    DangerousPaths = new DangerousPathPolicy
                    {
                        ProtectSensitiveDefaults = false,
                    },
                },
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Unrestricted,
                },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        await fixture.Provider.StartAsync(spec);

        AppleVirtualizationProcessStartRequest request = fixture.Helper.Requests.Single().ProcessStartRequest!;
        request.Isolation.Should().Be(spec.Isolation);
        request.SandboxPlan.Should().NotBeNull();
        request.SandboxPlan!.EnforcementLocation.Should().Be(HPD.Agent.Sandbox.ProcessIsolation.SandboxEnforcementLocation.Guest);
        request.SandboxPlan.ExecutionPlatform.OperatingSystem.Should().Be("linux");
        request.SandboxPlan.Plan.Filesystem.Rules.Should().ContainSingle(rule =>
            rule.Kind == PathAccessRuleKind.AllowWrite &&
            rule.Path.Value == "/workspace");
    }

    [Fact]
    public async Task Start_registers_active_process_on_owning_unit()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            fixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        unit.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Running);
        unit.Status.ActiveProcesses.Should().ContainSingle()
            .Which.Id.Value.Should().Be(handle.Resource!.Value.Id.Value);
    }

    [Fact]
    public async Task Wait_for_terminal_process_removes_active_process_from_owning_unit()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        _ = await handle.WaitAsync();

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            fixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        unit.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        unit.Status.ActiveProcesses.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_preserves_arguments_environment_and_relative_workdir_without_projection_precondition()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Command = new ProcessCommandSpec
            {
                FileName = "relative-command",
                Arguments = ["--first", "two words", "--flag=value"],
                WorkingDirectory = "relative/subdir",
                Environment = new Dictionary<string, string?>
                {
                    ["HPD_OVERRIDE"] = "2",
                    ["HPD_UNSET"] = null,
                },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        await fixture.Provider.StartAsync(spec);

        AppleVirtualizationProcessStartRequest request = fixture.Helper.Requests.Single().ProcessStartRequest!;
        request.Command.FileName.Should().Be("relative-command");
        request.Command.Arguments.Should().Equal("--first", "two words", "--flag=value");
        request.Command.WorkingDirectory.Should().Be("relative/subdir");
        request.Command.Environment.Should().ContainKey("HPD_OVERRIDE").WhoseValue.Should().Be("2");
        request.Command.Environment.Should().ContainKey("HPD_UNSET").WhoseValue.Should().BeNull();
        request.RequireVerifiedProjection.Should().BeFalse();
        request.RequiredProjectionId.Should().BeNull();
        request.RequiredProjectionGuestPath.Should().BeNull();
    }

    [Fact]
    public async Task Run_returns_failed_to_start_result_when_helper_start_fails()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessError(
            AppleVirtualizationHelperOperation.ProcessStart,
            "AppleVirtualization.GuestProcessFailedToStart",
            "guest exec failed before process start"));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec);

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.ExitCode.Should().BeNull();
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.GuestProcessFailedToStart");
        fixture.Helper.Requests.Should().ContainSingle();
        fixture.Helper.Requests[0].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Run_with_no_output_returns_empty_captured_output_counts()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
        var sink = new RecordingProcessOutputSink();

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec, sink);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.Output.Stdout.CapturedBytes.IsEmpty.Should().BeTrue();
        result.Output.Stdout.BytesObserved.Should().Be(0);
        result.Output.Stdout.BytesCaptured.Should().Be(0);
        result.Output.Stdout.BytesDiscarded.Should().Be(0);
        result.Output.Stderr.CapturedBytes.IsEmpty.Should().BeTrue();
        result.Output.Stderr.BytesObserved.Should().Be(0);
        sink.Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_with_large_output_remains_bounded_and_accounted()
    {
        var fixture = CreateFixture();
        byte[] output = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardOutput = fixture.Spec.Io.StandardOutput with
                {
                    MaxCapturedBytes = 4096,
                },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, output, final: true));
        fixture.Helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));

        ProcessInvocationResult result = await fixture.Provider.RunAsync(spec);

        result.Output.Stdout.CapturedBytes.Length.Should().Be(4096);
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(output.Take(4096));
        result.Output.Stdout.BytesObserved.Should().Be(8192);
        result.Output.Stdout.BytesCaptured.Should().Be(4096);
        result.Output.Stdout.BytesDiscarded.Should().Be(4096);
        result.Output.Stdout.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task Start_with_inline_stdin_sends_stdin_bytes_and_closes_input_deterministically()
    {
        var fixture = CreateFixture();
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Io = fixture.Spec.Io with
            {
                StandardInput = new ProcessInputSpec
                {
                    Kind = ProcessInputKind.InlineBytes,
                    InlineBytes = new byte[] { 0x68, 0x70, 0x64 },
                },
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        await fixture.Provider.StartAsync(spec);

        fixture.Helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessStdin);
        AppleVirtualizationProcessStdinRequest stdin = fixture.Helper.Requests[1].ProcessStdinRequest!;
        stdin.ProcessId.Should().Be("process-1");
        stdin.Bytes.ToArray().Should().Equal(0x68, 0x70, 0x64);
        stdin.CloseAfterWrite.Should().BeTrue();
        stdin.Sequence.Should().BePositive();
    }

    [Fact]
    public async Task Close_stdin_sends_close_request_with_empty_payload_and_sequence()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);

        await handle.CloseStdinAsync();

        fixture.Helper.Requests[^1].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessCloseStdin);
        AppleVirtualizationProcessStdinRequest stdin = fixture.Helper.Requests[^1].ProcessStdinRequest!;
        stdin.ProcessId.Should().Be("process-1");
        stdin.Bytes.IsEmpty.Should().BeTrue();
        stdin.CloseAfterWrite.Should().BeTrue();
        stdin.Sequence.Should().BePositive();
    }

    [Fact]
    public async Task Run_returns_failed_result_without_helper_when_guest_is_not_ready()
    {
        var fixture = CreateFixture(hostSeed: HostSeed.NotReady);

        ProcessInvocationResult result = await fixture.Provider.RunAsync(fixture.Spec);

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessGuestNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_with_unverified_projected_workdir_returns_failed_handle_without_helper()
    {
        var fixture = CreateFixture(seedProjection: ProjectionSeed.Projecting);

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessProjectionNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_with_verified_projected_workdir_sends_projection_precondition_to_helper()
    {
        var fixture = CreateFixture(seedProjection: ProjectionSeed.Projected);
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        await fixture.Provider.StartAsync(fixture.Spec);

        AppleVirtualizationProcessStartRequest request = fixture.Helper.Requests.Single().ProcessStartRequest!;
        request.RequireVerifiedProjection.Should().BeTrue();
        request.RequiredProjectionId.Should().Be("projection-1");
        request.RequiredProjectionGuestPath.Should().Be("/workspace");
    }

    [Fact]
    public async Task Start_validates_process_isolation_authority_before_helper_dispatch()
    {
        var fixture = CreateFixture();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            fixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        ResourceRef<AuthorityBinding> binding = SeedProjectedAuthorityBinding(fixture.Ledger, unit.TargetHandle);
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                AuthorityBindings = [binding],
            },
        };
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));

        await fixture.Provider.StartAsync(spec);

        fixture.Helper.Requests.Should().ContainSingle();
        fixture.Helper.Requests[0].Operation.Should().Be(AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Start_rejects_process_isolation_authority_when_binding_is_not_projected()
    {
        var fixture = CreateFixture();
        ResourceRef<AuthorityBinding> binding = SeedFailedAuthorityBinding(fixture.Ledger);
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                AuthorityBindings = [binding],
            },
        };

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(spec);
        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_rejects_authority_binding_attached_to_unrelated_execution_unit()
    {
        var fixture = CreateFixture();
        var otherUnit = fixture.Ledger.UpsertExecutionUnit(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-2", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
            });
        ResourceRef<AuthorityBinding> binding = SeedProjectedAuthorityBinding(fixture.Ledger, otherUnit.TargetHandle, "authority-2");
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                AuthorityBindings = [binding],
            },
        };

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(spec);
        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_rejects_revoked_or_expired_authority_binding_before_helper_dispatch()
    {
        var expiredFixture = CreateFixture();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> expiredUnit =
            expiredFixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        ResourceRef<AuthorityBinding> expiredBinding = SeedProjectedAuthorityBinding(
            expiredFixture.Ledger,
            expiredUnit.TargetHandle,
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        ProcessInvocationSpec expiredSpec = expiredFixture.Spec with
        {
            Isolation = expiredFixture.Spec.Isolation with
            {
                AuthorityBindings = [expiredBinding],
            },
        };

        IProcessInvocationHandle expiredHandle = await expiredFixture.Provider.StartAsync(expiredSpec);
        ProcessInvocationResult expiredResult = await expiredHandle.WaitAsync();

        expiredResult.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        expiredResult.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        expiredFixture.Helper.Requests.Should().BeEmpty();

        var revokedFixture = CreateFixture();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> revokedUnit =
            revokedFixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        ResourceRef<AuthorityBinding> revokedBinding = SeedProjectedAuthorityBinding(
            revokedFixture.Ledger,
            revokedUnit.TargetHandle,
            revocationStatus: RevocationVerificationStatus.Verified);
        ProcessInvocationSpec revokedSpec = revokedFixture.Spec with
        {
            Isolation = revokedFixture.Spec.Isolation with
            {
                AuthorityBindings = [revokedBinding],
            },
        };

        IProcessInvocationHandle revokedHandle = await revokedFixture.Provider.StartAsync(revokedSpec);
        ProcessInvocationResult revokedResult = await revokedHandle.WaitAsync();

        revokedResult.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        revokedResult.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        revokedFixture.Helper.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ResourcePhase.Deleting, AuthorityBindingPhase.Revoking, RevocationVerificationStatus.Pending)]
    [InlineData(ResourcePhase.Deleting, AuthorityBindingPhase.Revoking, RevocationVerificationStatus.Failed)]
    [InlineData(ResourcePhase.Deleting, AuthorityBindingPhase.Revoking, RevocationVerificationStatus.NotSupported)]
    [InlineData(ResourcePhase.Deleted, AuthorityBindingPhase.Revoked, RevocationVerificationStatus.Verified)]
    [InlineData(ResourcePhase.Failed, AuthorityBindingPhase.Failed, RevocationVerificationStatus.Pending)]
    public async Task Start_rejects_process_isolation_authority_after_each_non_projected_state(
        ResourcePhase resourcePhase,
        AuthorityBindingPhase bindingPhase,
        RevocationVerificationStatus revocationStatus)
    {
        var fixture = CreateFixture();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            fixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        ResourceRef<AuthorityBinding> binding = SeedProjectedAuthorityBinding(
            fixture.Ledger,
            unit.TargetHandle,
            resourcePhase: resourcePhase,
            bindingPhase: bindingPhase,
            revocationStatus: revocationStatus);
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                AuthorityBindings = [binding],
            },
        };

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(spec);
        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_rejects_stale_authority_binding_generation_before_helper_dispatch()
    {
        var fixture = CreateFixture();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit =
            fixture.Ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef()).Entry!;
        ResourceRef<AuthorityBinding> binding = SeedProjectedAuthorityBinding(fixture.Ledger, unit.TargetHandle);
        var staleBinding = binding with { Generation = new ResourceGeneration(2) };
        ProcessInvocationSpec spec = fixture.Spec with
        {
            Isolation = fixture.Spec.Isolation with
            {
                AuthorityBindings = [staleBinding],
            },
        };

        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(spec);
        ProcessInvocationResult result = await handle.WaitAsync();

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle()
            .Which.Reason.Should().Be("AppleVirtualization.ProcessAuthorityBindingNotReady");
        fixture.Helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Wait_with_stale_handle_uses_ledger_diagnostic()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        IProcessInvocationHandle handle = await fixture.Provider.StartAsync(fixture.Spec);
        fixture.Ledger.AdvanceProviderGeneration();

        Func<Task> act = async () => await fixture.Provider.WaitAsync(handle.Handle);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.StaleHandle:*");
    }

    [Fact]
    public async Task Start_with_missing_unit_handle_uses_ledger_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationProcessProvider(ledger, helper);
        ProcessInvocationSpec spec = AppleVirtualizationContractFixtures.ProcessInvocationSpec();

        Func<Task> act = async () => await provider.StartAsync(spec);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.HandleMissing:*");
        helper.Requests.Should().BeEmpty();
    }

    private static ProcessFixture CreateFixture(
        HostSeed hostSeed = HostSeed.Ready,
        ProjectionSeed seedProjection = ProjectionSeed.None)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        if (hostSeed == HostSeed.Ready)
        {
            SeedHost(ledger, ready: true);
        }
        else if (hostSeed == HostSeed.NotReady)
        {
            SeedHost(ledger, ready: false);
        }

        if (seedProjection != ProjectionSeed.None)
        {
            SeedProjection(ledger, seedProjection == ProjectionSeed.Projected);
        }

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedUnit(ledger, seedProjection != ProjectionSeed.None);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationProcessProvider(ledger, helper);
        ProcessInvocationSpec spec = AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.TargetHandle);
        return new ProcessFixture(ledger, helper, provider, spec);
    }

    private static AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> SeedHost(
        AppleVirtualizationProviderStateLedger ledger,
        bool ready) =>
        ledger.UpsertRuntimeHost(
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host"),
            new RuntimeHostStatus
            {
                Phase = ready ? ResourcePhase.Ready : ResourcePhase.Reconciling,
                ObservedGeneration = new ResourceGeneration(1),
                LastTransitionAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                HostPhase = ready ? RuntimeHostPhase.Ready : RuntimeHostPhase.Running,
                GuestControl = new GuestControlStatus(
                    Expected: true,
                    Installed: ready,
                    Reachable: ready),
                Readiness = new RuntimeHostReadinessStatus(Ready: ready),
            });

    private static AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> SeedProjection(
        AppleVirtualizationProviderStateLedger ledger,
        bool projected) =>
        ledger.UpsertContentProjection(
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection"),
            new ContentProjectionStatus
            {
                Phase = projected ? ResourcePhase.Ready : ResourcePhase.Reconciling,
                ObservedGeneration = new ResourceGeneration(1),
                LastTransitionAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                ProjectionPhase = projected ? ContentProjectionPhase.Projected : ContentProjectionPhase.Projecting,
                Views =
                [
                    new RealizedProjectionView
                    {
                        Kind = ProjectionViewKind.FilesystemTree,
                        GuestPath = new GuestPath("/workspace"),
                        EffectiveAccess = AccessMode.ReadOnly,
                        EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                        EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                        EffectiveCoherence = CoherenceClass.CloseToOpen,
                    },
                ],
            });

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedUnit(
        AppleVirtualizationProviderStateLedger ledger,
        bool includeProjection = false) =>
        ledger.UpsertExecutionUnit(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                LastTransitionAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                UnitPhase = ExecutionUnitPhase.Ready,
                AssignedHost = AppleVirtualizationContractFixtures.RuntimeHostRef(),
                RealizedContentProjections = includeProjection
                    ? [AppleVirtualizationContractFixtures.ContentProjectionRef()]
                    : Array.Empty<ResourceRef<ContentProjection>>(),
            });

    private static ResourceRef<AuthorityBinding> SeedProjectedAuthorityBinding(
        AppleVirtualizationProviderStateLedger ledger,
        TargetHandle<ExecutionUnit> targetUnit,
        string id = "authority-1",
        DateTimeOffset? expiresAt = null,
        RevocationVerificationStatus revocationStatus = RevocationVerificationStatus.Pending,
        ResourcePhase resourcePhase = ResourcePhase.Ready,
        AuthorityBindingPhase bindingPhase = AuthorityBindingPhase.Projected)
    {
        ResourceMetadata<AuthorityBinding> metadata =
            AppleVirtualizationContractFixtures.Metadata<AuthorityBinding>(id, "authority-binding");
        var resource = new ResourceRef<AuthorityBinding>(metadata.Id, metadata.Scope, metadata.Generation);
        ledger.UpsertAuthorityBinding(
            metadata,
            new AuthorityBindingStatus
            {
                Phase = resourcePhase,
                ObservedGeneration = metadata.Generation,
                BindingPhase = bindingPhase,
                BoundAuthority = new BoundAuthority
                {
                    SourceKind = AuthoritySourceKind.HostService,
                    ProjectionKind = AuthorityProjectionKind.SocketPath,
                    Direction = AuthorityBindingDirection.HostToGuest,
                    EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                    TargetSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
                    BoundAt = DateTimeOffset.UtcNow,
                    ExpiresAt = expiresAt,
                    RevocationStatus = revocationStatus,
                    AuditCorrelationId = "authority-" + id,
                },
            },
            AuthoritySpec(targetUnit));
        ledger.AttachAuthorityBindingToExecutionUnit(targetUnit, resource);
        return resource;
    }

    private static ResourceRef<AuthorityBinding> SeedFailedAuthorityBinding(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<AuthorityBinding> metadata =
            AppleVirtualizationContractFixtures.Metadata<AuthorityBinding>("authority-1", "authority-binding");
        var resource = new ResourceRef<AuthorityBinding>(metadata.Id, metadata.Scope, metadata.Generation);
        ledger.UpsertAuthorityBinding(
            metadata,
            new AuthorityBindingStatus
            {
                Phase = ResourcePhase.Failed,
                ObservedGeneration = metadata.Generation,
                BindingPhase = AuthorityBindingPhase.Failed,
            },
            AuthoritySpec(AppleVirtualizationContractFixtures.ExecutionUnitHandle()));
        return resource;
    }

    private static AuthorityBindingSpec AuthoritySpec(TargetHandle<ExecutionUnit> targetUnit) =>
        new()
        {
            Kind = AuthorityBindingKind.HostService,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.SshAgent,
                Locus = BoundaryLocus.Host,
            },
            Target = new AuthorityBindingTarget(AuthorityTargetKind.ExecutionUnit, Unit: targetUnit),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
                ReadOnly = true,
            },
            Policy = new AuthorityBindingPolicy
            {
                AuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                Redaction = SensitiveRedactionLevel.RedactSecretValues,
                RequireAudit = true,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessStatus(
        AppleVirtualizationHelperOperation operation,
        string processId,
        ProcessInvocationPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ProcessIoState.Open,
                ProviderProcessId = "guest-" + processId,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessExited(string processId, int exitCode) =>
        ProcessResult(processId, ProcessInvocationPhase.Exited, ProcessCompletionKind.Exited, exitCode);

    private static async Task<ProcessInvocationResult> RunWithWaitOutputAsync(
        ProcessFixture fixture,
        ProcessInvocationSpec spec,
        ProcessCapturedOutput output)
    {
        fixture.Helper.EnqueueResponse(ProcessStatus(
            AppleVirtualizationHelperOperation.ProcessStart,
            "process-1",
            ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus(
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            "process-1",
            ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessResult(
            "process-1",
            ProcessInvocationPhase.Exited,
            ProcessCompletionKind.Exited,
            exitCode: 0,
            output: output));
        return await fixture.Provider.RunAsync(spec);
    }

    private static ProcessCapturedOutput CapturedOutput(
        ReadOnlyMemory<byte> stdout,
        ReadOnlyMemory<byte> stderr,
        bool stdoutTruncated = false,
        bool stderrTruncated = false) =>
        new()
        {
            Stdout = new ProcessStreamOutput
            {
                CapturedBytes = stdout,
                BytesObserved = stdout.Length,
                BytesCaptured = stdout.Length,
                Truncated = stdoutTruncated,
            },
            Stderr = new ProcessStreamOutput
            {
                CapturedBytes = stderr,
                BytesObserved = stderr.Length,
                BytesCaptured = stderr.Length,
                Truncated = stderrTruncated,
            },
            OutputDrainTimeout = ProcessInvocationPolicy.Default.OutputDrainTimeout,
        };

    private static AppleVirtualizationHelperEnvelope ProcessResult(
        string processId,
        ProcessInvocationPhase phase,
        ProcessCompletionKind completionKind,
        int? exitCode,
        bool outputDrainTimedOut = false,
        ProcessCapturedOutput? output = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProcessWait,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ProcessIoState.Closed,
                ProviderProcessId = "guest-" + processId,
                Result = new ProcessInvocationResult
                {
                    ProcessId = new ResourceId<ProcessInvocation>(processId),
                    ProviderProcessId = "guest-" + processId,
                    ExitCode = exitCode,
                    CompletionKind = completionKind,
                    StartedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                    ExitedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 1, TimeSpan.Zero),
                    Duration = TimeSpan.FromSeconds(1),
                    Output = output ?? new ProcessCapturedOutput
                    {
                        Stdout = new ProcessStreamOutput(),
                        Stderr = new ProcessStreamOutput(),
                        OutputDrainTimedOut = outputDrainTimedOut,
                        OutputDrainTimeout = ProcessInvocationPolicy.Default.OutputDrainTimeout,
                    },
                },
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessOutput(
        string processId,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        bool final = false,
        bool truncated = false,
        long sequence = 1) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            EventKind = AppleVirtualizationHelperEventKind.ProcessOutput,
            SequenceNumber = sequence,
            ProviderGeneration = 1,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                Bytes = bytes,
                Flags = (final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None) |
                    (truncated ? ProcessOutputChunkFlags.Truncated : ProcessOutputChunkFlags.None),
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessError(
        AppleVirtualizationHelperOperation operation,
        string code,
        string message) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            Error = new AppleVirtualizationHelperError
            {
                Code = code,
                Message = message,
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(operation),
                Retryable = false,
                Severity = DiagnosticSeverity.Error,
            },
        };

    private sealed record ProcessFixture(
        AppleVirtualizationProviderStateLedger Ledger,
        FakeAppleVirtualizationHelperClient Helper,
        AppleVirtualizationProcessProvider Provider,
        ProcessInvocationSpec Spec);

    private enum ProjectionSeed
    {
        None,
        Projecting,
        Projected,
    }

    private enum HostSeed
    {
        None,
        NotReady,
        Ready,
    }

    private sealed class RecordingProcessOutputSink : IProcessOutputSink
    {
        public List<ProcessOutputChunk> Chunks { get; } = [];

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            Chunks.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingProcessOutputSink : IProcessOutputSink
    {
        private readonly CancellationTokenSource _source;

        public CancellingProcessOutputSink(CancellationTokenSource source)
        {
            _source = source;
        }

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
