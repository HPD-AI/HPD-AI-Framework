// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Studio;

/// <summary>
/// Configuration for Agent Studio's process-local OpenTelemetry activity collection.
/// The activity projection is Studio evidence, not a replacement for the canonical Agent journal.
/// </summary>
public sealed class AgentStudioTelemetryOptions
{
    /// <summary>Gets the ActivitySource names whose spans can be correlated to HPD Agent traces.</summary>
    public ISet<string> ActivitySources { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "HPD.Agent"
    };

    /// <summary>Gets or sets the maximum number of completed spans retained by the default store.</summary>
    public int MaximumRetainedSpans { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the JSONL directory for durable Activity span evidence. When null, Studio uses a
    /// bounded process-local store.
    /// </summary>
    public string? SpanStorePath { get; set; }
}

/// <summary>A correlated event recorded on an OpenTelemetry activity.</summary>
public sealed record AgentStudioTelemetrySpanEvent(
    string Name,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>A completed OpenTelemetry span correlated to HPD Agent trace and span identities.</summary>
public sealed record AgentStudioTelemetrySpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string ActivityTraceId,
    string ActivitySpanId,
    string Name,
    string Kind,
    string Status,
    DateTimeOffset StartedAt,
    double DurationMilliseconds,
    string SourceName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<AgentStudioTelemetrySpanEvent> Events);

/// <summary>A trace assembled from completed OpenTelemetry activities.</summary>
public sealed record AgentStudioTelemetryTrace(
    string TraceId,
    IReadOnlyList<AgentStudioTelemetrySpan> Spans);

/// <summary>
/// Stores correlated Studio span evidence. Implementations may be process-local caches or durable
/// providers; the store never owns sessions, threads, events, or canonical Agent execution state.
/// </summary>
public interface IAgentStudioTelemetryStore
{
    /// <summary>Records one completed span projection.</summary>
    void Record(AgentStudioTelemetrySpan span);

    /// <summary>Gets the complete currently retained span projection for a trace.</summary>
    bool TryGetTrace(string traceId, out AgentStudioTelemetryTrace trace);

    /// <summary>Gets a bounded snapshot of retained traces, newest first.</summary>
    IReadOnlyList<AgentStudioTelemetryTrace> ListTraces(int maximum);
}

/// <summary>
/// Bounded default span projection used until a host replaces it with a durable telemetry provider.
/// </summary>
public sealed class InMemoryAgentStudioTelemetryStore(IOptions<AgentStudioTelemetryOptions> options)
    : IAgentStudioTelemetryStore
{
    private readonly int _capacity = Math.Max(1, options.Value.MaximumRetainedSpans);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AgentStudioTelemetrySpan>> _traces =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string TraceId, string SpanId)> _retention = new();
    private int _count;

    /// <inheritdoc />
    public void Record(AgentStudioTelemetrySpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        ConcurrentDictionary<string, AgentStudioTelemetrySpan> spans =
            _traces.GetOrAdd(span.TraceId, static _ =>
                new ConcurrentDictionary<string, AgentStudioTelemetrySpan>(StringComparer.Ordinal));
        spans[span.SpanId] = span;
        _retention.Enqueue((span.TraceId, span.SpanId));
        if (Interlocked.Increment(ref _count) <= _capacity) return;

        while (Interlocked.CompareExchange(ref _count, 0, 0) > _capacity &&
               _retention.TryDequeue(out (string TraceId, string SpanId) expired))
        {
            if (!_traces.TryGetValue(expired.TraceId, out ConcurrentDictionary<string, AgentStudioTelemetrySpan>? trace) ||
                !trace.TryRemove(expired.SpanId, out _))
                continue;
            if (trace.IsEmpty) _traces.TryRemove(expired.TraceId, out _);
            Interlocked.Decrement(ref _count);
        }
    }

