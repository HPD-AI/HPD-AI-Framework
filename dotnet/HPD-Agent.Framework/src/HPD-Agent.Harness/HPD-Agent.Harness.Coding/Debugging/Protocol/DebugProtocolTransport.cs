using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public sealed record DebugTransportDiagnosticChunk(
    ReadOnlyMemory<byte> Bytes,
    long DroppedChunks,
    long DroppedBytes,
    bool IsFinal = false);

public sealed record DebugTransportExit(
    ProcessCompletionKind CompletionKind,
    int? ExitCode = null,
    string? SafeReasonCode = null);

public sealed record DebugTransportStopRequest(
    StopKind Kind = StopKind.GracefulThenKill,
    string Reason = "DEBUG_TRANSPORT_STOP",
    TimeSpan? GracePeriod = null);

public sealed record DebugProtocolTransportLimits
{
    public int ProtocolChannelCapacity { get; init; } = 32;
    public int DiagnosticChannelCapacity { get; init; } = 32;
    public int MaxBufferedProtocolBytes { get; init; } = 4 * 1024 * 1024 + 32 * 1024;
    public int MaxDiagnosticBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        if (ProtocolChannelCapacity is <= 0 or > 1024 || DiagnosticChannelCapacity is <= 0 or > 1024 ||
            MaxBufferedProtocolBytes is <= 0 or > 32 * 1024 * 1024 || MaxDiagnosticBytes is <= 0 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(DebugProtocolTransportLimits));
    }
}

public interface IDebugProtocolTransport : IAsyncDisposable
{
    bool IsAlive { get; }
    ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(CancellationToken cancellationToken = default);
    ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic byte transport used by protocol conformance tests.</summary>
public sealed class InMemoryDebugProtocolTransport : IDebugProtocolTransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _written = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<DebugTransportDiagnosticChunk> _diagnostics = Channel.CreateUnbounded<DebugTransportDiagnosticChunk>();
    private readonly TaskCompletionSource<DebugTransportExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte[]? _current;
    private int _offset;
    private int _disposed;

    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_exit.Task.IsCompleted;

    public ValueTask FeedProtocolAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        => _incoming.Writer.WriteAsync(bytes.ToArray(), cancellationToken);

    public ValueTask FeedDiagnosticAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        => _diagnostics.Writer.WriteAsync(new(bytes.ToArray(), 0, 0), cancellationToken);

    public void Complete(DebugTransportExit? exit = null)
    {
        _incoming.Writer.TryComplete();
        _diagnostics.Writer.TryComplete();
        _written.Writer.TryComplete();
        _exit.TrySetResult(exit ?? new(ProcessCompletionKind.Completed));
    }

    public IAsyncEnumerable<byte[]> ReadWrittenAsync(CancellationToken cancellationToken = default)
        => _written.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
            return 0;
        while (_current is null || _offset == _current.Length)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                return 0;
            if (!_incoming.Reader.TryRead(out _current))
                continue;
            _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!IsAlive)
            return ValueTask.FromException(new InvalidOperationException("The debug transport is closed."));
        return _written.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
    }

    public IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(CancellationToken cancellationToken = default)
        => _diagnostics.Reader.ReadAllAsync(cancellationToken);

    public async ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default)
        => await _exit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken = default)
    {
        Complete(new(ProcessCompletionKind.Stopped, SafeReasonCode: request.Reason));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            Complete(new(ProcessCompletionKind.Stopped, SafeReasonCode: "TRANSPORT_DISPOSED"));
        return ValueTask.CompletedTask;
    }
}

internal sealed class DebugEnvironmentProcessTransport : IDebugProtocolTransport
{
    private readonly IProcessInvocationHandle _process;
    private readonly DebugProtocolTransportLimits _limits;
    private readonly Channel<byte[]> _protocol;
    private readonly Channel<DebugTransportDiagnosticChunk> _diagnostics;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<DebugTransportExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private byte[]? _current;
    private int _offset;
    private long _protocolBufferedBytes;
    private long _diagnosticBytes;
    private long _droppedDiagnosticChunks;
    private long _droppedDiagnosticBytes;
    private int _diagnosticReaderClaimed;
    private int _disposed;

