using System.Collections.Immutable;
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

    public ValueTask ReplaceSourceAsync(IReadOnlyList<DebugSourceBreakpoint> value, Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Source, apply, static (state, items) => state with { Source = items }, cancellationToken);

    public ValueTask MutateSourceAsync(Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>> mutation, Func<IReadOnlyList<DebugSourceBreakpoint>, IReadOnlyList<DebugSourceBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Source, apply, static (state, items) => state with { Source = items }, cancellationToken);

    public ValueTask ReplaceFunctionAsync(IReadOnlyList<DebugFunctionBreakpoint> value, Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Function, apply, static (state, items) => state with { Function = items }, cancellationToken);

    public ValueTask MutateFunctionAsync(Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>> mutation, Func<IReadOnlyList<DebugFunctionBreakpoint>, IReadOnlyList<DebugFunctionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Function, apply, static (state, items) => state with { Function = items }, cancellationToken);

    public ValueTask ReplaceExceptionAsync(IReadOnlyList<DebugExceptionFilter> value, Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Exception, apply, static (state, items) => state with { Exception = items }, cancellationToken);

    public ValueTask MutateExceptionAsync(Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>> mutation, Func<IReadOnlyList<DebugExceptionFilter>, IReadOnlyList<DebugExceptionFilter>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Exception, apply, static (state, items) => state with { Exception = items }, cancellationToken);

    public ValueTask ReplaceInstructionAsync(IReadOnlyList<DebugInstructionBreakpoint> value, Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Instruction, apply, static (state, items) => state with { Instruction = items }, cancellationToken);

    public ValueTask MutateInstructionAsync(Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>> mutation, Func<IReadOnlyList<DebugInstructionBreakpoint>, IReadOnlyList<DebugInstructionBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Instruction, apply, static (state, items) => state with { Instruction = items }, cancellationToken);

    public ValueTask ReplaceDataAsync(IReadOnlyList<DebugDataBreakpoint> value, Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(_ => value, static state => state.Data, apply, static (state, items) => state with { Data = items }, cancellationToken);

    public ValueTask MutateDataAsync(Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>> mutation, Func<IReadOnlyList<DebugDataBreakpoint>, IReadOnlyList<DebugDataBreakpoint>, CancellationToken, ValueTask> apply, CancellationToken cancellationToken = default)
        => MutateAsync(mutation, static state => state.Data, apply, static (state, items) => state with { Data = items }, cancellationToken);

    private async ValueTask MutateAsync<T>(
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
            var prior = select(_desired);
            var frozen = (mutation(prior) ?? throw new InvalidOperationException("A breakpoint mutation returned null."))
                .ToImmutableArray();
            await apply(prior, frozen, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _desired, update(_desired, frozen));
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

public sealed record DebugConfirmedBreakpoint(
    DebugBreakpointKind Kind,
    int? AdapterId,
    bool Verified,
    string? Message,
    string? SourcePath,
    long? Line,
    long? Column,
    string? InstructionReference,
    long? Offset);

/// <summary>Adapter-confirmed breakpoint state owned by exactly one protocol session.</summary>
internal sealed class DebugConfirmedBreakpointStore
{
    private readonly object _gate = new();
    private ImmutableArray<DebugConfirmedBreakpoint> _items = [];

    public ImmutableArray<DebugConfirmedBreakpoint> Snapshot
    {
        get { lock (_gate) return _items; }
    }

    public void Replace(DebugBreakpointKind kind, IReadOnlyList<Breakpoint> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        var replacement = breakpoints.Select(value => FromProtocol(kind, value)).ToImmutableArray();
        lock (_gate) _items = _items.Where(x => x.Kind != kind).Concat(replacement).ToImmutableArray();
    }

    public void ReplaceSource(string sourcePath, IReadOnlyList<Breakpoint> breakpoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(breakpoints);
        var replacement = breakpoints.Select(value => FromProtocol(DebugBreakpointKind.Source, value, sourcePath)).ToImmutableArray();
        lock (_gate)
            _items = _items.Where(x => x.Kind != DebugBreakpointKind.Source ||
                !string.Equals(x.SourcePath, sourcePath, StringComparison.Ordinal)).Concat(replacement).ToImmutableArray();
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
            var confirmed = FromProtocol(kind, value);
            _items = index >= 0 ? _items.SetItem(index, confirmed) : _items.Add(confirmed);
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
            string.Equals(x.SourcePath, state.Source?.Path, StringComparison.Ordinal) &&
            x.Line == state.Line && x.Column == state.Column &&
            string.Equals(x.InstructionReference, state.InstructionReference, StringComparison.Ordinal) &&
            x.Offset == state.Offset, value);
    }

    private int IndexOf<TState>(Func<DebugConfirmedBreakpoint, TState, bool> predicate, TState state)
    {
        for (var index = 0; index < _items.Length; index++)
            if (predicate(_items[index], state)) return index;
        return -1;
    }

    private static DebugBreakpointKind InferKind(Breakpoint value)
        => value.InstructionReference is not null ? DebugBreakpointKind.Instruction : DebugBreakpointKind.Source;

    private static DebugConfirmedBreakpoint FromProtocol(DebugBreakpointKind kind, Breakpoint value, string? fallbackSourcePath = null) => new(
        kind, value.Id, value.Verified, value.Message, value.Source?.Path ?? fallbackSourcePath, value.Line, value.Column,
        value.InstructionReference, value.Offset);
}
