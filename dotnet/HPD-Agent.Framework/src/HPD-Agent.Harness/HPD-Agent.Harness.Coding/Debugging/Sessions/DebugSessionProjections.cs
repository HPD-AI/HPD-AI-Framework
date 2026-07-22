using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugProcessSnapshot(
    string Name, int? SystemProcessId, bool? IsLocalProcess, string? StartMethod, long? PointerSize);

internal sealed record DebugModuleSnapshot(
    string OpaqueId, string Name, string? Path, bool? IsOptimized, bool? IsUserCode,
    string? Version, string? SymbolStatus);

internal sealed record DebugLoadedSourceSnapshot(
    string Key, string? Name, string? Path, int? SourceReference, string? Origin,
    JsonElement? AdapterData);

internal sealed record DebugStackFrameSnapshot(
    int FrameId, string Name, string? SourcePath, long Line, long Column,
    string? InstructionReference, bool? CanRestart);

internal sealed record DebugProjectionGenerations(
    long All, long Threads, long Stacks, long Variables, long Sources, long Modules, long Memory);

internal sealed class DebugSessionProjections
{
    private const int MaximumModules = 4096;
    private const int MaximumSources = 4096;
    private const int MaximumMemoryRanges = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, DebugModuleSnapshot> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DebugLoadedSourceSnapshot> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<long, MemoryRange> _memoryRanges = [];
    private readonly Dictionary<int, long> _threadGenerations = [];
    private readonly Dictionary<(int ThreadId, int FrameId), long> _frameGenerations = [];
    private readonly Dictionary<string, SuspensionToken> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<DebugSemanticScope>> _scopeCaches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DebugSemanticVariables> _variableCaches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceResponseBody> _sourceCaches = new(StringComparer.Ordinal);
    private readonly Dictionary<int, StackCache> _stackFrames = [];
    private long _nextMemoryRangeId;
    private long _allGeneration;
    private long _threadGeneration;
    private long _stackGeneration;
    private long _variableGeneration;
    private long _sourceGeneration;
    private long _moduleGeneration;
    private long _memoryGeneration;
    private const int MaximumTokens = 4096;
    private long _followUpFailures;

    public DebugProcessSnapshot? Process { get; private set; }

    public IReadOnlyList<DebugModuleSnapshot> Modules
    {
        get { lock (_gate) return _modules.Values.OrderBy(x => x.OpaqueId, StringComparer.Ordinal).ToArray(); }
    }

    public IReadOnlyList<DebugLoadedSourceSnapshot> Sources
    {
        get { lock (_gate) return _sources.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(); }
    }

    public DebugProjectionGenerations Generations
    {
        get { lock (_gate) return SnapshotGenerationsLocked(); }
    }

    public long FollowUpFailures => Interlocked.Read(ref _followUpFailures);

