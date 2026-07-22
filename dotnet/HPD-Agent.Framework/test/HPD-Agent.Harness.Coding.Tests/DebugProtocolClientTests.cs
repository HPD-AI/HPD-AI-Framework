using System.Text;
using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugProtocolClientTests
{
    [Fact]
    public void Replacement_launch_relays_opaque_restart_data_without_merging_or_interpreting_it()
    {
        using var configuration = JsonDocument.Parse("{\"program\":\"fixture\"}");
        using var restart = JsonDocument.Parse("{\"vendorToken\":{\"opaque\":true}}");

        var arguments = DebugProtocolArgumentComposer.Launch(
            configuration.RootElement, noDebug: false, restart.RootElement);
        var json = JsonSerializer.SerializeToElement(arguments, DapJsonContext.Default.LaunchRequestArguments);

        json.GetProperty("program").GetString().Should().Be("fixture");
        json.GetProperty("__restart").GetRawText().Should().Be(restart.RootElement.GetRawText());
        json.GetProperty("noDebug").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Initialize_policy_advertises_only_active_end_to_end_features()
    {
        var arguments = new DebugInitializePolicy().Create("fixture", new()
        {
            RunInTerminalHandler = true,
            ShellArgumentAuthorization = false,
            MemoryOperations = true,
            MemoryEventHandling = false,
            ProgressHandling = true
        });

        arguments.AdapterID.Should().Be("fixture");
        arguments.LinesStartAt1.Should().BeTrue();
        arguments.ColumnsStartAt1.Should().BeTrue();
        arguments.SupportsRunInTerminalRequest.Should().BeTrue();
        arguments.SupportsArgsCanBeInterpretedByShell.Should().BeFalse();
        arguments.SupportsMemoryReferences.Should().BeTrue();
        arguments.SupportsMemoryEvent.Should().BeFalse();
        arguments.SupportsProgressReporting.Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_is_first_exactly_once_and_enables_advertised_cancellation()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = new DebugProtocolClient(transport);
        var preInitialize = () => client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()).AsTask();
        await preInitialize.Should().ThrowAsync<InvalidOperationException>();

        var initialize = client.InitializeAsync(new DebugInitializePolicy().Create("fixture", new())).AsTask();
        var request = await ReadWrittenMessageAsync(transport);
        request.GetProperty("command").GetString().Should().Be("initialize");
        var sequence = request.GetProperty("seq").GetInt32();
        await FeedAsync(transport, $$$$"""
            {"seq":2,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"initialize","body":{"supportsCancelRequest":true}}
            """);
        (await initialize).SupportsCancelRequest.Should().BeTrue();

        var secondInitialize = () => client.InitializeAsync(new DebugInitializePolicy().Create("fixture", new())).AsTask();
        await secondInitialize.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Typed_request_serialization_and_response_correlation_use_descriptor_metadata()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);

        var responseTask = client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()).AsTask();
        var request = await ReadWrittenMessageAsync(transport);
        request.GetProperty("type").GetString().Should().Be("request");
        request.GetProperty("command").GetString().Should().Be("threads");
        var sequence = request.GetProperty("seq").GetInt32();

        await FeedAsync(transport, $$$$"""
            {"seq":7,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"threads","body":{"threads":[{"id":1,"name":"main"}]}}
            """);

        var response = await responseTask;
        response.Threads.Should().ContainSingle().Which.Name.Should().Be("main");
        client.PendingRequestCount.Should().Be(0);
    }

    [Fact]
    public async Task Command_mismatch_faults_the_request_and_protocol()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        var responseTask = client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()).AsTask();
        var sequence = (await ReadWrittenMessageAsync(transport)).GetProperty("seq").GetInt32();

        await FeedAsync(transport, $$$$"""
            {"seq":8,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"stackTrace","body":{}}
            """);

        var action = async () => await responseTask;
        await action.Should().ThrowAsync<DebugProtocolException>()
            .Where(exception => exception.ReasonCode == "RESPONSE_COMMAND_MISMATCH");
        (await transport.WaitForExitAsync()).SafeReasonCode.Should().Be("PROTOCOL_FAULT");
    }

    [Fact]
    public async Task Adapter_failure_is_structured_redacted_and_not_a_protocol_violation()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        var responseTask = client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()).AsTask();
        var sequence = (await ReadWrittenMessageAsync(transport)).GetProperty("seq").GetInt32();

        await FeedAsync(transport, $$$$"""
            {"seq":9,"type":"response","request_seq":{{{{sequence}}}},"success":false,"command":"threads","message":"failed","body":{"error":{"id":42,"format":"Cannot inspect {target}","variables":{"target":"safe"},"sendTelemetry":true,"showUser":true,"url":"file:///secret","urlLabel":"details"}}}
            """);

        var action = async () => await responseTask;
        var exception = (await action.Should().ThrowAsync<DebugAdapterRequestException>()).Which;
        exception.Error.Id.Should().Be(42);
        exception.Error.Variables.Should().Contain("target", "safe");
        exception.Error.ShowUser.Should().BeTrue();
        exception.Error.ApprovedUrl.Should().BeNull();
        client.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task Event_handler_can_send_followup_request_without_reader_deadlock()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        var completed = new TaskCompletionSource<ThreadsResponseBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = client.OnEvent(async message =>
        {
            if (message.Event == "initialized")
                completed.TrySetResult(await client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()));
        });

        await FeedAsync(transport, """{"seq":1,"type":"event","event":"initialized"}""");
        var request = await ReadWrittenMessageAsync(transport);
        var sequence = request.GetProperty("seq").GetInt32();
        await FeedAsync(transport, $$$$"""
            {"seq":2,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"threads","body":{"threads":[]}}
            """);

        (await completed.Task.WaitAsync(TimeSpan.FromSeconds(2))).Threads.Should().BeEmpty();
    }

    [Fact]
    public async Task Events_are_dispatched_in_wire_order_even_when_the_first_handler_is_slow()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        using var registration = client.OnEvent(async message =>
        {
            if (message.Event == "continued")
                await releaseFirst.Task;
            lock (observed)
            {
                observed.Add(message.Event);
                if (observed.Count == 2)
                    completed.TrySetResult(observed.ToArray());
            }
        });

        await FeedAsync(transport, """{"seq":1,"type":"event","event":"continued","body":{"threadId":1}}""");
        await FeedAsync(transport, """{"seq":2,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");
        await Task.Delay(50);
        lock (observed) observed.Should().BeEmpty();

        releaseFirst.TrySetResult();
        (await completed.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().Equal("continued", "stopped");
        client.Health.ProcessedEvents.Should().Be(2);
    }

    [Fact]
    public async Task Event_handler_failure_is_observable_and_does_not_stop_later_events()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        var laterEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = client.OnEvent(message =>
        {
            if (message.Event == "continued")
                throw new InvalidOperationException("sensitive adapter detail");
            if (message.Event == "stopped")
                laterEvent.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await FeedAsync(transport, """{"seq":1,"type":"event","event":"continued","body":{"threadId":1}}""");
        await FeedAsync(transport, """{"seq":2,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");
        await laterEvent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => client.Health.ProcessedEvents == 2);

        client.Health.EventHandlerFailures.Should().Be(1);
        client.Health.LastFailedEvent.Should().Be("continued");
        client.Health.LastHandlerFailureType.Should().Be(typeof(InvalidOperationException).FullName);
        client.Health.LastHandlerFailureType.Should().NotContain("sensitive adapter detail");
        client.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task Event_queue_overflow_faults_instead_of_dropping_or_reordering_events()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = new DebugProtocolClient(transport, new DebugProtocolClientOptions
        {
            RequireInitializeFirst = false,
            MaxQueuedEvents = 1
        });
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = client.OnEvent(async _ =>
        {
            handlerEntered.TrySetResult();
            await releaseHandler.Task;
        });

        await FeedAsync(transport, """{"seq":1,"type":"event","event":"continued","body":{"threadId":1}}""");
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await FeedAsync(transport, """{"seq":2,"type":"event","event":"invalidated","body":{"areas":["stacks"]}}""");
        await WaitUntilAsync(() => client.Health.QueuedEvents == 1);
        await FeedAsync(transport, """{"seq":3,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");

        (await transport.WaitForExitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)))
            .SafeReasonCode.Should().Be("PROTOCOL_FAULT");
        releaseHandler.TrySetResult();
    }

    [Fact]
    public async Task Host_trace_is_opt_in_bounded_and_redacts_sensitive_protocol_fields()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        var trace = new DebugProtocolHostTraceBuffer(maximumRecords: 4, maximumBytes: 4096);
        await using var client = new DebugProtocolClient(transport, new DebugProtocolClientOptions
        {
            RequireInitializeFirst = false,
            HostTraceSink = trace
        });
        var requestTask = client.SendAsync(DebugProtocolDescriptors.ThreadsRequest, new DapNoArguments()).AsTask();
        var request = await ReadWrittenMessageAsync(transport);
        var sequence = request.GetProperty("seq").GetInt32();
        await FeedAsync(transport, $$$$"""
            {"seq":2,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"threads","body":{"threads":[]},"token":"do-not-retain"}
            """);
        await requestTask;

        var payloads = trace.Snapshot().Select(x => Encoding.UTF8.GetString(x.RedactedPayload.Span)).ToArray();
        payloads.Should().HaveCount(2);
        payloads.Should().NotContain(x => x.Contains("do-not-retain", StringComparison.Ordinal));
        payloads.Should().Contain(x => x.Contains("[REDACTED]", StringComparison.Ordinal));

        var store = new HPD.Agent.InMemoryContentStore();
        var address = await trace.PersistHostDiagnosticAsync(store, HPD.Agent.ContentScope.Create("host:trace"),
            new Dictionary<string, string> { ["debug-tree"] = "tree" });
        var info = await store.StatAsync(address);
        info!.Tags.Should().Contain("kind", "host-diagnostic")
            .And.Contain("model-visible", "false");
    }

    [Fact]
    public void Host_trace_types_are_absent_from_semantic_results_and_ordinary_events()
    {
        var forbidden = new[]
        {
            typeof(IDebugProtocolTraceSink), typeof(DebugProtocolHostTraceBuffer),
            typeof(DebugProtocolTraceRecord), typeof(ReadOnlyMemory<byte>)
        };
        var ordinaryTypes = new[]
        {
            typeof(DebugSemanticHealth), typeof(DebugOutputSnapshot),
            typeof(DebugOutputAvailableEvent), typeof(DebugSessionSummaryEvent)
        };

        foreach (var type in ordinaryTypes)
            type.GetProperties().Select(property => property.PropertyType)
                .Should().NotContain(propertyType => forbidden.Contains(propertyType),
                    $"{type.Name} is part of the ordinary semantic/event boundary");
    }

    [Fact]
    public async Task Unknown_reverse_request_receives_not_supported_response()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);

        await FeedAsync(transport, """{"seq":11,"type":"request","command":"futureRequest","arguments":{}}""");
        var response = await ReadWrittenMessageAsync(transport);

        response.GetProperty("type").GetString().Should().Be("response");
        response.GetProperty("request_seq").GetInt32().Should().Be(11);
        response.GetProperty("success").GetBoolean().Should().BeFalse();
        response.GetProperty("message").GetString().Should().Be("notSupported");
    }

    [Fact]
    public async Task Caller_cancellation_sends_cancel_and_late_response_is_ignored()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        client.SetSupportsCancelRequest(true);
        using var cancellation = new CancellationTokenSource();
        var responseTask = client.SendAsync(
            DebugProtocolDescriptors.ThreadsRequest,
            new DapNoArguments(),
            cancellation.Token).AsTask();
        var request = await ReadWrittenMessageAsync(transport);
        var sequence = request.GetProperty("seq").GetInt32();

        cancellation.Cancel();
        var cancelRequest = await ReadWrittenMessageAsync(transport);
        cancelRequest.GetProperty("command").GetString().Should().Be("cancel");
        cancelRequest.GetProperty("arguments").GetProperty("requestId").GetInt32().Should().Be(sequence);
        var action = async () => await responseTask;
        await action.Should().ThrowAsync<OperationCanceledException>();

        await FeedAsync(transport, $$$$"""
            {"seq":12,"type":"response","request_seq":{{{{sequence}}}},"success":true,"command":"threads","body":{"threads":[]}}
            """);
        client.IsAlive.Should().BeTrue();

        var cancelSequence = cancelRequest.GetProperty("seq").GetInt32();
        await FeedAsync(transport, $$$$"""
            {"seq":13,"type":"response","request_seq":{{{{cancelSequence}}}},"success":true,"command":"cancel"}
            """);
    }

    [Fact]
    public async Task Progress_cancellation_uses_progress_id_and_remains_live_until_end()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var client = CreateInitializedTestClient(transport);
        await FeedAsync(transport, """{"seq":1,"type":"event","event":"progressStart","body":{"progressId":"p1","requestId":7,"title":"work","cancellable":true}}""");
        await WaitUntilAsync(() => client.ActiveProgressIds.Contains("p1"));

        var cancellationTask = client.CancelProgressAsync("p1").AsTask();
        var request = await ReadWrittenMessageAsync(transport);
        request.GetProperty("arguments").GetProperty("progressId").GetString().Should().Be("p1");
        client.ActiveProgressIds.Should().Contain("p1");
        var requestSequence = request.GetProperty("seq").GetInt32();
        await FeedAsync(transport, $$$$"""
            {"seq":2,"type":"response","request_seq":{{{{requestSequence}}}},"success":true,"command":"cancel"}
            """);
        (await cancellationTask).Should().BeTrue();
        await FeedAsync(transport, """{"seq":3,"type":"event","event":"progressEnd","body":{"progressId":"p1"}}""");
        await WaitUntilAsync(() => !client.ActiveProgressIds.Contains("p1"));
    }

    private static async Task<JsonElement> ReadWrittenMessageAsync(InMemoryDebugProtocolTransport transport)
    {
        await foreach (var bytes in transport.ReadWrittenAsync().WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token))
        {
            var framer = new DebugProtocolFramer();
            var frame = framer.Append(bytes).Single();
            using var document = JsonDocument.Parse(frame);
            return document.RootElement.Clone();
        }
        throw new InvalidOperationException("Transport completed before a message was written.");
    }

    private static ValueTask FeedAsync(InMemoryDebugProtocolTransport transport, string json)
        => transport.FeedProtocolAsync(DebugProtocolFramer.Encode(Encoding.UTF8.GetBytes(json)));

    private static DebugProtocolClient CreateInitializedTestClient(InMemoryDebugProtocolTransport transport)
        => new(transport, new DebugProtocolClientOptions { RequireInitializeFirst = false });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