    /// <inheritdoc />
    public bool TryGetTrace(string traceId, out AgentStudioTelemetryTrace trace)
    {
        if (!_traces.TryGetValue(traceId, out ConcurrentDictionary<string, AgentStudioTelemetrySpan>? spans) ||
            spans.IsEmpty)
        {
            trace = null!;
            return false;
        }

        trace = new AgentStudioTelemetryTrace(
            traceId,
            spans.Values
                .OrderBy(static span => span.StartedAt)
                .ThenBy(static span => span.SpanId, StringComparer.Ordinal)
                .ToArray());
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentStudioTelemetryTrace> ListTraces(int maximum) =>
        Snapshot(_traces, maximum);

    internal static IReadOnlyList<AgentStudioTelemetryTrace> Snapshot(
        IReadOnlyDictionary<string, ConcurrentDictionary<string, AgentStudioTelemetrySpan>> traces,
        int maximum)
    {
        int take = Math.Clamp(maximum, 0, 500);
        if (take == 0) return [];
        return traces
            .Select(pair => new AgentStudioTelemetryTrace(
                pair.Key,
                pair.Value.Values
                    .OrderBy(static span => span.StartedAt)
                    .ThenBy(static span => span.SpanId, StringComparer.Ordinal)
                    .ToArray()))
            .Where(static trace => trace.Spans.Count > 0)
            .OrderByDescending(static trace => trace.Spans[0].StartedAt)
            .ThenBy(static trace => trace.TraceId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
    }
}

/// <summary>
/// Durable append-only JSONL span projection. The in-memory index performs bounded retention while
/// atomic compaction keeps the backing file close to the configured capacity. The file provider is
/// single-writer; use a host-specific store for multi-process or multi-instance deployments.
/// </summary>
public sealed class FileAgentStudioTelemetryStore : IAgentStudioTelemetryStore
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly string _filePath;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AgentStudioTelemetrySpan>> _traces =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string TraceId, string SpanId)> _retention = new();
    private int _count;
    private int _appendedSinceCompaction;

    /// <summary>Creates a durable store at the configured path.</summary>
    public FileAgentStudioTelemetryStore(IOptions<AgentStudioTelemetryOptions> options)
    {
        AgentStudioTelemetryOptions value = options.Value;
        _capacity = Math.Max(1, value.MaximumRetainedSpans);
        if (String.IsNullOrWhiteSpace(value.SpanStorePath))
            throw new ArgumentException("Agent Studio telemetry requires a SpanStorePath.", nameof(options));
        _filePath = Path.GetFullPath(value.SpanStorePath);
        _directory = Path.GetDirectoryName(_filePath)
            ?? throw new ArgumentException("The telemetry path must include a directory.", nameof(options));
        Load();
    }

    /// <inheritdoc />
    public void Record(AgentStudioTelemetrySpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        lock (_gate)
        {
            AddToIndex(span);
            Directory.CreateDirectory(_directory);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                span, AgentStudioTelemetryJsonContext.Default.AgentStudioTelemetrySpan);
            using (var stream = new FileStream(
                       _filePath, FileMode.Append, FileAccess.Write, FileShare.Read,
                       bufferSize: 16_384, FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.WriteByte((byte)'\n');
            }

            _appendedSinceCompaction++;
            if (_appendedSinceCompaction >= Math.Max(64, _capacity))
                Compact();
        }
    }