    public void ObserveProcess(ProcessEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_gate)
            Process = new(Bound(body.Name, 1024)!, body.SystemProcessId, body.IsLocalProcess,
                Bound(body.StartMethod, 128), body.PointerSize);
    }

    public void ObserveModule(ModuleEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var id = CanonicalOpaqueId(body.Module.Id);
        lock (_gate)
        {
            if (body.Reason == "removed") _modules.Remove(id);
            else if (body.Reason is "new" or "changed")
            {
                if (!_modules.ContainsKey(id) && _modules.Count >= MaximumModules)
                    RemoveFirstLocked(_modules);
                _modules[id] = new(id, Bound(body.Module.Name, 1024)!, Bound(body.Module.Path, 4096),
                    body.Module.IsOptimized, body.Module.IsUserCode, Bound(body.Module.Version, 256),
                    Bound(body.Module.SymbolStatus, 512));
            }
            else throw new InvalidOperationException($"Unsupported DAP module reason '{Bound(body.Reason, 64)}'.");
            checked { _moduleGeneration++; }
            InvalidateTokensLocked(static token => token.Kind == "module");
        }
    }

    public void ObserveLoadedSource(LoadedSourceEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var key = SourceKey(body.Source);
        lock (_gate)
        {
            if (body.Reason == "removed") _sources.Remove(key);
            else if (body.Reason is "new" or "changed")
            {
                if (!_sources.ContainsKey(key) && _sources.Count >= MaximumSources)
                    RemoveFirstLocked(_sources);
                _sources[key] = new(key, Bound(body.Source.Name, 1024), Bound(body.Source.Path, 4096),
                    body.Source.SourceReference, Bound(body.Source.Origin, 256),
                    body.Source.AdapterData?.Clone());
            }
            else throw new InvalidOperationException($"Unsupported DAP loadedSource reason '{Bound(body.Reason, 64)}'.");
            checked { _sourceGeneration++; }
            InvalidateTokensLocked(static token => token.Kind == "source" && token.ThreadId == 0);
        }
    }

    public DebugProjectionGenerations Invalidate(InvalidatedEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_gate)
        {
            var areas = body.Areas;
            if (areas is null || areas.Count == 0 || areas.Any(x => x.Value == InvalidatedAreas.All.Value))
            {
                checked
                {
                    _allGeneration++; _threadGeneration++; _stackGeneration++; _variableGeneration++;
                    _sourceGeneration++; _moduleGeneration++; _memoryGeneration++;
                }
                _stackFrames.Clear();
                _sources.Clear();
                _modules.Clear();
                _memoryRanges.Clear();
                Process = null;
                InvalidateTokensLocked(static _ => true);
            }
            else
            {
                foreach (var area in areas.Select(x => x.Value).Distinct(StringComparer.Ordinal))
                    switch (area)
                    {
                        case "threads":
                            checked { _threadGeneration++; }
                            InvalidateTargetLocked(body.ThreadId, body.StackFrameId, includeThread: true);
                            break;
                        case "stacks":
                            checked { _stackGeneration++; _variableGeneration++; }
                            InvalidateTargetLocked(body.ThreadId, body.StackFrameId, includeThread: true);
                            break;
                        case "variables":
                            checked { _variableGeneration++; }
                            InvalidateVariableTokensLocked(body.ThreadId, body.StackFrameId);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported DAP invalidation area '{Bound(area, 64)}'.");
                    }
            }
            return SnapshotGenerationsLocked();
        }
    }

    public DebugProjectionGenerations InvalidateForContinue(int threadId, bool allThreadsContinued)
    {
        lock (_gate)
        {
            checked { _stackGeneration++; _variableGeneration++; }
            if (allThreadsContinued)
            {
                checked { _threadGeneration++; }
                foreach (var id in _threadGenerations.Keys.ToArray()) _threadGenerations[id]++;
                _frameGenerations.Clear();
                _stackFrames.Clear();
                InvalidateTokensLocked(static _ => true);
            }
            else
            {
                if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
                AdvanceThreadLocked(threadId);
                InvalidateTokensLocked(token => token.ThreadId == threadId || token.ThreadId == 0);
            }
            return SnapshotGenerationsLocked();
        }
    }

    public void ObserveStopped(int? threadId, bool allThreadsStopped)
    {
        lock (_gate)
        {
            if (allThreadsStopped)
            {
                foreach (var id in _threadGenerations.Keys.ToArray()) AdvanceThreadLocked(id);
                InvalidateTokensLocked(static _ => true);
            }
            else if (threadId is > 0)
            {
                AdvanceThreadLocked(threadId.Value);
                InvalidateTokensLocked(token => token.ThreadId == threadId.Value || token.ThreadId == 0);
            }
        }
    }

    public void ObserveThreadRemoved(int threadId)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        lock (_gate)
        {
            AdvanceThreadLocked(threadId);
            InvalidateTokensLocked(token => token.ThreadId == threadId);
            checked { _threadGeneration++; }
        }
    }

    public void InvalidateForCapabilityRemoval(IReadOnlyList<string> disabled)
    {
        ArgumentNullException.ThrowIfNull(disabled);
        if (disabled.Count == 0) return;
        lock (_gate)
        {
            InvalidateTokensLocked(static _ => true);
            checked { _allGeneration++; }
            if (disabled.Contains("readMemory", StringComparer.Ordinal))
            {
                _memoryRanges.Clear();
                checked { _memoryGeneration++; }
            }
        }
    }

    public string CreateSuspensionToken(int threadId, int? frameId, string kind, int adapterReference = 0)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _threadGenerations.TryGetValue(threadId, out var threadGeneration);
            var frameGeneration = frameId is { } id && _frameGenerations.TryGetValue((threadId, id), out var value) ? value : 0;
            _tokens[token] = new(threadId, frameId, kind, adapterReference, null, null, threadGeneration, frameGeneration);
            return token;
        }
    }

    public string CreateSessionToken(string kind, int adapterReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (adapterReference <= 0) throw new ArgumentOutOfRangeException(nameof(adapterReference));
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _tokens[token] = new(0, null, kind, adapterReference, null, null, _allGeneration, 0);
            return token;
        }
    }

    public string CreateSessionTextToken(string kind, string adapterText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterText);
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _tokens[token] = new(0, null, kind, 0, adapterText, null, _allGeneration, 0);
            return token;
        }
    }

    public string CreateSuspensionTextToken(int threadId, int? frameId, string kind, string adapterText)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterText);
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _threadGenerations.TryGetValue(threadId, out var threadGeneration);
            var frameGeneration = frameId is { } id && _frameGenerations.TryGetValue((threadId, id), out var value) ? value : 0;
            _tokens[token] = new(threadId, frameId, kind, 0, adapterText, null, threadGeneration, frameGeneration);
            return token;
        }
    }

    public string CreateSourceToken(int threadId, int? frameId, Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reference = source.SourceReference.GetValueOrDefault();
        if (reference <= 0 && string.IsNullOrWhiteSpace(source.Path))
            throw new ArgumentException("A source token requires a source reference or path.", nameof(source));
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _threadGenerations.TryGetValue(threadId, out var threadGeneration);
            var frameGeneration = frameId is { } id && _frameGenerations.TryGetValue((threadId, id), out var value) ? value : 0;
            _tokens[token] = new(threadId, frameId, "source", reference, source.Path,
                source.AdapterData?.Clone(), threadGeneration, frameGeneration);
            return token;
        }
    }

    public Source ResolveSourceToken(string token)
    {
        var reference = ResolveSuspensionToken(token, "source", out _, out _);
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var value))
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The source reference expired.");
            return new Source
            {
                Path = value.AdapterText,
                SourceReference = reference > 0 ? reference : null,
                AdapterData = value.AdapterData?.Clone()
            };
        }
    }

    public string CreateDataBreakpointToken(int threadId, int? frameId, string dataId)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataId);
        lock (_gate)
        {
            if (_tokens.Count >= MaximumTokens) RemoveTokenLocked(_tokens.Keys.First());
            var token = Guid.NewGuid().ToString("N");
            _threadGenerations.TryGetValue(threadId, out var threadGeneration);
            var frameGeneration = frameId is { } id && _frameGenerations.TryGetValue((threadId, id), out var value) ? value : 0;
            _tokens[token] = new(threadId, frameId, "dataBreakpoint", 0,
                dataId, null, threadGeneration, frameGeneration);
            return token;
        }
    }

    public string ResolveDataBreakpointToken(string token)
    {
        _ = ResolveSuspensionToken(token, "dataBreakpoint", out _, out _);
        lock (_gate)
            return _tokens.TryGetValue(token, out var value) && value.AdapterText is { } dataId
                ? dataId : throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The data-breakpoint discovery expired.");
    }

    public string ResolveTextToken(string token, string expectedKind, out int threadId, out int? frameId)
    {
        _ = ResolveSuspensionToken(token, expectedKind, out threadId, out frameId);
        lock (_gate)
            return _tokens.TryGetValue(token, out var value) && value.AdapterText is { } text
                ? text : throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The debugger text reference expired.");
    }

    public int ResolveSuspensionToken(string token, string expectedKind, out int threadId, out int? frameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKind);
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var value) ||
                !string.Equals(value.Kind, expectedKind, StringComparison.Ordinal))
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The debugger reference is unknown, expired, or has the wrong kind.");
            var threadGeneration = value.ThreadId == 0 ? _allGeneration :
                _threadGenerations.TryGetValue(value.ThreadId, out var currentThread) ? currentThread : 0;
            var frameGeneration = value.FrameId is { } id &&
                _frameGenerations.TryGetValue((value.ThreadId, id), out var currentFrame) ? currentFrame : 0;
            if (threadGeneration != value.ThreadGeneration || frameGeneration != value.FrameGeneration)
            {
                RemoveTokenLocked(token);
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The debugger reference expired when suspension state changed.");
            }
            threadId = value.ThreadId;
            frameId = value.FrameId;
            return value.AdapterReference;
        }
    }

    public bool IsSuspensionTokenValid(string token)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var value)) return false;
            var threadGeneration = value.ThreadId == 0 ? _allGeneration :
                _threadGenerations.TryGetValue(value.ThreadId, out var currentThread) ? currentThread : 0;
            var frameGeneration = value.FrameId is { } id && _frameGenerations.TryGetValue((value.ThreadId, id), out var frame) ? frame : 0;
            return threadGeneration == value.ThreadGeneration && frameGeneration == value.FrameGeneration;
        }
    }

    public void CacheScopes(string frameToken, IReadOnlyList<DebugSemanticScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        lock (_gate)
        {
            if (!_tokens.ContainsKey(frameToken)) throw new InvalidOperationException("The frame reference expired.");
            _scopeCaches[frameToken] = scopes.ToArray();
        }
    }

    public void CacheVariables(string variablesToken, DebugSemanticVariables variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        lock (_gate)
        {
            if (!_tokens.ContainsKey(variablesToken)) throw new InvalidOperationException("The variables reference expired.");
            _variableCaches[variablesToken] = variables;
        }
    }

    public void CacheSource(string sourceToken, SourceResponseBody source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (!_tokens.ContainsKey(sourceToken)) throw new InvalidOperationException("The source reference expired.");
            _sourceCaches[sourceToken] = new SourceResponseBody
            {
                Content = Bound(source.Content, 256 * 1024)!,
                MimeType = Bound(source.MimeType, 256)
            };
        }
    }

    public void CacheStackFrames(int threadId, long suspensionEpoch, IReadOnlyList<StackFrame> frames)
    {
        if (threadId <= 0 || suspensionEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        ArgumentNullException.ThrowIfNull(frames);
        lock (_gate)
            _stackFrames[threadId] = new(suspensionEpoch, frames.Take(64).Select(frame => new DebugStackFrameSnapshot(
                frame.Id, Bound(frame.Name, 1024)!, Bound(frame.Source?.Path, 4096), frame.Line, frame.Column,
                Bound(frame.InstructionPointerReference, 1024), frame.CanRestart)).ToArray());
    }

    public IReadOnlyList<DebugStackFrameSnapshot> GetStackFrames(int threadId, long suspensionEpoch)
    {
        lock (_gate)
            return _stackFrames.TryGetValue(threadId, out var cache) && cache.SuspensionEpoch == suspensionEpoch
                ? cache.Frames : [];
    }

    public DebugStackFrameSnapshot? FindStackFrame(int threadId, long suspensionEpoch, int frameId)
    {
        lock (_gate)
            return _stackFrames.TryGetValue(threadId, out var cache) && cache.SuspensionEpoch == suspensionEpoch
                ? cache.Frames.FirstOrDefault(frame => frame.FrameId == frameId) : null;
    }

    public void RecordFollowUpFailure() => Interlocked.Increment(ref _followUpFailures);

    public long TrackMemoryRange(string memoryReference, long offset, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryReference);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        lock (_gate)
        {
            if (_memoryRanges.Count >= MaximumMemoryRanges) RemoveFirstLocked(_memoryRanges);
            var id = checked(++_nextMemoryRangeId);
            _memoryRanges[id] = new(memoryReference, offset, count);
            return id;
        }
    }

    public int ObserveMemory(MemoryEventBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.MemoryReference) || body.Count <= 0)
            throw new InvalidOperationException("A DAP memory event requires a reference and positive count.");
        lock (_gate)
        {
            var removed = 0;
            foreach (var entry in _memoryRanges.ToArray())
                if (string.Equals(entry.Value.Reference, body.MemoryReference, StringComparison.Ordinal) &&
                    Overlaps(entry.Value.Offset, entry.Value.Count, body.Offset, body.Count))
                {
                    _memoryRanges.Remove(entry.Key);
                    removed++;
                }
            if (removed != 0)
            {
                checked { _memoryGeneration++; }
                InvalidateTokensLocked(static token => token.Kind is "memoryRange" or "instruction");
            }
            return removed;
        }
    }

    public bool ContainsMemoryRange(long id)
    {
        lock (_gate) return _memoryRanges.ContainsKey(id);
    }

    private DebugProjectionGenerations SnapshotGenerationsLocked() => new(
        _allGeneration, _threadGeneration, _stackGeneration, _variableGeneration,
        _sourceGeneration, _moduleGeneration, _memoryGeneration);

    private void InvalidateTargetLocked(int? threadId, int? frameId, bool includeThread)
    {
        if (threadId is null)
        {
            InvalidateTokensLocked(static _ => true);
            return;
        }
        if (frameId is { } frame)
        {
            var key = (threadId.Value, frame);
            _frameGenerations.TryGetValue(key, out var generation);
            _frameGenerations[key] = checked(generation + 1);
            InvalidateTokensLocked(token => token.ThreadId == threadId && token.FrameId == frame);
        }
        else if (includeThread)
        {
            AdvanceThreadLocked(threadId.Value);
            InvalidateTokensLocked(token => token.ThreadId == threadId);
        }
        else InvalidateTokensLocked(token => token.ThreadId == threadId);
    }

    private void InvalidateVariableTokensLocked(int? threadId, int? frameId)
    {
        static bool IsVariableDerived(SuspensionToken token)
            => token.Kind is "variables" or "memory" or "location";

        InvalidateTokensLocked(token => IsVariableDerived(token) &&
            (threadId is null || token.ThreadId == threadId) &&
            (frameId is null || token.FrameId == frameId));
    }

    private void AdvanceThreadLocked(int threadId)
    {
        _threadGenerations.TryGetValue(threadId, out var generation);
        _threadGenerations[threadId] = checked(generation + 1);
        _stackFrames.Remove(threadId);
        foreach (var key in _frameGenerations.Keys.Where(x => x.ThreadId == threadId).ToArray())
            _frameGenerations.Remove(key);
    }

    private void InvalidateTokensLocked(Func<SuspensionToken, bool> predicate)
    {
        foreach (var item in _tokens.Where(x => predicate(x.Value)).Select(x => x.Key).ToArray())
            RemoveTokenLocked(item);
        foreach (var frameToken in _scopeCaches.Keys.Where(x => !_tokens.ContainsKey(x)).ToArray())
            _scopeCaches.Remove(frameToken);
    }

    private void RemoveTokenLocked(string token)
    {
        _tokens.Remove(token);
        _scopeCaches.Remove(token);
        _variableCaches.Remove(token);
        _sourceCaches.Remove(token);
    }

    private static bool Overlaps(long firstOffset, long firstCount, long secondOffset, long secondCount)
    {
        decimal firstStart = firstOffset, firstEnd = firstStart + firstCount;
        decimal secondStart = secondOffset, secondEnd = secondStart + secondCount;
        return firstStart < secondEnd && secondStart < firstEnd;
    }

    private static string SourceKey(Source source)
        => source.SourceReference is > 0 ? $"ref:{source.SourceReference.Value}"
            : source.Path is { Length: > 0 } path ? $"path:{path}"
            : $"name:{source.Name ?? string.Empty}";

    internal static string CanonicalOpaqueId(JsonElement id)
        => id.ValueKind is JsonValueKind.String ? $"s:{id.GetString()}"
            : id.ValueKind is JsonValueKind.Number ? $"n:{id.GetRawText()}"
            : throw new InvalidOperationException("A DAP module ID must be a string or number.");

    private static void RemoveFirstLocked<TKey, TValue>(Dictionary<TKey, TValue> values) where TKey : notnull
        => values.Remove(values.Keys.First());

    private static string? Bound(string? value, int maximum)
        => value is null ? null : value[..Math.Min(value.Length, maximum)];

    private sealed record MemoryRange(string Reference, long Offset, long Count);
    private sealed record SuspensionToken(
        int ThreadId, int? FrameId, string Kind, int AdapterReference, string? AdapterText, JsonElement? AdapterData,
        long ThreadGeneration, long FrameGeneration);
    private sealed record StackCache(long SuspensionEpoch, IReadOnlyList<DebugStackFrameSnapshot> Frames);
}