    public DebugEnvironmentProcessTransport(IProcessInvocationHandle process, DebugProtocolTransportLimits? limits = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _limits = limits ?? new();
        _limits.Validate();
        _protocol = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_limits.ProtocolChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _diagnostics = Channel.CreateBounded<DebugTransportDiagnosticChunk>(new BoundedChannelOptions(_limits.DiagnosticChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _pump = PumpAsync();
    }

    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_exit.Task.IsCompleted;

    public async ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
            return 0;
        while (_current is null || _offset == _current.Length)
        {
            if (_current is not null)
                Interlocked.Add(ref _protocolBufferedBytes, -_current.Length);
            _current = null;
            if (!await _protocol.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                return 0;
            if (!_protocol.Reader.TryRead(out _current))
                continue;
            _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _process.WriteStdinAsync(buffer, cancellationToken);

    public async IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _diagnosticReaderClaimed, 1) != 0)
            throw new InvalidOperationException("The diagnostic stream supports exactly one reader.");
        await foreach (var chunk in _diagnostics.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return chunk;
        var droppedChunks = Volatile.Read(ref _droppedDiagnosticChunks);
        var droppedBytes = Volatile.Read(ref _droppedDiagnosticBytes);
        if (droppedChunks != 0)
            yield return new(ReadOnlyMemory<byte>.Empty, droppedChunks, droppedBytes, IsFinal: true);
    }

    public async ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default)
        => await _exit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken = default)
        => _process.StopAsync(new(request.Kind, request.Reason, request.GracePeriod), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lifetime.Cancel();
        try { await StopAsync(new(Reason: "TRANSPORT_DISPOSED")).ConfigureAwait(false); } catch { }
        try { await _pump.ConfigureAwait(false); } catch { }
        await _process.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task PumpAsync()
    {
        Exception? failure = null;
        try
        {
            await foreach (var chunk in _process.ReadOutputAsync(_lifetime.Token).ConfigureAwait(false))
            {
                var bytes = chunk.Bytes.ToArray();
                if (chunk.Stream == ProcessOutputStream.Stdout)
                {
                    if ((chunk.Flags & ProcessOutputChunkFlags.Truncated) != 0)
                        throw new InvalidDataException("The HPD Environment truncated protocol stdout.");
                    if (Interlocked.Add(ref _protocolBufferedBytes, bytes.Length) > _limits.MaxBufferedProtocolBytes)
                        throw new InvalidDataException("The protocol stdout hard buffer limit was exceeded.");
                    await _protocol.Writer.WriteAsync(bytes, _lifetime.Token).ConfigureAwait(false);
                }
                else
                {
                    var retained = Interlocked.Add(ref _diagnosticBytes, bytes.Length);
                    if (retained > _limits.MaxDiagnosticBytes || !_diagnostics.Writer.TryWrite(new(
                        bytes,
                        Volatile.Read(ref _droppedDiagnosticChunks),
                        Volatile.Read(ref _droppedDiagnosticBytes),
                        (chunk.Flags & ProcessOutputChunkFlags.Final) != 0)))
                    {
                        Interlocked.Increment(ref _droppedDiagnosticChunks);
                        Interlocked.Add(ref _droppedDiagnosticBytes, bytes.Length);
                    }
                }
            }
            var result = await _process.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            _exit.TrySetResult(new(result.CompletionKind, result.ExitCode));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            _exit.TrySetResult(new(ProcessCompletionKind.Stopped, SafeReasonCode: "TRANSPORT_STOPPED"));
        }
        catch (Exception exception)
        {
            failure = exception;
            _exit.TrySetResult(new(ProcessCompletionKind.Faulted, SafeReasonCode: "TRANSPORT_PUMP_FAILED"));
        }
        finally
        {
            _protocol.Writer.TryComplete(failure);
            _diagnostics.Writer.TryComplete(failure);
        }
    }
}
