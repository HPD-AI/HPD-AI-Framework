using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugBreakpointKind
{
    Source,
    Function,
    Exception,
    Instruction,
    Data
}

public sealed record DebugDataBreakpointRecipe(
    string Name,
    int? VariablesReference = null,
    int? FrameId = null,
    long? Bytes = null,
    bool? AsAddress = null,
    string? Mode = null);

public sealed record DebugDesiredBreakpointSnapshot
{
    public ImmutableArray<DebugSourceBreakpoint> Source { get; init; } = [];
    public ImmutableArray<DebugFunctionBreakpoint> Function { get; init; } = [];
    public ImmutableArray<DebugExceptionFilter> Exception { get; init; } = [];
    public ImmutableArray<DebugInstructionBreakpoint> Instruction { get; init; } = [];
    public ImmutableArray<DebugDataBreakpoint> Data { get; init; } = [];
}

/// <summary>
/// Root-owned desired breakpoint state. The mutation callback sends the complete replacement to
/// the adapter while the gate is held; desired state changes only after that send succeeds.
/// </summary>
internal sealed class DebugBreakpointStore : IAsyncDisposable
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private DebugDesiredBreakpointSnapshot _desired = new();
    private int _disposed;

    public DebugDesiredBreakpointSnapshot Snapshot => Volatile.Read(ref _desired);

    public void Seed(DebugInitialConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        Volatile.Write(ref _desired, new DebugDesiredBreakpointSnapshot
        {
            Source = configuration.SourceBreakpoints.ToImmutableArray(),
            Function = configuration.FunctionBreakpoints.ToImmutableArray(),
            Exception = configuration.ExceptionFilters.ToImmutableArray(),
            Instruction = configuration.InstructionBreakpoints.ToImmutableArray(),
            Data = configuration.DataBreakpoints.ToImmutableArray()
        });
    }

    public ValueTask<DebugDesiredBreakpointMutation> ReplaceSourceAsync(IReadOnlyList<DebugSourceBreakpoint> value, Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Source, apply, static (state, items) => state with { Source = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> MutateSourceAsync(Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>> mutation, Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Source, apply, static (state, items) => state with { Source = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> ReplaceFunctionAsync(IReadOnlyList<DebugFunctionBreakpoint> value, Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Function, apply, static (state, items) => state with { Function = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> MutateFunctionAsync(Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>> mutation, Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Function, apply, static (state, items) => state with { Function = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> ReplaceExceptionAsync(IReadOnlyList<DebugExceptionFilter> value, Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Exception, apply, static (state, items) => state with { Exception = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> MutateExceptionAsync(Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>> mutation, Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Exception, apply, static (state, items) => state with { Exception = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> ReplaceInstructionAsync(IReadOnlyList<DebugInstructionBreakpoint> value, Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Instruction, apply, static (state, items) => state with { Instruction = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> MutateInstructionAsync(Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>> mutation, Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Instruction, apply, static (state, items) => state with { Instruction = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> ReplaceDataAsync(IReadOnlyList<DebugDataBreakpoint> value, Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Data, apply, static (state, items) => state with { Data = items }, cancellationToken);

    public ValueTask<DebugDesiredBreakpointMutation> MutateDataAsync(Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>> mutation, Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Data, apply, static (state, items) => state with { Data = items }, cancellationToken);

    private async ValueTask<DebugDesiredBreakpointMutation> MutateAsync<T>(
        Func<IReadOnlyList<T>, IReadOnlyList<T>> mutation,
        Func<DebugDesiredBreakpointSnapshot, ImmutableArray<T>> select,
        Func<IReadOnlyList<T>, IReadOnlyList<T>, CancellationToken, ValueTask> apply,
        Func<DebugDesiredBreakpointSnapshot, ImmutableArray<T>, DebugDesiredBreakpointSnapshot> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(apply);
        ThrowIfDisposed();
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var before = _desired;
            var prior = select(before);
            var frozen = (mutation(prior) ?? throw new InvalidOperationException("A breakpoint mutation returned null."))
                .ToImmutableArray();
            await apply(prior, frozen, cancellationToken).ConfigureAwait(false);
            var after = update(before, frozen);
            Volatile.Write(ref _desired, after);
            return new DebugDesiredBreakpointMutation(before, after);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A successfully committed desired-breakpoint transition.</summary>
internal sealed record DebugDesiredBreakpointMutation(
    DebugDesiredBreakpointSnapshot Before,
    DebugDesiredBreakpointSnapshot After);

/// <summary>
/// Correlates one semantic breakpoint request with the adapter location that
/// acknowledged it in one protocol session.
/// </summary>
internal sealed record DebugBreakpointBindingState
{
    public required DebugBreakpointKind Kind { get; init; }
    public required string ClientBreakpointId { get; init; }
    public int? AdapterId { get; init; }
    public required bool Acknowledged { get; init; }
    public required bool Verified { get; init; }
    public string? Message { get; init; }

    public string? RequestedPath { get; init; }
    public long? RequestedLine { get; init; }
    public long? RequestedColumn { get; init; }
    public string? RequestedName { get; init; }
    public string? RequestedInstructionReference { get; init; }
    public long? RequestedOffset { get; init; }

    public string? ResolvedPath { get; init; }
    public long? ResolvedLine { get; init; }
    public long? ResolvedColumn { get; init; }
    public string? ResolvedInstructionReference { get; init; }
    public long? ResolvedOffset { get; init; }

    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }
}

/// <summary>Aggregate desired and adapter-acknowledged breakpoint counts.</summary>
public sealed record DebugBreakpointCounts(
    int Requested,
    int Acknowledged,
    int Verified,
    int Pending);

/// <summary>Adapter breakpoint state owned by exactly one protocol session.</summary>
internal sealed class DebugAdapterBreakpointStateStore
{
    private readonly object _gate = new();
    private ImmutableArray<DebugBreakpointBindingState> _items = [];
    private ImmutableArray<DebugUnmatchedAdapterBreakpointDiagnostic> _unmatched = [];

    public ImmutableArray<DebugBreakpointBindingState> Snapshot
    {
        get { lock (_gate) return _items; }
    }

    public ImmutableArray<DebugUnmatchedAdapterBreakpointDiagnostic> UnmatchedResponses
    {
        get { lock (_gate) return _unmatched; }
    }

    public void ReplaceFunction(
        IReadOnlyList<DebugFunctionBreakpoint> requested,
        IReadOnlyList<Breakpoint> breakpoints)
        => Replace(
            DebugBreakpointKind.Function,
            requested,
            breakpoints,
            static value => BreakpointIdentity.Function(value),
            static value => new DebugBreakpointBindingState
            {
                Kind = DebugBreakpointKind.Function,
                ClientBreakpointId = BreakpointIdentity.Function(value),
                RequestedName = value.Name,
                Condition = value.Condition,
                HitCondition = value.HitCondition,
                Acknowledged = false,
                Verified = false
            });

    public void ReplaceException(
        IReadOnlyList<DebugExceptionFilter> requested,
        IReadOnlyList<Breakpoint> breakpoints)
        => Replace(
            DebugBreakpointKind.Exception,
            requested,
            breakpoints,
            static value => BreakpointIdentity.Exception(value),
            static value => new DebugBreakpointBindingState
            {
                Kind = DebugBreakpointKind.Exception,
                ClientBreakpointId = BreakpointIdentity.Exception(value),
                RequestedName = value.FilterId,
                Condition = value.Condition,
                Acknowledged = false,
                Verified = false
            });

    public void ReplaceInstruction(
        IReadOnlyList<DebugInstructionBreakpoint> requested,
        IReadOnlyList<Breakpoint> breakpoints)
        => Replace(
            DebugBreakpointKind.Instruction,
            requested,
            breakpoints,
            static value => BreakpointIdentity.Instruction(value),
            static value => new DebugBreakpointBindingState
            {
                Kind = DebugBreakpointKind.Instruction,
                ClientBreakpointId = BreakpointIdentity.Instruction(value),
                RequestedInstructionReference = value.InstructionReference,
                RequestedOffset = value.Offset,
                Condition = value.Condition,
                HitCondition = value.HitCondition,
                Acknowledged = false,
                Verified = false
            });

    public void ReplaceData(
        IReadOnlyList<DebugDataBreakpoint> requested,
        IReadOnlyList<Breakpoint> breakpoints)
        => Replace(
            DebugBreakpointKind.Data,
            requested,
            breakpoints,
            static value => BreakpointIdentity.Data(value),
            static value => new DebugBreakpointBindingState
            {
                Kind = DebugBreakpointKind.Data,
                ClientBreakpointId = BreakpointIdentity.Data(value),
                RequestedName = value.DataId,
                Condition = value.Condition,
                HitCondition = value.HitCondition,
                Acknowledged = false,
                Verified = false
            });

    private void Replace<T>(
        DebugBreakpointKind kind,
        IReadOnlyList<T> requested,
        IReadOnlyList<Breakpoint> breakpoints,
        Func<T, string> identity,
        Func<T, DebugBreakpointBindingState> create)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(breakpoints);
        var replacement = ImmutableArray.CreateBuilder<DebugBreakpointBindingState>(requested.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < requested.Count; index++)
        {
            var request = requested[index];
            var baseIdentity = identity(request);
            occurrences.TryGetValue(baseIdentity, out var occurrence);
            occurrences[baseIdentity] = occurrence + 1;
            var state = create(request) with
            {
                ClientBreakpointId = BreakpointIdentity.Occurrence(baseIdentity, occurrence)
            };
            replacement.Add(index < breakpoints.Count
                ? ApplyProtocol(state, breakpoints[index])
                : state);
        }

        lock (_gate)
        {
            _items = _items.Where(x => x.Kind != kind).Concat(replacement).ToImmutableArray();
            RetainUnmatched(kind, breakpoints.Skip(requested.Count));
        }
    }

    public void ReplaceSource(
        string sourcePath,
        IReadOnlyList<DebugSourceBreakpoint> requested,
        IReadOnlyList<Breakpoint> breakpoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(breakpoints);
        var replacement = ImmutableArray.CreateBuilder<DebugBreakpointBindingState>(requested.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < requested.Count; index++)
        {
            var request = requested[index];
            var baseIdentity = BreakpointIdentity.Source(request);
            occurrences.TryGetValue(baseIdentity, out var occurrence);
            occurrences[baseIdentity] = occurrence + 1;
            var state = new DebugBreakpointBindingState
            {
                Kind = DebugBreakpointKind.Source,
                ClientBreakpointId = BreakpointIdentity.Occurrence(baseIdentity, occurrence),
                RequestedPath = request.Path,
                RequestedLine = request.Line,
                RequestedColumn = request.Column,
                Condition = request.Condition,
                HitCondition = request.HitCondition,
                LogMessage = request.LogMessage,
                Acknowledged = false,
                Verified = false
            };
            replacement.Add(index < breakpoints.Count
                ? ApplyProtocol(state, breakpoints[index], sourcePath)
                : state);
        }

        lock (_gate)
        {
            _items = _items.Where(x => x.Kind != DebugBreakpointKind.Source ||
                !string.Equals(x.RequestedPath, sourcePath, StringComparison.Ordinal))
                .Concat(replacement)
                .ToImmutableArray();
            RetainUnmatched(DebugBreakpointKind.Source, breakpoints.Skip(requested.Count));
        }
    }

    public void Reconcile(string reason, Breakpoint value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            var index = FindIndex(value);
            if (string.Equals(reason, "removed", StringComparison.Ordinal))
            {
                if (index >= 0) _items = _items.RemoveAt(index);
                return;
            }

            var kind = index >= 0 ? _items[index].Kind : InferKind(value);
            var state = index >= 0
                ? ApplyProtocol(_items[index], value)
                : ApplyProtocol(CreateUnmatched(kind, value), value);
            _items = index >= 0 ? _items.SetItem(index, state) : _items.Add(state);
        }
    }

    private int FindIndex(Breakpoint value)
    {
        if (value.Id is { } id)
        {
            var byId = IndexOf(static (x, state) => x.AdapterId == state, id);
            if (byId >= 0) return byId;
        }
        return IndexOf(static (x, state) =>
            string.Equals(x.ResolvedPath, state.Source?.Path, StringComparison.Ordinal) &&
            x.ResolvedLine == state.Line && x.ResolvedColumn == state.Column &&
            string.Equals(x.ResolvedInstructionReference, state.InstructionReference, StringComparison.Ordinal) &&
            x.ResolvedOffset == state.Offset, value);
    }

    private int IndexOf<TState>(Func<DebugBreakpointBindingState, TState, bool> predicate, TState state)
    {
        for (var index = 0; index < _items.Length; index++)
            if (predicate(_items[index], state)) return index;
        return -1;
    }

    private static DebugBreakpointKind InferKind(Breakpoint value)
        => value.InstructionReference is not null ? DebugBreakpointKind.Instruction : DebugBreakpointKind.Source;

    private static DebugBreakpointBindingState CreateUnmatched(
        DebugBreakpointKind kind,
        Breakpoint value)
        => new()
        {
            Kind = kind,
            ClientBreakpointId = BreakpointIdentity.Unmatched(value),
            Acknowledged = false,
            Verified = false
        };

    private static DebugBreakpointBindingState ApplyProtocol(
        DebugBreakpointBindingState state,
        Breakpoint value,
        string? fallbackSourcePath = null)
        => state with
        {
            AdapterId = value.Id,
            Acknowledged = true,
            Verified = value.Verified,
            Message = value.Message,
            ResolvedPath = value.Source?.Path ?? fallbackSourcePath,
            ResolvedLine = value.Line,
            ResolvedColumn = value.Column,
            ResolvedInstructionReference = value.InstructionReference,
            ResolvedOffset = value.Offset
        };

    private void RetainUnmatched(
        DebugBreakpointKind kind,
        IEnumerable<Breakpoint> values)
    {
        foreach (var value in values)
        {
            _unmatched = _unmatched.Add(new(
                kind,
                value.Id,
                value.Verified,
                value.Message is { Length: > 256 } message ? message[..256] : value.Message));
            if (_unmatched.Length > 32) _unmatched = _unmatched.RemoveAt(0);
        }
    }
}

internal sealed record DebugUnmatchedAdapterBreakpointDiagnostic(
    DebugBreakpointKind Kind,
    int? AdapterId,
    bool Verified,
    string? SafeMessage);

internal static class BreakpointIdentity
{
    public static string Occurrence(string identity, int occurrence)
        => occurrence == 0 ? identity : Digest("duplicate", identity, occurrence);

    public static string Source(DebugSourceBreakpoint value)
        => Digest("source", value.Path, value.Line, value.Column, value.Condition, value.HitCondition, value.LogMessage);

    public static string Function(DebugFunctionBreakpoint value)
        => Digest("function", value.Name, value.Condition, value.HitCondition);

    public static string Exception(DebugExceptionFilter value)
        => Digest("exception", value.FilterId, value.Condition);

    public static string Instruction(DebugInstructionBreakpoint value)
        => Digest("instruction", value.InstructionReference, value.Offset, value.Condition, value.HitCondition);

    public static string Data(DebugDataBreakpoint value)
        => Digest("data", value.DataId, value.AccessType, value.Condition, value.HitCondition);

    public static string Unmatched(Breakpoint value)
        => Digest("adapter", value.Id, value.Source?.Path, value.Line, value.Column,
            value.InstructionReference, value.Offset);

    private static string Digest(params object?[] values)
    {
        var canonical = string.Join('\u001f', values.Select(static value => value?.ToString() ?? ""));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}
