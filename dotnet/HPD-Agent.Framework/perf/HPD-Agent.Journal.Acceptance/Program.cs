using System.Diagnostics;
using System.Text;
using HPD.Agent;

const int EventCount = 51_043;
const int TextDeltaCount = 47_288;
const int MessageCount = 531;
const int BatchSize = 256;
const long ReplayMemoryBudgetBytes = 256L * 1024 * 1024;
const long IdleMemoryRangeBudgetBytes = 32L * 1024 * 1024;

var options = AcceptanceOptions.Parse(args);
var root = options.StorePath ?? Path.Combine(Path.GetTempPath(), $"hpd-journal-acceptance-{Guid.NewGuid():N}");
var deleteStore = options.StorePath is null;

try
{
    Console.WriteLine($"acceptance.store={root}");
    Console.WriteLine($"acceptance.events={EventCount}");
    Console.WriteLine($"acceptance.text_deltas={TextDeltaCount}");
    Console.WriteLine($"acceptance.messages={MessageCount}");
    Console.WriteLine($"acceptance.idle_duration={options.IdleDuration}");

    var key = new ThreadKey("acceptance-session", "main");
    await BuildFixtureAsync(root, key);
    await RunReplayAsync(root, key);
    await RunIdleSoakAsync(root, key, options.IdleDuration);

    Console.WriteLine("acceptance.result=PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"acceptance.result=FAIL type={exception.GetType().Name} message={exception.Message}");
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    if (deleteStore && Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

static async Task BuildFixtureAsync(string root, ThreadKey key)
{
    var store = new FileSessionStore(
        root,
        new FileSessionStoreOptions(SegmentEventCapacity: 1024, FlushToDiskOnCommit: false));
    var batch = new List<AgentEvent>(BatchSize);
    var appended = 0;
    var stopwatch = Stopwatch.StartNew();

    foreach (var evt in CreateFixtureEvents(key))
    {
        batch.Add(evt);
        if (batch.Count < BatchSize)
            continue;

        var result = await store.AppendThreadEventsAsync(key, batch);
        appended += result.CommittedEvents.Count;
        batch.Clear();
    }

    if (batch.Count > 0)
    {
        var result = await store.AppendThreadEventsAsync(key, batch);
        appended += result.CommittedEvents.Count;
    }

    stopwatch.Stop();
    Require(appended == EventCount, $"Fixture appended {appended} events instead of {EventCount}.");
    var journalPath = Path.Combine(root, "sessions", key.SessionId, "threads", key.ThreadId, "journal");
    var journalBytes = Directory.EnumerateFiles(journalPath, "segment-*.events").Sum(path => new FileInfo(path).Length);
    var segments = Directory.EnumerateFiles(journalPath, "segment-*.events").Count();
    Console.WriteLine($"fixture.append_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    Console.WriteLine($"fixture.journal_bytes={journalBytes}");
    Console.WriteLine($"fixture.segments={segments}");
}

static IEnumerable<AgentEvent> CreateFixtureEvents(ThreadKey key)
{
    yield return new ThreadCreatedEvent(
        Name: "Journal acceptance fixture",
        Description: null,
        Tags: null,
        ThreadMetadata: null,
        CreatedAt: DateTime.UnixEpoch);

    var remainingDeltas = TextDeltaCount;
    var baseDeltas = TextDeltaCount / MessageCount;
    var messagesWithExtraDelta = TextDeltaCount % MessageCount;
    var payload = new string('x', 128);

    for (var messageIndex = 0; messageIndex < MessageCount; messageIndex++)
    {
        var messageId = $"message-{messageIndex:D4}";
        yield return new TextMessageStartEvent(messageId, "assistant");
        var deltaCount = baseDeltas + (messageIndex < messagesWithExtraDelta ? 1 : 0);
        for (var deltaIndex = 0; deltaIndex < deltaCount; deltaIndex++)
        {
            yield return new TextDeltaEvent(
                $"{messageIndex:D4}:{deltaIndex:D3}:{payload}",
                messageId);
            remainingDeltas--;
        }
        yield return new TextMessageEndEvent(messageId);
    }

    Require(remainingDeltas == 0, "Synthetic text-delta distribution was incorrect.");

    var largeToolResultSizes = new[]
    {
        228_366, 95_923, 70_048, 62_478, 53_754, 45_815,
        45_655, 40_059, 39_967, 39_130, 34_968
    };
    for (var index = 0; index < 535; index++)
    {
        var callId = $"call-{index:D4}";
        var messageId = $"message-{index % MessageCount:D4}";
        var argsJson = $"{{\"payload\":\"{new string('a', 800)}\"}}";
        yield return new ToolCallStartEvent(callId, "SyntheticStressTool", messageId);
        if (index < 532)
            yield return new ToolCallArgsEvent(callId, argsJson);
        yield return new ToolCallEndEvent(callId, messageId, "SyntheticStressTool", argsJson);

        var resultLength = index < largeToolResultSizes.Length
            ? Math.Max(1, largeToolResultSizes[index] - 400)
            : 3_200;
        yield return new ToolCallResultEvent(
            callId,
            new ToolResultPayload(new string('r', resultLength)),
            Name: "SyntheticStressTool")
        {
            MessageId = messageId
        };
    }

    for (var index = 0; index < 277; index++)
    {
        var executionId = $"execution-{index:D4}";
        yield return new ThreadExecutionStartedEvent(executionId, "acceptance-agent", DateTimeOffset.UnixEpoch);
        yield return new ThreadExecutionFinishedEvent(
            executionId,
            "acceptance-agent",
            ThreadExecutionOutcome.Succeeded,
            DateTimeOffset.UnixEpoch);
    }

    yield return new ThreadUpdatedEvent(
        Name: "Journal acceptance fixture",
        Description: "Synthetic large-payload distribution",
        Tags: null,
        ThreadMetadata: null);
}

static async Task RunReplayAsync(string root, ThreadKey key)
{
    var store = new FileSessionStore(
        root,
        new FileSessionStoreOptions(SegmentEventCapacity: 1024, FlushToDiskOnCommit: false));
    var head = await store.GetThreadEventHeadAsync(key)
        ?? throw new InvalidOperationException("Fixture journal did not have a head.");
    Require(head.ThreadSequenceNumber == EventCount, $"Fixture head was {head.ThreadSequenceNumber}.");

    ForceCollection();
    var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var peakMemory = baselineMemory;
    using var samplingCancellation = new CancellationTokenSource();
    var sampler = Task.Run(async () =>
    {
        while (!samplingCancellation.IsCancellationRequested)
        {
            InterlockedExtensions.Max(ref peakMemory, GC.GetTotalMemory(forceFullCollection: false));
            await Task.Delay(10, samplingCancellation.Token).ConfigureAwait(false);
        }
    });

    var activeMessages = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
    var transcript = new List<string>(MessageCount);
    var eventCount = 0;
    var maxBatchCount = 0;
    var firstBatchElapsed = TimeSpan.Zero;
    var stopwatch = Stopwatch.StartNew();

    await foreach (var batch in store.ReadThreadEventsAsync(
        key,
        new ThreadEventReadRequest(MaxBatchEventCount: BatchSize)))
    {
        if (eventCount == 0)
            firstBatchElapsed = stopwatch.Elapsed;
        maxBatchCount = Math.Max(maxBatchCount, batch.Events.Count);
        foreach (var evt in batch.Events)
        {
            eventCount++;
            switch (evt)
            {
                case TextMessageStartEvent start:
                    activeMessages[start.MessageId] = new StringBuilder();
                    break;
                case TextDeltaEvent delta:
                    activeMessages[delta.MessageId].Append(delta.Text);
                    break;
                case TextMessageEndEvent end:
                    transcript.Add(activeMessages.Remove(end.MessageId, out var text)
                        ? text.ToString()
                        : string.Empty);
                    break;
            }
        }
    }

    stopwatch.Stop();
    samplingCancellation.Cancel();
    try { await sampler; }
    catch (OperationCanceledException) { }
    InterlockedExtensions.Max(ref peakMemory, GC.GetTotalMemory(forceFullCollection: false));

    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var peakIncrement = Math.Max(0, peakMemory - baselineMemory);
    var retainedTranscriptBytes = transcript.Sum(text => (long)text.Length * sizeof(char));
    var diagnostics = store.GetDiagnostics();

    Require(eventCount == EventCount, $"Replay returned {eventCount} events instead of {EventCount}.");
    Require(maxBatchCount <= BatchSize, $"Replay yielded an oversized batch of {maxBatchCount} events.");
    Require(transcript.Count == MessageCount, $"Projection produced {transcript.Count} messages instead of {MessageCount}.");
    Require(activeMessages.Count == 0, "Projection retained active message buffers after replay.");
    Require(peakIncrement <= ReplayMemoryBudgetBytes,
        $"Replay peak incremental managed memory was {FormatBytes(peakIncrement)}, above {FormatBytes(ReplayMemoryBudgetBytes)}.");

    Console.WriteLine($"replay.first_batch_ms={firstBatchElapsed.TotalMilliseconds:F1}");
    Console.WriteLine($"replay.total_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    Console.WriteLine($"replay.events={eventCount}");
    Console.WriteLine($"replay.max_batch_events={maxBatchCount}");
    Console.WriteLine($"replay.allocated_bytes={allocatedBytes}");
    Console.WriteLine($"replay.peak_incremental_managed_bytes={peakIncrement}");
    Console.WriteLine($"replay.retained_transcript_bytes={retainedTranscriptBytes}");
    Console.WriteLine($"replay.segment_reads={diagnostics.SegmentReadCount}");
    Console.WriteLine($"replay.segment_bytes_read={diagnostics.SegmentBytesRead}");
    Console.WriteLine($"replay.events_decoded={diagnostics.EventDecodeCount}");
}

static async Task RunIdleSoakAsync(string root, ThreadKey key, TimeSpan duration)
{
    var store = new FileSessionStore(
        root,
        new FileSessionStoreOptions(SegmentEventCapacity: 1024, FlushToDiskOnCommit: false));
    var head = await store.GetThreadEventHeadAsync(key)
        ?? throw new InvalidOperationException("Fixture journal did not have a head.");
    using var cancellation = new CancellationTokenSource();
    await using var first = store.ObserveThreadEventsAsync(
        key,
        head.ThreadSequenceNumber,
        new ThreadObservationOptions(MaxBatchEventCount: BatchSize),
        cancellation.Token).GetAsyncEnumerator();
    await using var second = store.ObserveThreadEventsAsync(
        key,
        head.ThreadSequenceNumber,
        new ThreadObservationOptions(MaxBatchEventCount: BatchSize),
        cancellation.Token).GetAsyncEnumerator();

    var firstPending = first.MoveNextAsync().AsTask();
    var secondPending = second.MoveNextAsync().AsTask();
    await WaitUntilAsync(
        () => store.GetDiagnostics().ObservationWaitCount == 2,
        TimeSpan.FromSeconds(10));

    ForceCollection();
    var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
    var minimumMemory = baselineMemory;
    var maximumMemory = baselineMemory;
    var before = store.GetDiagnostics();
    var stopwatch = Stopwatch.StartNew();
    var nextReport = TimeSpan.Zero;

    while (stopwatch.Elapsed < duration)
    {
        var remaining = duration - stopwatch.Elapsed;
        await Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5));
        var memory = GC.GetTotalMemory(forceFullCollection: false);
        minimumMemory = Math.Min(minimumMemory, memory);
        maximumMemory = Math.Max(maximumMemory, memory);
        if (stopwatch.Elapsed >= nextReport)
        {
            Console.WriteLine(
                $"idle.elapsed={stopwatch.Elapsed:c} managed_bytes={memory} segment_reads={store.GetDiagnostics().SegmentReadCount} events_decoded={store.GetDiagnostics().EventDecodeCount}");
            nextReport = stopwatch.Elapsed + TimeSpan.FromMinutes(1);
        }
    }

    var after = store.GetDiagnostics();
    var memoryRange = maximumMemory - minimumMemory;
    Require(!firstPending.IsCompleted && !secondPending.IsCompleted, "An idle observer completed unexpectedly.");
    Require(after.SegmentReadCount == before.SegmentReadCount, "Idle observers read journal segments.");
    Require(after.SegmentBytesRead == before.SegmentBytesRead, "Idle observers read journal bytes.");
    Require(after.EventDecodeCount == before.EventDecodeCount, "Idle observers decoded historical events.");
    Require(after.ObservationWaitCount == before.ObservationWaitCount, "Idle observers left and re-entered their wait state.");
    Require(memoryRange <= IdleMemoryRangeBudgetBytes,
        $"Idle managed-memory range was {FormatBytes(memoryRange)}, above {FormatBytes(IdleMemoryRangeBudgetBytes)}.");

    cancellation.Cancel();
    await IgnoreCancellationAsync(firstPending);
    await IgnoreCancellationAsync(secondPending);

    Console.WriteLine($"idle.duration_ms={stopwatch.Elapsed.TotalMilliseconds:F0}");
    Console.WriteLine($"idle.observers=2");
    Console.WriteLine($"idle.segment_reads={after.SegmentReadCount - before.SegmentReadCount}");
    Console.WriteLine($"idle.segment_bytes_read={after.SegmentBytesRead - before.SegmentBytesRead}");
    Console.WriteLine($"idle.events_decoded={after.EventDecodeCount - before.EventDecodeCount}");
    Console.WriteLine($"idle.managed_memory_range_bytes={memoryRange}");
}

static async Task IgnoreCancellationAsync(Task<bool> task)
{
    try { await task; }
    catch (OperationCanceledException) { }
}

static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!predicate())
    {
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("Acceptance condition was not reached before the timeout.");
        await Task.Delay(10);
    }
}

static void ForceCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d:F1} MiB";

internal sealed record AcceptanceOptions(TimeSpan IdleDuration, string? StorePath)
{
    public static AcceptanceOptions Parse(string[] args)
    {
        var idleDuration = TimeSpan.FromMinutes(30);
        string? storePath = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--idle-minutes":
                    idleDuration = TimeSpan.FromMinutes(double.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--store":
                    storePath = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown acceptance option '{args[index]}'.");
            }
        }
        if (idleDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(args), "Idle duration must be positive.");
        return new AcceptanceOptions(idleDuration, storePath);
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref long location, long value)
    {
        var current = Interlocked.Read(ref location);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
