using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

/// <summary>Durable S9 implementation of the sole neutral session authority journal.</summary>
/// <remarks>The file contains only checksummed canonical append requests plus the trusted admission instant. Recovery replays every record through the same production admission and fold implementation used by the in-memory conformance backend.</remarks>
internal sealed class FileAuthorityJournalV1 : IAuthorityJournalV1, IAsyncDisposable
{
    private const int HeaderLength = 12;
    private const int RecordPrefixLength = 13;
    private const int ChecksumLength = 32;
    private const int MaximumRecordLength = ProposedAuthorityFactV1.MaximumPayloadBytes + 65_536;
    private static readonly byte[] Magic = "HPDAJ001"u8.ToArray();
    private static readonly byte[] ChecksumDomain = Encoding.ASCII.GetBytes("hpd-authority-journal-record-v1\0");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Func<UtcInstant> _clock;
    private readonly AuthorityJournalCapacityV1 _capacity;
    private readonly FileStream _stream;
    private readonly Func<BoundedAscii, ValueTask>? _stageFault;
    private readonly List<StoredAppend> _records = [];
    private InMemoryAuthorityJournalV1 _memory;
    private bool _disposed;

    private FileAuthorityJournalV1(string path, AuthorityPayloadAdmissionRegistryV1 registry,
        Func<UtcInstant> clock, AuthorityJournalCapacityV1 capacity,
        Func<BoundedAscii, ValueTask>? stageFault)
    {
        _registry = registry;
        _clock = clock;
        _capacity = capacity;
        _stageFault = stageFault;
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _memory = new InMemoryAuthorityJournalV1(_registry, _clock, _capacity);
        ReloadFromDurableFile();
    }

    internal static ValueTask<FileAuthorityJournalV1> OpenAsync(string path,
        AuthorityPayloadAdmissionRegistryV1 registry, Func<UtcInstant> clock,
        AuthorityJournalCapacityV1 capacity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A journal directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        return ValueTask.FromResult(new FileAuthorityJournalV1(fullPath, registry, clock, capacity, null));
    }

    internal static ValueTask<FileAuthorityJournalV1> OpenForTestingAsync(string path,
        AuthorityPayloadAdmissionRegistryV1 registry, Func<UtcInstant> clock,
        AuthorityJournalCapacityV1 capacity, Func<BoundedAscii, ValueTask> stageFault,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stageFault);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A journal directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        return ValueTask.FromResult(new FileAuthorityJournalV1(fullPath, registry, clock, capacity, stageFault));
    }

    public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var admittedAt = _clock();
            var candidate = Rebuild(_records, admittedAt);
            var result = await candidate.AppendAsync(request, cancellationToken).ConfigureAwait(false);
            if (result is not AppendAuthorityResultV1.Committed) return result;

            var canonicalBatch = AuthorityCanonicalCborV1.EncodeAppendBatch(request);
            var frame = EncodeFrame(admittedAt, canonicalBatch);
            var start = _stream.Length;
            try
            {
                await InvokeStageFault("before-record-write").ConfigureAwait(false);
                _stream.Position = start;
                await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await InvokeStageFault("after-record-write-before-flush").ConfigureAwait(false);
                _stream.Flush(flushToDisk: true);
                await InvokeStageFault("after-record-flush").ConfigureAwait(false);
            }
            catch
            {
                ReloadFromDurableFile();
                throw;
            }

            _records.Add(new StoredAppend(request, admittedAt));
            _memory = candidate;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await _memory.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private InMemoryAuthorityJournalV1 Rebuild(IReadOnlyList<StoredAppend> records,
        UtcInstant? nextAdmission = null)
    {
        var admissions = new Queue<UtcInstant>(records.Select(static record => record.AdmittedAt));
        if (nextAdmission is { } next) admissions.Enqueue(next);
        var rebuilt = new InMemoryAuthorityJournalV1(_registry,
            () => admissions.Count > 0 ? admissions.Dequeue() : throw new InvalidDataException("The durable admission clock is incomplete."),
            _capacity);
        foreach (var record in records)
        {
            var replay = rebuilt.AppendAsync(record.Request).AsTask().GetAwaiter().GetResult();
            if (replay is not AppendAuthorityResultV1.Committed)
                throw new InvalidDataException("The durable authority journal does not replay as one canonical committed prefix.");
        }
        return rebuilt;
    }

    private void ReloadFromDurableFile()
    {
        _records.Clear();
        _stream.Flush();
        _stream.Position = 0;
        long lastComplete = 0;
        var headerBuffer = new byte[HeaderLength];
        while (_stream.Position < _stream.Length)
        {
            var remaining = _stream.Length - _stream.Position;
            if (remaining < HeaderLength) break;
            var header = headerBuffer.AsSpan();
            _stream.ReadExactly(header);
            if (!header[..Magic.Length].SequenceEqual(Magic))
                throw new InvalidDataException("The durable authority journal record magic is invalid.");
            var bodyLength = BinaryPrimitives.ReadInt32BigEndian(header[Magic.Length..]);
            if (bodyLength is < RecordPrefixLength or > MaximumRecordLength)
                throw new InvalidDataException("The durable authority journal record length is invalid.");
            if (_stream.Length - _stream.Position < bodyLength + ChecksumLength) break;
            var body = new byte[bodyLength];
            var checksum = new byte[ChecksumLength];
            _stream.ReadExactly(body);
            _stream.ReadExactly(checksum);
            if (!CryptographicOperations.FixedTimeEquals(ComputeChecksum(body), checksum))
                throw new InvalidDataException("The durable authority journal record checksum is invalid.");
            if (body[0] != 1) throw new InvalidDataException("The durable authority journal record version is unsupported.");
            var admittedAt = new UtcInstant(BinaryPrimitives.ReadInt64BigEndian(body.AsSpan(1, 8)));
            var batchLength = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(9, 4));
            if (batchLength <= 0 || batchLength != body.Length - RecordPrefixLength ||
                !AuthorityCanonicalCborV1.TryDecodeAppendBatch(body.AsMemory(RecordPrefixLength, batchLength), out var request) || request is null)
                throw new InvalidDataException("The durable authority journal append request is not canonical.");
            _records.Add(new StoredAppend(request, admittedAt));
            lastComplete = _stream.Position;
        }
        if (lastComplete != _stream.Length)
        {
            _stream.SetLength(lastComplete);
            _stream.Flush(flushToDisk: true);
        }
        _memory = Rebuild(_records);
        _stream.Position = _stream.Length;
    }

    private static byte[] EncodeFrame(UtcInstant admittedAt, byte[] canonicalBatch)
    {
        var body = new byte[checked(RecordPrefixLength + canonicalBatch.Length)];
        body[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(1, 8), admittedAt.NanosecondsSinceUnixEpoch);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(9, 4), canonicalBatch.Length);
        canonicalBatch.CopyTo(body, RecordPrefixLength);
        var frame = new byte[checked(HeaderLength + body.Length + ChecksumLength)];
        Magic.CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(Magic.Length, 4), body.Length);
        body.CopyTo(frame, HeaderLength);
        ComputeChecksum(body).CopyTo(frame, HeaderLength + body.Length);
        return frame;
    }

    private static byte[] ComputeChecksum(ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ChecksumDomain);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }

    private ValueTask InvokeStageFault(string stage) =>
        _stageFault is null ? ValueTask.CompletedTask : _stageFault(new BoundedAscii(stage));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record StoredAppend(AppendAuthorityBatchV1 Request, UtcInstant AdmittedAt);
}