    /// <inheritdoc />
    public bool TryGetTrace(string traceId, out AgentStudioTelemetryTrace trace)
    {
        if (!_traces.TryGetValue(traceId, out ConcurrentDictionary<string, AgentStudioTelemetrySpan>? spans) ||
            spans.IsEmpty)
        {
            trace = null!;
            return false;
        }

        trace = new AgentStudioTelemetryTrace(
            traceId,
            spans.Values
                .OrderBy(static span => span.StartedAt)
                .ThenBy(static span => span.SpanId, StringComparer.Ordinal)
                .ToArray());
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentStudioTelemetryTrace> ListTraces(int maximum) =>
        InMemoryAgentStudioTelemetryStore.Snapshot(_traces, maximum);

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        lock (_gate)
        {
            foreach (string line in File.ReadLines(_filePath))
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    AgentStudioTelemetrySpan? span =
                        JsonSerializer.Deserialize(
                            line, AgentStudioTelemetryJsonContext.Default.AgentStudioTelemetrySpan);
                    if (span is not null) AddToIndex(span);
                }
                catch (JsonException)
                {
                    // A torn final append must not prevent Studio from loading earlier evidence.
                }
            }
        }
    }

    private void AddToIndex(AgentStudioTelemetrySpan span)
    {
        ConcurrentDictionary<string, AgentStudioTelemetrySpan> spans =
            _traces.GetOrAdd(span.TraceId, static _ =>
                new ConcurrentDictionary<string, AgentStudioTelemetrySpan>(StringComparer.Ordinal));
        if (spans.TryGetValue(span.SpanId, out _))
        {
            spans[span.SpanId] = span;
        }
        else
        {
            spans[span.SpanId] = span;
            _retention.Enqueue((span.TraceId, span.SpanId));
            _count++;
        }

        while (_count > _capacity && _retention.TryDequeue(out (string TraceId, string SpanId) expired))
        {
            if (!_traces.TryGetValue(expired.TraceId, out ConcurrentDictionary<string, AgentStudioTelemetrySpan>? trace) ||
                !trace.TryRemove(expired.SpanId, out _))
                continue;
            if (trace.IsEmpty) _traces.TryRemove(expired.TraceId, out _);
            _count--;
        }
    }

    private void Compact()
    {
        string temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        using (var stream = new FileStream(
                   temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   bufferSize: 65_536, FileOptions.WriteThrough))
        {
            foreach (AgentStudioTelemetrySpan span in _traces.Values
                         .SelectMany(static value => value.Values)
                         .OrderBy(static span => span.StartedAt)
                         .ThenBy(static span => span.SpanId, StringComparer.Ordinal))
            {
                JsonSerializer.Serialize(
                    stream, span, AgentStudioTelemetryJsonContext.Default.AgentStudioTelemetrySpan);
                stream.WriteByte((byte)'\n');
            }
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
        _appendedSinceCompaction = 0;
    }
}

/// <summary>AOT-safe serialization metadata for durable Studio span evidence.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AgentStudioTelemetrySpan))]
internal partial class AgentStudioTelemetryJsonContext : JsonSerializerContext;

/// <summary>
/// Collects completed HPD Agent activities and maps their HPD correlation tags into the Studio span store.
/// A host that does not enable <c>WithTracing()</c> produces no activity evidence; Studio then discloses
/// the canonical-journal span projection instead.
/// </summary>
public sealed class AgentStudioActivityTelemetryCollector(
    IAgentStudioTelemetryStore store,
    IOptions<AgentStudioTelemetryOptions> options) : IHostedService, IDisposable
{
    private const int MaximumTagCount = 128;
    private const int MaximumEventCount = 128;
    private const int MaximumValueLength = 4_096;
    private readonly HashSet<string> _sources = new(options.Value.ActivitySources, StringComparer.Ordinal);
    private ActivityListener? _listener;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener is not null) return Task.CompletedTask;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => _sources.Contains(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }

    private void OnActivityStopped(Activity activity)
    {
        string? traceId = TagString(activity, "hpd.trace_id");
        string? spanId = TagString(activity, "hpd.span_id");
        if (traceId is null || spanId is null) return;

        store.Record(new AgentStudioTelemetrySpan(
            traceId,
            spanId,
            TagString(activity, "hpd.parent_span_id"),
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            Bound(activity.DisplayName),
            activity.Kind.ToString(),
            activity.Status == ActivityStatusCode.Error ? "failed" : "completed",
            activity.StartTimeUtc,
            Math.Max(0, activity.Duration.TotalMilliseconds),
            activity.Source.Name,
            Attributes(activity.TagObjects),
            activity.Events
                .Take(MaximumEventCount)
                .Select(static value => new AgentStudioTelemetrySpanEvent(
                    Bound(value.Name),
                    value.Timestamp,
                    Attributes(value.Tags)))
                .ToArray()));
    }

    private static string? TagString(Activity activity, string key) =>
        activity.GetTagItem(key) as string is { Length: > 0 } value ? value : null;

    private static IReadOnlyDictionary<string, string> Attributes(
        IEnumerable<KeyValuePair<string, object?>> values) =>
        values
            .Where(static value => value.Key.Length > 0)
            .OrderBy(static value => value.Key, StringComparer.Ordinal)
            .Take(MaximumTagCount)
            .ToDictionary(
                static value => Bound(value.Key),
                static value => Bound(Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                StringComparer.Ordinal);

    private static string Bound(string value) =>
        value.Length <= MaximumValueLength ? value : value[..MaximumValueLength];
}
